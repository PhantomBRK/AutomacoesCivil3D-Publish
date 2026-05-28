// Para PropertySetDefinition, PropertyDefinition, PropertySetData, PropertySetDefinitionServices, PropertySetDataServices
// Estas classes são fundamentais para trabalhar com Property Sets e estão tipicamente na biblioteca AecBaseMgd.dll,
// que deve ser referenciada no seu projeto (geralmente localizada em C:\Program Files\Autodesk\AutoCAD 20xx\AecBaseMgd.dll).
using Autodesk.Aec.PropertyData.DatabaseServices; // Importante!
using Autodesk.AutoCAD.ApplicationServices;
// Certifique-se de adicionar as seguintes referências ao seu projeto no Visual Studio:
// - AcCoreMgd.dll
// - AcDbMgd.dll
// - AcMgd.dll
// - AecBaseMgd.dll (essencial para DataStore e outras funcionalidades AEC)
// - AecPropDataMgd.dll (para as classes de Property Set)


using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms.Design;

// Aliases para evitar conflitos de namespace, conforme sua instrução.
// É uma boa prática para melhorar a legibilidade e evitar ambiguidades.
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ObjectIdCollection = Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;

namespace AutomacoesCivil3D
{
    public class PropertySets2
    {

        static double width = 0;
        static double baseDepth = 0;
        static double subBaseDepth = 0;
        static double pave1Depth = 0;
        static double depth = 0;
        static double height = 0;
        static double length = 0;
        static double guiaDepth = 0;
        static double passeioDepth = 0;
        static double slope = 0;

        public enum ResultadoProcessamento
        {
            Sucesso,
            SemPsetCorridor,   // sólido não tem PSets nativos de corredor anexados
            SemCategoriaMatch, // CodeName não bateu em PAVIMENTO/PASSEIO/BASE/SUB_BASE/GUIA
            ErroPsetC          // falhou ao acessar/escrever no PSet_C
        }

        [CommandMethod("AplicarPSet")]
        public void pSet21()
        {
            Database db = Manager.DocData;
            Editor docEditor = Manager.DocEditor;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione um Solid3d ou Body de corredor");
            peo.SetRejectMessage("\nPor favor, selecione apenas Solid3d ou Body.");
            peo.AddAllowedClass(typeof(Solid3d), false);
            peo.AddAllowedClass(typeof(Body), false);
            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nSeleção cancelada.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                string psetCName = ResolverNomePsetC(dictionary, tr, db, docEditor);
                if (psetCName == null)
                {
                    docEditor.WriteMessage("\n[ABORTADO] PSet 'Propriedades Físicas' não encontrado no desenho.");
                    return;
                }
                ObjectId psetCId = dictionary.GetAt(psetCName);

                Autodesk.AutoCAD.DatabaseServices.Entity ent =
                    (Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                var resultado = ProcessarEntidadeCorredor(ent, psetCId, db, docEditor, tr, dictionary, verbose: true);
                docEditor.WriteMessage($"\nResultado: {resultado}");
                tr.Commit();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Comando batch: processa TODOS os Solid3d e Body do ModelSpace que tenham
        // os PSets nativos do Civil 3D anexados (vindos de corridor.ExportSolids()).
        // ─────────────────────────────────────────────────────────────────────────
        [CommandMethod("AplicarPsetTodos")]
        public void AplicarPsetTodos()
        {
            Database db = Manager.DocData;
            Editor docEditor = Manager.DocEditor;

            int totalSolid = 0, totalBody = 0, sucesso = 0, semCorridor = 0, semCategoria = 0, erro = 0;
            docEditor.WriteMessage("\n=== AplicarPsetTodos: varrendo ModelSpace (Solid3d + Body) ===");

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                string psetCName = ResolverNomePsetC(dictionary, tr, db, docEditor);
                if (psetCName == null)
                {
                    docEditor.WriteMessage("\n[ABORTADO] PSet 'Propriedades Físicas' não encontrado no desenho.");
                    return;
                }
                ObjectId psetCId = dictionary.GetAt(psetCName);
                docEditor.WriteMessage($"\n[OK] PSet_C resolvido: '{psetCName}'");

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in ms)
                {
                    DBObject obj = tr.GetObject(entId, OpenMode.ForRead);
                    Autodesk.AutoCAD.DatabaseServices.Entity ent = obj as Autodesk.AutoCAD.DatabaseServices.Entity;
                    if (ent == null) continue;

                    // Aceita Solid3d OU Body — ambos podem ter PSets nativos de corredor
                    bool ehSolid = ent is Solid3d;
                    bool ehBody = ent is Body;
                    if (!ehSolid && !ehBody) continue;

                    if (ehSolid) totalSolid++; else totalBody++;

                    var resultado = ProcessarEntidadeCorredor(ent, psetCId, db, docEditor, tr, dictionary, verbose: false);
                    switch (resultado)
                    {
                        case ResultadoProcessamento.Sucesso: sucesso++; break;
                        case ResultadoProcessamento.SemPsetCorridor: semCorridor++; break;
                        case ResultadoProcessamento.SemCategoriaMatch:
                            semCategoria++;
                            docEditor.WriteMessage($"\n  [SEM CATEGORIA] {ent.GetType().Name} Handle {ent.Handle}");
                            break;
                        case ResultadoProcessamento.ErroPsetC: erro++; break;
                    }

                    if (sucesso > 0 && sucesso % 50 == 0)
                        docEditor.WriteMessage($"\n  ... {sucesso} entidades processadas até agora ...");
                }

                tr.Commit();
            }

            docEditor.WriteMessage(
                $"\n=== AplicarPsetTodos: fim ===" +
                $"\n  Total Solid3d no ModelSpace:   {totalSolid}" +
                $"\n  Total Body    no ModelSpace:   {totalBody}" +
                $"\n  Processados com sucesso:       {sucesso}" +
                $"\n  Sem PSets nativos de corredor: {semCorridor}" +
                $"\n  CodeName sem categoria match:  {semCategoria}" +
                $"\n  Erros ao escrever no Pset_C:   {erro}");
        }

        // Processa uma Entity (Solid3d OU Body): lê os 4 PSets nativos, calcula dimensões via
        // ParametrosGuid, classifica via CodeMatches e escreve no Pset_C alvo.
        // verbose=true imprime debug detalhado (usado pelo AplicarPSet interativo).
        // verbose=false só imprime avisos importantes (usado pelo AplicarPsetTodos batch).
        public static ResultadoProcessamento ProcessarEntidadeCorredor(
            Autodesk.AutoCAD.DatabaseServices.Entity entity,
            ObjectId psetCId,
            Database db,
            Editor docEditor,
            Transaction tr,
            DictionaryPropertySetDefinitions dictionary,
            bool verbose)
        {
            // Zera estáticas (Fix C) — sem isso valores vazam entre entidades no batch
            width = 0; baseDepth = 0; subBaseDepth = 0; pave1Depth = 0;
            depth = 0; height = 0; length = 0;
            guiaDepth = 0; passeioDepth = 0; slope = 0;

            string tipoEnt = entity.GetType().Name; // "Solid3d" ou "Body"

            // Lê os 4 valores chave dos PSets nativos do Civil 3D
            string nomeCamada = LerPropriedadeNativa(tr, dictionary, entity, "Corridor Shape Information", "CodeName", docEditor);
            string nomeSub = LerPropriedadeNativa(tr, dictionary, entity, "Corridor Identity", "SubassemblyName", docEditor);
            string nomeCorredor = LerPropriedadeNativa(tr, dictionary, entity, "Corridor Model Information", "CorridorName", docEditor);
            string guid = LerPropriedadeNativa(tr, dictionary, entity, "Corridor Model Information", "RegionName", docEditor);

            if (verbose)
            {
                docEditor.WriteMessage($"\n--- {tipoEnt} handle {entity.Handle} ---");
                docEditor.WriteMessage($"\n  CodeName='{nomeCamada}'  SubassemblyName='{nomeSub}'");
                docEditor.WriteMessage($"\n  Corredor='{nomeCorredor}'  RegionName='{guid}'");
            }

            if (string.IsNullOrWhiteSpace(nomeCorredor) || string.IsNullOrWhiteSpace(guid))
                return ResultadoProcessamento.SemPsetCorridor;

            // Varre o corredor para preencher width/baseDepth/subBaseDepth/.../length/slope
            ParametrosGuid(guid, nomeCorredor, nomeSub, verbose, docEditor);

            // Abre o PSet_C anexado à entidade para escrita
            PropertySet psetC;
            try
            {
                ObjectId psetInstId = PropertyDataServices.GetPropertySet(entity, psetCId);
                if (psetInstId == ObjectId.Null)
                {
                    if (verbose) docEditor.WriteMessage($"\n[AVISO] PSet_C não está anexado a {tipoEnt} handle {entity.Handle}.");
                    return ResultadoProcessamento.ErroPsetC;
                }
                psetC = (PropertySet)tr.GetObject(psetInstId, OpenMode.ForWrite);
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO] Falha ao abrir PSet_C de {tipoEnt} {entity.Handle}: {ex.Message}");
                return ResultadoProcessamento.ErroPsetC;
            }

            // Classifica CodeName em PAVIMENTO/PASSEIO/BASE/SUB_BASE/GUIA via aliases
            bool ehPasseio = CodeMatches(nomeSub, "PASSEIO", "TrilhoLD", "TrilhoLE", "Sidewalk");
            double altura = double.NaN;

            if (CodeMatches(nomeCamada, "PAVIMENTO", "PAVIMENTO1", "PAVIMENTO2", "CBUQ", "CBUQ - CAP",
                    "Pave1", "Pave2", "pave1", "pave2",
                    "CONCRETO ARMADO FCK = 30 MPA", "BLOCOS INTERTRAVADOS", "TSD", "TSS", "TST")
                && !ehPasseio)
            {
                altura = pave1Depth;
            }
            else if (CodeMatches(nomeCamada, "PAVIMENTO", "PAVIMENTO1", "PAVIMENTO2", "PASSEIO",
                    "Pave1", "Pave2", "Sidewalk", "TrilhoLD", "TrilhoLE")
                && ehPasseio)
            {
                altura = passeioDepth;
            }
            else if (CodeMatches(nomeCamada, "BASE", "IMPRIMAÇÃO DE BASE", "BASE DE BRITA GRADUADA", "Base")
                && !CodeMatches(nomeCamada, "SUB_BASE", "SUBBASE", "SubBase", "SUB BASE",
                    "SUB BASE/COLCHÃO DRENANTE", "ACOSTAMENTO_BASE"))
            {
                altura = baseDepth;
            }
            else if (CodeMatches(nomeCamada, "SUB_BASE", "SUBBASE", "SUB BASE", "SubBase", "SUBBASE_2", "SUBBASE_3",
                    "SUB BASE/COLCHÃO DRENANTE", "FERROVIA", "LEITO", "Lastro", "SUBLEITO",
                    "SoilFill", "SoloReforço"))
            {
                altura = subBaseDepth;
            }
            else if (CodeMatches(nomeCamada, "GUIA", "Rip Rap", "Curb", "Top_Curb", "Face_Curb"))
            {
                altura = guiaDepth;
            }

            if (double.IsNaN(altura))
            {
                if (verbose)
                    docEditor.WriteMessage(
                        $"\n[AVISO] CodeName '{nomeCamada}' não bateu com nenhuma categoria. " +
                        "Adicione o alias à lista CodeMatches correspondente se for novo.");
                return ResultadoProcessamento.SemCategoriaMatch;
            }

            EscreverPsetCField(psetC, entity, altura, docEditor);
            if (verbose) docEditor.WriteMessage($"\n[OK] Pset_C atualizado para {tipoEnt} CodeName='{nomeCamada}'.");
            return ResultadoProcessamento.Sucesso;
        }

        // Wrapper legado mantido por compatibilidade — delega para ProcessarEntidadeCorredor.
        public static ResultadoProcessamento ProcessarSolidoCorredor(
            Solid3d solid, ObjectId psetCId, Database db, Editor docEditor,
            Transaction tr, DictionaryPropertySetDefinitions dictionary, bool verbose)
            => ProcessarEntidadeCorredor(solid, psetCId, db, docEditor, tr, dictionary, verbose);

        // Calcula Volume da entidade. Solid3d via MassProperties.Volume (direto).
        // Body NÃO expõe MassProperties em algumas versões da API .NET — tenta via reflection
        // como fallback, retorna 0 se falhar.
        public static double ObterVolumeEntidade(Autodesk.AutoCAD.DatabaseServices.Entity entity, Editor docEditor)
        {
            if (entity is Solid3d s3d)
            {
                try { return s3d.MassProperties.Volume; }
                catch (System.Exception ex)
                {
                    docEditor.WriteMessage($"\n[AVISO] Solid3d.MassProperties falhou para handle {entity.Handle}: {ex.Message}");
                    return 0.0;
                }
            }

            // Body — tenta reflection: alguns runtimes expõem .MassProperties, outros não.
            try
            {
                var mpProp = entity.GetType().GetProperty("MassProperties");
                if (mpProp != null)
                {
                    object mp = mpProp.GetValue(entity);
                    if (mp != null)
                    {
                        var volProp = mp.GetType().GetProperty("Volume");
                        if (volProp != null)
                            return System.Convert.ToDouble(volProp.GetValue(mp), System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[AVISO] Reflection MassProperties falhou para {entity.GetType().Name} {entity.Handle}: {ex.Message}");
            }

            // Fallback: Body sem API direta de Volume — registra aviso, escreve 0
            docEditor.WriteMessage($"\n[AVISO] Volume não disponível via API para {entity.GetType().Name} {entity.Handle}. Escrevendo 0.");
            return 0.0;
        }

        // Escreve no PSet_C todos os campos calculáveis. Tolerante a campos inexistentes.
        // Aceita Solid3d ou Body. Volume é resolvido por ObterVolumeEntidade (que sabe lidar com ambos).
        public static void EscreverPsetCField(PropertySet pset, Autodesk.AutoCAD.DatabaseServices.Entity entity, double altura, Editor docEditor)
        {
            TrySetPsetField(pset, "Largura", width.ToString("F2"), docEditor);
            TrySetPsetField(pset, "Altura", altura.ToString("F2"), docEditor);
            TrySetPsetField(pset, "Inclinação", slope.ToString("F2"), docEditor);

            double volume = ObterVolumeEntidade(entity, docEditor);
            TrySetPsetField(pset, "Volume", volume.ToString("F2"), docEditor);

            TrySetPsetField(pset, "Comprimento", length.ToString("F2"), docEditor);

            double area = width * length;
            TrySetPsetField(pset, "Area", area.ToString("F2"), docEditor);
            TrySetPsetField(pset, "Área", area.ToString("F2"), docEditor, silentNotFound: true);
        }

        // Resolve o nome do PSet "Propriedades Físicas" unificado no desenho.
        // Devolve null se não achar.
        // 1º tenta 6 candidatos exatos (variações de acentuação e prefixo);
        // 2º busca tolerante por NormalizeCodeKey no NOD.
        // PSet único (unificado entre disciplinas) — todos os Pset_C* têm os mesmos campos.
        public static string ResolverNomePsetC(DictionaryPropertySetDefinitions dictionary, Transaction tr, Database db, Editor docEditor)
        {
            string[] candidatos = {
                "Pset_C - Propriedades Fisicas dos Objetos",          // canonical novo
                "Pset_C - Propriedades Físicas dos Objetos e Elementos",
                "Pset_C - Propriedades Fisicas dos Objetos e Elementos",
                "C - Propriedades Físicas dos Objetos e Elementos",
                "C - Propriedades Fisicas dos Objetos e Elementos",
                "Propriedades Físicas dos Objetos e Elementos",
                "Propriedades Fisicas dos Objetos e Elementos"
            };
            foreach (var c in candidatos)
                if (dictionary.Has(c, tr)) return c;

            // Busca tolerante via NOD → AEC_PROPERTY_SET_DEFS
            string achado = null;
            List<string> disponiveis = new List<string>();
            string alvoNorm = NormalizeCodeKey("Propriedades Fisicas");
            try
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (nod.Contains("AEC_PROPERTY_SET_DEFS"))
                {
                    DBDictionary psetDefsDict = (DBDictionary)tr.GetObject(nod.GetAt("AEC_PROPERTY_SET_DEFS"), OpenMode.ForRead);
                    foreach (DBDictionaryEntry entry in psetDefsDict)
                    {
                        var def = tr.GetObject((ObjectId)entry.Value, OpenMode.ForRead) as PropertySetDefinition;
                        if (def == null) continue;
                        disponiveis.Add(def.Name);
                        if (achado == null && NormalizeCodeKey(def.Name).Contains(alvoNorm))
                            achado = def.Name;
                    }
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO] ResolverNomePsetC: {ex.Message}");
            }

            if (achado != null)
            {
                docEditor.WriteMessage($"\n[INFO] PSet_C resolvido por busca tolerante: '{achado}'");
                return achado;
            }

            docEditor.WriteMessage($"\n[AVISO] PSet 'Propriedades Físicas' não encontrado. PSets disponíveis ({disponiveis.Count}):");
            foreach (var n in disponiveis)
                docEditor.WriteMessage($"\n  - '{n}'");
            return null;
        }
        // Lê um campo de um PSet NATIVO do Civil 3D anexado ao sólido (ex.: "Corridor Shape Information",
        // "Corridor Identity", "Corridor Model Information"). Retorna "" se o PSet ou o campo não existir,
        // para que o chamador decida como proceder. Não escreve nada — apenas lê.
        private static string LerPropriedadeNativa(
            Transaction tr,
            DictionaryPropertySetDefinitions dictionary,
            Autodesk.AutoCAD.DatabaseServices.Entity host,
            string propSetName,
            string propertyName,
            Editor docEditor)
        {
            try
            {
                if (!dictionary.Has(propSetName, tr))
                {
                    docEditor.WriteMessage($"\n[AVISO] PSet nativo '{propSetName}' não está no dicionário do desenho.");
                    return string.Empty;
                }

                ObjectId propSetId = dictionary.GetAt(propSetName);
                ObjectId psetId = PropertyDataServices.GetPropertySet(host, propSetId);
                if (psetId == ObjectId.Null)
                {
                    docEditor.WriteMessage($"\n[AVISO] PSet '{propSetName}' não está anexado a este sólido.");
                    return string.Empty;
                }

                PropertySet pset = (PropertySet)tr.GetObject(psetId, OpenMode.ForRead);
                int index = pset.PropertyNameToId(propertyName);
                object value;
                try { value = pset.GetAt(index, host); }
                catch { value = pset.GetAt(index); }
                return value?.ToString() ?? string.Empty;
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO] Falha ao ler '{propertyName}' de '{propSetName}': {ex.Message}");
                return string.Empty;
            }
        }

        // Helper: escreve um valor em um campo do PSet com tolerância a campo inexistente.
        // PropertyNameToId lança quando o nome não existe — capturamos e seguimos.
        // silentNotFound=true: não loga quando o campo não existir (útil ao testar variantes ex.: "Area"/"Área").
        public static void TrySetPsetField(PropertySet pset, string propertyName, string value, Editor docEditor, bool silentNotFound = false)
        {
            try
            {
                int idx = pset.PropertyNameToId(propertyName);
                pset.SetAt(idx, value);
            }
            catch (System.Exception ex)
            {
                if (!silentNotFound)
                    docEditor.WriteMessage($"\n[AVISO] Campo '{propertyName}' não escrito no PSet: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers de classificação por CodeName (portados de LOIN/legacy_PropertySets.cs)
        // Aceitam aliases case-insensitive com normalização Unicode (remove acentos,
        // troca espaços/hífens por '_'), permitindo bater tanto strings PT-BR
        // ("PAVIMENTO", "SUB_BASE", "GUIA") quanto códigos nativos Civil 3D
        // ("Pave1", "SubBase", "Curb") na mesma comparação.
        // ─────────────────────────────────────────────────────────────────────────

        public static bool CodeMatches(string codeName, params string[] patterns)
        {
            return ContainsAnyPattern(codeName, patterns);
        }

        public static bool ContainsAnyPattern(string candidate, params string[] patterns)
        {
            string normalizedCandidate = NormalizeCodeKey(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
                return false;

            foreach (string pattern in patterns)
            {
                string normalizedPattern = NormalizeCodeKey(pattern);
                if (!string.IsNullOrWhiteSpace(normalizedPattern) &&
                    normalizedCandidate.Contains(normalizedPattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public static string NormalizeCodeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(normalized.Length);
            bool lastWasSeparator = false;

            foreach (char ch in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToUpperInvariant(ch));
                    lastWasSeparator = false;
                    continue;
                }

                if (!lastWasSeparator)
                {
                    sb.Append('_');
                    lastWasSeparator = true;
                }
            }
            return sb.ToString().Trim('_');
        }

        public static void ParametrosGuid(string guid, string nomeCorredorSolido, string nomeSubAlvo, bool verbose, Editor docEditor)
        {
            Database db = Manager.DocData;
            CivilDocument docCivil = Manager.DocCivil;

            // Iniciar uma transação aninhada (independente da transação externa, pois só lê)
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {

                foreach (ObjectId id in docCivil.CorridorCollection)
                {
                    Corridor corridor = (Corridor)tr.GetObject(id, OpenMode.ForRead);

                    if(corridor.Name == nomeCorredorSolido)
                    {
                        foreach (Baseline baseLine in corridor.Baselines)
                        {

                            foreach (BaselineRegion region in baseLine.BaselineRegions)
                            {

                                if (region.Name.ToString().Contains(guid))
                                {
                                    // Comprimento da região = EndStation - StartStation (em unidades do desenho, geralmente m).
                                    length = region.EndStation - region.StartStation;
                                    if (verbose) docEditor.WriteMessage($"\n[LENGTH] region '{region.Name}': {region.EndStation:F3} - {region.StartStation:F3} = {length:F3}");

                                    AppliedAssembly assembly = baseLine.GetAppliedAssemblyAtStation(region.StartStation);
                                    foreach (AppliedSubassembly appliedSub in assembly.GetAppliedSubassemblies())
                                    {
                                        Autodesk.Civil.DatabaseServices.Subassembly sub = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(appliedSub.SubassemblyId, OpenMode.ForRead);

                                        // Fix B: filtrar pela subassembly cujo Name bate com o SubassemblyName
                                        // lido do PSet nativo "Corridor Identity". Sem isso, todas as subassemblies
                                        // da region são processadas e as variáveis estáticas ficam com a última
                                        // sobrescrita, não com a que originou o sólido selecionado.
                                        string subName = sub.Name ?? string.Empty;
                                        if (!string.IsNullOrWhiteSpace(nomeSubAlvo) &&
                                            !string.Equals(subName.Trim(), nomeSubAlvo.Trim(), StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (verbose) docEditor.WriteMessage($"\n[SKIP] Subassembly '{subName}' != alvo='{nomeSubAlvo}'");
                                            continue;
                                        }
                                        if (verbose) docEditor.WriteMessage($"\n[MATCH] Subassembly '{subName}' | classe: {sub.GeometryGenerator.MacroOrClassName}");

                                        if (sub.GeometryGenerator.MacroOrClassName.Contains("LaneSuperelevationAOR"))
                                        {

                                            foreach (var param in sub.ParamsDouble)
                                            {
                                                string paramName = param.Key;

                                                // Fix A: igualdade exata. Antes, Contains("BaseDepth") batia em
                                                // "SubBaseDepth" e poluía baseDepth com o valor de SubBase.
                                                if (paramName == "Width")
                                                {
                                                    width = param.Value;
                                                    if (verbose) docEditor.WriteMessage($"\n  Width = {width}");
                                                }
                                                else if (paramName == "Pave1Depth")
                                                {
                                                    pave1Depth = param.Value;
                                                    if (verbose) docEditor.WriteMessage($"\n  Pave1Depth = {pave1Depth}");
                                                }
                                                else if (paramName == "Pave2Depth")
                                                {
                                                    pave1Depth += param.Value;
                                                    if (verbose) docEditor.WriteMessage($"\n  Pave2Depth (acum em pave1Depth) = {pave1Depth}");
                                                }
                                                else if (paramName == "SubBaseDepth")
                                                {
                                                    subBaseDepth = param.Value;
                                                    if (verbose) docEditor.WriteMessage($"\n  SubBaseDepth = {subBaseDepth}");
                                                }
                                                else if (paramName == "BaseDepth")
                                                {
                                                    baseDepth = param.Value;
                                                    if (verbose) docEditor.WriteMessage($"\n  BaseDepth = {baseDepth}");
                                                }
                                                else if (paramName == "DefaultSlope")
                                                {
                                                    slope = param.Value;
                                                    if (verbose) docEditor.WriteMessage($"\n  DefaultSlope = {slope}");
                                                }

                                            }

                                        }
                                        else
                                        {
                                            if (sub.GeometryGenerator.MacroOrClassName.Contains("BasicLane"))
                                            {
                                                foreach (var param in sub.ParamsDouble)
                                                {
                                                    string paramName = param.Key;

                                                    if (paramName == "Width")
                                                    {
                                                        width = param.Value;
                                                    }
                                                    else if (paramName == "Depth")
                                                    {
                                                        pave1Depth = param.Value;
                                                    }
                                                    else if (paramName == "Slope" || paramName == "DefaultSlope")
                                                    {
                                                        slope = param.Value;
                                                    }

                                                }

                                            }
                                            else
                                            {

                                                if (sub.GeometryGenerator.MacroOrClassName.Contains("BasicCurb"))
                                                {
                                                    foreach (var param in sub.ParamsDouble)
                                                    {
                                                        string paramName = param.Key;

                                                        if (paramName == "Width")
                                                        {
                                                            width = param.Value;
                                                        }
                                                        else if (paramName == "Depth")
                                                        {
                                                            guiaDepth = param.Value;
                                                        }
                                                        else if (paramName == "Deflection" || paramName == "DefaultDeflection")
                                                        {
                                                            slope = param.Value;
                                                        }

                                                    }

                                                }

                                                else
                                                {

                                                    if (sub.GeometryGenerator.MacroOrClassName.Contains("ShoulderExtendAll"))
                                                    {
                                                        foreach (var param in sub.ParamsDouble)
                                                        {
                                                            string paramName = param.Key;

                                                            // Fix A: igualdade exata aqui também. Atenção: 'SubbaseDepth'
                                                            // (b minúsculo) é o nome real do parâmetro nessa subassembly.
                                                            if (paramName == "ShoulderWidth")
                                                            {
                                                                width = param.Value;
                                                            }
                                                            else if (paramName == "Pave1Depth")
                                                            {
                                                                pave1Depth = param.Value;
                                                            }
                                                            else if (paramName == "Pave2Depth")
                                                            {
                                                                pave1Depth += param.Value;
                                                            }
                                                            else if (paramName == "SubbaseDepth" || paramName == "SubBaseDepth")
                                                            {
                                                                subBaseDepth = param.Value;
                                                            }
                                                            else if (paramName == "BaseDepth")
                                                            {
                                                                baseDepth = param.Value;
                                                            }
                                                            else if (paramName == "ShoulderSlope")
                                                            {
                                                                slope = param.Value;
                                                            }

                                                        }


                                                    }
                                                    else
                                                    {
                                                        //docEditor.WriteMessage($"\nSub-assembly não encontrada: {sub.Name}");
                                                    }

                                                }
                                            }
                                        }
                                    }// Fim do foreach AppliedSubassembly

                                }
                                else
                                {
                                    //docEditor.WriteMessage($"\nA região com GUID {guid} já foi processada.");
                                }


                            }// Fim do foreach BaselineRegion

                        } // Fim do foreach Baseline




                    }
                    

                }// Fim do foreach Corridor



                tr.Commit();
            }
        }
      
    }
}
