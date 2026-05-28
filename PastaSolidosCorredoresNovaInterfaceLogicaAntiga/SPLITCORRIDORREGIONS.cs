using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

// Aliases para evitar conflitos:
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.Civil.Runtime;

using AutomacoesCivil3D;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    public class CorridorRegionSplitter
    {
        [CommandMethod("SPLITCORRIDORREGIONS")]
        public void SplitCorridorRegions()
        {
            // Obtém objetos padrão via Manager (como você solicitou)
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            Transaction TransCad = null; // Inicializa fora do try para garantir acesso no finally

            try
            {
                // Solicita seleção do corredor de origem
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione o corredor de origem:");
                peo.SetRejectMessage("\nA entidade selecionada não é um corredor.");
                peo.AddAllowedClass(typeof(Corridor), false);

                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                ObjectId sourceCorridorId = per.ObjectId;

                using (DocumentLock docLock = civilDoc.LockDocument())
                {
                    TransCad = civilDoc.TransactionManager.StartTransaction(); // Inicia a transação aqui

                    // Abre o corredor de origem
                    Corridor sourceCorridor = (Corridor)TransCad.GetObject(sourceCorridorId, OpenMode.ForRead);
                    if (sourceCorridor == null)
                    {
                        docEditor.WriteMessage("\nNão foi possível acessar o corredor de origem.");
                        TransCad.Abort(); // Aborta a transação
                        return;
                    }

                    string sourceName = sourceCorridor.Name;
                    docEditor.WriteMessage($"\nCorredor selecionado: {sourceName}");

                    // Contadores para nomenclatura
                    int baselineIndex = 0;
                    int createdCount = 0;
                    int n = 1; // Contador para cópia de alinhamentos

                    // Percorre cada Baseline do corredor
                    foreach (Baseline baseline in sourceCorridor.Baselines)
                    {
                        if (baseline == null)
                        {
                            baselineIndex++;
                            continue;
                        }

                        // Obtém IDs de alinhamento e perfil da baseline original
                        ObjectId originalAlignmentId = baseline.AlignmentId;
                        ObjectId originalProfileId = baseline.ProfileId;

                        if (originalAlignmentId.IsNull || originalProfileId.IsNull)
                        {
                            docEditor.WriteMessage($"\nBaseline {baselineIndex + 1}: alinhamento ou perfil originais inválidos. Pulando.");
                            baselineIndex++;
                            continue;
                        }

                        int regionIndex = 0;

                        // Percorre cada região da baseline
                        foreach (BaselineRegion region in baseline.BaselineRegions)
                        {
                            if (region == null)
                            {
                                regionIndex++;
                                continue;
                            }

                            // Dados fundamentais da região
                            double startStation = region.StartStation;
                            double endStation = region.EndStation;
                            ObjectId assemblyId = region.AssemblyId;

                            if (assemblyId.IsNull)
                            {
                                docEditor.WriteMessage($"\nBaseline {baselineIndex + 1}, Região {regionIndex + 1}: assembly inválido. Pulando.");
                                regionIndex++;
                                continue;
                            }

                            // Gera um nome base para o novo corredor e seus componentes
                            string newBaseName = $"{sourceName}_BL{baselineIndex + 1}_RG{regionIndex + 1}";

                            // --- PASSO 1: COPIAR O ALINHAMENTO E O PERFIL ORIGINAIS USANDO Alignment.CopyToSameSite() ---
                            Tuple<ObjectId, ObjectId> copiedObjects = CopyOriginalAlignmentAndProfileViaCopyMethod(
                                civilDb, TransCad, originalAlignmentId, originalProfileId, newBaseName, docEditor, n);
                            n++; // Incrementa o contador para o próximo alinhamento copiado

                            ObjectId newAlignmentId = copiedObjects.Item1;
                            ObjectId newProfileId = copiedObjects.Item2;


                            

                            if (newAlignmentId.IsNull || newProfileId.IsNull)
                            {
                                docEditor.WriteMessage($"\nFalha ao copiar Alinhamento e/ou Perfil para '{newBaseName}'. Pulando região.");
                                regionIndex++;
                                continue;
                            }

                            // --- PASSO 2: CRIAR O NOVO CORREDOR ---
                            ObjectId newCorridorId = CreateCorridor(civilDb, TransCad, newBaseName, docEditor);
                            if (newCorridorId.IsNull)
                            {
                                docEditor.WriteMessage($"\nFalha ao criar corredor '{newBaseName}'. Pulando região.");
                                // Limpa os objetos de geometria recém-copiados se o corredor não puder ser criado.
                                TransCad.GetObject(newAlignmentId, OpenMode.ForWrite).Erase();
                                TransCad.GetObject(newProfileId, OpenMode.ForWrite).Erase();
                                regionIndex++;
                                continue;
                            }

                            // Abre o corredor novo para edição
                            Corridor newCorridor = (Corridor)TransCad.GetObject(newCorridorId, OpenMode.ForWrite);


                            // --- PASSO 3: ADICIONA NOVA BASELINE AO CORREDOR NOVO COM OS ALINHAMENTO E PERFIL COPIADOS ---
                            Baseline newBaseline = AddBaseline(newCorridor, newAlignmentId, newProfileId);
                            if (newBaseline == null)
                            {
                                docEditor.WriteMessage($"\nNão foi possível criar a baseline no corredor '{newBaseName}'. Pulando região.");
                                // Limpa os objetos.
                                TransCad.GetObject(newCorridorId, OpenMode.ForWrite).Erase();
                                TransCad.GetObject(newAlignmentId, OpenMode.ForWrite).Erase();
                                TransCad.GetObject(newProfileId, OpenMode.ForWrite).Erase();
                                regionIndex++;
                                continue;
                            }

                            // --- PASSO 4: CRIA A REGIÃO NO NOVO CORREDOR ---
                            // As estacas da nova região serão as mesmas da original, pois o alinhamento copiado é o original completo.
                            BaselineRegion newRegion = AddRegion(newBaseline, assemblyId, startStation, endStation);
                            if (newRegion == null)
                            {
                                docEditor.WriteMessage($"\nNão foi possível criar a região no corredor '{newBaseName}'. Pulando.");
                                // Limpa os objetos.
                                TransCad.GetObject(newCorridorId, OpenMode.ForWrite).Erase();
                                TransCad.GetObject(newAlignmentId, OpenMode.ForWrite).Erase();
                                TransCad.GetObject(newProfileId, OpenMode.ForWrite).Erase();
                                SubassemblyTargetInfoCollection subassemblyTargetInfos = region.GetTargets();
                                foreach (SubassemblyTargetInfo targetInfo in subassemblyTargetInfos)
                                {
                                    docEditor.WriteMessage($"\n - Target: {targetInfo.LogicalName}, Type: {targetInfo.TargetType}, Name: {targetInfo.DisplayName}");
                                }
                                regionIndex++;
                                continue;
                            }

                            

                            // ================================
                            // Pontos de extensão (opcionais):
                            // - Replicar parâmetros de frequência
                            // - Replicar estilos e code set styles
                            // - Replicar superfícies do corredor e limites
                            // (Isto ainda pode ser adicionado com base na sua versão do Civil 3D)
                            // ================================

                            // Rebuild do corredor novo (se suportado/necessário)
                            try
                            {
                                newCorridor.Rebuild();
                                newCorridor.CopyFrom(sourceCorridor);
                                CorridorSurface cs = newCorridor.CorridorSurfaces.Add(newCorridor.Name);
                                cs.AddLinkCode("DATUM", true);
                                cs.AddLinkCode("datum", true);
                                cs.IsLinkCodeAsBreakLine("DATUM");
                                cs.IsLinkCodeAsBreakLine("datum");
                                cs.OverhangCorrection = OverhangCorrectionType.BottomLinks;



                                Polyline polyType = new Polyline();
                                polyType.GetOffsetCurves(10);
                            }

                            catch (System.Exception exRebuild)
                            {
                                docEditor.WriteMessage($"\nAviso: Falha ao reconstruir {newCorridor.Name}: {exRebuild.Message}. Pode requerer rebuild manual.");
                            }

                            createdCount++;
                            docEditor.WriteMessage($"\nCriado: {newCorridor.Name} (BL {baselineIndex + 1}, RG {regionIndex + 1})");

                            regionIndex++;
                        }

                        baselineIndex++;
                    }

                    TransCad.Commit(); // Confirma as alterações no desenho

                    if (createdCount == 0)
                    {
                        docEditor.WriteMessage("\nNenhum corredor foi criado. Verifique se o corredor possui baselines e regiões válidas.");
                    }
                    else
                    {
                        docEditor.WriteMessage($"\nConcluído. {createdCount} corredor(es) criado(s) a partir das regiões, com Alinhamentos e Perfis copiados do original.");
                    }
                } // using (DocumentLock)
            }
            catch (Exception ex) // Exceções da API AutoCAD (Autodesk.AutoCAD.Runtime.Exception)
            {
                Application.ShowAlertDialog($"Erro da API AutoCAD: {ex.Message}");
                if (TransCad != null) TransCad.Abort(); // Garante que a transação seja abortada em caso de erro
            }
            catch (System.Exception ex) // Outras exceções .NET
            {
                Application.ShowAlertDialog($"Erro: {ex.Message}");
                if (TransCad != null) TransCad.Abort(); // Garante que a transação seja abortada em caso de erro
            }
            finally
            {
                // Garante que a transação seja descartada caso não tenha sido committed ou aborted
                if (TransCad != null && !TransCad.IsDisposed)
                {
                    TransCad.Dispose();
                }
            }
        }

        /// <summary>
        /// Copia o Alinhamento e o Perfil associado usando Alignment.CopyToSameSite().
        /// Isso replica o comportamento do comando nativo _copy.
        /// </summary>
        /// <param name="civilDb">O documento Civil3D.</param>
        /// <param name="TransCad">A transação corrente.</param>
        /// <param name="sourceAlignmentId">ObjectId do Alinhamento original.</param>
        /// <param name="sourceProfileId">ObjectId do Perfil original (associado ao alinhamento).</param>
        /// <param name="baseName">Nome base para os novos alinhamento e perfil.</param>
        /// <param name="docEditor">Editor para mensagens.</param>
        /// <param name="n">Int para contagem.</param>
        /// <returns>Um Tuple contendo os ObjectIds do novo Alinhamento e Perfil, ou ObjectId.Null se falhar.</returns>
        private Tuple<ObjectId, ObjectId> CopyOriginalAlignmentAndProfileViaCopyMethod(
            CivilDocument civilDb, Transaction TransCad, ObjectId sourceAlignmentId, ObjectId sourceProfileId,
            string baseName, Editor docEditor, int n)
        {
            ObjectId newAlignmentId = ObjectId.Null;
            ObjectId newProfileId = ObjectId.Null;

            try
            {
                // Obtém o alinhamento original
                Alignment originalAlignment = (Alignment)TransCad.GetObject(sourceAlignmentId, OpenMode.ForRead);




                if (originalAlignment == null)
                {
                    docEditor.WriteMessage("\nErro: Alinhamento original não encontrado para cópia.");
                    return Tuple.Create(ObjectId.Null, ObjectId.Null);
                }

                // *** PASSO 1: COPIAR O ALINHAMENTO USANDO CopyToSameSite() ***
                // Este método copia o alinhamento e seus perfis associados, incluindo a geometria.
                originalAlignment.CopyToSameSite();
                // Abre o novo alinhamento para escrita para renomeá-lo
                


                foreach (ObjectId alnId in civilDb.GetAlignmentIds())
                {
                    Alignment aln = (Alignment)TransCad.GetObject(alnId, OpenMode.ForRead);
                    if (aln.Name.Contains($"({n})"))
                    {
                        newAlignmentId = alnId;
                        break;
                    }
                }

                Alignment newAlignment = (Alignment)TransCad.GetObject(newAlignmentId, OpenMode.ForWrite);
                
                string newAlignmentName = baseName + "_Align";




                docEditor.WriteMessage($"\nCopiado Alinhamento: {newAlignment.Name}");

                // *** PASSO 2: ENCONTRAR E RENOMEAR O PERFIL COPIADO ***
                // O CopyToSameSite copia os perfis. Precisamos encontrar qual perfil copiado corresponde ao original.
                // Geralmente, o perfil copiado terá o mesmo tipo e o nome do original (com um prefixo como (1)).
                Profile originalProfile = (Profile)TransCad.GetObject(sourceProfileId, OpenMode.ForRead);
                if (originalProfile == null)
                {
                    docEditor.WriteMessage("\nErro: Perfil original não encontrado para cópia.");
                    // Limpa o alinhamento recém-criado
                    TransCad.GetObject(newAlignmentId, OpenMode.ForWrite).Erase();
                    return Tuple.Create(ObjectId.Null, ObjectId.Null);
                }

                // Itera sobre os perfis do NOVO alinhamento para encontrar o que foi copiado
                foreach (ObjectId profileId in newAlignment.GetProfileIds())
                {
                    Profile copiedProfileCandidate = (Profile)TransCad.GetObject(profileId, OpenMode.ForRead);
                    // Compara pelo tipo de perfil e verifica se o nome original está contido no nome copiado.
                    // Isso é um heuristic, pois o nome do perfil copiado é "(1) OriginalProfileName".
                    if (copiedProfileCandidate.ProfileType == originalProfile.ProfileType &&
                        copiedProfileCandidate.Name.Contains(originalProfile.Name))
                    {
                        newProfileId = profileId;
                        // Abre o novo perfil para escrita para renomeá-lo
                        Profile newProfile = (Profile)TransCad.GetObject(newProfileId, OpenMode.ForWrite);
                        string newProfileName = baseName + "_Prof";
                        
                        docEditor.WriteMessage($"\nCopiado Perfil: {newProfile.Name}");
                        break; // Perfil encontrado e renomeado
                    }
                }

                if (newProfileId.IsNull)
                {
                    docEditor.WriteMessage("\nErro: Perfil copiado não encontrado ou associado ao novo alinhamento.");
                    TransCad.GetObject(newAlignmentId, OpenMode.ForWrite).Erase(); // Limpa o alinhamento recém-criado
                    return Tuple.Create(ObjectId.Null, ObjectId.Null);
                }

                return Tuple.Create(newAlignmentId, newProfileId);
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\nErro ao copiar Alinhamento e Perfil usando Alignment.CopyToSameSite(): {ex.Message}");
                // Em caso de erro, apaga os objetos recém-criados para evitar sujeira no desenho
                if (!newAlignmentId.IsNull) TransCad.GetObject(newAlignmentId, OpenMode.ForWrite).Erase();
                // O perfil deve ser apagado automaticamente se o alinhamento for apagado.
                return Tuple.Create(ObjectId.Null, ObjectId.Null);
            }
        }

        /// <summary>
        /// Renomeia um objeto Civil 3D garantindo que o novo nome seja único.
        /// Este método é para uso interno da função CopyOriginalAlignmentAndProfileViaCopyMethod.
        /// </summary>
        /// <typeparam name="T">Tipo do objeto Civil 3D (ex: Alignment, Profile).</typeparam>
        /// <param name="collection">A coleção à qual o objeto pertence (ex: civilDb.Alignments, newAlignment.Profiles).</param>
        /// <param name="baseName">O nome base desejado.</param>
        /// <returns>O nome único gerado.</returns>
       

        /// <summary>
        /// Cria um corredor por nome, garantindo tentativa de nome único ao detectar conflito
        /// </summary>
        private ObjectId CreateCorridor(CivilDocument civilDb, Transaction TransCad, string baseName, Editor docEditor)
        {
            // Tenta criar com o nome base
            try
            {
                ObjectId corridorId = civilDb.CorridorCollection.Add(baseName);
                return corridorId;
            }
            catch (System.Exception) // Captura a exceção para tentar com nome alternativo
            {
                // Se houver conflito de nome, adiciona sufixos incrementais
                for (int i = 1; i <= 999; i++)
                {
                    string altName = $"{baseName}_{i}";
                    try
                    {
                        ObjectId altId = civilDb.CorridorCollection.Add(altName);
                        docEditor.WriteMessage($"\nAviso: nome '{baseName}' já existia. Usando '{altName}'.");
                        return altId;
                    }
                    catch (System.Exception)
                    {
                        // Continua tentando
                    }
                }
            }

            
            return ObjectId.Null;
        }

        /// <summary>
        /// Adiciona uma Baseline ao corredor novo usando alinhamento e perfil fornecidos
        /// </summary>
        private Baseline AddBaseline(Corridor corridor, ObjectId alignmentId, ObjectId profileId)
        {
            try
            {
                // BaselineCollection.Add(alignmentId, profileId)
                // Retorna Baseline (objeto gerenciado pela API)
                Baseline baseline = corridor.Baselines.Add("BL - ", alignmentId, profileId);
                return baseline;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Adiciona uma região à baseline com assembly e range de estações
        /// </summary>
        private BaselineRegion AddRegion(Baseline baseline, ObjectId assemblyId, double startStation, double endStation)
        {
            try
            {
                // BaselineRegionCollection.Add(assemblyId, startStation, endStation)
                BaselineRegion region = baseline.BaselineRegions.Add("RG - ", assemblyId, startStation, endStation);
                return region;
            }
            catch
            {
                return null;
            }
        }

       
    }
}

