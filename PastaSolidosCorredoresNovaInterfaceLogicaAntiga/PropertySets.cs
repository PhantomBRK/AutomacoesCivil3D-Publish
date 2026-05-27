using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AutomacoesCivil3D;
using DocumentFormat.OpenXml.Vml.Office;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Body = Autodesk.AutoCAD.DatabaseServices.Body;
using Color = Autodesk.AutoCAD.Colors.Color;
using Document = Autodesk.AutoCAD.ApplicationServices.Document;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    /// <summary>
    /// Classe refatorada para coletar parÃƒÂ¢metros de forma genÃƒÂ©rica de QUALQUER subassembly
    /// e alimentar os Property Sets conforme a lÃƒÂ³gica jÃƒÂ¡ existente.
    /// MantÃƒÂ©m as assinaturas de PSet22 e ParametrosGuid2.
    /// </summary>
    public class PropertySets
    {
        public readonly CodeNameMappingCatalog? _codeNameMappingCatalog;

        public PropertySets()
            : this(null)
        {
        }

        public PropertySets(CodeNameMappingCatalog? codeNameMappingCatalog)
        {
            _codeNameMappingCatalog = codeNameMappingCatalog;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers resilientes (introduzidos para corrigir os erros em loop de
        // EnsureLayer eInvalidInput e PSetBody eKeyNotFound).
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lê um campo de um PSet com tolerância: retorna "" se a chave não existir,
        /// em vez de jogar eKeyNotFound como PropertyNameToId cru faz.
        /// </summary>
        public static string TryReadPSetField(PropertySet ps, string propertyName, Entity host)
        {
            try
            {
                int idx = ps.PropertyNameToId(propertyName);
                object val;
                try { val = ps.GetAt(idx, host); }
                catch { val = ps.GetAt(idx); }
                return val?.ToString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Sanitiza um nome para uso como nome de Layer no AutoCAD.
        /// Substitui caracteres reservados ( &lt; &gt; / \ " : ; ? * | = ' ` , ) por '_',
        /// colapsa espaços múltiplos, faz trim, limita a 255 chars.
        /// Retorna null se o resultado for vazio (caller deve abortar criação do layer).
        /// </summary>
        public static string SanitizarNomeLayer(string nomeBruto)
        {
            if (string.IsNullOrWhiteSpace(nomeBruto)) return null;
            var sb = new System.Text.StringBuilder(nomeBruto.Length);
            foreach (char c in nomeBruto)
            {
                if ("<>/\\\":;?*|='`,".IndexOf(c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            string s = sb.ToString().Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            if (s.Length == 0) return null;
            if (s.Length > 255) s = s.Substring(0, 255);
            return s;
        }

        // Container por-sÃƒÂ³lido (evita estÃƒÂ¡ticos Ã¢â‚¬Å“vazandoÃ¢â‚¬Â entre sÃƒÂ³lidos)
        public class ExtractedValues
        {
            public double Width { get; set; }
            public double Pave1Depth { get; set; }
            public double Pave2Depth { get; set; }
            public double BaseDepth { get; set; }
            public double SubBaseDepth { get; set; }
            public double GuiaDepth { get; set; }
            public double PasseioDepth { get; set; }
            public double Height { get; set; } // para CFTDepth
            public double Slope { get; set; }  // em fraÃƒÂ§ÃƒÂ£o; no final converto para %
            public double LengthKm { get; set; } // jÃƒÂ¡ convertido para km
            public double Area { get; set; }
            public double TotalPavementDepth => Pave1Depth + Pave2Depth;

            public void RecomputeArea()
            {
                Area = Width * LengthKm; // mantÃƒÂ©m sua lÃƒÂ³gica atual
            }
        }

        public static string FormatarInclinacaoPsetC(double inclinacaoBruta)
        {
            if (double.IsNaN(inclinacaoBruta) || double.IsInfinity(inclinacaoBruta))
            {
                return string.Empty;
            }

            double inclinacaoPercentual = Math.Abs(inclinacaoBruta) <= 1.0
                ? inclinacaoBruta * 100.0
                : inclinacaoBruta;

            double inclinacaoArredondada = Math.Round(inclinacaoPercentual, MidpointRounding.AwayFromZero);
            return string.Format(CultureInfo.InvariantCulture, "{0:0}%", inclinacaoArredondada);
        }

        public void PSetBody(Body solid, Database db, Transaction tr, ISet<string>? allowedPropertySets = null)
        {
            Editor ed = Manager.DocEditor;
            Database docData = ed.Document.Database;
            string nomeCamada = "";
            string nomeSub = "";
            string nomeCorredor = "";
            string guid = "";
            string comprimentoStr = "";
            string nomeAssembly = "";

            // DicionÃƒÂ¡rios de PSets
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            const string PSET_F = "Corridor Shape Information";

            var values = new ExtractedValues();

            try
            {


                // --- Ler PSet F (mantendo sua lÃƒÂ³gica) ---
             
                PropertySet? psF = GetOrCreatePropertySet(solid, dictionary, tr, PSET_F);


                if (psF != null)
                {
                    //NOME ASSEMBLY NO LAYER — campo 'AssemblyName' está no PSet "Corridor Shape Information"
                    nomeAssembly = LimparPrefixoContador(TryReadPSetField(psF, "AssemblyName", solid));
                }

                // Fix: CorridorName NÃO está em "Corridor Shape Information" — está em "Corridor Model Information".
                // Antes: psF.PropertyNameToId("CorridorName") jogava eKeyNotFound no loop.
                PropertySet? psModel = GetOrCreatePropertySet(solid, dictionary, tr, "Corridor Model Information");
                if (psModel != null)
                {
                    nomeCorredor = LimparPrefixoContador(TryReadPSetField(psModel, "CorridorName", solid));
                }

                /*||||||||||||||||EXPORTAÃƒâ€¡ÃƒÆ’O IFC E LAYERS IFC||||||||||||||||*/
                ApplyToEntity(solid, nomeCorredor, db, tr);
                //ApplyToEntity(solid, nomeAssembly, db, tr);



            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na classe PropertySets3 (PSet22): {ex.Message}");
                ed.WriteMessage($"\nDetalhes: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// MantÃƒÂ©m a assinatura original. 
        /// LÃƒÂª dados do PSet B, chama coleta genÃƒÂ©rica por GUID, e preenche PSet C conforme CodeName.
        /// </summary>
        public void PSetSolid(Solid3d solid, Database db, Transaction tr, ISet<string>? allowedPropertySets = null)
        {
            Editor ed = Manager.DocEditor;
            Document document = Manager.DocCad;
            Database docData = Manager.DocData;

            string nomeCamada = "";
            string nomeSub = "";
            string nomeCorredor = "";
            string guid = "";
            string comprimentoStr = "";
            string nomeAssembly = "";

            // DicionÃƒÂ¡rios de PSets
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            const string PSET_B = "Pset_B - Informações dos Objetos e Elementos";
            const string PSET_C = "Pset_C - Propriedades Físicas dos Objetos e Elementos";
            const string PSET_F = "CorridorShapeInformation";

            var values = new ExtractedValues();
            string categoriaCodigo = nomeCamada;
            bool possuiCategoriaMapeada = false;

            try
            {
                // --- Ler PSet B (mantendo sua lÃƒÂ³gica) ---
                PropertySet? psB = GetOrCreatePropertySet(
                    solid,
                    dictionary,
                    tr,
                    PSET_B,
                    "Pset_B - Informações dos Objetos e Elementos");

                if (psB != null)
                {
                    int idxCodeName = psB.PropertyNameToId("CodeName");
                    int idxSubName = psB.PropertyNameToId("SubassemblyName");
                    int idxAssName = psB.PropertyNameToId("SubassemblyName");
                    int idxCorridorName = psB.PropertyNameToId("NomeCorredorSolido");
                    int idxRegionGUID = psB.PropertyNameToId("RegionName");
                    int idxComprimento = psB.PropertyNameToId("Comprimento");

                    nomeCamada = psB.GetAt(idxCodeName, solid).ToString();
                    nomeSub = psB.GetAt(idxSubName, solid).ToString();
                    //nomeAssembly = psB.GetAt(idxAssName, solid).ToString();
                    nomeCorredor = psB.GetAt(idxCorridorName, solid).ToString();
                    guid = psB.GetAt(idxRegionGUID, solid).ToString();
                    comprimentoStr = psB.GetAt(idxComprimento, solid).ToString();

                    DefinirLayerPorCorredor(solid, docData, tr, nomeCamada);

                    if (double.TryParse(comprimentoStr, out double tempLen))
                        values.LengthKm = tempLen / 100.0; // manter conversÃƒÂ£o que vocÃƒÂª jÃƒÂ¡ usa
                    else
                        values.LengthKm = 0.0;

                    possuiCategoriaMapeada = TryResolveMappedCodeCategory(nomeCamada, out categoriaCodigo);

                    // Coleta genÃƒÂ©rica de parÃƒÂ¢metros da subassembly (por GUID / corredor)
                    ColetarParametrosPorGuidGenerico(guid, nomeCorredor, nomeCamada, nomeSub, categoriaCodigo, db, tr, values);
                }




                // --- Preencher PSet C conforme sua lÃƒÂ³gica existente ---
                PropertySet? psC = GetOrCreatePropertySet(
                    solid,
                    dictionary,
                    tr,
                    PSET_C,
                    "Pset_C - Propriedades Fisicas dos Objetos e Elementos");
                if (psC != null)
                {
                    string slopePercent = FormatarInclinacaoPsetC(values.Slope);
                    values.RecomputeArea();

                    void SetStr(string prop, string val)
                    {
                        try { psC.SetAt(psC.PropertyNameToId(prop), val); }
                        catch
                        { /* evita travar */
                        }
                    }

                    void SetSlope()
                    {
                        SetStr("Declividade da Pista", slopePercent);
                        SetStr("Inclinação", slopePercent);
                        SetStr("Inclinacao", slopePercent);
                    }

                    void SetArea()
                    {
                        string area = values.Area.ToString("F2");
                        SetStr("Área", area);
                        SetStr("Area", area);
                    }

                    double GetBestAvailableHeight()
                    {
                        if (values.TotalPavementDepth > 0.0) return values.TotalPavementDepth;
                        if (values.BaseDepth > 0.0) return values.BaseDepth;
                        if (values.SubBaseDepth > 0.0) return values.SubBaseDepth;
                        if (values.Height > 0.0) return values.Height;
                        if (values.PasseioDepth > 0.0) return values.PasseioDepth;
                        if (values.GuiaDepth > 0.0) return values.GuiaDepth;
                        return 0.0;
                    }

                    void SetCommonPhysicalValues(double height)
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", height.ToString("F2"));
                        SetSlope();
                        SetArea();
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    bool psetCPreenchido = false;

                    // Regras idÃƒÂªnticas ao que vocÃƒÂª faz hoje (CodeName contÃƒÂ©m ...)
                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "PAVIMENTO", "PAVIMENTO1", "PAVIMENTO2", "CBUQ", "CBUQ - CAP",
                        "pave1", "pave2", "Pave2", "Pave1", "CONCRETO ARMADO FCK = 30 MPA", "BLOCOS INTERTRAVADOS", "TSD", "TSS", "TST"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.TotalPavementDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "PASSEIO", "TrilhoLD", "TrilhoLE"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.PasseioDepth > 0.0 ? values.PasseioDepth : values.TotalPavementDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "BASE", "IMPRIMAÇÃO DE BASE", "BASE DE BRITA GRADUADA", "Base") &&
                        !CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "SUB_BASE", "SUBBASE", "SubBase", "Pintura 1", "Pintura 2",
                        "BASE DE BRITA GRADUADA",  "SUB BASE/COLCHÃO DRENANTE", "IMPRIMAÇÃO DE BASE", "PINTURA DE LIGAÇÃO", "ACOSTAMENTO_BASE", "ACOSTAMENTO_PAVIMENTO", 
                        "BARREIRA-CONCRETO_MAGRO", "ACOSTAMENTO_REFORÇO_SUB_LEITO"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.BaseDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "SUB_BASE", "SUBBASE", "SUB BASE", "SUBBASE_2", "SUBBASE_3",
                        "SUB BASE/COLCHÃO DRENANTE", "SUB BASE /COLCHÃO DRENANTE", "FERROVIA", "LEITO", "Lastro", "SUBLEITO", "SUB_BASE", "SoilFill", "SoloReforço", ""))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.SubBaseDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "GUIA", "Rip Rap"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.GuiaDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "CFT", "REGULARIZAÇÃO E COMPACTAÇÃO DE SUBLEITO", 
                        "PINTURA DE LIGAÇÃO", "REFORÇO DO SUBLEITO"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.Height);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "TOP"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.GuiaDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "OFFSET_TALUDE"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.GuiaDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "TALUDE_ATERRO"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.GuiaDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "TALUDE_CORTE"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.GuiaDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "BARREIRA"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.GuiaDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "PONTE"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(values.GuiaDepth);
                    }

                    if (CodeMatches(nomeCamada, categoriaCodigo, possuiCategoriaMapeada, "ACOSTAMENTO_PAVIMENTO"))
                    {
                        psetCPreenchido = true;
                        SetCommonPhysicalValues(GetBestAvailableHeight());
                    }

                    if (!psetCPreenchido)
                    {
                        SetCommonPhysicalValues(GetBestAvailableHeight());
                    }
                }

                // --- Ler PSet F (mantendo sua lÃƒÂ³gica) ---
                PropertySet? psF = GetOrCreatePropertySet(solid, dictionary, tr, PSET_F);
                if (psF != null)
                {
                    int idxAssName = psF.PropertyNameToId("AssemblyName");
                    nomeAssembly = psF.GetAt(idxAssName, solid).ToString();
                    LimparPrefixoContador(nomeAssembly);
                    //NOME DO CORREDOR NO LAYER
                    int idxCorrName = psF.PropertyNameToId("CorridorName");
                    nomeCorredor = psF.GetAt(idxCorrName, solid).ToString();
                    nomeCorredor = LimparPrefixoContador(nomeCorredor);

                }

                /*||||||||||||||||EXPORTAÃƒâ€¡ÃƒÆ’O IFC E LAYERS IFC||||||||||||||||*/
                ApplyToEntity(solid, nomeCorredor, db, tr);
                //ApplyToEntity(solid, nomeAssembly, db, tr);


            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na classe PropertySets3 (PSet22): {ex.Message}");
                ed.WriteMessage($"\nDetalhes: {ex.StackTrace}");
            }
        }

        public static void DefinirLayerPorCorredor(
            Entity ent,
            Database db,
            Transaction tr,
            string layerBaseName)
        {
            if (ent == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(layerBaseName))
            {
                return;
            }

            LayerTable layerTable =
                (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!layerTable.Has(layerBaseName))
            {
                layerTable.UpgradeOpen();

                LayerTableRecord layerRecord = new LayerTableRecord();
                layerRecord.Name = layerBaseName;

                ObjectId layerId = layerTable.Add(layerRecord);
                tr.AddNewlyCreatedDBObject(layerRecord, true);
            }

            ent.Layer = layerBaseName;
        }

        public string LimparPrefixoContador(string s)
        {
            // remove " (n)" ou "(n)" do inÃƒÂ­cio, repetidos ou nÃƒÂ£o, e espaÃƒÂ§os
            string limpo = Regex.Replace(s, @"^(?:\s*\(\d+\)\s*)+", "");
            return limpo.TrimStart();
        }

        /// <summary>
        /// MantÃƒÂ©m assinatura original: busca o Region por GUID no corredor e extrai parÃƒÂ¢metros
        /// de forma genÃƒÂ©rica (funciona para QUALQUER subassembly).
        /// </summary>
        public static void ParametrosGuid2(string guid, string nomeCorredorSolido, Database db, Transaction tr)
        {
            // Mantido por compatibilidade, mas agora a extraÃƒÂ§ÃƒÂ£o genÃƒÂ©rica ÃƒÂ© feita por ColetarParametrosPorGuidGenerico
            // Este mÃƒÂ©todo segue existindo para nÃƒÂ£o quebrar chamadas externas (se houver).
        }

        // ============================================================
        // ===========   IMPLEMENTAÃƒâ€¡ÃƒÆ’O GENÃƒâ€°RICA: CORE   ===============
        // ============================================================

        public static void ColetarParametrosPorGuidGenerico(
            string guid,
            string nomeCorredorSolido,
            string codeName,
            string subassemblyName,
            string categoriaCodigo,
            Database db,
            Transaction tr,
            ExtractedValues values)
        {
            Editor ed = Manager.DocEditor;
            CivilDocument docCivil = Manager.DocCivil;
            string categoriaEfetiva = ResolveCategoryForExtraction(codeName, categoriaCodigo);

            try
            {
                foreach (ObjectId id in docCivil.CorridorCollection)
                {
                    Corridor corridor = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                    if (!corridor.Name.Contains(nomeCorredorSolido, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (Baseline bl in corridor.Baselines)
                    {
                        foreach (BaselineRegion region in bl.BaselineRegions)
                        {
                            if (!region.RegionGUID.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase) &&
                                !region.RegionGUID.ToString().Contains(guid, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            AppliedAssembly assembly = bl.GetAppliedAssemblyAtStation(region.StartStation);
                            List<(Autodesk.Civil.DatabaseServices.Subassembly Subassembly, int Score)> candidates =
                                new List<(Autodesk.Civil.DatabaseServices.Subassembly Subassembly, int Score)>();

                            foreach (AppliedSubassembly appliedSub in assembly.GetAppliedSubassemblies())
                            {
                                Autodesk.Civil.DatabaseServices.Subassembly sub =
                                    (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(appliedSub.SubassemblyId, OpenMode.ForRead);
                                int score = ScoreSubassembly(sub, codeName, subassemblyName, categoriaEfetiva);
                                candidates.Add((sub, score));
                            }

                            candidates.Sort((left, right) =>
                            {
                                int scoreCompare = right.Score.CompareTo(left.Score);
                                if (scoreCompare != 0)
                                {
                                    return scoreCompare;
                                }

                                return string.Compare(left.Subassembly.Name, right.Subassembly.Name, StringComparison.OrdinalIgnoreCase);
                            });

                            foreach ((Autodesk.Civil.DatabaseServices.Subassembly Subassembly, int Score) candidate in candidates)
                            {
                                MapSubassemblyParameters(values, candidate.Subassembly, overwriteExisting: false);

                                if (HasEnoughValuesForCategory(values, categoriaEfetiva))
                                {
                                    break;
                                }
                            }

                            return;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na coleta genérica por GUID: {ex.Message}");
            }
        }

        public static void ColetarParametrosPorGuidGenerico(
            string guid,
            string nomeCorredorSolido,
            Database db,
            Transaction tr,
            ExtractedValues values)
        {
            ColetarParametrosPorGuidGenerico(guid, nomeCorredorSolido, string.Empty, string.Empty, string.Empty, db, tr, values);
        }

        public static string ResolveCategoryForExtraction(string codeName, string categoriaCodigo)
        {
            return string.IsNullOrWhiteSpace(categoriaCodigo)
                ? codeName
                : categoriaCodigo;
        }

        public static int ScoreSubassembly(
            Autodesk.Civil.DatabaseServices.Subassembly sub,
            string codeName,
            string subassemblyName,
            string categoriaCodigo)
        {
            int score = 0;
            string normalizedSubName = NormalizeCodeKey(sub.Name);
            string normalizedRequestedSubName = NormalizeCodeKey(subassemblyName);
            string normalizedCodeName = NormalizeCodeKey(codeName);
            string normalizedMacroName = NormalizeCodeKey(sub.GeometryGenerator?.MacroOrClassName);

            if (!string.IsNullOrWhiteSpace(normalizedRequestedSubName))
            {
                if (string.Equals(normalizedSubName, normalizedRequestedSubName, StringComparison.Ordinal))
                {
                    score += 100;
                }
                else if (normalizedSubName.Contains(normalizedRequestedSubName, StringComparison.Ordinal) ||
                         normalizedRequestedSubName.Contains(normalizedSubName, StringComparison.Ordinal))
                {
                    score += 75;
                }
            }

            if (!string.IsNullOrWhiteSpace(normalizedCodeName) &&
                (normalizedSubName.Contains(normalizedCodeName, StringComparison.Ordinal) ||
                 normalizedCodeName.Contains(normalizedSubName, StringComparison.Ordinal)))
            {
                score += 20;
            }

            if (MacroMatchesCategory(normalizedMacroName, categoriaCodigo))
            {
                score += 35;
            }

            score += GetParameterSignatureScore(sub, categoriaCodigo);
            return score;
        }

        public static bool MacroMatchesCategory(string normalizedMacroName, string categoriaCodigo)
        {
            string categoriaNormalizada = NormalizeCodeKey(categoriaCodigo);

            switch (categoriaNormalizada)
            {
                case "PAVIMENTO":
                case "BASE":
                case "SUB_BASE":
                case "CFT":
                case "PASSEIO":
                case "ACOSTAMENTO_PAVIMENTO":
                    return normalizedMacroName.Contains("LANE", StringComparison.Ordinal) ||
                           normalizedMacroName.Contains("SHOULDER", StringComparison.Ordinal);
                case "GUIA":
                    return normalizedMacroName.Contains("CURB", StringComparison.Ordinal);
                case "OFFSET_TALUDE":
                case "TALUDE_ATERRO":
                case "TALUDE_CORTE":
                    return normalizedMacroName.Contains("DAYLIGHT", StringComparison.Ordinal) ||
                           normalizedMacroName.Contains("SLOPE", StringComparison.Ordinal) ||
                           normalizedMacroName.Contains("LINK", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        public static int GetParameterSignatureScore(
            Autodesk.Civil.DatabaseServices.Subassembly sub,
            string categoriaCodigo)
        {
            bool hasWidth = false;
            bool hasPaveDepth = false;
            bool hasBaseDepth = false;
            bool hasSubBaseDepth = false;
            bool hasDepth = false;
            bool hasSidewalkDepth = false;
            bool hasCftDepth = false;
            bool hasSlope = false;

            foreach (var kv in sub.ParamsDouble)
            {
                string normalizedParamName = NormalizeCodeKey(kv.Key);
                if (string.IsNullOrWhiteSpace(normalizedParamName))
                {
                    continue;
                }

                if (normalizedParamName == "WIDTH" || normalizedParamName == "LANEWIDTH" || normalizedParamName == "SHOULDERWIDTH" || normalizedParamName == "LARGURA")
                {
                    hasWidth = true;
                }

                if (normalizedParamName == "PAVE1DEPTH" || normalizedParamName == "PAVE2DEPTH" || normalizedParamName == "ESPESSURA_1_CAMADA_PAVIMENTO" || normalizedParamName == "ESPESSURA_2_CAMADA_PAVIMENTO")
                {
                    hasPaveDepth = true;
                }

                if (normalizedParamName.Contains("BASEDEPTH", StringComparison.Ordinal) || normalizedParamName.Contains("ESPESSURA_BASE", StringComparison.Ordinal))
                {
                    hasBaseDepth = true;
                }

                if (normalizedParamName.Contains("SUBBASEDEPTH", StringComparison.Ordinal) || normalizedParamName.Contains("ESPESSURA_SUB_BASE", StringComparison.Ordinal))
                {
                    hasSubBaseDepth = true;
                }

                if (normalizedParamName == "DEPTH")
                {
                    hasDepth = true;
                }

                if (normalizedParamName == "SIDEWALKDEPTH")
                {
                    hasSidewalkDepth = true;
                }

                if (normalizedParamName.Contains("CFTDEPTH", StringComparison.Ordinal) || normalizedParamName.Contains("REFORCO_SUBLEITO", StringComparison.Ordinal))
                {
                    hasCftDepth = true;
                }

                if (normalizedParamName.Contains("SLOPE", StringComparison.Ordinal) ||
                    normalizedParamName.Contains("DEFLECTION", StringComparison.Ordinal) ||
                    normalizedParamName.Contains("INCLINACAO", StringComparison.Ordinal))
                {
                    hasSlope = true;
                }
            }

            int score = 0;
            if (hasWidth)
            {
                score += 10;
            }

            if (hasSlope)
            {
                score += 5;
            }

            switch (NormalizeCodeKey(categoriaCodigo))
            {
                case "PAVIMENTO":
                    if (hasPaveDepth || hasDepth)
                    {
                        score += 35;
                    }
                    break;
                case "BASE":
                    if (hasBaseDepth)
                    {
                        score += 35;
                    }
                    break;
                case "SUB_BASE":
                    if (hasSubBaseDepth)
                    {
                        score += 35;
                    }
                    break;
                case "GUIA":
                    if (hasDepth)
                    {
                        score += 35;
                    }
                    break;
                case "PASSEIO":
                    if (hasSidewalkDepth || hasDepth)
                    {
                        score += 35;
                    }
                    break;
                case "CFT":
                    if (hasCftDepth)
                    {
                        score += 35;
                    }
                    break;
            }

            return score;
        }

        public static bool HasEnoughValuesForCategory(ExtractedValues values, string categoriaCodigo)
        {
            switch (NormalizeCodeKey(categoriaCodigo))
            {
                case "PAVIMENTO":
                    return values.Width > 0.0 && values.TotalPavementDepth > 0.0;
                case "BASE":
                    return values.Width > 0.0 && values.BaseDepth > 0.0;
                case "SUB_BASE":
                    return values.Width > 0.0 && values.SubBaseDepth > 0.0;
                case "GUIA":
                    return values.Width > 0.0 && values.GuiaDepth > 0.0;
                case "PASSEIO":
                    return values.Width > 0.0 && (values.PasseioDepth > 0.0 || values.TotalPavementDepth > 0.0);
                case "CFT":
                    return values.Width > 0.0 && values.Height > 0.0;
                default:
                    return values.Width > 0.0;
            }
        }

        public static void MapSubassemblyParameters(
            ExtractedValues values,
            Autodesk.Civil.DatabaseServices.Subassembly sub,
            bool overwriteExisting)
        {
            foreach (var kv in sub.ParamsDouble)
            {
                MapDouble(values, kv.Key, kv.Value, overwriteExisting);
            }

            foreach (var kv in sub.ParamsLong)
            {
                MapLong(values, kv.Key, kv.Value);
            }
        }

        // ---------- Mapeamento genÃƒÂ©rico de nomes -> valores usados na sua lÃƒÂ³gica ----------
        public static void MapDouble(ExtractedValues v, string paramName, double val, bool overwriteExisting = true)
        {
            if (EqualsAny(paramName, "Width", "LaneWidth", "ShoulderWidth", "LARGURA") &&
                (overwriteExisting || IsUnset(v.Width)))
            {
                v.Width = val;
            }

            if (EqualsAny(paramName, "Pave1Depth", "ESPESSURA 1 CAMADA PAVIMENTO") &&
                (overwriteExisting || IsUnset(v.Pave1Depth)))
            {
                v.Pave1Depth = val;
            }

            if (EqualsAny(paramName, "Pave2Depth", "ESPESSURA 2 CAMADA PAVIMENTO") &&
                (overwriteExisting || IsUnset(v.Pave2Depth)))
            {
                v.Pave2Depth = val;
            }

            if (ContainsAny(paramName, "BaseDepth", "ESPESSURA BASE") &&
                (overwriteExisting || IsUnset(v.BaseDepth)))
            {
                v.BaseDepth = val;
            }

            if (ContainsAny(paramName, "SubBaseDepth", "SubbaseDepth", "ESPESSURA SUB BASE") &&
                (overwriteExisting || IsUnset(v.SubBaseDepth)))
            {
                v.SubBaseDepth = val;
            }

            if (EqualsAny(paramName, "Depth") &&
                (overwriteExisting || IsUnset(v.GuiaDepth)))
            {
                v.GuiaDepth = val;
            }

            if (EqualsAny(paramName, "SidewalkDepth") &&
                (overwriteExisting || IsUnset(v.PasseioDepth)))
            {
                v.PasseioDepth = val;
            }

            if (ContainsAny(paramName, "CFTDepth", "ESPESSURA REFORÇO SUBLEITO") &&
                (overwriteExisting || IsUnset(v.Height)))
            {
                v.Height = val;
            }

            if (ContainsAny(paramName, "DefaultSlope", "Slope", "ShoulderSlope", "Deflection", "LinkSlope", "INCLINAÇÃO") &&
                (overwriteExisting || IsUnset(v.Slope)))
            {
                v.Slope = val;
            }

            v.RecomputeArea();
        }

        public static bool IsUnset(double value)
        {
            return Math.Abs(value) < 0.000001d;
        }

        public static void MapLong(ExtractedValues v, string paramName, long val)
        {
            // Por enquanto, nÃƒÂ£o altera largura/altura/slope/ÃƒÂ¡rea.
            // Se precisar, mapeie enums (Side etc.).
        }

        // Helpers de comparaÃƒÂ§ÃƒÂ£o:
        public static bool EqualsAny(string input, params string[] opts)
        {
            foreach (var o in opts)
                if (input.Equals(o, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static bool ContainsAny(string input, params string[] opts)
        {
            foreach (var o in opts)
                if (input.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }



        public static readonly Dictionary<string, short> LayerColors = new Dictionary<string, short>
        {
            { "DREN_TUBO_IFC",        3 },   // Verde
            { "DREN_BUEIRO_IFC",      130 },
            { "DREN_CONEXAO_IFC",     6 },   // Magenta
            { "DREN_ABERTA_IFC",      1 },   // Vermelho
            { "DREN_ESTRUTURA_IFC",   5 },   // Azul
            { "IFC_PROXY",            8 }    // Cinza
        };



        public static void ApplyToEntity(Entity entity, string corridorName, Database db, Transaction tr)
        {
            Document docCad = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            // garante layers prontos
            EnsureLayer(db, tr, corridorName, (short)7);


            //string ifcClass = "IfcBuildingElementProxy";
            //string predef = "USERDEFINED";
            string tgtLayer = $"PAV_{corridorName}";

            string key = (corridorName ?? "").ToUpperInvariant();


            // aplica layer
            SetEntityLayer(entity, db, tr, tgtLayer);

        }

        // Helpers
        public static void EnsureLayer(Database db, Transaction tr, string layerName, short aci)
        {
            // Fix: sanitiza o nome — antes, corridorName cru podia disparar eInvalidInput
            // em loop quando vinha vazio ou com caractere reservado (< > / \ " : ; ? * | = ' ` ,).
            string nomeLimpo = SanitizarNomeLayer(layerName);
            if (nomeLimpo == null) return; // nome inválido — ignora silenciosamente

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nomeLimpo)) return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = nomeLimpo;
            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            ObjectId newId = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        public static void SetEntityLayer(Entity ent, Database db, Transaction tr, string layerName)
        {
            string nomeLimpo = SanitizarNomeLayer(layerName);
            if (nomeLimpo == null) return; // sem nome válido, não muda layer

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(nomeLimpo))
                EnsureLayer(db, tr, nomeLimpo, (short)7);

            ent.UpgradeOpen();
            ent.Layer = nomeLimpo;
        }

        public bool TryResolveMappedCodeCategory(string rawCodeName, out string category)
        {
            if (_codeNameMappingCatalog != null &&
                _codeNameMappingCatalog.TryGetMappedCategory(rawCodeName, out category))
            {
                return true;
            }

            if (CodeNameMappingCatalog.TryResolveBuiltInCategory(rawCodeName, out category))
            {
                return true;
            }

            category = rawCodeName;
            return false;
        }

        public static bool CodeMatches(string rawCodeName, string mappedCategory, bool hasMappedCategory, params string[] patterns)
        {
            if (hasMappedCategory)
            {
                return ContainsAnyPattern(mappedCategory, patterns);
            }

            return ContainsAnyPattern(rawCodeName, patterns);
        }

        public static bool ContainsAnyPattern(string candidate, params string[] patterns)
        {
            string normalizedCandidate = NormalizeCodeKey(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return false;
            }

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

        public static string NormalizeCodeKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(normalized.Length);
            bool lastWasSeparator = false;

            foreach (char ch in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

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

        public static PropertySet? GetOrCreatePropertySet(
            Entity entity,
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr,
            params string[] definitionNames)
        {
            ObjectId definitionId = TryGetPropertySetDefinitionId(dictionary, tr, definitionNames);
            if (definitionId == ObjectId.Null || !definitionId.IsValid)
            {
                return null;
            }

            ObjectId propertySetId = ObjectId.Null;

            try
            {
                propertySetId = PropertyDataServices.GetPropertySet(entity, definitionId);
            }
            catch
            {
            }

            if (propertySetId == ObjectId.Null || !propertySetId.IsValid)
            {
                try
                {
                    PropertyDataServices.AddPropertySet(entity, definitionId);
                    propertySetId = PropertyDataServices.GetPropertySet(entity, definitionId);
                }
                catch (System.Exception ex)
                {
                    Manager.DocEditor.WriteMessage($"\nFalha ao anexar Property Set ao sÃ³lido: {ex.Message}");
                    return null;
                }
            }

            if (propertySetId == ObjectId.Null || !propertySetId.IsValid)
            {
                return null;
            }

            return tr.GetObject(propertySetId, OpenMode.ForWrite, false) as PropertySet;
        }

        public static ObjectId TryGetPropertySetDefinitionId(
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr,
            params string[] definitionNames)
        {
            Autodesk.Aec.DatabaseServices.Dictionary? rawDictionary = dictionary as Autodesk.Aec.DatabaseServices.Dictionary;

            foreach (string definitionName in definitionNames)
            {
                if (string.IsNullOrWhiteSpace(definitionName))
                {
                    continue;
                }

                try
                {
                    if (dictionary.Has(definitionName, tr))
                    {
                        return dictionary.GetAt(definitionName);
                    }
                }
                catch
                {
                }

                try
                {
                    if (rawDictionary != null && rawDictionary.Has(definitionName, tr))
                    {
                        return rawDictionary.GetAt(definitionName);
                    }
                }
                catch
                {
                }
            }

            return ObjectId.Null;
        }

        /*||||||||||||||||EXPORTAÃƒâ€¡ÃƒÆ’O IFC E LAYERS IFC||||||||||||||||

        public static readonly Dictionary<string, short> LayerColors = new Dictionary<string, short>
        {
            { "DREN_TUBO_IFC",        3 },   // Verde
            { "DREN_BUEIRO_IFC",      130 },
            { "DREN_CONEXAO_IFC",     6 },   // Magenta
            { "DREN_ABERTA_IFC",      1 },   // Vermelho
            { "DREN_ESTRUTURA_IFC",   5 },   // Azul
            { "IFC_PROXY",            8 }    // Cinza
        };

        // 1) Cria TODOS os layers IFC padronizados (chamar 1x por transaÃƒÂ§ÃƒÂ£o)
        public static void EnsureIfcLayers(Database db, Transaction tr)
        {
            // Sempre inclui PROXY
            HashSet<string> set = new HashSet<string>(LayerColors.Keys);
            foreach (KeyValuePair<string, (string IfcClass, string PreType, string Layer)> kv in Rules)
                set.Add(kv.Value.Layer);

            foreach (string layerName in set)
                EnsureLayer(db, tr, layerName, LayerColors.ContainsKey(layerName) ? LayerColors[layerName] : (short)7);
        }

        public static void ApplyToEntity(Entity entity, string codeName, Database db, Transaction tr)
        {
            Document docCad = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            // garante layers prontos
            EnsureIfcLayers(db, tr);


            string ifcClass = "IfcBuildingElementProxy";
            string predef = "USERDEFINED";
            string tgtLayer = "IFC_PROXY";

            string key = (codeName ?? "").ToUpperInvariant();
            foreach (KeyValuePair<string, (string IfcClass, string PreType, string Layer)> kv in Rules)
            {
                if (key.Contains(kv.Key))
                {
                    ifcClass = kv.Value.IfcClass;
                    predef = kv.Value.PreType;
                    tgtLayer = kv.Value.Layer;
                    break;
            }
        }

        public static PropertySet? GetOrCreatePropertySet(
            Entity entity,
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr,
            params string[] definitionNames)
        {
            ObjectId definitionId = TryGetPropertySetDefinitionId(dictionary, tr, definitionNames);
            if (definitionId == ObjectId.Null || !definitionId.IsValid)
            {
                return null;
            }

            ObjectId propertySetId = ObjectId.Null;

            try
            {
                propertySetId = PropertyDataServices.GetPropertySet(entity, definitionId);
            }
            catch
            {
            }

            if (propertySetId == ObjectId.Null || !propertySetId.IsValid)
            {
                try
                {
                    PropertyDataServices.AddPropertySet(entity, definitionId);
                    propertySetId = PropertyDataServices.GetPropertySet(entity, definitionId);
                }
                catch (System.Exception ex)
                {
                    Manager.DocEditor.WriteMessage($"\nFalha ao anexar Property Set ao sÃ³lido: {ex.Message}");
                    return null;
                }
            }

            if (propertySetId == ObjectId.Null || !propertySetId.IsValid)
            {
                return null;
            }

            return tr.GetObject(propertySetId, OpenMode.ForWrite, false) as PropertySet;
        }

        public static ObjectId TryGetPropertySetDefinitionId(
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr,
            params string[] definitionNames)
        {
            Autodesk.Aec.DatabaseServices.Dictionary? rawDictionary = dictionary as Autodesk.Aec.DatabaseServices.Dictionary;

            foreach (string definitionName in definitionNames)
            {
                if (string.IsNullOrWhiteSpace(definitionName))
                {
                    continue;
                }

                try
                {
                    if (dictionary.Has(definitionName, tr))
                    {
                        return dictionary.GetAt(definitionName);
                    }
                }
                catch
                {
                }

                try
                {
                    if (rawDictionary != null && rawDictionary.Has(definitionName, tr))
                    {
                        return rawDictionary.GetAt(definitionName);
                    }
                }
                catch
                {
                }
            }

            return ObjectId.Null;
        }

            // aplica layer
            SetEntityLayer(entity, db, tr, tgtLayer);
            // injeta PSET IFC:: se existir no DWG
            string psetIfc = "IfcObject Properties";
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);
            PropertySetDefinition novo = new PropertySetDefinition();

            if (dict.Has(psetIfc, tr))
            {
                ObjectId defId = dict.GetAt(psetIfc);
                PropertyDataServices.AddPropertySet(entity, defId);
                PropertySet pset = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(entity, defId), OpenMode.ForWrite);


                int idExportAs = pset.PropertyNameToId("IFC::IfcExportAs");
                if (idExportAs != -1) pset.SetAt(idExportAs, ifcClass);

                int idPreType = pset.PropertyNameToId("IFC::PredefinedType");
                if (idPreType != -1) pset.SetAt(idPreType, predef);
            }

        }

        // Helpers
        public static void EnsureLayer(Database db, Transaction tr, string layerName, short aci)
        {
            // Fix: sanitiza o nome — antes, corridorName cru podia disparar eInvalidInput
            // em loop quando vinha vazio ou com caractere reservado (< > / \ " : ; ? * | = ' ` ,).
            string nomeLimpo = SanitizarNomeLayer(layerName);
            if (nomeLimpo == null) return; // nome inválido — ignora silenciosamente

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nomeLimpo)) return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = nomeLimpo;
            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            ObjectId newId = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        public static void SetEntityLayer(Entity ent, Database db, Transaction tr, string layerName)
        {
            string nomeLimpo = SanitizarNomeLayer(layerName);
            if (nomeLimpo == null) return; // sem nome válido, não muda layer

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(nomeLimpo))
                EnsureLayer(db, tr, nomeLimpo, (short)7);

            ent.UpgradeOpen();
            ent.Layer = nomeLimpo;
        }


        public static readonly Dictionary<string, (string IfcClass, string PreType, string Layer)> Rules =
               new Dictionary<string, (string, string, string)>
               {
                { "BUEIRO",   ("IfcPipeSegment", "CULVERT",       "DREN_BUEIRO_IFC") },
                { "TUBO",     ("IfcPipeSegment", "RIGIDSEGMENT",  "DREN_TUBO_IFC") },
                { "JOELHO",   ("IfcPipeFitting", "BEND",          "DREN_CONEXAO_IFC") },
                { "CURVA",    ("IfcPipeFitting", "BEND",          "DREN_CONEXAO_IFC") },
                { "TEE",      ("IfcPipeFitting", "JUNCTION",      "DREN_CONEXAO_IFC") },
                { "JUNCTION", ("IfcPipeFitting", "JUNCTION",      "DREN_CONEXAO_IFC") },
                { "REDU",     ("IfcPipeFitting", "TRANSITION",    "DREN_CONEXAO_IFC") },
                { "TRANSITION",("IfcPipeFitting","TRANSITION",    "DREN_CONEXAO_IFC") },
                { "VALETA",   ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "CANALETA", ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "DESCIDA",  ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "PV",       ("IfcDistributionChamberElement","MANHOLE","DREN_ESTRUTURA_IFC") },
                { "MANHOLE",  ("IfcDistributionChamberElement","MANHOLE","DREN_ESTRUTURA_IFC") },
                { "BL",       ("IfcDistributionChamberElement","INLET",  "DREN_ESTRUTURA_IFC") },
                { "INLET",    ("IfcDistributionChamberElement","INLET",  "DREN_ESTRUTURA_IFC") },
               };


        */
    }
}


