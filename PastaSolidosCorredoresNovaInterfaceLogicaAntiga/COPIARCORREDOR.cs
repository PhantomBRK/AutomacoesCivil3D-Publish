
    using Autodesk.AutoCAD.ApplicationServices;
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.EditorInput;
    using Autodesk.Civil.ApplicationServices;
    using Autodesk.Civil.DatabaseServices;
    using Autodesk.Civil.DatabaseServices.Styles; // Para estilos de Feature Line, se necessário
    using System.Collections.Generic;
    using System.Linq;
    using Exception = Autodesk.AutoCAD.Runtime.Exception;
    using Application = Autodesk.AutoCAD.ApplicationServices.Application;


using AutomacoesCivil3D;

namespace AutomacoesCivil3D.PastaSolidosCorredoresNovaInterfaceLogicaAntiga
{
    /// <summary>
    /// Classe para rotinas de extração de dados de corredores.
    /// </summary>
    public class ExtratorCorredor
    {
        /// <summary>
        /// Exporta todas as Feature Lines de um corredor como Polyline3d.
        /// </summary>
        /// <param name="corridorId">O ObjectId do corredor a ser processado.</param>
        public void ExportarFeatureLinesComoPolyline3d(ObjectId corridorId)
        {
            Document docCad = Manager.DocCad;
            CivilDocument docCivil = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            using (Transaction transCad = docCad.Database.TransactionManager.StartTransaction())
            {
                Corridor corridor = transCad.GetObject(corridorId, OpenMode.ForRead) as Corridor;

                if (corridor == null)
                {
                    docEditor.WriteMessage("\nErro: Não foi possível abrir o corredor com o ID fornecido.");
                    return;
                }

                docEditor.WriteMessage($"\n--- Iniciando extração de Feature Lines para o corredor: '{corridor.Name}' ---");

                // As coleções de códigos são úteis para identificar quais Feature Lines existem.
                string[] pointCodes = corridor.GetPointCodes();
                string[] linkCodes = corridor.GetLinkCodes();
                string[] shapeCodes = corridor.GetShapeCodes();

                docEditor.WriteMessage("\n  Códigos de Feature Line identificados no corredor:");
                docEditor.WriteMessage("    Códigos de Ponto: " + (pointCodes.Any() ? string.Join(", ", pointCodes) : "Nenhum"));
                docEditor.WriteMessage("    Códigos de Link: " + (linkCodes.Any() ? string.Join(", ", linkCodes) : "Nenhum"));
                docEditor.WriteMessage("    Códigos de Shape: " + (shapeCodes.Any() ? string.Join(", ", shapeCodes) : "Nenhum"));

                docEditor.WriteMessage("\n  Iniciando iteração pelas Baselines e Regiões para extrair Feature Lines...");

                int totalFeatureLinesExportadas = 0;

                // 1. Itera sobre as Baselines do corredor
                foreach (Baseline baseline in corridor.Baselines)
                {
                    docEditor.WriteMessage($"\n  Processando Baseline: '{baseline.Name}' (ID: {baseline.StartStation})");

                    // 2. Acessa as CorridorRegions da Baseline.
                    //    A propriedade 'CorridorRegions' não estava na sua definição parcial de 'Baseline',
                    //    mas é uma coleção padrão da API do Civil 3D para acessar as regiões de uma Baseline.
                    //    Se sua classe 'Baseline' não tiver essa propriedade, a abordagem precisará ser ajustada.
                    //    Assumimos aqui que 'baseline' possui uma propriedade 'CorridorRegions'.
                    dynamic baselineRegions = GetCorridorRegionsFromBaseline(baseline); // Usamos 'dynamic' para evitar erro de compilação sem a definição exata

                    if (baselineRegions != null)
                    {
                        foreach (dynamic corridorRegion in baselineRegions) // Itera sobre as regiões
                        {
                            docEditor.WriteMessage($"\n    Processando Região: '{corridorRegion.Name}' (ID: {corridorRegion.ObjectId})");

                            // 3. Obtém as Feature Lines da região.
                            //    O método 'GetCorridorFeatureLines()' não estava na sua definição parcial de 'CorridorRegion',
                            //    mas é um método padrão da API do Civil 3D para extrair Feature Lines de uma região.
                            //    Assumimos aqui que 'corridorRegion' possui um método 'GetCorridorFeatureLines()'.
                            IEnumerable<CorridorFeatureLine> featureLinesDaRegiao = GetCorridorFeatureLinesFromRegion(corridorRegion);

                            if (featureLinesDaRegiao != null)
                            {
                                foreach (CorridorFeatureLine featureLine in featureLinesDaRegiao)
                                {
                                    docEditor.WriteMessage($"\n      Feature Line encontrada: '{featureLine.CodeName}' (Style: '{featureLine.StyleName}')");

                                    
                                }
                            }
                            else
                            {
                                docEditor.WriteMessage("      Nenhuma Feature Line encontrada nesta região.");
                            }
                        }
                    }
                    else
                    {
                        docEditor.WriteMessage("    Nenhuma região encontrada para esta Baseline (verifique a propriedade CorridorRegions).");
                    }
                }

                docEditor.WriteMessage($"\n--- Extração concluída. Total de {totalFeatureLinesExportadas} Feature Lines exportadas como Polyline3d. ---");
                transCad.Commit();
            }
        }

        // --- Métodos Auxiliares para lidar com as classes não totalmente fornecidas ---
        // ESTES MÉTODOS SÃO CONCEITUAIS E PRECISAM DE REFINAMENTO COM AS DEFINIÇÕES COMPLETAS DA SUA API.

        /// <summary>
        /// Método auxiliar conceitual para obter CorridorRegions de uma Baseline.
        /// Substitua pela implementação real da sua API.
        /// </summary>
        /// <param name="baseline">O objeto Baseline.</param>
        /// <returns>Uma coleção de CorridorRegion, ou null se não houver.</returns>
        private dynamic GetCorridorRegionsFromBaseline(Baseline baseline)
        {
            // Na API do Civil 3D, 'Baseline' geralmente tem uma propriedade 'CorridorRegions'.
            // Ex: return baseline.CorridorRegions;
            // Como não temos a definição completa, retornamos um mock ou null.
            // VOCÊ PRECISA SUBSTITUIR ISTO PELA IMPLEMENTAÇÃO REAL DA SUA ASSEMBLY.
            // Exemplo hipotético:
            return (dynamic)baseline.GetType().GetProperty("CorridorRegions")?.GetValue(baseline, null);

            // Para que o código compile e demonstre o fluxo, podemos retornar uma lista vazia ou nula.
            // Para um ambiente de desenvolvimento real, você precisará refletir a API ou ter as referências completas.
            Manager.DocEditor.WriteMessage("\n      [AVISO]: O acesso a 'CorridorRegions' de 'Baseline' é conceitual. Verifique sua API.");

            // Exemplo de como você obteria as regiões se elas fossem uma propriedade:
            var regionsProperty = baseline.GetType().GetProperty("CorridorRegions");
            if (regionsProperty != null)
            {
                 return regionsProperty.GetValue(baseline) as IEnumerable<CorridorRegion>;
            }
            return null; // Retorne null ou uma coleção vazia para evitar erros se não implementado
        }

        /// <summary>
        /// Método auxiliar conceitual para obter CorridorFeatureLines de uma CorridorRegion.
        /// Substitua pela implementação real da sua API.
        /// </summary>
        /// <param name="corridorRegion">O objeto CorridorRegion.</param>
        /// <returns>Uma coleção de CorridorFeatureLine, ou null se não houver.</returns>
        private IEnumerable<CorridorFeatureLine> GetCorridorFeatureLinesFromRegion(dynamic corridorRegion)
        {
            // Na API do Civil 3D, 'CorridorRegion' geralmente tem um método como 'GetCorridorFeatureLines()'.
            // Ex: return corridorRegion.GetCorridorFeatureLines();
            // Como não temos a definição completa, retornamos um mock ou null.
            // VOCÊ PRECISA SUBSTITUIR ISTO PELA IMPLEMENTAÇÃO REAL DA SUA ASSEMBLY.
            // Exemplo hipotético:

            return (IEnumerable<CorridorFeatureLine>)corridorRegion.GetType().GetMethod("GetCorridorFeatureLines")?.Invoke(corridorRegion, null);


            Manager.DocEditor.WriteMessage("\n      [AVISO]: O acesso a 'CorridorFeatureLines' de 'CorridorRegion' é conceitual. Verifique sua API.");

            // Para que o código compile e demonstre o fluxo, retornamos uma lista vazia.
            // Para um ambiente de desenvolvimento real, você precisará refletir a API ou ter as referências completas.
            //return Enumerable.Empty<CorridorFeatureLine>();
        }
    }
}

