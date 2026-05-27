using Autodesk.AutoCAD.EditorInput;
using System;
using System.Linq;
using System.Text;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;
using Xbim.IO;
using static AutomacoesCivil3D.ifc4x3.IfcPavimentacaoPost_Ifc4x3;

// DESIGN NOTE — isolamento de assinatura de métodos:
// IfcDrenagemPost_Ifc4x3 (classe pública, carregada ao JIT de SifcDrePost4x3) NÃO
// pode ter tipos Xbim em assinaturas de métodos. O SOLIDOS.bundle carrega sua própria
// cópia de Xbim.Ifc4.dll; se ela divergir da nossa, o CLR lança TypeLoadException ao
// carregar a classe — antes de Reset() executar, impedindo diagnóstico via log.
// Todos os métodos com IIfcObject / IIfcPropertySet / IfcStore na assinatura ficam em
// DrainageIfcPostImpl (carregada mais tarde, já dentro do try-catch).

namespace AutomacoesCivil3D.ifc4x3
{
    public static class IfcDrenagemPost_Ifc4x3
    {
        public enum DrainageCategory { PipeSegment, PipeFitting, Chamber }

        public sealed class DrainageElementInfo
        {
            public DrainageCategory Category     { get; set; }
            public string PredefinedType         { get; set; }
            // Pset_DrenagemPetrobras
            public string AreaOperacional        { get; set; }
            public string Sistema                { get; set; }
            public string Subsistema             { get; set; }
            public string NomeObjeto             { get; set; }
            public string Catalogo               { get; set; }
            public string CodigoObjeto           { get; set; }
            public string Tag                    { get; set; }
            public string FamilyName             { get; set; }
            public string FuncaoDrenagem         { get; set; }
            public string Status                 { get; set; }
            public string TipoTampa              { get; set; }
            public string TipoGrelha             { get; set; }
            public string MaiorTubo              { get; set; }
            public string Observacoes            { get; set; }
            public double? DiametroNominal       { get; set; }
            public double? Declividade           { get; set; }
            public double? Comprimento           { get; set; }
            public double? Largura               { get; set; }
            public double? Base                  { get; set; }
            public double? CotaFundoMontante     { get; set; }
            public double? CotaFundoJusante      { get; set; }
            public double? CotaTampa             { get; set; }
            public double? ProfundidadeUtil      { get; set; }
            public double? CoefManning           { get; set; }
            public double? CoefHazenWilliams     { get; set; }
            public double? CoefDarcyWeisbach     { get; set; }
            public double? CoverHeight           { get; set; }
            public double? AlturaTampa           { get; set; }
            public double? AlturaGrelha          { get; set; }
            public double? AlturaPiso            { get; set; }
            public double? EspessuraParede       { get; set; }
            public double? EspessuraLaje         { get; set; }
            public double? ComprGrelha           { get; set; }
            public double? LargGrelha            { get; set; }
            public double? QuantTampas           { get; set; }
            public double? QuantAco              { get; set; }
            public double? VolumeEstrutura       { get; set; }
            public double? VolumeExterno         { get; set; }
            public double? VolConcretoArmado     { get; set; }
            public double? VolConcretoMagro      { get; set; }
            public double? ElevMaxConexao        { get; set; }
            public double? ElevMinConexao        { get; set; }
            public double? FolgaTopo             { get; set; }
            public double? FolgaTampa            { get; set; }
            public double? Deflexao              { get; set; }
            // Quantidades (lidas das PSets string do binder ou calculadas)
            public double? GrossCrossSectionArea { get; set; }
            public double? NetCrossSectionArea   { get; set; }
            public double? OuterSurfaceArea      { get; set; }
            public double? FootPrintArea         { get; set; }
            public double? GrossSurfaceArea      { get; set; }
            public double? NetSurfaceArea        { get; set; }
            public double? GrossVolume           { get; set; }
            public double? NetVolume             { get; set; }
            public double? Depth                 { get; set; }
        }

        // ── Entry point (sem tipos Xbim na assinatura) ───────────────────────────

        public static void RunPostProcessing(Editor docEditor, string inputIfcPath, string outputIfcPath)
        {
            if (docEditor == null)
                throw new ArgumentNullException(nameof(docEditor));
            if (string.IsNullOrWhiteSpace(inputIfcPath))
                throw new ArgumentException("Caminho do IFC de entrada nao informado.", nameof(inputIfcPath));
            if (string.IsNullOrWhiteSpace(outputIfcPath))
                throw new ArgumentException("Caminho do IFC de saida nao informado.", nameof(outputIfcPath));

            IfcDrePostTrace.Write("worker-start", $"input={inputIfcPath} | output={outputIfcPath}");

            (int processed, int qtoUpdated, int enriched) = ProcessDrainageIfc4x3(inputIfcPath, outputIfcPath);

            IfcDrePostTrace.Write("worker-success", $"processed={processed} | qto={qtoUpdated} | enriched={enriched}");
            docEditor.WriteMessage(
                $"\nOK (IFC4x3). Drenagem: {processed} elementos | QTO atualizado: {qtoUpdated} | PSets enriquecidos: {enriched}\nSaída: {outputIfcPath}\n"
            );
        }

        // ── Worker (sem tipos Xbim na assinatura; corpo usa DrainageIfcPostImpl) ─

        private static (int processed, int qtoUpdated, int enriched) ProcessDrainageIfc4x3(
            string inputIfcPath, string outputIfcPath)
        {
            // DrainageIfcPostImpl é carregada aqui (dentro do try-catch do comando),
            // não durante JIT de SifcDrePost4x3. TypeLoadException de conflito Xbim é capturada.
            IfcDrePostTrace.Write("initialize-ifc-services-start");
            InitializeIfcServices();
            IfcDrePostTrace.Write("initialize-ifc-services-success");

            Xbim.Ifc.XbimEditorCredentials editor = new Xbim.Ifc.XbimEditorCredentials
            {
                ApplicationDevelopersName = "AutomacoesCivil3D",
                ApplicationFullName       = "Drenagem IFC4x3 Post",
                ApplicationIdentifier     = "AutomacoesCivil3D.DREIFCPOST",
                ApplicationVersion        = "1.0",
                EditorsFamilyName         = "Gleison",
                EditorsGivenName          = "Engenheiro",
                EditorsOrganisationName   = "AutomacoesCivil3D"
            };

            IfcDrePostTrace.Write("ifc-open-start");
            using Xbim.Ifc.IfcStore model = Xbim.Ifc.IfcStore.Open(inputIfcPath, editorDetails: editor, accessMode: XbimDBAccess.ReadWrite);
            IfcDrePostTrace.Write("ifc-open-success", $"schema={model.SchemaVersion}");
            using Xbim.Common.ITransaction tr = model.BeginTransaction("Drenagem: QTO + PSets (IFC4x3)");

            int processed = 0, qtoUpdated = 0, enriched = 0;

            IIfcObject[] allObjs = model.Instances.OfType<IIfcObject>().ToArray();
            IfcDrePostTrace.Write("ifc-object-scan", $"objects={allObjs.Length}");

            foreach (IIfcObject obj in allObjs)
            {
                IIfcPropertySet psetDre     = FindPsetByPrefix(obj, "Pset_DrenagemPetrobras");
                IIfcPropertySet psetObjProp = FindPsetByPrefix(obj, "IfcObject Properties");

                // Tipo detectado pelo nome da classe concreta — evita isinst cross-DLL.
                string entityName = obj.GetType().Name;
                bool isPipeSeg = entityName.IndexOf("PipeSegment",               StringComparison.OrdinalIgnoreCase) >= 0;
                bool isPipeFit = entityName.IndexOf("PipeFitting",               StringComparison.OrdinalIgnoreCase) >= 0;
                bool isChamber = entityName.IndexOf("DistributionChamberElement", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isPipeSeg && !isPipeFit && !isChamber)
                {
                    if (psetDre != null)
                    {
                        string exportAs = ReadFirstString(psetObjProp, "IFC::IfcExportAs");
                        (isPipeSeg, isPipeFit, isChamber) = ClassifyByExportAs(exportAs);
                        if (!isPipeSeg && !isPipeFit && !isChamber)
                            isChamber = true;
                    }
                    else
                    {
                        continue;
                    }
                }

                DrainageCategory category = isPipeSeg ? DrainageCategory.PipeSegment
                    : isPipeFit ? DrainageCategory.PipeFitting
                    : DrainageCategory.Chamber;

                string predefinedType = FirstNonEmpty(
                    ReadFirstString(psetObjProp, "IFC::PredefinedType"),
                    DrainageIfcPostImpl.GetPredefinedType(obj));

                DrainageElementInfo info = DrainageIfcPostImpl.ReadInfo(obj, psetDre, category, predefinedType);

                string displayName = FirstNonEmpty(info.NomeObjeto, info.FuncaoDrenagem, info.CodigoObjeto, info.FamilyName);
                string description = BuildDrainageDescription(info);

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    obj.Name = displayName;
                    if (obj is IIfcElement el)
                        el.Tag = FirstNonEmpty(info.CodigoObjeto, info.Tag, displayName);
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    obj.ObjectType  = FirstNonEmpty(info.FuncaoDrenagem, displayName);
                    obj.Description = description;
                }

                if (DrainageIfcPostImpl.UpdateQto(model, obj, info, category))
                    qtoUpdated++;

                enriched += DrainageIfcPostImpl.EnrichPsets(model, obj, info, category, predefinedType);
                processed++;
            }

            IfcDrePostTrace.Write("ifc-commit-start");
            tr.Commit();
            IfcDrePostTrace.Write("ifc-commit-success");
            IfcDrePostTrace.Write("ifc-save-start");
            model.SaveAs(outputIfcPath);
            IfcDrePostTrace.Write("ifc-save-success");

            return (processed, qtoUpdated, enriched);
        }

        // ── Helpers sem tipos Xbim na assinatura (ficam aqui) ───────────────────

        internal static string BuildDrainageDescription(DrainageElementInfo info)
        {
            string[] parts = new[]
            {
                info.NomeObjeto?.Trim(),
                info.FuncaoDrenagem?.Trim(),
                info.Sistema?.Trim(),
                info.Subsistema?.Trim(),
                info.CodigoObjeto?.Trim()
            }.Where(s => !string.IsNullOrWhiteSpace(s))
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToArray();

            return parts.Length > 0 ? string.Join(" | ", parts) : string.Empty;
        }

        internal static (bool isPipeSeg, bool isPipeFit, bool isChamber) ClassifyByExportAs(string exportAs)
        {
            string up = (exportAs ?? string.Empty).ToUpperInvariant();
            if (up.Contains("PIPESEGMENT"))         return (true,  false, false);
            if (up.Contains("PIPEFITTING"))         return (false, true,  false);
            if (up.Contains("DISTRIBUTIONCHAMBER")) return (false, false, true);
            return (false, false, false);
        }

        internal static double? ComputePipeGrossCrossArea(DrainageElementInfo info)
        {
            double? span  = info.Largura ?? info.Base;
            double? depth = info.ProfundidadeUtil;
            if (span.HasValue && depth.HasValue)
                return Math.Abs(span.Value * depth.Value);

            if (info.DiametroNominal.HasValue)
                return Math.PI * info.DiametroNominal.Value * info.DiametroNominal.Value / 4d;

            return null;
        }

        internal static double? ComputePipeFootprint(DrainageElementInfo info)
        {
            double? width = info.DiametroNominal ?? info.Largura ?? info.Base;
            if (!width.HasValue || !info.Comprimento.HasValue)
                return null;

            return Math.Abs(width.Value) * Math.Abs(info.Comprimento.Value);
        }
    }

    // ── Implementação com tipos Xbim (carregada somente dentro do try-catch) ────
    // Todos os métodos com IIfcObject / IIfcPropertySet / IfcStore na assinatura ficam
    // aqui. A classe é referenciada apenas no corpo de ProcessDrainageIfc4x3(), que é
    // JIT-compilado quando chamado de dentro do bloco try em SifcDrePost4x3().

    internal static class DrainageIfcPostImpl
    {
        private static readonly IfcDrenagemPost_Ifc4x3.DrainageCategory PipeSegment = IfcDrenagemPost_Ifc4x3.DrainageCategory.PipeSegment;
        private static readonly IfcDrenagemPost_Ifc4x3.DrainageCategory PipeFitting = IfcDrenagemPost_Ifc4x3.DrainageCategory.PipeFitting;

        // ── Leitura ─────────────────────────────────────────────────────────────

        internal static IfcDrenagemPost_Ifc4x3.DrainageElementInfo ReadInfo(
            IIfcObject obj,
            IIfcPropertySet psetDre,
            IfcDrenagemPost_Ifc4x3.DrainageCategory category,
            string predefinedType)
        {
            IIfcPropertySet psetPipeCommon = FindPsetByPrefix(obj, "Pset_PipeSegmentTypeCommon");
            IIfcPropertySet psetPipeOcc    = FindPsetByPrefix(obj, "Pset_PipeSegmentOccurrence");
            IIfcPropertySet psetChamCommon = FindPsetByPrefix(obj, "Pset_DistributionChamberElementCommon");
            IIfcPropertySet psetQtoSeg     = FindPsetByPrefix(obj, "Qto_PipeSegmentBaseQuantities");
            IIfcPropertySet psetQtoFit     = FindPsetByPrefix(obj, "Qto_PipeFittingBaseQuantities");
            IIfcPropertySet psetQtoCham    = FindPsetByPrefix(obj, "Qto_DistributionChamberElementBaseQuantities");

            var info = new IfcDrenagemPost_Ifc4x3.DrainageElementInfo
            {
                Category          = category,
                PredefinedType    = predefinedType,
                AreaOperacional   = ReadFirstString(psetDre, "AreaOperacional"),
                Sistema           = ReadFirstString(psetDre, "Sistema"),
                Subsistema        = ReadFirstString(psetDre, "Subsistema"),
                NomeObjeto        = ReadFirstString(psetDre, "NomeObjeto"),
                Catalogo          = ReadFirstString(psetDre, "Catalogo"),
                CodigoObjeto      = ReadFirstString(psetDre, "CodigoObjeto"),
                Tag               = ReadFirstString(psetDre, "Tag"),
                FamilyName        = ReadFirstString(psetDre, "FamilyNameSolidos"),
                FuncaoDrenagem    = ReadFirstString(psetDre, "FuncaoDrenagem"),
                TipoTampa         = ReadFirstString(psetDre, "TipoTampaSolidos"),
                TipoGrelha        = ReadFirstString(psetDre, "TipoGrelhaSolidos"),
                MaiorTubo         = ReadFirstString(psetDre, "MaiorTuboConectado"),
                Observacoes       = ReadFirstString(psetDre, "Observacoes"),
                Status            = FirstNonEmpty(
                    ReadFirstString(psetDre, "Status"),
                    ReadFirstString(psetPipeCommon, "Status"),
                    ReadFirstString(psetChamCommon, "Status")),

                DiametroNominal   = ReadFirstDouble(psetDre, "DiametroNominalProjeto")
                                    ?? ReadFirstDouble(psetPipeCommon, "NominalDiameter"),
                Declividade       = ReadFirstDouble(psetDre, "DeclividadeProjeto")
                                    ?? ReadFirstDouble(psetPipeOcc, "Gradient"),
                Comprimento       = ReadFirstDouble(psetDre, "ComprimentoProjeto")
                                    ?? ReadFirstDouble(psetPipeCommon, "Length"),
                Largura           = ReadFirstDouble(psetDre, "LarguraProjeto"),
                Base              = ReadFirstDouble(psetDre, "BaseProjeto"),
                CotaFundoMontante = ReadFirstDouble(psetDre, "CotaFundoMontante"),
                CotaFundoJusante  = ReadFirstDouble(psetDre, "CotaFundoJusante")
                                    ?? ReadFirstDouble(psetPipeOcc, "InvertElevation"),
                CotaTampa         = ReadFirstDouble(psetDre, "CotaTampa"),
                ProfundidadeUtil  = ReadFirstDouble(psetDre, "ProfundidadeUtil"),
                CoefManning       = ReadFirstDouble(psetDre, "CoefManning"),
                CoefHazenWilliams = ReadFirstDouble(psetDre, "CoefHazenWilliams"),
                CoefDarcyWeisbach = ReadFirstDouble(psetDre, "CoefDarcyWeisbach"),
                CoverHeight       = ReadFirstDouble(psetDre, "CoverHeight"),
                AlturaTampa       = ReadFirstDouble(psetDre, "AlturaTampa"),
                AlturaGrelha      = ReadFirstDouble(psetDre, "AlturaGrelha"),
                AlturaPiso        = ReadFirstDouble(psetDre, "AlturaPiso"),
                EspessuraParede   = ReadFirstDouble(psetDre, "EspessuraParede"),
                EspessuraLaje     = ReadFirstDouble(psetDre, "EspessuraLaje"),
                ComprGrelha       = ReadFirstDouble(psetDre, "ComprimentoGrelha"),
                LargGrelha        = ReadFirstDouble(psetDre, "LarguraGrelha"),
                QuantTampas       = ReadFirstDouble(psetDre, "QuantidadeTampas"),
                QuantAco          = ReadFirstDouble(psetDre, "QuantidadeAco"),
                VolumeEstrutura   = ReadFirstDouble(psetDre, "VolumeEstrutura"),
                VolumeExterno     = ReadFirstDouble(psetDre, "VolumeExterno"),
                VolConcretoArmado = ReadFirstDouble(psetDre, "VolumeConcretoArmado"),
                VolConcretoMagro  = ReadFirstDouble(psetDre, "VolumeConcretoMagro"),
                ElevMaxConexao    = ReadFirstDouble(psetDre, "ElevacaoMaximaConexao"),
                ElevMinConexao    = ReadFirstDouble(psetDre, "ElevacaoMinimaConexao"),
                FolgaTopo         = ReadFirstDouble(psetDre, "FolgaTopo"),
                FolgaTampa        = ReadFirstDouble(psetDre, "FolgaTampa"),
                Deflexao          = ReadFirstDouble(psetDre, "Deflexao"),

                GrossCrossSectionArea = ReadFirstDouble(psetQtoSeg, "GrossCrossSectionArea")
                                        ?? ReadFirstDouble(psetQtoFit, "GrossCrossSectionArea"),
                NetCrossSectionArea   = ReadFirstDouble(psetQtoSeg, "NetCrossSectionArea")
                                        ?? ReadFirstDouble(psetQtoFit, "NetCrossSectionArea"),
                OuterSurfaceArea      = ReadFirstDouble(psetQtoSeg, "OuterSurfaceArea")
                                        ?? ReadFirstDouble(psetQtoFit, "OuterSurfaceArea"),
                FootPrintArea         = ReadFirstDouble(psetQtoSeg, "FootPrintArea"),
                GrossSurfaceArea      = ReadFirstDouble(psetQtoCham, "GrossSurfaceArea"),
                NetSurfaceArea        = ReadFirstDouble(psetQtoCham, "NetSurfaceArea"),
                GrossVolume           = ReadFirstDouble(psetQtoCham, "GrossVolume"),
                NetVolume             = ReadFirstDouble(psetQtoCham, "NetVolume"),
                Depth                 = ReadFirstDouble(psetQtoCham, "Depth"),
            };

            if (!info.GrossCrossSectionArea.HasValue && category == PipeSegment)
                info.GrossCrossSectionArea = IfcDrenagemPost_Ifc4x3.ComputePipeGrossCrossArea(info);
            if (!info.FootPrintArea.HasValue && category == PipeSegment)
                info.FootPrintArea = IfcDrenagemPost_Ifc4x3.ComputePipeFootprint(info);
            if (!info.Depth.HasValue && category == IfcDrenagemPost_Ifc4x3.DrainageCategory.Chamber)
                info.Depth = info.ProfundidadeUtil;
            if (!info.GrossVolume.HasValue && category == IfcDrenagemPost_Ifc4x3.DrainageCategory.Chamber)
                info.GrossVolume = info.VolumeExterno ?? info.VolumeEstrutura;
            if (!info.NetVolume.HasValue && category == IfcDrenagemPost_Ifc4x3.DrainageCategory.Chamber)
                info.NetVolume = info.VolumeEstrutura;

            return info;
        }

        // ── QTO tipado ───────────────────────────────────────────────────────────

        internal static bool UpdateQto(
            Xbim.Ifc.IfcStore model,
            IIfcObject obj,
            IfcDrenagemPost_Ifc4x3.DrainageElementInfo info,
            IfcDrenagemPost_Ifc4x3.DrainageCategory category)
        {
            bool anyAdded = false;

            if (category == PipeSegment)
            {
                IIfcElementQuantity qto = GetOrCreateElementQuantity(model, obj, "Qto_PipeSegmentBaseQuantities");
                qto.Quantities.Clear();
                anyAdded |= AddLengthQuantity(model, qto, "Length",                info.Comprimento);
                anyAdded |= AddAreaQuantity(  model, qto, "GrossCrossSectionArea", info.GrossCrossSectionArea);
                anyAdded |= AddAreaQuantity(  model, qto, "NetCrossSectionArea",   info.NetCrossSectionArea);
                anyAdded |= AddAreaQuantity(  model, qto, "OuterSurfaceArea",      info.OuterSurfaceArea);
                anyAdded |= AddAreaQuantity(  model, qto, "FootPrintArea",         info.FootPrintArea);
            }
            else if (category == PipeFitting)
            {
                IIfcElementQuantity qto = GetOrCreateElementQuantity(model, obj, "Qto_PipeFittingBaseQuantities");
                qto.Quantities.Clear();
                anyAdded |= AddLengthQuantity(model, qto, "Length",                info.Comprimento);
                anyAdded |= AddAreaQuantity(  model, qto, "GrossCrossSectionArea", info.GrossCrossSectionArea);
                anyAdded |= AddAreaQuantity(  model, qto, "NetCrossSectionArea",   info.NetCrossSectionArea);
                anyAdded |= AddAreaQuantity(  model, qto, "OuterSurfaceArea",      info.OuterSurfaceArea);
            }
            else
            {
                IIfcElementQuantity qto = GetOrCreateElementQuantity(model, obj, "Qto_DistributionChamberElementBaseQuantities");
                qto.Quantities.Clear();
                anyAdded |= AddAreaQuantity(  model, qto, "GrossSurfaceArea", info.GrossSurfaceArea);
                anyAdded |= AddAreaQuantity(  model, qto, "NetSurfaceArea",   info.NetSurfaceArea);
                anyAdded |= AddVolumeQuantity(model, qto, "GrossVolume",      info.GrossVolume);
                anyAdded |= AddVolumeQuantity(model, qto, "NetVolume",        info.NetVolume);
                anyAdded |= AddLengthQuantity(model, qto, "Depth",            info.Depth);
            }

            return anyAdded;
        }

        // ── Enriquecimento de PSets ──────────────────────────────────────────────

        internal static int EnrichPsets(
            Xbim.Ifc.IfcStore model,
            IIfcObject obj,
            IfcDrenagemPost_Ifc4x3.DrainageElementInfo info,
            IfcDrenagemPost_Ifc4x3.DrainageCategory category,
            string predefinedType)
        {
            int count = 0;
            string reference = FirstNonEmpty(info.CodigoObjeto, info.Tag, info.Catalogo, info.FamilyName);
            string status    = FirstNonEmpty(info.Status, "Projeto");

            IIfcPropertySet psetDre = GetOrCreatePropertySet(model, obj, "Pset_DrenagemPetrobras", "Propriedades de drenagem Petrobras.");
            SetTextProperty(  model, psetDre, "AreaOperacional",        info.AreaOperacional);
            SetTextProperty(  model, psetDre, "Sistema",                info.Sistema);
            SetTextProperty(  model, psetDre, "Subsistema",             info.Subsistema);
            SetTextProperty(  model, psetDre, "NomeObjeto",             info.NomeObjeto);
            SetTextProperty(  model, psetDre, "Catalogo",               info.Catalogo);
            SetTextProperty(  model, psetDre, "CodigoObjeto",           info.CodigoObjeto);
            SetTextProperty(  model, psetDre, "Tag",                    info.Tag);
            SetTextProperty(  model, psetDre, "FamilyNameSolidos",      info.FamilyName);
            SetTextProperty(  model, psetDre, "FuncaoDrenagem",         info.FuncaoDrenagem);
            SetTextProperty(  model, psetDre, "TipoTampaSolidos",       info.TipoTampa);
            SetTextProperty(  model, psetDre, "TipoGrelhaSolidos",      info.TipoGrelha);
            SetTextProperty(  model, psetDre, "MaiorTuboConectado",     info.MaiorTubo);
            SetTextProperty(  model, psetDre, "Observacoes",            info.Observacoes);
            SetLengthProperty(model, psetDre, "DiametroNominalProjeto", info.DiametroNominal);
            SetRealProperty(  model, psetDre, "DeclividadeProjeto",     info.Declividade);
            SetLengthProperty(model, psetDre, "ComprimentoProjeto",     info.Comprimento);
            SetLengthProperty(model, psetDre, "LarguraProjeto",         info.Largura);
            SetLengthProperty(model, psetDre, "BaseProjeto",            info.Base);
            SetLengthProperty(model, psetDre, "CotaFundoMontante",      info.CotaFundoMontante);
            SetLengthProperty(model, psetDre, "CotaFundoJusante",       info.CotaFundoJusante);
            SetLengthProperty(model, psetDre, "CotaTampa",              info.CotaTampa);
            SetLengthProperty(model, psetDre, "ProfundidadeUtil",       info.ProfundidadeUtil);
            SetRealProperty(  model, psetDre, "CoefManning",            info.CoefManning);
            SetRealProperty(  model, psetDre, "CoefHazenWilliams",      info.CoefHazenWilliams);
            SetRealProperty(  model, psetDre, "CoefDarcyWeisbach",      info.CoefDarcyWeisbach);
            SetLengthProperty(model, psetDre, "CoverHeight",            info.CoverHeight);
            SetLengthProperty(model, psetDre, "AlturaTampa",            info.AlturaTampa);
            SetLengthProperty(model, psetDre, "AlturaGrelha",           info.AlturaGrelha);
            SetLengthProperty(model, psetDre, "AlturaPiso",             info.AlturaPiso);
            SetLengthProperty(model, psetDre, "EspessuraParede",        info.EspessuraParede);
            SetLengthProperty(model, psetDre, "EspessuraLaje",          info.EspessuraLaje);
            SetLengthProperty(model, psetDre, "ComprimentoGrelha",      info.ComprGrelha);
            SetLengthProperty(model, psetDre, "LarguraGrelha",          info.LargGrelha);
            SetRealProperty(  model, psetDre, "QuantidadeTampas",       info.QuantTampas);
            SetRealProperty(  model, psetDre, "QuantidadeAco",          info.QuantAco);
            SetVolumeProperty(model, psetDre, "VolumeEstrutura",        info.VolumeEstrutura);
            SetVolumeProperty(model, psetDre, "VolumeExterno",          info.VolumeExterno);
            SetVolumeProperty(model, psetDre, "VolumeConcretoArmado",   info.VolConcretoArmado);
            SetVolumeProperty(model, psetDre, "VolumeConcretoMagro",    info.VolConcretoMagro);
            SetLengthProperty(model, psetDre, "ElevacaoMaximaConexao",  info.ElevMaxConexao);
            SetLengthProperty(model, psetDre, "ElevacaoMinimaConexao",  info.ElevMinConexao);
            SetLengthProperty(model, psetDre, "FolgaTopo",              info.FolgaTopo);
            SetLengthProperty(model, psetDre, "FolgaTampa",             info.FolgaTampa);
            SetRealProperty(  model, psetDre, "Deflexao",               info.Deflexao);
            count++;

            if (category == PipeSegment)
            {
                IIfcPropertySet psetCommon = GetOrCreatePropertySet(model, obj, "Pset_PipeSegmentTypeCommon", "Propriedades comuns de segmento de tubo.");
                SetTextProperty(  model, psetCommon, "Reference",       reference);
                SetTextProperty(  model, psetCommon, "Status",          status);
                SetLengthProperty(model, psetCommon, "NominalDiameter", info.DiametroNominal);
                SetLengthProperty(model, psetCommon, "Length",          info.Comprimento);
                count++;

                IIfcPropertySet psetOcc = GetOrCreatePropertySet(model, obj, "Pset_PipeSegmentOccurrence", "Propriedades de ocorrencia de segmento de tubo.");
                SetRealProperty(  model, psetOcc, "Gradient",       info.Declividade);
                SetLengthProperty(model, psetOcc, "InvertElevation", info.CotaFundoJusante);
                count++;

                string ptNorm = (predefinedType ?? string.Empty).ToUpperInvariant();
                if (ptNorm == "GUTTER")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_PipeSegmentTypeGutter", "Propriedades de canaleta.");
                    SetRealProperty(  model, ps, "Slope",             info.Declividade);
                    SetLengthProperty(model, ps, "OrthometricHeight", info.CotaTampa);
                    count++;
                }
                else if (ptNorm == "CULVERT")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_PipeSegmentTypeCulvert", "Propriedades de bueiro.");
                    SetLengthProperty(model, ps, "InternalWidth", info.Largura ?? info.Base);
                    SetLengthProperty(model, ps, "ClearDepth",    info.ProfundidadeUtil);
                    count++;
                }
            }
            else if (category == PipeFitting)
            {
                IIfcPropertySet psetCommon = GetOrCreatePropertySet(model, obj, "Pset_PipeFittingTypeCommon", "Propriedades comuns de conexao de tubo.");
                SetTextProperty(model, psetCommon, "Reference", reference);
                SetTextProperty(model, psetCommon, "Status",    status);
                count++;

                string ptNorm = (predefinedType ?? string.Empty).ToUpperInvariant();
                if (ptNorm == "BEND")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_FittingBend", "Propriedades de joelho/curva.");
                    SetRealProperty(model, ps, "BendAngle", info.Deflexao);
                    count++;
                }
                else if (ptNorm == "JUNCTION")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_FittingJunction", "Propriedades de juncao/cruzeta.");
                    SetTextProperty(model, ps, "JunctionType", info.FamilyName);
                    count++;
                }
                else if (ptNorm == "TRANSITION")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_FittingTransition", "Propriedades de reducao/transicao.");
                    SetLengthProperty(model, ps, "NominalLength", info.Comprimento);
                    count++;
                }
            }
            else // Chamber
            {
                IIfcPropertySet psetCommon = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementCommon", "Propriedades comuns de camara de distribuicao.");
                SetTextProperty(  model, psetCommon, "Reference",     reference);
                SetTextProperty(  model, psetCommon, "Status",        status);
                SetLengthProperty(model, psetCommon, "InvertLevel",   info.CotaFundoJusante);
                SetLengthProperty(model, psetCommon, "SoffitLevel",   info.CotaTampa);
                SetLengthProperty(model, psetCommon, "WallThickness", info.EspessuraParede);
                count++;

                string ptNorm = (predefinedType ?? string.Empty).ToUpperInvariant();
                if (ptNorm == "MANHOLE")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementTypeManhole", "Propriedades de poco de visita.");
                    SetLengthProperty(model, ps, "InvertLevel",          info.CotaFundoJusante);
                    SetLengthProperty(model, ps, "SoffitLevel",          info.CotaTampa);
                    SetLengthProperty(model, ps, "WallThickness",        info.EspessuraParede);
                    SetLengthProperty(model, ps, "BaseThickness",        info.AlturaPiso);
                    SetLengthProperty(model, ps, "AccessLengthOrRadius", info.ComprGrelha);
                    SetLengthProperty(model, ps, "AccessWidth",          info.LargGrelha);
                    SetTextProperty(  model, ps, "AccessCoverLoadRating", FirstNonEmpty(info.TipoTampa, info.TipoGrelha));
                    SetRealProperty(  model, ps, "NumberOfManholeCovers", info.QuantTampas);
                    count++;
                }
                else if (ptNorm == "INSPECTIONCHAMBER")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementTypeInspectionChamber", "Propriedades de caixa de passagem.");
                    SetLengthProperty(model, ps, "InspectionChamberInvertLevel", info.CotaFundoJusante);
                    SetLengthProperty(model, ps, "SoffitLevel",                  info.CotaTampa);
                    SetLengthProperty(model, ps, "WallThickness",                info.EspessuraParede);
                    SetLengthProperty(model, ps, "BaseThickness",                info.AlturaPiso);
                    SetLengthProperty(model, ps, "ChamberLengthOrRadius",        info.Comprimento ?? info.Largura);
                    SetLengthProperty(model, ps, "ChamberWidth",                 info.Largura);
                    SetLengthProperty(model, ps, "AccessLengthOrRadius",         info.ComprGrelha);
                    SetLengthProperty(model, ps, "AccessWidth",                  info.LargGrelha);
                    SetTextProperty(  model, ps, "AccessCoverLoadRating",         FirstNonEmpty(info.TipoTampa, info.TipoGrelha));
                    count++;
                }
                else if (ptNorm == "INSPECTIONPIT")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementTypeInspectionPit", "Propriedades de poco de inspecao.");
                    SetLengthProperty(model, ps, "Length", info.Comprimento);
                    SetLengthProperty(model, ps, "Width",  info.Largura);
                    SetLengthProperty(model, ps, "Depth",  info.ProfundidadeUtil);
                    count++;
                }
                else if (ptNorm == "SUMP")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementTypeSump", "Propriedades de boca de lobo/ralo.");
                    SetLengthProperty(model, ps, "Length",          info.Comprimento);
                    SetLengthProperty(model, ps, "Width",           info.Largura);
                    SetLengthProperty(model, ps, "SumpInvertLevel", info.CotaFundoJusante);
                    count++;
                }
                else if (ptNorm == "TRENCH")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementTypeTrench", "Propriedades de vala/trincheira.");
                    SetLengthProperty(model, ps, "Width",       info.Largura ?? info.Base);
                    SetLengthProperty(model, ps, "Depth",       info.ProfundidadeUtil);
                    SetLengthProperty(model, ps, "InvertLevel", info.CotaFundoJusante);
                    count++;
                }
                else if (ptNorm == "METERCHAMBER")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementTypeMeterChamber", "Propriedades de caixa de medicao.");
                    SetLengthProperty(model, ps, "ChamberLengthOrRadius", info.Comprimento ?? info.Largura);
                    SetLengthProperty(model, ps, "ChamberWidth",          info.Largura);
                    SetLengthProperty(model, ps, "WallThickness",         info.EspessuraParede);
                    SetLengthProperty(model, ps, "BaseThickness",         info.AlturaPiso);
                    SetTextProperty(  model, ps, "AccessCoverMaterial",   FirstNonEmpty(info.TipoTampa, info.TipoGrelha));
                    count++;
                }
                else if (ptNorm == "VALVECHAMBER")
                {
                    IIfcPropertySet ps = GetOrCreatePropertySet(model, obj, "Pset_DistributionChamberElementTypeValveChamber", "Propriedades de caixa de valvula.");
                    SetLengthProperty(model, ps, "ChamberLengthOrRadius", info.Comprimento ?? info.Largura);
                    SetLengthProperty(model, ps, "ChamberWidth",          info.Largura);
                    SetLengthProperty(model, ps, "WallThickness",         info.EspessuraParede);
                    SetLengthProperty(model, ps, "BaseThickness",         info.AlturaPiso);
                    SetTextProperty(  model, ps, "AccessCoverMaterial",   FirstNonEmpty(info.TipoTampa, info.TipoGrelha));
                    count++;
                }
            }

            return count;
        }

        // ── Helper: tipo predefinido via reflexão ────────────────────────────────

        internal static string GetPredefinedType(IIfcObject obj)
        {
            try
            {
                System.Reflection.PropertyInfo prop = obj?.GetType().GetProperty("PredefinedType");
                if (prop == null) return string.Empty;
                object val = prop.GetValue(obj);
                if (val == null) return string.Empty;
                string s = val.ToString();
                if (s == "USERDEFINED" || s == "NOTDEFINED") return string.Empty;
                return s;
            }
            catch { return string.Empty; }
        }
    }
}
