using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.Kernel;
using Xbim.Ifc4x3.MeasureResource;
using Xbim.Ifc4x3.ProductExtension;
using Xbim.Ifc4x3.PropertyResource;
using Xbim.Ifc4x3.QuantityResource;
using Xbim.IO;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DialogResult = System.Windows.Forms.DialogResult;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;

namespace AutomacoesCivil3D.ifc4x3
{
    public static class IfcPavimentacaoPost_Ifc4x3
    {
        public static void RunPostProcessing(Editor docEditor, string inputIfcPath, string outputIfcPath)
        {
            if (docEditor == null)
                throw new ArgumentNullException(nameof(docEditor));

            if (string.IsNullOrWhiteSpace(inputIfcPath))
                throw new ArgumentException("Caminho do IFC de entrada nao informado.", nameof(inputIfcPath));

            if (string.IsNullOrWhiteSpace(outputIfcPath))
                throw new ArgumentException("Caminho do IFC de saida nao informado.", nameof(outputIfcPath));

            IfcPavPostTrace.Write("worker-start", $"input={inputIfcPath} | output={outputIfcPath}");

            (int qtoUpdated, int renamed, int layersTransferred) = ProcessPavementIfc4x3(inputIfcPath, outputIfcPath);

            IfcPavPostTrace.Write("worker-success", $"qto={qtoUpdated} | renamed={renamed} | layersTransferred={layersTransferred}");
            docEditor.WriteMessage(
                $"\nOK (IFC4x3). QTO Pavimentação: {qtoUpdated} | Renomeados/Tipados: {renamed} | Camadas preenchidas: {layersTransferred}\nSaída: {outputIfcPath}\n"
            );
        }

        public static (int qtoUpdated, int renamed, int layersTransferred) ProcessPavementIfc4x3(string inputIfcPath, string outputIfcPath)
        {
            IfcPavPostTrace.Write("initialize-ifc-services-start");
            InitializeIfcServices();
            IfcPavPostTrace.Write("initialize-ifc-services-success");

            Xbim.Ifc.XbimEditorCredentials editor = new Xbim.Ifc.XbimEditorCredentials
            {
                ApplicationDevelopersName = "AutomacoesCivil3D",
                ApplicationFullName = "Pavimentacao IFC4x3 Post",
                ApplicationIdentifier = "AutomacoesCivil3D.PAVIFCPOST",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Gleison",
                EditorsGivenName = "Engenheiro",
                EditorsOrganisationName = "AutomacoesCivil3D"
            };

            IfcPavPostTrace.Write("ifc-open-start");
            using Xbim.Ifc.IfcStore model = Xbim.Ifc.IfcStore.Open(inputIfcPath, editorDetails: editor, accessMode: XbimDBAccess.ReadWrite);
            IfcPavPostTrace.Write("ifc-open-success", $"schema={model.SchemaVersion}");
            using Xbim.Common.ITransaction tr = model.BeginTransaction("Pavimentacao: QTO + Meta (IFC4x3)");

            int qtoUpdated = 0;
            int renamed = 0;
            int layersTransferred = 0;
            List<SolidSourceInfo> solidSources = new List<SolidSourceInfo>();

            // Você pode restringir para IIfcElement, mas em export de infra às vezes cai em proxy/civil element.
            IIfcObject[] objs = model.Instances
                .OfType<IIfcObject>()
                .ToArray();
            IfcPavPostTrace.Write("ifc-object-scan", $"objects={objs.Length}");

            foreach (IIfcObject obj in objs)
            {
                IIfcPropertySet psetA = FindPsetByPrefix(obj, "Pset_A - Dados do Projeto");
                IIfcPropertySet psetB = FindPsetByPrefix(obj, "Pset_B - Informações");
                IIfcPropertySet psetC = FindPsetByPrefix(obj, "Pset_C - Propriedades");
                IIfcPropertySet psetD = FindPsetByPrefix(obj, "Pset_D - Propriedades");
                IIfcPropertySet psetE = FindPsetByPrefix(obj, "Pset_E - Requisitos");
                IIfcPropertySet coordenacao = FindPsetByPrefix(obj, "Pset_COORD");
                IIfcPropertySet corridorIdentity = FindPsetByPrefix(obj, "Pset_CorridorIdentity");
                IIfcPropertySet corridorModelInfo = FindPsetByPrefix(obj, "Pset_CorridorModelInformation");
                IIfcPropertySet corridorShapeInfo = FindPsetByPrefix(obj, "Pset_CorridorShapeInformation");
                IIfcPropertySet psetRodoviarioFonte = FindPsetByPrefix(obj, "Pset_Rodoviario");
                IIfcPropertySet psetPavimentacaoFonte = FindPsetByPrefix(obj, "Pset_Pavimentacao");
                IIfcPropertySet psetPavementCommonFonte = FindPsetByPrefix(obj, "Pset_PavementCommon");

                string disciplina = ReadFirstString(psetB, "Disciplina");
                string rawCodeName = FirstNonEmpty(
                    ReadFirstString(psetB, "CodeName"),
                    ReadFirstString(corridorShapeInfo, "CodeName"),
                    ReadFirstString(psetRodoviarioFonte, "CodeName"),
                    ReadFirstString(obj as IIfcObject, "CodeName"));

                bool hasGeometryData =
                    TryGetDouble(psetC, "Volume", out double _) ||
                    TryGetDouble(psetC, "Área", out double _) ||
                    TryGetDouble(psetC, "Area", out double _) ||
                    TryGetDouble(psetC, "Altura", out double _);

                bool isPav = string.Equals(disciplina?.Trim(), "Pavimentação", StringComparison.OrdinalIgnoreCase)
                    || LooksLikePavementCode(rawCodeName)
                    || hasGeometryData;

                if (!isPav)
                    continue;

                string codigoObj = FirstNonEmpty(
                    ReadFirstString(psetB, "Código_do_Objeto", "Codigo_do_Objeto", "CodigoObjeto"),
                    ReadFirstString(psetRodoviarioFonte, "CodigoObjeto"),
                    ReadFirstString(psetA, "Identificador do Projeto"));
                string subassembly = FirstNonEmpty(
                    ReadFirstString(psetRodoviarioFonte, "SubassemblyName"),
                    ReadFirstString(psetB, "SubassemblyName"),
                    ReadFirstString(corridorIdentity, "SubassemblyName"));
                string regionName = FirstNonEmpty(
                    ReadFirstString(psetRodoviarioFonte, "RegionName"),
                    ReadFirstString(psetB, "RegionName", "RegionGUID"),
                    ReadFirstString(corridorIdentity, "RegionGuid", "RegionGUID"));
                string nomeCorredor = FirstNonEmpty(
                    ReadFirstString(psetRodoviarioFonte, "NomeCorredor", "Segmento", "Trecho"),
                    ReadFirstString(psetB, "NomeCorredorSolidos", "NomeCorredorSolido", "CorridorName"),
                    ReadFirstString(corridorModelInfo, "CorridorName"),
                    ReadFirstString(psetA, "Segmento", "Trecho"));
                string situacao = FirstNonEmpty(
                    ReadFirstString(psetRodoviarioFonte, "Situacao"),
                    ReadFirstString(psetB, "Situação", "Situacao"),
                    "Projeto");
                string lado = FirstNonEmpty(
                    ReadFirstString(corridorShapeInfo, "Side"),
                    ReadFirstString(psetRodoviarioFonte, "Side"),
                    ReadFirstString(psetB, "Lado"),
                    InferSide(rawCodeName));

                double? len = ReadFirstDouble(coordenacao, "COMPRIMENTO_SOLIDOS_CORREDOR") ??
                    ReadFirstDouble(psetPavimentacaoFonte, "ComprimentoProjeto") ??
                    ReadFirstDouble(psetPavementCommonFonte, "NominalLength") ??
                    ReadFirstDouble(psetC, "Comprimento");
                double? area = ReadFirstDouble(psetPavimentacaoFonte, "AreaProjeto") ??
                    ReadFirstDouble(psetC, "Área", "Area");
                double? vol = ReadFirstDouble(psetPavimentacaoFonte, "VolumeProjeto") ??
                    ReadFirstDouble(psetC, "Volume");
                double? width = ReadFirstDouble(psetPavimentacaoFonte, "LarguraProjeto") ??
                    ReadFirstDouble(psetPavementCommonFonte, "NominalWidth") ??
                    ReadFirstDouble(psetC, "Largura");
                double? thick = ReadFirstDouble(psetPavimentacaoFonte, "EspessuraProjeto") ??
                    ReadFirstDouble(psetPavementCommonFonte, "NominalThickness", "NominalThicknessEnd") ??
                    ReadFirstDouble(psetC, "Altura");
                double? slope = ReadFirstDouble(psetPavimentacaoFonte, "CrossfallProjeto") ??
                    ReadFirstDouble(psetC, "Inclinação", "Inclinacao");
                double? startStation = ReadFirstDouble(psetRodoviarioFonte, "EstaqueamentoInicial") ??
                    ReadFirstDouble(psetB, "EstaqueamentoInicial", "Estaqueamento_Inicial") ??
                    ReadFirstDouble(corridorIdentity, "StartStation");
                double? endStation = ReadFirstDouble(psetRodoviarioFonte, "EstaqueamentoFinal") ??
                    ReadFirstDouble(psetB, "EstaqueamentoFinal", "Estaqueamento_Final") ??
                    ReadFirstDouble(corridorIdentity, "EndStation");

                string camada = ResolveFriendlyLayerName(
                    FirstNonEmpty(ReadFirstString(psetPavimentacaoFonte, "Camada"), rawCodeName),
                    rawCodeName);
                string funcaoCamada = FirstNonEmpty(
                    ReadFirstString(psetPavimentacaoFonte, "FuncaoCamada"),
                    ResolveLayerFunction(rawCodeName));
                string tipoPavimento = FirstNonEmpty(
                    ReadFirstString(psetPavimentacaoFonte, "TipoPavimento"),
                    ResolvePavementType(rawCodeName));
                string material = FirstNonEmpty(
                    ReadFirstString(psetPavimentacaoFonte, "Material"),
                    ReadFirstString(psetE, "Material", "ClasseMaterial"),
                    ResolveFriendlyMaterial(rawCodeName));

                bool gotAny = false;

                IIfcElementQuantity qto = GetOrCreateElementQuantity(model, obj, "Qto_Pavimentacao_SMEC");
                qto.Quantities.Clear();

                gotAny |= AddLengthQuantity(model, qto, "Length", len);
                gotAny |= AddAreaQuantity(model, qto, "Area", area);
                gotAny |= AddVolumeQuantity(model, qto, "Volume", vol);
                gotAny |= AddLengthQuantity(model, qto, "Width", width);
                gotAny |= AddLengthQuantity(model, qto, "Thickness", thick);

                if (gotAny)
                    qtoUpdated++;

                string displayName = FirstNonEmpty(camada, codigoObj, regionName, obj.Name?.ToString());
                string tagValue = FirstNonEmpty(codigoObj, camada, regionName);
                string description = BuildDescription(camada, material, subassembly, nomeCorredor, startStation, endStation);

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    obj.Name = displayName;
                    if (obj is IIfcElement el)
                        el.Tag = tagValue;

                    renamed++;
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    obj.ObjectType = displayName;
                    obj.Description = description;
                }

                IIfcPropertySet psetRodoviario = GetOrCreatePropertySet(model, obj, "Pset_Rodoviario", "Metadados rodoviarios do corredor.");
                SetTextProperty(model, psetRodoviario, "Segmento", nomeCorredor);
                SetTextProperty(model, psetRodoviario, "Trecho", nomeCorredor);
                SetTextProperty(model, psetRodoviario, "CodigoObjeto", codigoObj);
                SetTextProperty(model, psetRodoviario, "CodeName", rawCodeName);
                SetTextProperty(model, psetRodoviario, "SubassemblyName", subassembly);
                SetTextProperty(model, psetRodoviario, "NomeCorredor", nomeCorredor);
                SetTextProperty(model, psetRodoviario, "RegionName", regionName);
                SetTextProperty(model, psetRodoviario, "Side", lado);
                SetTextProperty(model, psetRodoviario, "Situacao", situacao);
                SetLengthProperty(model, psetRodoviario, "EstaqueamentoInicial", startStation);
                SetLengthProperty(model, psetRodoviario, "EstaqueamentoFinal", endStation);
                SetTextProperty(model, psetRodoviario, "EstaqueamentoInicialTexto", FormatStation(startStation));
                SetTextProperty(model, psetRodoviario, "EstaqueamentoFinalTexto", FormatStation(endStation));
                SetTextProperty(model, psetRodoviario, "IntervaloEstacas", BuildStationRange(startStation, endStation));
                SetTextProperty(model, psetRodoviario, "LRMName", "Estaqueamento");
                SetTextProperty(model, psetRodoviario, "EstagioProjeto", "Projeto");

                IIfcPropertySet psetPavimentacao = GetOrCreatePropertySet(model, obj, "Pset_Pavimentacao", "Metadados de pavimentacao do corredor.");
                SetTextProperty(model, psetPavimentacao, "Disciplina", FirstNonEmpty(disciplina, "Pavimentação"));
                SetTextProperty(model, psetPavimentacao, "Faixa", FirstNonEmpty(subassembly, lado));
                SetTextProperty(model, psetPavimentacao, "Camada", FirstNonEmpty(rawCodeName, camada));
                SetTextProperty(model, psetPavimentacao, "FuncaoCamada", funcaoCamada);
                SetTextProperty(model, psetPavimentacao, "TipoPavimento", tipoPavimento);
                SetTextProperty(model, psetPavimentacao, "Material", material);
                SetLengthProperty(model, psetPavimentacao, "EspessuraProjeto", thick);
                SetLengthProperty(model, psetPavimentacao, "LarguraProjeto", width);
                SetLengthProperty(model, psetPavimentacao, "ComprimentoProjeto", len);
                SetAreaProperty(model, psetPavimentacao, "AreaProjeto", area);
                SetVolumeProperty(model, psetPavimentacao, "VolumeProjeto", vol);
                SetRealProperty(model, psetPavimentacao, "CrossfallProjeto", slope);
                SetTextProperty(model, psetPavimentacao, "EstaqueamentoInicialTexto", FormatStation(startStation));
                SetTextProperty(model, psetPavimentacao, "EstaqueamentoFinalTexto", FormatStation(endStation));

                IIfcPropertySet psetPavementCommon = GetOrCreatePropertySet(model, obj, "Pset_PavementCommon", "Pset padrao para estruturas de pavimento.");
                SetTextProperty(model, psetPavementCommon, "Reference", FirstNonEmpty(nomeCorredor, camada));
                SetTextProperty(model, psetPavementCommon, "Status", situacao);
                SetLengthProperty(model, psetPavementCommon, "NominalThicknessEnd", thick);
                SetRealProperty(model, psetPavementCommon, "StructuralSlope", slope);
                SetTextProperty(model, psetPavementCommon, "StructuralSlopeType", "Crossfall");
                SetLengthProperty(model, psetPavementCommon, "NominalWidth", width);
                SetLengthProperty(model, psetPavementCommon, "NominalLength", len);
                SetLengthProperty(model, psetPavementCommon, "NominalThickness", thick);

                IIfcPropertySet psetPavementSurface = GetOrCreatePropertySet(model, obj, "Pset_PavementSurfaceCommon", "Pset padrao para a superficie de pavimento.");
                SetTextProperty(model, psetPavementSurface, "PavementTexture", camada);

                IIfcPropertySet psetStationing = GetOrCreatePropertySet(model, obj, "Pset_Stationing", "Pset padrao de estacas e progressivas.");
                SetLengthProperty(model, psetStationing, "IncomingStation", startStation ?? endStation);
                SetLengthProperty(model, psetStationing, "Station", endStation ?? startStation);
                SetTextProperty(model, psetStationing, "HasIncreasingStation", "true");
                SetTextProperty(model, psetStationing, "StationInterval", BuildStationRange(startStation, endStation));

                IIfcPropertySet psetLinearReferencing = GetOrCreatePropertySet(model, obj, "Pset_LinearReferencingMethod", "Pset padrao de metodo de referenciacao linear.");
                SetTextProperty(model, psetLinearReferencing, "LRMName", "Estaqueamento");
                SetTextProperty(model, psetLinearReferencing, "LRMType", "CHAINAGE");
                SetTextProperty(model, psetLinearReferencing, "LRMUnit", "m");

                solidSources.Add(new SolidSourceInfo
                {
                    CorridorName      = nomeCorredor,
                    RegionName        = regionName,
                    LaneName          = subassembly,
                    CodeName          = rawCodeName,
                    Side              = lado,
                    Discipline        = FirstNonEmpty(disciplina, "Pavimentação"),
                    FriendlyLayerName = camada,
                    FunctionLayer     = funcaoCamada,
                    PavementType      = tipoPavimento,
                    Material          = material,
                    Status            = situacao,
                    StartStation      = startStation,
                    EndStation        = endStation,
                    Length            = len,
                    Area              = area,
                    Volume            = vol,
                    Width             = width,
                    Thickness         = thick,
                    Slope             = slope
                });
            }

            IfcPavPostTrace.Write("intra-transfer-start", $"sources={solidSources.Count}");
            layersTransferred = TransferPsetsToCorridorLayers(model, objs, solidSources);
            IfcPavPostTrace.Write("intra-transfer-done", $"layersTransferred={layersTransferred}");

            IfcPavPostTrace.Write("ifc-commit-start");
            tr.Commit();
            IfcPavPostTrace.Write("ifc-commit-success");
            IfcPavPostTrace.Write("ifc-save-start");
            model.SaveAs(outputIfcPath);
            IfcPavPostTrace.Write("ifc-save-success");

            return (qtoUpdated, renamed, layersTransferred);
        }

        public static string BuildDescription(string camada, string material, string subassembly, string corridor, double? startStation, double? endStation)
        {
            string[] parts = new[]
            {
                camada?.Trim(),
                material?.Trim(),
                subassembly?.Trim(),
                corridor?.Trim(),
                BuildStationRange(startStation, endStation)
            }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            if (parts.Length == 0) return string.Empty;
            return string.Join(" | ", parts);
        }

        public static string BuildStationRange(double? startStation, double? endStation)
        {
            string start = FormatStation(startStation);
            string end = FormatStation(endStation);

            if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
                return $"Estacas: {start} a {end}";

            return !string.IsNullOrWhiteSpace(start)
                ? $"Estaca: {start}"
                : $"Estaca: {end}";
        }

        public static string FormatStation(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " m"
                : string.Empty;
        }

        public static string FirstNonEmpty(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate.Trim();
            }

            return string.Empty;
        }

        public static string ReadFirstString(IIfcPropertySet pset, params string[] propNames)
        {
            foreach (string propName in propNames)
            {
                string value = TryGetString(pset, propName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        public static string ReadFirstString(IIfcObject obj, params string[] propNames)
        {
            if (obj == null)
                return string.Empty;

            foreach (IIfcRelDefinesByProperties rel in obj.IsDefinedBy.OfType<IIfcRelDefinesByProperties>())
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet pset)
                {
                    string value = ReadFirstString(pset, propNames);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return string.Empty;
        }

        public static double? ReadFirstDouble(IIfcPropertySet pset, params string[] propNames)
        {
            foreach (string propName in propNames)
            {
                if (TryGetDouble(pset, propName, out double value))
                    return value;
            }

            return null;
        }

        public static string NormalizeCode(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");
        }

        public static bool LooksLikePavementCode(string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);

            return key.Contains("PAVE")
                || key.Contains("PAVIMENTO")
                || key.Contains("WEARING")
                || key.Contains("BINDER")
                || key.Contains("BASE")
                || key.Contains("SUBBASE")
                || key.Contains("SUB_BASE")
                || key.Contains("ACOSTAMENTO")
                || key.Contains("PASSEIO")
                || key.Contains("CALCADA")
                || key.Contains("GUIA")
                || key.Contains("MEIO_FIO")
                || key.Contains("MEIOFIO")
                || key.Contains("FRES")
                || key.Contains("MILL");
        }

        public static string ResolveFriendlyLayerName(string currentValue, string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);
            string current = (currentValue ?? string.Empty).Trim();
            string normalizedCurrent = NormalizeCode(current);

            if (!string.IsNullOrWhiteSpace(current) && normalizedCurrent != key && !IsGenericLayerCode(normalizedCurrent))
                return current;

            if (key.Contains("SUB_BASE") || key.Contains("SUBBASE"))
                return "Sub-base";

            if (key.Contains("PAVIMENTO2") || key.Contains("PAVE2") || key.Contains("BINDER"))
                return "Binder";

            if (key.Contains("BASE"))
                return "Base";

            if (key.Contains("PAVIMENTO1") || key.Contains("PAVE1") || key.Contains("WEARING"))
                return "Revestimento";

            if (key.Contains("PAVIMENTO") || key.Contains("PAVE"))
                return "Camada asfaltica";

            if (key.Contains("PASSEIO") || key.Contains("CALCADA"))
                return "Passeio";

            if (key.Contains("GUIA") || key.Contains("MEIO_FIO") || key.Contains("MEIOFIO"))
                return "Guia";

            if (key.Contains("ACOSTAMENTO"))
                return "Acostamento";

            if (key.Contains("FRES") || key.Contains("MILL"))
                return "Fresagem";

            return FirstNonEmpty(current, rawCodeName);
        }

        public static string ResolveLayerFunction(string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);

            if (key.Contains("SUB_BASE") || key.Contains("SUBBASE")) return "SUBBASE";
            if (key.Contains("BASE")) return "BASE";
            if (key.Contains("PAVIMENTO2") || key.Contains("PAVE2") || key.Contains("BINDER")) return "BINDER";
            if (key.Contains("PAVIMENTO1") || key.Contains("PAVE1") || key.Contains("WEARING")) return "WEARING";
            if (key.Contains("FRES") || key.Contains("MILL")) return "MILLING";
            if (key.Contains("PASSEIO") || key.Contains("CALCADA")) return "SIDEWALK";
            if (key.Contains("GUIA") || key.Contains("MEIO_FIO") || key.Contains("MEIOFIO")) return "CURB";
            if (key.Contains("ACOSTAMENTO")) return "SHOULDER";
            return "COURSE";
        }

        public static string ResolvePavementType(string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);

            if (key.Contains("SUB_BASE") || key.Contains("SUBBASE")) return "GRANULAR";
            if (key.Contains("BASE")) return "ESTRUTURAL";
            if (key.Contains("PAVIMENTO") || key.Contains("PAVE") || key.Contains("WEARING") || key.Contains("BINDER")) return "ASFALTICO";
            if (key.Contains("PASSEIO") || key.Contains("CALCADA") || key.Contains("GUIA") || key.Contains("MEIO_FIO") || key.Contains("MEIOFIO")) return "CONCRETO";
            if (key.Contains("FRES") || key.Contains("MILL")) return "REABILITACAO";
            return "PAVIMENTO";
        }

        public static string ResolveFriendlyMaterial(string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);

            if (key.Contains("SUB_BASE") || key.Contains("SUBBASE") || key.Contains("BASE"))
                return "Material granular";

            if (key.Contains("PAVIMENTO") || key.Contains("PAVE") || key.Contains("WEARING") || key.Contains("BINDER") || key.Contains("FRES") || key.Contains("MILL"))
                return "Mistura asfaltica";

            if (key.Contains("PASSEIO") || key.Contains("CALCADA") || key.Contains("GUIA") || key.Contains("MEIO_FIO") || key.Contains("MEIOFIO"))
                return "Concreto";

            return string.Empty;
        }

        public static string InferSide(string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);

            if (key.Contains("LEFT") || key.Contains("ESQ"))
                return "Left";

            if (key.Contains("RIGHT") || key.Contains("DIR") || key.Contains("LD"))
                return "Right";

            return string.Empty;
        }

        public static bool IsGenericLayerCode(string value)
        {
            switch (NormalizeCode(value))
            {
                case "BASE":
                case "SUBBASE":
                case "SUB_BASE":
                case "PAVE":
                case "PAVE1":
                case "PAVE2":
                case "PAVIMENTO":
                case "PAVIMENTO1":
                case "PAVIMENTO2":
                case "WEARING":
                case "BINDER":
                    return true;
                default:
                    return false;
            }
        }

        public static IIfcPropertySet FindPsetByPrefix(IIfcObject obj, string prefix)
        {
            IIfcRelDefinesByProperties[] rels = obj.IsDefinedBy
                .OfType<IIfcRelDefinesByProperties>()
                .ToArray();

            foreach (IIfcRelDefinesByProperties rel in rels)
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet ps)
                {
                    string name = SafeToString(ps.Name);
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return ps;
                }
            }

            return null;
        }

        public static string TryGetString(IIfcPropertySet pset, string propName)
        {
            if (pset == null) return string.Empty;

            IIfcPropertySingleValue prop = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propName, StringComparison.OrdinalIgnoreCase));

            if (prop?.NominalValue == null) return string.Empty;

            string raw = SafeToString(prop.NominalValue);
            return raw?.Trim() ?? string.Empty;
        }

        public static bool TryGetDouble(IIfcPropertySet pset, string propName, out double value)
        {
            value = 0.0;
            if (pset == null) return false;

            IIfcPropertySingleValue prop = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propName, StringComparison.OrdinalIgnoreCase));

            if (prop?.NominalValue == null) return false;

            string raw = SafeToString(prop.NominalValue);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            return TryExtractNumber(raw, out value);
        }

        public static bool TryExtractNumber(string raw, out double value)
        {
            value = 0.0;

            // pega "91.32", "91,32", "3,198 m³", etc.
            string token = Regex.Match(raw ?? string.Empty, @"-?\d[\d\.,]*").Value;
            if (string.IsNullOrWhiteSpace(token)) return false;

            // "0.176"
            if (token.Contains('.') && !token.Contains(','))
                return double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

            // "0,176"
            if (token.Contains(',') && !token.Contains('.'))
            {
                CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");
                if (double.TryParse(token, NumberStyles.Any, ptBr, out value)) return true;

                string normComma = token.Replace(',', '.');
                return double.TryParse(normComma, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            }

            // "1.234,56" ou "1,234.56"
            if (token.Contains('.') && token.Contains(','))
            {
                int lastDot = token.LastIndexOf('.');
                int lastComma = token.LastIndexOf(',');

                char decimalSep = lastComma > lastDot ? ',' : '.';
                char thousandSep = decimalSep == ',' ? '.' : ',';

                string normalized = token.Replace(thousandSep.ToString(), "");
                normalized = decimalSep == ',' ? normalized.Replace(',', '.') : normalized;

                return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            }

            return double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        public static bool AddLengthQuantity(Xbim.Ifc.IfcStore model, IIfcElementQuantity qto, string quantityName, double? value)
        {
            if (!value.HasValue)
                return false;

            var quantity = model.Instances.New<Xbim.Ifc4x3.QuantityResource.IfcQuantityLength>();
            quantity.Name = quantityName;
            quantity.LengthValue = new Xbim.Ifc4x3.MeasureResource.IfcLengthMeasure(value.Value);
            qto.Quantities.Add(quantity);
            return true;
        }

        public static bool AddAreaQuantity(Xbim.Ifc.IfcStore model, IIfcElementQuantity qto, string quantityName, double? value)
        {
            if (!value.HasValue)
                return false;

            var quantity = model.Instances.New<Xbim.Ifc4x3.QuantityResource.IfcQuantityArea>();
            quantity.Name = quantityName;
            quantity.AreaValue = new Xbim.Ifc4x3.MeasureResource.IfcAreaMeasure(value.Value);
            qto.Quantities.Add(quantity);
            return true;
        }

        public static bool AddVolumeQuantity(Xbim.Ifc.IfcStore model, IIfcElementQuantity qto, string quantityName, double? value)
        {
            if (!value.HasValue)
                return false;

            var quantity = model.Instances.New<Xbim.Ifc4x3.QuantityResource.IfcQuantityVolume>();
            quantity.Name = quantityName;
            quantity.VolumeValue = new Xbim.Ifc4x3.MeasureResource.IfcVolumeMeasure(value.Value);
            qto.Quantities.Add(quantity);
            return true;
        }

        public static IIfcElementQuantity GetOrCreateElementQuantity(Xbim.Ifc.IfcStore model, IIfcObject obj, string qtoName)
        {
            foreach (IIfcRelDefinesByProperties rel in obj.IsDefinedBy.OfType<IIfcRelDefinesByProperties>())
            {
                if (rel.RelatingPropertyDefinition is IIfcElementQuantity existing &&
                    string.Equals(SafeToString(existing.Name), qtoName, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }

            var newQto = model.Instances.New<Xbim.Ifc4x3.ProductExtension.IfcElementQuantity>();
            newQto.Name = qtoName;
            newQto.Description = "QTO Pavimentação (gerado a partir do Pset C - Propriedades Fisicas)";

            var relNew = model.Instances.New<Xbim.Ifc4x3.Kernel.IfcRelDefinesByProperties>();
            relNew.RelatingPropertyDefinition = newQto;
            relNew.RelatedObjects.Add((Xbim.Ifc4x3.Kernel.IfcObjectDefinition)obj);

            return newQto;
        }

        public static IIfcPropertySet GetOrCreatePropertySet(Xbim.Ifc.IfcStore model, IIfcObject obj, string psetName, string description)
        {
            foreach (IIfcRelDefinesByProperties rel in obj.IsDefinedBy.OfType<IIfcRelDefinesByProperties>())
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet existing &&
                    string.Equals(SafeToString(existing.Name), psetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(description))
                        existing.Description = description;

                    return existing;
                }
            }

            var newPset = model.Instances.New<Xbim.Ifc4x3.Kernel.IfcPropertySet>();
            newPset.Name = psetName;
            newPset.Description = description;

            var relNew = model.Instances.New<Xbim.Ifc4x3.Kernel.IfcRelDefinesByProperties>();
            relNew.RelatingPropertyDefinition = newPset;
            relNew.RelatedObjects.Add((Xbim.Ifc4x3.Kernel.IfcObjectDefinition)obj);

            return newPset;
        }

        public static IIfcPropertySingleValue GetOrCreateSingleValueProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName)
        {
            IIfcPropertySingleValue existing = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propertyName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            var property = model.Instances.New<Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue>();
            property.Name = propertyName;
            pset.HasProperties.Add(property);
            return property;
        }

        public static void RemoveProperty(IIfcPropertySet pset, string propertyName)
        {
            IIfcPropertySingleValue existing = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propertyName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                pset.HasProperties.Remove(existing);
        }

        public static void SetTextProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
            property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcLabel(value.Trim());
        }

        public static void SetLengthProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
            property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcLengthMeasure(value.Value);
        }

        public static void SetAreaProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
            property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcAreaMeasure(value.Value);
        }

        public static void SetVolumeProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
            property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcVolumeMeasure(value.Value);
        }

        public static void SetRealProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
            property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcReal(value.Value);
        }

        public static string SafeToString(object value)
        {
            if (value == null) return string.Empty;
            return value.ToString() ?? string.Empty;
        }


        public static string FormatExceptionChain(System.Exception ex)
        {
            StringBuilder sb = new StringBuilder();

            while (ex != null)
            {
                if (sb.Length > 0)
                    sb.Append(" | INNER: ");

                sb.Append(ex.GetType().Name);
                sb.Append(": ");
                sb.Append(ex.Message);
                ex = ex.InnerException;
            }

            return sb.ToString();
        }

        // ── Intra-file transfer: solidos → camadas do corredor ──────────────────

        public sealed class SolidSourceInfo
        {
            public string  CorridorName      { get; set; }
            public string  RegionName        { get; set; }
            public string  LaneName          { get; set; }
            public string  CodeName          { get; set; }
            public string  Side              { get; set; }
            public string  Discipline        { get; set; }
            public string  FriendlyLayerName { get; set; }
            public string  FunctionLayer     { get; set; }
            public string  PavementType      { get; set; }
            public string  Material          { get; set; }
            public string  Status            { get; set; }
            public double? StartStation      { get; set; }
            public double? EndStation        { get; set; }
            public double? Length            { get; set; }
            public double? Area              { get; set; }
            public double? Volume            { get; set; }
            public double? Width             { get; set; }
            public double? Thickness         { get; set; }
            public double? Slope             { get; set; }
        }

        public static int TransferPsetsToCorridorLayers(
            Xbim.Ifc.IfcStore model,
            IIfcObject[] allObjects,
            IReadOnlyList<SolidSourceInfo> solidSources)
        {
            if (solidSources.Count == 0)
                return 0;

            // Índices de correspondência em ordem decrescente de especificidade
            Dictionary<string, SolidSourceInfo> byFullKey         = new Dictionary<string, SolidSourceInfo>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, SolidSourceInfo> byRegionLaneSideCode = new Dictionary<string, SolidSourceInfo>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, SolidSourceInfo> byRegionLaneCode  = new Dictionary<string, SolidSourceInfo>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, SolidSourceInfo> byLaneCode        = new Dictionary<string, SolidSourceInfo>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, SolidSourceInfo> byCodeOnly        = new Dictionary<string, SolidSourceInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, SolidSourceInfo> group in solidSources
                .GroupBy(s => BuildTransferKey(s.CorridorName, s.RegionName, s.LaneName, s.CodeName, s.Side)))
            {
                SolidSourceInfo agg = AggregateSolidSources(group.ToList());

                TryAdd(byFullKey,            BuildTransferKey(agg.CorridorName, agg.RegionName, agg.LaneName, agg.CodeName, agg.Side), agg);
                TryAdd(byRegionLaneSideCode, BuildTransferKey(agg.RegionName,   agg.LaneName,   agg.CodeName, agg.Side),               agg);
                TryAdd(byRegionLaneCode,     BuildTransferKey(agg.RegionName,   agg.LaneName,   agg.CodeName),                         agg);
                TryAdd(byLaneCode,           BuildTransferKey(agg.LaneName,     agg.CodeName),                                         agg);
                TryAdd(byCodeOnly,           NormalizeTransferKey(agg.CodeName),                                                       agg);
            }

            int transferred = 0;

            foreach (IIfcObject obj in allObjects)
            {
                IIfcPropertySet corridorShapeInfo = FindPsetByPrefix(obj, "Corridor Shape Information");
                IIfcPropertySet corridorIdentity  = FindPsetByPrefix(obj, "Corridor Identity");
                IIfcPropertySet corridorModelInfo = FindPsetByPrefix(obj, "Corridor Model Information");

                // Apenas objetos com PSets nativos do Civil 3D (camadas do corredor)
                if (corridorShapeInfo == null && corridorIdentity == null && corridorModelInfo == null)
                    continue;

                // Pula se o primeiro passo já preencheu Pset_Rodoviario (é um sólido)
                if (FindPsetByPrefix(obj, "Pset_Rodoviario") != null)
                    continue;

                string codeName = FirstNonEmpty(
                    ReadFirstString(corridorShapeInfo, "CodeName"),
                    ReadFirstString(corridorIdentity,  "CodeName"),
                    SafeToString(obj.Name));

                if (string.IsNullOrWhiteSpace(codeName))
                    continue;

                string laneName = FirstNonEmpty(
                    ReadFirstString(corridorIdentity,  "SubassemblyName", "AssemblyName"),
                    ReadFirstString(corridorShapeInfo, "SubassemblyName"));

                string regionName = FirstNonEmpty(
                    ReadFirstString(corridorModelInfo, "RegionName"),
                    ReadFirstString(corridorIdentity,  "RegionGuid", "RegionName"));

                string corridorName = FirstNonEmpty(
                    ReadFirstString(corridorModelInfo, "CorridorName", "BaselineName"),
                    ReadFirstString(corridorIdentity,  "CorridorName"));

                string side = FirstNonEmpty(
                    ReadFirstString(corridorShapeInfo, "Side"),
                    ReadFirstString(corridorIdentity,  "Side"),
                    InferSide(laneName),
                    InferSide(codeName));

                SolidSourceInfo source = null;

                if (source == null) byFullKey.TryGetValue(            BuildTransferKey(corridorName, regionName, laneName, codeName, side), out source);
                if (source == null) byRegionLaneSideCode.TryGetValue( BuildTransferKey(regionName, laneName, codeName, side),               out source);
                if (source == null) byRegionLaneCode.TryGetValue(     BuildTransferKey(regionName, laneName, codeName),                     out source);
                if (source == null) byLaneCode.TryGetValue(           BuildTransferKey(laneName, codeName),                                 out source);
                if (source == null) byCodeOnly.TryGetValue(           NormalizeTransferKey(codeName),                                      out source);

                if (source == null)
                    continue;

                ApplySourceToCorridorLayer(model, obj, source,
                    FirstNonEmpty(corridorName, source.CorridorName),
                    FirstNonEmpty(regionName,   source.RegionName),
                    FirstNonEmpty(laneName,     source.LaneName),
                    FirstNonEmpty(side,         source.Side));

                transferred++;
            }

            return transferred;
        }

        public static SolidSourceInfo AggregateSolidSources(List<SolidSourceInfo> group)
        {
            SolidSourceInfo rep = group
                .OrderByDescending(x => x.Length    ?? 0)
                .ThenByDescending( x => x.Volume    ?? 0)
                .ThenByDescending( x => x.Area      ?? 0)
                .First();

            return new SolidSourceInfo
            {
                CorridorName      = rep.CorridorName,
                RegionName        = rep.RegionName,
                LaneName          = rep.LaneName,
                CodeName          = rep.CodeName,
                Side              = rep.Side,
                Discipline        = rep.Discipline,
                FriendlyLayerName = rep.FriendlyLayerName,
                FunctionLayer     = rep.FunctionLayer,
                PavementType      = rep.PavementType,
                Material          = rep.Material,
                Status            = rep.Status,
                StartStation      = group.Any(x => x.StartStation.HasValue) ? group.Where(x => x.StartStation.HasValue).Min(x => x.StartStation.Value) : (double?)null,
                EndStation        = group.Any(x => x.EndStation.HasValue)   ? group.Where(x => x.EndStation.HasValue).Max(x => x.EndStation.Value)     : (double?)null,
                Length            = group.Any(x => x.Length.HasValue)       ? (double?)group.Where(x => x.Length.HasValue).Sum(x => x.Length.Value)    : null,
                Area              = group.Any(x => x.Area.HasValue)         ? (double?)group.Where(x => x.Area.HasValue).Sum(x => x.Area.Value)        : null,
                Volume            = group.Any(x => x.Volume.HasValue)       ? (double?)group.Where(x => x.Volume.HasValue).Sum(x => x.Volume.Value)    : null,
                Width             = rep.Width,
                Thickness         = rep.Thickness,
                Slope             = rep.Slope
            };
        }

        public static void ApplySourceToCorridorLayer(
            Xbim.Ifc.IfcStore model,
            IIfcObject obj,
            SolidSourceInfo source,
            string resolvedCorridorName,
            string resolvedRegionName,
            string resolvedLaneName,
            string resolvedSide)
        {
            string camada     = FirstNonEmpty(source.FriendlyLayerName, source.CodeName);
            string codigoObj  = FirstNonEmpty(source.CodeName, camada);
            string displayName = FirstNonEmpty(camada, codigoObj, resolvedRegionName, SafeToString(obj.Name));
            string description = BuildDescription(camada, source.Material, resolvedLaneName, resolvedCorridorName, source.StartStation, source.EndStation);

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                obj.Name = displayName;
                if (obj is IIfcElement el)
                    el.Tag = codigoObj;
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                obj.ObjectType   = displayName;
                obj.Description  = description;
            }

            IIfcPropertySet psetRodoviario = GetOrCreatePropertySet(model, obj, "Pset_Rodoviario", "Metadados rodoviarios transferidos dos solidos do corredor.");
            SetTextProperty(model, psetRodoviario, "Segmento",                   resolvedCorridorName);
            SetTextProperty(model, psetRodoviario, "Trecho",                     resolvedCorridorName);
            SetTextProperty(model, psetRodoviario, "CodigoObjeto",               codigoObj);
            SetTextProperty(model, psetRodoviario, "CodeName",                   source.CodeName);
            SetTextProperty(model, psetRodoviario, "SubassemblyName",            resolvedLaneName);
            SetTextProperty(model, psetRodoviario, "NomeCorredor",               resolvedCorridorName);
            SetTextProperty(model, psetRodoviario, "RegionName",                 resolvedRegionName);
            SetTextProperty(model, psetRodoviario, "Side",                       resolvedSide);
            SetTextProperty(model, psetRodoviario, "Situacao",                   FirstNonEmpty(source.Status, "Projeto"));
            SetLengthProperty(model, psetRodoviario, "EstaqueamentoInicial",     source.StartStation);
            SetLengthProperty(model, psetRodoviario, "EstaqueamentoFinal",       source.EndStation);
            SetTextProperty(model, psetRodoviario, "EstaqueamentoInicialTexto",  FormatStation(source.StartStation));
            SetTextProperty(model, psetRodoviario, "EstaqueamentoFinalTexto",    FormatStation(source.EndStation));
            SetTextProperty(model, psetRodoviario, "IntervaloEstacas",           BuildStationRange(source.StartStation, source.EndStation));
            SetTextProperty(model, psetRodoviario, "LRMName",                   "Estaqueamento");
            SetTextProperty(model, psetRodoviario, "EstagioProjeto",             FirstNonEmpty(source.Status, "Projeto"));

            IIfcPropertySet psetPavimentacao = GetOrCreatePropertySet(model, obj, "Pset_Pavimentacao", "Metadados de pavimentacao transferidos dos solidos do corredor.");
            SetTextProperty(model, psetPavimentacao, "Disciplina",               FirstNonEmpty(source.Discipline, "Pavimentação"));
            SetTextProperty(model, psetPavimentacao, "Faixa",                   FirstNonEmpty(resolvedLaneName, resolvedSide));
            SetTextProperty(model, psetPavimentacao, "Camada",                  FirstNonEmpty(source.CodeName, camada));
            SetTextProperty(model, psetPavimentacao, "FuncaoCamada",            FirstNonEmpty(source.FunctionLayer, ResolveLayerFunction(source.CodeName)));
            SetTextProperty(model, psetPavimentacao, "TipoPavimento",           FirstNonEmpty(source.PavementType, ResolvePavementType(source.CodeName)));
            SetTextProperty(model, psetPavimentacao, "Material",                FirstNonEmpty(source.Material, ResolveFriendlyMaterial(source.CodeName)));
            SetLengthProperty(model, psetPavimentacao, "EspessuraProjeto",      source.Thickness);
            SetLengthProperty(model, psetPavimentacao, "LarguraProjeto",        source.Width);
            SetLengthProperty(model, psetPavimentacao, "ComprimentoProjeto",    source.Length);
            SetAreaProperty(model, psetPavimentacao, "AreaProjeto",             source.Area);
            SetVolumeProperty(model, psetPavimentacao, "VolumeProjeto",         source.Volume);
            SetRealProperty(model, psetPavimentacao, "CrossfallProjeto",        source.Slope);
            SetTextProperty(model, psetPavimentacao, "EstaqueamentoInicialTexto", FormatStation(source.StartStation));
            SetTextProperty(model, psetPavimentacao, "EstaqueamentoFinalTexto",   FormatStation(source.EndStation));

            IIfcPropertySet psetPavementCommon = GetOrCreatePropertySet(model, obj, "Pset_PavementCommon", "Pset padrao para estruturas de pavimento.");
            SetTextProperty(model, psetPavementCommon, "Reference",             FirstNonEmpty(resolvedCorridorName, camada));
            SetTextProperty(model, psetPavementCommon, "Status",                FirstNonEmpty(source.Status, "Projeto"));
            SetLengthProperty(model, psetPavementCommon, "NominalThicknessEnd", source.Thickness);
            SetRealProperty(model, psetPavementCommon, "StructuralSlope",       source.Slope);
            SetTextProperty(model, psetPavementCommon, "StructuralSlopeType",   "Crossfall");
            SetLengthProperty(model, psetPavementCommon, "NominalWidth",        source.Width);
            SetLengthProperty(model, psetPavementCommon, "NominalLength",       source.Length);
            SetLengthProperty(model, psetPavementCommon, "NominalThickness",    source.Thickness);

            IIfcPropertySet psetPavementSurface = GetOrCreatePropertySet(model, obj, "Pset_PavementSurfaceCommon", "Pset padrao para a superficie de pavimento.");
            SetTextProperty(model, psetPavementSurface, "PavementTexture",      camada);

            IIfcPropertySet psetStationing = GetOrCreatePropertySet(model, obj, "Pset_Stationing", "Pset padrao de estacas e progressivas.");
            SetLengthProperty(model, psetStationing, "IncomingStation",         source.StartStation ?? source.EndStation);
            SetLengthProperty(model, psetStationing, "Station",                 source.EndStation   ?? source.StartStation);
            SetTextProperty(model, psetStationing, "HasIncreasingStation",      "true");
            SetTextProperty(model, psetStationing, "StationInterval",           BuildStationRange(source.StartStation, source.EndStation));

            IIfcPropertySet psetLinearReferencing = GetOrCreatePropertySet(model, obj, "Pset_LinearReferencingMethod", "Pset padrao de metodo de referenciacao linear.");
            SetTextProperty(model, psetLinearReferencing, "LRMName",            "Estaqueamento");
            SetTextProperty(model, psetLinearReferencing, "LRMType",            "CHAINAGE");
            SetTextProperty(model, psetLinearReferencing, "LRMUnit",            "m");
        }

        public static string BuildTransferKey(params string[] parts)
        {
            return string.Join("|", parts.Select(NormalizeTransferKey));
        }

        public static string NormalizeTransferKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            string clean = sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
            return Regex.Replace(clean, @"[^A-Z0-9]+", "_").Trim('_');
        }

        public static void TryAdd(Dictionary<string, SolidSourceInfo> dict, string key, SolidSourceInfo value)
        {
            if (!string.IsNullOrWhiteSpace(key) && !dict.ContainsKey(key))
                dict[key] = value;
        }

        // ── /Intra-file transfer ─────────────────────────────────────────────────

        public static void InitializeIfcServices()
        {
            global::AutomacoesCivil3D.XbimServiceBootstrap.EnsureInitialized();
        }
    }
}
