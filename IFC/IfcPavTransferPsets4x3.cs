using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Common.Step21;
using Xbim.Ifc4.Interfaces;
using Xbim.IO;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
    public static class IfcPavTransferPsets4x3
    {
        private static readonly string[] SourcePsetNames =
        {
            "Pset_Rodoviario",
            "Pset_Pavimentacao",
            "Pset_PavementCommon",
            "Pset_PavementSurfaceCommon",
            "Pset_Stationing",
            "Pset_LinearReferencingMethod"
        };

        [CommandMethod("SIFC_PAV_TRANSFERIR_PSETS_4X3", CommandFlags.Session)]
        public static void TransferirPsetsPavimentacaoIfc4x3()
        {
            IfcPavTransferTrace.Reset();
            IfcPavTransferTrace.Write("command-enter");

            Document civilDoc = Application.DocumentManager.MdiActiveDocument;
            Editor docEditor = civilDoc?.Editor ?? Manager.DocEditor;
            if (docEditor == null)
            {
                IfcPavTransferTrace.Write("command-no-editor");
                Application.ShowAlertDialog("Nao ha desenho ativo para executar a transferencia de Psets IFC 4x3.");
                return;
            }

            try
            {
                string sourceIfcPath = PromptIfcOpen(docEditor, "\nSelecione o IFC 4x3 fonte (solidos com Psets):");
                if (string.IsNullOrWhiteSpace(sourceIfcPath))
                {
                    IfcPavTransferTrace.Write("command-cancel-source");
                    return;
                }

                string targetIfcPath = PromptIfcOpen(docEditor, "\nSelecione o IFC 4x3 alvo (corredor exportado):");
                if (string.IsNullOrWhiteSpace(targetIfcPath))
                {
                    IfcPavTransferTrace.Write("command-cancel-target");
                    return;
                }

                string outputIfcPath = PromptIfcSave(docEditor, "\nSalvar IFC 4x3 de saida (Psets transferidos):");
                if (string.IsNullOrWhiteSpace(outputIfcPath))
                {
                    IfcPavTransferTrace.Write("command-cancel-save");
                    return;
                }

                IfcPavTransferTrace.Write("command-dispatch-worker", $"source={sourceIfcPath} | target={targetIfcPath} | output={outputIfcPath}");
                TransferResult result = Transfer(sourceIfcPath, targetIfcPath, outputIfcPath);

                docEditor.WriteMessage(
                    $"\nOK (IFC4x3). Fonte: {result.SourceObjects} objetos lidos | Grupos: {result.SourceGroups} | " +
                    $"Alvo: {result.TargetCandidates} candidatos | Transferidos: {result.Transferred} | " +
                    $"Sem match: {result.Unmatched} | Ambiguos: {result.Ambiguous} | Saida: {result.OutputPath}\n");
                IfcPavTransferTrace.Write("command-success", $"sourceObjects={result.SourceObjects} | groups={result.SourceGroups} | targetCandidates={result.TargetCandidates} | transferred={result.Transferred} | unmatched={result.Unmatched} | ambiguous={result.Ambiguous}");

                foreach (string warning in result.Warnings.Take(20))
                    docEditor.WriteMessage("[AVISO] " + warning + "\n");
            }
            catch (Exception ex)
            {
                IfcPavTransferTrace.Write("command-autocad-exception", FormatExceptionChain(ex));
                docEditor.WriteMessage($"\n[AutoCAD] Erro: {ex.Message}\n");
            }
            catch (System.Exception ex)
            {
                IfcPavTransferTrace.Write("command-dotnet-exception", FormatExceptionChain(ex));
                docEditor.WriteMessage($"\n[.NET] Erro: {FormatExceptionChain(ex)}\n");
            }
        }

        private static TransferResult Transfer(string sourceIfcPath, string targetIfcPath, string outputIfcPath)
        {
            IfcPavTransferTrace.Write("worker-start", $"source={sourceIfcPath} | target={targetIfcPath} | output={outputIfcPath}");
            InitializeIfcServices();
            IfcPavTransferTrace.Write("worker-ifc-init-success");

            Xbim.Ifc.XbimEditorCredentials editor = new Xbim.Ifc.XbimEditorCredentials
            {
                ApplicationDevelopersName = "AutomacoesCivil3D",
                ApplicationFullName = "Pavimentacao IFC4x3 Transfer",
                ApplicationIdentifier = "AutomacoesCivil3D.PAVIFCTRANSFER",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Gleison",
                EditorsGivenName = "Engenheiro",
                EditorsOrganisationName = "AutomacoesCivil3D"
            };

            TransferResult result = new TransferResult
            {
                OutputPath = outputIfcPath
            };

            using (Xbim.Ifc.IfcStore sourceModel = Xbim.Ifc.IfcStore.Open(sourceIfcPath, editorDetails: editor, accessMode: XbimDBAccess.Read))
            {
                IfcPavTransferTrace.Write("worker-source-open", $"schema={sourceModel.SchemaVersion}");
                List<SourceGroup> sourceGroups = BuildSourceGroups(sourceModel, result);
                IfcPavTransferTrace.Write("worker-source-groups", $"objects={result.SourceObjects} | groups={result.SourceGroups}");

                using (Xbim.Ifc.IfcStore targetModel = Xbim.Ifc.IfcStore.Open(targetIfcPath, editorDetails: editor, accessMode: XbimDBAccess.ReadWrite))
                {
                    XbimSchemaVersion schema = targetModel.SchemaVersion;
                    IfcPavTransferTrace.Write("worker-target-open", $"schema={schema}");
                    if (schema != XbimSchemaVersion.Ifc4 && schema != XbimSchemaVersion.Ifc4x3)
                        throw new System.Exception($"Schema IFC nao suportado: {schema}. Apenas IFC4 e IFC4x3 sao aceitos.");

                    using (ITransaction tr = targetModel.BeginTransaction("Transferir Psets de pavimentacao"))
                    {
                        IIfcObject[] targetObjects = targetModel.Instances
                            .OfType<IIfcObject>()
                            .ToArray();
                        IfcPavTransferTrace.Write("worker-target-objects", $"count={targetObjects.Length}");

                        foreach (IIfcObject obj in targetObjects)
                        {
                            TargetDescriptor target = BuildTargetDescriptor(obj);
                            if (!target.IsCandidate)
                                continue;

                            result.TargetCandidates++;

                            MatchOutcome match = MatchSourceGroup(sourceGroups, target);
                            if (match.Group == null)
                            {
                                if (!string.IsNullOrWhiteSpace(match.Warning))
                                {
                                    result.Warnings.Add(match.Warning);
                                    if (match.IsAmbiguous)
                                        result.Ambiguous++;
                                    else
                                        result.Unmatched++;
                                }

                                continue;
                            }

                            ApplyAggregateToTarget(targetModel, obj, match.Group.Aggregate);
                            result.Transferred++;
                        }

                        tr.Commit();
                        IfcPavTransferTrace.Write("worker-commit-success");
                        targetModel.SaveAs(outputIfcPath);
                        IfcPavTransferTrace.Write("worker-save-success");
                    }
                }
            }

            return result;
        }

        private static List<SourceGroup> BuildSourceGroups(Xbim.Ifc.IfcStore sourceModel, TransferResult result)
        {
            List<SourceRecord> records = new List<SourceRecord>();

            foreach (IIfcObject obj in sourceModel.Instances.OfType<IIfcObject>())
            {
                SourceRecord record = BuildSourceRecord(obj);
                if (!record.HasTransferData)
                    continue;

                records.Add(record);
            }

            result.SourceObjects = records.Count;

            List<SourceGroup> groups = records
                .GroupBy(record => BuildFullKey(record.CorridorName, record.RegionName, record.LaneName, record.CodeName, record.Side))
                .Select(group => BuildSourceGroup(group.Key, group.ToList(), result.Warnings))
                .OrderBy(group => group.Aggregate.RegionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Aggregate.LaneName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Aggregate.CodeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.SourceGroups = groups.Count;
            return groups;
        }

        private static SourceGroup BuildSourceGroup(string fullKey, List<SourceRecord> members, ICollection<string> warnings)
        {
            SourceRecord representative = SelectRepresentative(members);

            SourceRecord aggregate = new SourceRecord
            {
                CorridorName = ResolveDominantText(members.Select(x => x.CorridorName), representative.CorridorName),
                RegionName = ResolveDominantText(members.Select(x => x.RegionName), representative.RegionName),
                LaneName = ResolveDominantText(members.Select(x => x.LaneName), representative.LaneName),
                CodeName = ResolveDominantText(members.Select(x => x.CodeName), representative.CodeName),
                Side = ResolveDominantText(members.Select(x => x.Side), representative.Side),
                Discipline = ResolveDominantText(members.Select(x => x.Discipline), FirstNonEmpty(representative.Discipline, "Pavimentacao")),
                FriendlyLayerName = ResolveDominantText(members.Select(x => x.FriendlyLayerName), representative.FriendlyLayerName),
                FunctionLayer = ResolveDominantText(members.Select(x => x.FunctionLayer), FirstNonEmpty(representative.FunctionLayer, ResolveLayerFunction(representative.CodeName))),
                PavementType = ResolveDominantText(members.Select(x => x.PavementType), FirstNonEmpty(representative.PavementType, ResolvePavementType(representative.CodeName))),
                Material = ResolveDominantText(members.Select(x => x.Material), FirstNonEmpty(representative.Material, ResolveFriendlyMaterial(representative.CodeName))),
                Status = ResolveDominantText(members.Select(x => x.Status), FirstNonEmpty(representative.Status, "Projeto")),
                Reference = ResolveDominantText(members.Select(x => x.Reference), FirstNonEmpty(representative.Reference, representative.CorridorName, representative.FriendlyLayerName)),
                LrmName = ResolveDominantText(members.Select(x => x.LrmName), FirstNonEmpty(representative.LrmName, "Estaqueamento")),
                LrmType = ResolveDominantText(members.Select(x => x.LrmType), FirstNonEmpty(representative.LrmType, "CHAINAGE")),
                LrmUnit = ResolveDominantText(members.Select(x => x.LrmUnit), FirstNonEmpty(representative.LrmUnit, "m")),
                StartStation = members.Any(x => x.StartStation.HasValue)
                    ? members.Where(x => x.StartStation.HasValue).Min(x => x.StartStation.Value)
                    : null,
                EndStation = members.Any(x => x.EndStation.HasValue)
                    ? members.Where(x => x.EndStation.HasValue).Max(x => x.EndStation.Value)
                    : null,
                Length = SumValues(members.Select(x => x.Length)),
                Area = SumValues(members.Select(x => x.Area)),
                Volume = SumValues(members.Select(x => x.Volume)),
                Width = ResolveStableDouble(members, x => x.Width, 0.001, "LarguraProjeto/NominalWidth", fullKey, warnings, representative.Width),
                Thickness = ResolveStableDouble(members, x => x.Thickness, 0.001, "EspessuraProjeto/NominalThickness", fullKey, warnings, representative.Thickness),
                Slope = ResolveStableDouble(members, x => x.Slope, 0.0001, "CrossfallProjeto/StructuralSlope", fullKey, warnings, representative.Slope)
            };

            if (!aggregate.Length.HasValue && aggregate.StartStation.HasValue && aggregate.EndStation.HasValue)
                aggregate.Length = Math.Max(0.0, aggregate.EndStation.Value - aggregate.StartStation.Value);

            aggregate.FriendlyLayerName = FirstNonEmpty(
                aggregate.FriendlyLayerName,
                ResolveFriendlyLayerName(aggregate.FriendlyLayerName, aggregate.CodeName),
                aggregate.CodeName);

            aggregate.Reference = FirstNonEmpty(aggregate.Reference, aggregate.CorridorName, aggregate.FriendlyLayerName, aggregate.CodeName);

            return new SourceGroup
            {
                FullKey = fullKey,
                RegionLaneCodeSideKey = BuildRegionLaneCodeSideKey(aggregate.RegionName, aggregate.LaneName, aggregate.CodeName, aggregate.Side),
                RegionLaneCodeKey = BuildRegionLaneCodeKey(aggregate.RegionName, aggregate.LaneName, aggregate.CodeName),
                LaneCodeKey = BuildLaneCodeKey(aggregate.LaneName, aggregate.CodeName),
                Aggregate = aggregate,
                Members = members
            };
        }

        private static MatchOutcome MatchSourceGroup(IReadOnlyCollection<SourceGroup> sourceGroups, TargetDescriptor target)
        {
            string fullKey = BuildFullKey(target.CorridorName, target.RegionName, target.LaneName, target.CodeName, target.Side);
            SourceGroup exact = sourceGroups.FirstOrDefault(x => string.Equals(x.FullKey, fullKey, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return MatchOutcome.Success(exact);

            List<SourceGroup> regionLaneSide = sourceGroups
                .Where(x => string.Equals(x.RegionLaneCodeSideKey, BuildRegionLaneCodeSideKey(target.RegionName, target.LaneName, target.CodeName, target.Side), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (regionLaneSide.Count == 1)
                return MatchOutcome.Success(regionLaneSide[0]);
            if (regionLaneSide.Count > 1)
                return MatchOutcome.AmbiguousResult($"Match ambiguo por Regiao/Faixa/Codigo/Lado para '{target.DisplayLabel}'.");

            List<SourceGroup> regionLane = sourceGroups
                .Where(x => string.Equals(x.RegionLaneCodeKey, BuildRegionLaneCodeKey(target.RegionName, target.LaneName, target.CodeName), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (regionLane.Count == 1)
                return MatchOutcome.Success(regionLane[0]);
            if (regionLane.Count > 1)
                return MatchOutcome.AmbiguousResult($"Match ambiguo por Regiao/Faixa/Codigo para '{target.DisplayLabel}'.");

            List<SourceGroup> laneCode = sourceGroups
                .Where(x => string.Equals(x.LaneCodeKey, BuildLaneCodeKey(target.LaneName, target.CodeName), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (laneCode.Count == 1)
                return MatchOutcome.Success(laneCode[0]);
            if (laneCode.Count > 1)
                return MatchOutcome.AmbiguousResult($"Match ambiguo por Faixa/Codigo para '{target.DisplayLabel}'.");

            return MatchOutcome.NotFound($"Sem match para '{target.DisplayLabel}'.");
        }

        private static void ApplyAggregateToTarget(Xbim.Ifc.IfcStore model, IIfcObject target, SourceRecord source)
        {
            IIfcPropertySet psetRodoviario = GetOrCreatePropertySet(model, target, "Pset_Rodoviario", "Metadados rodoviarios transferidos do IFC de solidos.");
            SetTextProperty(model, psetRodoviario, "Segmento", source.CorridorName);
            SetTextProperty(model, psetRodoviario, "Trecho", source.CorridorName);
            SetTextProperty(model, psetRodoviario, "CodigoObjeto", FirstNonEmpty(source.CodeName, source.FriendlyLayerName));
            SetTextProperty(model, psetRodoviario, "CodeName", source.CodeName);
            SetTextProperty(model, psetRodoviario, "SubassemblyName", source.LaneName);
            SetTextProperty(model, psetRodoviario, "NomeCorredor", source.CorridorName);
            SetTextProperty(model, psetRodoviario, "RegionName", source.RegionName);
            SetTextProperty(model, psetRodoviario, "Side", source.Side);
            SetTextProperty(model, psetRodoviario, "Situacao", FirstNonEmpty(source.Status, "Projeto"));
            SetLengthProperty(model, psetRodoviario, "EstaqueamentoInicial", source.StartStation);
            SetLengthProperty(model, psetRodoviario, "EstaqueamentoFinal", source.EndStation);
            SetTextProperty(model, psetRodoviario, "EstaqueamentoInicialTexto", FormatStation(source.StartStation));
            SetTextProperty(model, psetRodoviario, "EstaqueamentoFinalTexto", FormatStation(source.EndStation));
            SetTextProperty(model, psetRodoviario, "IntervaloEstacas", BuildStationRange(source.StartStation, source.EndStation));
            SetTextProperty(model, psetRodoviario, "LRMName", FirstNonEmpty(source.LrmName, "Estaqueamento"));
            SetTextProperty(model, psetRodoviario, "EstagioProjeto", FirstNonEmpty(source.Status, "Projeto"));

            IIfcPropertySet psetPavimentacao = GetOrCreatePropertySet(model, target, "Pset_Pavimentacao", "Metadados de pavimentacao transferidos do IFC de solidos.");
            SetTextProperty(model, psetPavimentacao, "Disciplina", FirstNonEmpty(source.Discipline, "Pavimentacao"));
            SetTextProperty(model, psetPavimentacao, "Faixa", source.LaneName);
            SetTextProperty(model, psetPavimentacao, "Camada", source.CodeName);
            SetTextProperty(model, psetPavimentacao, "FuncaoCamada", FirstNonEmpty(source.FunctionLayer, ResolveLayerFunction(source.CodeName)));
            SetTextProperty(model, psetPavimentacao, "TipoPavimento", FirstNonEmpty(source.PavementType, ResolvePavementType(source.CodeName)));
            SetTextProperty(model, psetPavimentacao, "Material", FirstNonEmpty(source.Material, ResolveFriendlyMaterial(source.CodeName)));
            SetLengthProperty(model, psetPavimentacao, "EspessuraProjeto", source.Thickness);
            SetLengthProperty(model, psetPavimentacao, "LarguraProjeto", source.Width);
            SetLengthProperty(model, psetPavimentacao, "ComprimentoProjeto", source.Length);
            SetAreaProperty(model, psetPavimentacao, "AreaProjeto", source.Area);
            SetVolumeProperty(model, psetPavimentacao, "VolumeProjeto", source.Volume);
            SetRealProperty(model, psetPavimentacao, "CrossfallProjeto", source.Slope);
            SetTextProperty(model, psetPavimentacao, "EstaqueamentoInicialTexto", FormatStation(source.StartStation));
            SetTextProperty(model, psetPavimentacao, "EstaqueamentoFinalTexto", FormatStation(source.EndStation));

            IIfcPropertySet psetPavementCommon = GetOrCreatePropertySet(model, target, "Pset_PavementCommon", "Pset de pavimentacao transferido do IFC de solidos.");
            SetTextProperty(model, psetPavementCommon, "Reference", FirstNonEmpty(source.Reference, source.CorridorName, source.FriendlyLayerName, source.CodeName));
            SetTextProperty(model, psetPavementCommon, "Status", FirstNonEmpty(source.Status, "Projeto"));
            SetLengthProperty(model, psetPavementCommon, "NominalThicknessEnd", source.Thickness);
            SetRealProperty(model, psetPavementCommon, "StructuralSlope", source.Slope);
            SetTextProperty(model, psetPavementCommon, "StructuralSlopeType", "Crossfall");
            SetLengthProperty(model, psetPavementCommon, "NominalWidth", source.Width);
            SetLengthProperty(model, psetPavementCommon, "NominalLength", source.Length);
            SetLengthProperty(model, psetPavementCommon, "NominalThickness", source.Thickness);

            IIfcPropertySet psetPavementSurface = GetOrCreatePropertySet(model, target, "Pset_PavementSurfaceCommon", "Pset de superficie transferido do IFC de solidos.");
            SetTextProperty(model, psetPavementSurface, "PavementTexture", FirstNonEmpty(source.FriendlyLayerName, source.CodeName));

            IIfcPropertySet psetStationing = GetOrCreatePropertySet(model, target, "Pset_Stationing", "Pset de estacas transferido do IFC de solidos.");
            SetLengthProperty(model, psetStationing, "IncomingStation", source.StartStation ?? source.EndStation);
            SetLengthProperty(model, psetStationing, "Station", source.EndStation ?? source.StartStation);
            SetTextProperty(model, psetStationing, "HasIncreasingStation", "true");
            SetTextProperty(model, psetStationing, "StationInterval", BuildStationRange(source.StartStation, source.EndStation));

            IIfcPropertySet psetLinearReferencing = GetOrCreatePropertySet(model, target, "Pset_LinearReferencingMethod", "Pset de referenciacao linear transferido do IFC de solidos.");
            SetTextProperty(model, psetLinearReferencing, "LRMName", FirstNonEmpty(source.LrmName, "Estaqueamento"));
            SetTextProperty(model, psetLinearReferencing, "LRMType", FirstNonEmpty(source.LrmType, "CHAINAGE"));
            SetTextProperty(model, psetLinearReferencing, "LRMUnit", FirstNonEmpty(source.LrmUnit, "m"));
        }

        private static SourceRecord BuildSourceRecord(IIfcObject obj)
        {
            IIfcPropertySet psetRodoviario = FindPsetByPrefix(obj, "Pset_Rodoviario");
            IIfcPropertySet psetPavimentacao = FindPsetByPrefix(obj, "Pset_Pavimentacao");
            IIfcPropertySet psetPavementCommon = FindPsetByPrefix(obj, "Pset_PavementCommon");
            IIfcPropertySet psetPavementSurface = FindPsetByPrefix(obj, "Pset_PavementSurfaceCommon");
            IIfcPropertySet psetStationing = FindPsetByPrefix(obj, "Pset_Stationing");
            IIfcPropertySet psetLinearReferencing = FindPsetByPrefix(obj, "Pset_LinearReferencingMethod");
            IIfcPropertySet corridorIdentity = FindPsetByPrefix(obj, "Corridor Identity");
            IIfcPropertySet corridorModelInfo = FindPsetByPrefix(obj, "Corridor Model Information");
            IIfcPropertySet corridorShapeInfo = FindPsetByPrefix(obj, "Corridor Shape Information");
            IIfcPropertySet coordenacao = FindPsetByPrefix(obj, "COORD");

            List<string> hierarchy = GetMeaningfulHierarchyNames(obj);

            string codeName = FirstNonEmpty(
                ReadFirstString(psetRodoviario, "CodeName"),
                ReadFirstString(corridorShapeInfo, "CodeName"),
                ReadFirstString(psetPavimentacao, "Camada"),
                GetClassificationIdentification(obj),
                SafeToString(obj.Name));

            string laneName = FirstNonEmpty(
                ReadFirstString(psetPavimentacao, "Faixa"),
                ReadFirstString(psetRodoviario, "SubassemblyName"),
                ReadFirstString(corridorIdentity, "SubassemblyName", "AssemblyName"),
                ResolveLaneNameFromHierarchy(hierarchy));

            string regionName = FirstNonEmpty(
                ReadFirstString(psetRodoviario, "RegionName"),
                ReadFirstString(corridorModelInfo, "RegionName"),
                ReadFirstString(corridorIdentity, "RegionGuid"),
                ResolveRegionNameFromHierarchy(hierarchy));

            string corridorName = FirstNonEmpty(
                ReadFirstString(psetRodoviario, "NomeCorredor", "Segmento", "Trecho"),
                ReadFirstString(corridorModelInfo, "CorridorName", "BaselineName"),
                ResolveCorridorNameFromHierarchy(hierarchy));

            string side = FirstNonEmpty(
                ReadFirstString(psetRodoviario, "Side"),
                ReadFirstString(corridorShapeInfo, "Side"),
                InferSide(laneName),
                InferSide(codeName));

            SourceRecord record = new SourceRecord
            {
                CorridorName = corridorName,
                RegionName = regionName,
                LaneName = laneName,
                CodeName = codeName,
                Side = side,
                Discipline = FirstNonEmpty(ReadFirstString(psetPavimentacao, "Disciplina"), "Pavimentacao"),
                FriendlyLayerName = FirstNonEmpty(
                    ReadFirstString(psetPavementSurface, "PavementTexture"),
                    ReadFirstString(psetPavimentacao, "DescricaoCamada", "Descricao"),
                    ResolveFriendlyLayerName(ReadFirstString(psetPavimentacao, "Camada"), codeName)),
                FunctionLayer = FirstNonEmpty(
                    ReadFirstString(psetPavimentacao, "FuncaoCamada"),
                    ResolveLayerFunction(codeName)),
                PavementType = FirstNonEmpty(
                    ReadFirstString(psetPavimentacao, "TipoPavimento"),
                    ResolvePavementType(codeName)),
                Material = FirstNonEmpty(
                    ReadFirstString(psetPavimentacao, "Material"),
                    ResolveFriendlyMaterial(codeName)),
                Status = FirstNonEmpty(
                    ReadFirstString(psetPavementCommon, "Status"),
                    ReadFirstString(psetRodoviario, "Situacao"),
                    "Projeto"),
                Reference = FirstNonEmpty(
                    ReadFirstString(psetPavementCommon, "Reference"),
                    corridorName,
                    ReadFirstString(psetPavementSurface, "PavementTexture")),
                LrmName = ReadFirstString(psetLinearReferencing, "LRMName"),
                LrmType = ReadFirstString(psetLinearReferencing, "LRMType"),
                LrmUnit = ReadFirstString(psetLinearReferencing, "LRMUnit"),
                StartStation = ReadFirstDouble(psetRodoviario, "EstaqueamentoInicial")
                    ?? ReadFirstDouble(corridorIdentity, "StartStation")
                    ?? ReadFirstDouble(psetStationing, "IncomingStation"),
                EndStation = ReadFirstDouble(psetRodoviario, "EstaqueamentoFinal")
                    ?? ReadFirstDouble(corridorIdentity, "EndStation")
                    ?? ReadFirstDouble(psetStationing, "Station"),
                Length = ReadFirstDouble(psetPavimentacao, "ComprimentoProjeto")
                    ?? ReadFirstDouble(psetPavementCommon, "NominalLength")
                    ?? ReadFirstDouble(coordenacao, "COMPRIMENTO_SOLIDOS_CORREDOR"),
                Area = ReadFirstDouble(psetPavimentacao, "AreaProjeto"),
                Volume = ReadFirstDouble(psetPavimentacao, "VolumeProjeto"),
                Width = ReadFirstDouble(psetPavimentacao, "LarguraProjeto")
                    ?? ReadFirstDouble(psetPavementCommon, "NominalWidth"),
                Thickness = ReadFirstDouble(psetPavimentacao, "EspessuraProjeto")
                    ?? ReadFirstDouble(psetPavementCommon, "NominalThickness", "NominalThicknessEnd"),
                Slope = ReadFirstDouble(psetPavimentacao, "CrossfallProjeto")
                    ?? ReadFirstDouble(psetPavementCommon, "StructuralSlope")
            };

            bool hasAnySourcePset = SourcePsetNames.Any(name => FindPsetByPrefix(obj, name) != null);
            record.HasTransferData = hasAnySourcePset
                && !string.IsNullOrWhiteSpace(record.CodeName)
                && (LooksLikePavementCode(record.CodeName) || !string.IsNullOrWhiteSpace(record.Material) || !string.IsNullOrWhiteSpace(record.FunctionLayer));

            return record;
        }

        private static TargetDescriptor BuildTargetDescriptor(IIfcObject obj)
        {
            IIfcPropertySet psetRodoviario = FindPsetByPrefix(obj, "Pset_Rodoviario");
            IIfcPropertySet psetPavimentacao = FindPsetByPrefix(obj, "Pset_Pavimentacao");
            IIfcPropertySet corridorModelInfo = FindPsetByPrefix(obj, "Corridor Model Information");
            IIfcPropertySet corridorShapeInfo = FindPsetByPrefix(obj, "Corridor Shape Information");
            IIfcPropertySet corridorIdentity = FindPsetByPrefix(obj, "Corridor Identity");

            List<string> hierarchy = GetMeaningfulHierarchyNames(obj);

            string codeName = FirstNonEmpty(
                ReadFirstString(corridorShapeInfo, "CodeName"),
                ReadFirstString(psetRodoviario, "CodeName"),
                ReadFirstString(psetPavimentacao, "Camada"),
                GetClassificationIdentification(obj),
                SafeToString(obj.Name));

            string laneName = FirstNonEmpty(
                ReadFirstString(psetPavimentacao, "Faixa"),
                ReadFirstString(psetRodoviario, "SubassemblyName"),
                ResolveLaneNameFromHierarchy(hierarchy));

            string regionName = FirstNonEmpty(
                ReadFirstString(psetRodoviario, "RegionName"),
                ReadFirstString(corridorModelInfo, "RegionName"),
                ResolveRegionNameFromHierarchy(hierarchy));

            string corridorName = FirstNonEmpty(
                ReadFirstString(psetRodoviario, "NomeCorredor", "Segmento", "Trecho"),
                ReadFirstString(corridorModelInfo, "CorridorName", "BaselineName"),
                ResolveCorridorNameFromHierarchy(hierarchy));

            string side = FirstNonEmpty(
                ReadFirstString(corridorShapeInfo, "Side"),
                ReadFirstString(psetRodoviario, "Side"),
                ReadFirstString(corridorIdentity, "Side"),
                InferSide(laneName),
                InferSide(codeName));

            return new TargetDescriptor
            {
                CorridorName = corridorName,
                RegionName = regionName,
                LaneName = laneName,
                CodeName = codeName,
                Side = side,
                DisplayLabel = BuildDisplayLabel(corridorName, regionName, laneName, codeName),
                IsCandidate = !string.IsNullOrWhiteSpace(codeName)
                    && (!string.IsNullOrWhiteSpace(laneName) || !string.IsNullOrWhiteSpace(regionName))
            };
        }

        private static SourceRecord SelectRepresentative(IEnumerable<SourceRecord> members)
        {
            return members
                .OrderByDescending(x => x.Length ?? 0.0)
                .ThenByDescending(x => x.Volume ?? 0.0)
                .ThenByDescending(x => x.Area ?? 0.0)
                .First();
        }

        private static double? ResolveStableDouble(
            IEnumerable<SourceRecord> members,
            Func<SourceRecord, double?> selector,
            double tolerance,
            string propertyName,
            string groupKey,
            ICollection<string> warnings,
            double? fallback)
        {
            List<double> values = members
                .Select(selector)
                .Where(x => x.HasValue)
                .Select(x => x.Value)
                .ToList();

            if (values.Count == 0)
                return fallback;

            double min = values.Min();
            double max = values.Max();

            if (Math.Abs(max - min) <= tolerance)
                return values.Average();

            double dominant = values
                .GroupBy(x => Math.Round(x, 6))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.First())
                .First();

            warnings.Add($"Valores divergentes de '{propertyName}' no grupo '{groupKey}'. Valor usado: {dominant.ToString("0.######", CultureInfo.InvariantCulture)}.");
            return dominant;
        }

        private static double? SumValues(IEnumerable<double?> values)
        {
            List<double> present = values
                .Where(x => x.HasValue)
                .Select(x => x.Value)
                .ToList();

            if (present.Count == 0)
                return null;

            return present.Sum();
        }

        private static string ResolveDominantText(IEnumerable<string> values, string fallback)
        {
            List<string> present = values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            if (present.Count == 0)
                return fallback ?? string.Empty;

            return present
                .GroupBy(x => NormalizeName(x))
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.First().Length)
                .Select(g => g.First())
                .FirstOrDefault() ?? FirstNonEmpty(fallback);
        }

        private static string BuildDisplayLabel(string corridorName, string regionName, string laneName, string codeName)
        {
            string[] parts = new[]
            {
                corridorName?.Trim(),
                regionName?.Trim(),
                laneName?.Trim(),
                codeName?.Trim()
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            return parts.Length == 0 ? "(objeto sem chave)" : string.Join(" / ", parts);
        }

        private static string BuildFullKey(string corridorName, string regionName, string laneName, string codeName, string side)
        {
            return string.Join("|", new[]
            {
                NormalizeName(corridorName),
                NormalizeName(regionName),
                NormalizeName(laneName),
                NormalizeName(codeName),
                NormalizeName(side)
            });
        }

        private static string BuildRegionLaneCodeSideKey(string regionName, string laneName, string codeName, string side)
        {
            return string.Join("|", new[]
            {
                NormalizeName(regionName),
                NormalizeName(laneName),
                NormalizeName(codeName),
                NormalizeName(side)
            });
        }

        private static string BuildRegionLaneCodeKey(string regionName, string laneName, string codeName)
        {
            return string.Join("|", new[]
            {
                NormalizeName(regionName),
                NormalizeName(laneName),
                NormalizeName(codeName)
            });
        }

        private static string BuildLaneCodeKey(string laneName, string codeName)
        {
            return string.Join("|", new[]
            {
                NormalizeName(laneName),
                NormalizeName(codeName)
            });
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = RemoveDiacritics(value).ToUpperInvariant();
            normalized = Regex.Replace(normalized, @"[^A-Z0-9]+", "_").Trim('_');
            return normalized;
        }

        private static string RemoveDiacritics(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Normalize(NormalizationForm.FormD);
            StringBuilder builder = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string ResolveLaneNameFromHierarchy(IReadOnlyList<string> hierarchy)
        {
            if (hierarchy == null || hierarchy.Count < 2)
                return string.Empty;

            return hierarchy[hierarchy.Count - 2];
        }

        private static string ResolveRegionNameFromHierarchy(IReadOnlyList<string> hierarchy)
        {
            if (hierarchy == null || hierarchy.Count < 3)
                return string.Empty;

            return hierarchy[hierarchy.Count - 3];
        }

        private static string ResolveCorridorNameFromHierarchy(IReadOnlyList<string> hierarchy)
        {
            if (hierarchy == null || hierarchy.Count == 0)
                return string.Empty;

            string corridor = hierarchy.FirstOrDefault(x => NormalizeName(x).Contains("CORREDOR"));
            if (!string.IsNullOrWhiteSpace(corridor))
                return corridor;

            return hierarchy.FirstOrDefault() ?? string.Empty;
        }

        private static List<string> GetMeaningfulHierarchyNames(IIfcObject obj)
        {
            List<List<string>> paths = new List<List<string>>();
            CollectHierarchyPaths(obj, new HashSet<int>(), new List<string>(), paths, 0);

            List<string> best = paths
                .Select(path => path.Where(IsMeaningfulHierarchyName).ToList())
                .Where(path => path.Count > 0)
                .OrderByDescending(path => path.Count)
                .ThenByDescending(path => path.Sum(name => name.Length))
                .FirstOrDefault() ?? new List<string>();

            List<string> deduped = new List<string>();
            foreach (string name in best)
            {
                if (deduped.Count == 0 || !string.Equals(deduped[deduped.Count - 1], name, StringComparison.OrdinalIgnoreCase))
                    deduped.Add(name);
            }

            return deduped;
        }

        private static void CollectHierarchyPaths(
            IIfcObjectDefinition current,
            HashSet<int> visited,
            List<string> currentPath,
            ICollection<List<string>> paths,
            int depth)
        {
            if (current == null || depth > 20)
                return;

            if (!visited.Add(current.EntityLabel))
                return;

            string currentName = SafeToString(ReadPropertyValue(current, "Name"));
            List<string> nextPath = new List<string>(currentPath);
            if (!string.IsNullOrWhiteSpace(currentName))
                nextPath.Insert(0, currentName.Trim());

            List<IIfcObjectDefinition> parents = GetParentDefinitions(current);
            if (parents.Count == 0)
            {
                paths.Add(nextPath);
                return;
            }

            foreach (IIfcObjectDefinition parent in parents)
                CollectHierarchyPaths(parent, new HashSet<int>(visited), nextPath, paths, depth + 1);
        }

        private static List<IIfcObjectDefinition> GetParentDefinitions(IIfcObjectDefinition obj)
        {
            List<IIfcObjectDefinition> parents = new List<IIfcObjectDefinition>();

            foreach (object relation in ReadEnumerableValues(obj, "Decomposes", "Nests", "ContainedInStructure", "ContainedInSpatialStructure"))
            {
                object parent = ReadPropertyValue(relation, "RelatingObject")
                    ?? ReadPropertyValue(relation, "RelatingStructure");

                if (parent is IIfcObjectDefinition objectDefinition &&
                    parents.All(x => x.EntityLabel != objectDefinition.EntityLabel))
                {
                    parents.Add(objectDefinition);
                }
            }

            return parents;
        }

        private static IEnumerable<object> ReadEnumerableValues(object target, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                object value = ReadPropertyValue(target, propertyName);
                if (value == null)
                    continue;

                if (value is IEnumerable enumerable && !(value is string))
                {
                    foreach (object item in enumerable)
                    {
                        if (item != null)
                            yield return item;
                    }

                    continue;
                }

                yield return value;
            }
        }

        private static bool IsMeaningfulHierarchyName(string value)
        {
            string text = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = NormalizeName(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (normalized == "MESH")
                return false;

            if (normalized.StartsWith("IFC", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static string GetClassificationIdentification(IIfcObject obj)
        {
            if (obj == null)
                return string.Empty;

            foreach (IIfcRelAssociatesClassification rel in obj.HasAssociations.OfType<IIfcRelAssociatesClassification>())
            {
                string identification = GetClassificationIdentification(rel.RelatingClassification);
                if (!string.IsNullOrWhiteSpace(identification))
                    return identification;
            }

            return string.Empty;
        }

        private static string GetClassificationIdentification(object relatingClassification)
        {
            string identification = SafeToString(ReadPropertyValue(relatingClassification, "Identification"));
            if (!string.IsNullOrWhiteSpace(identification))
                return identification;

            return SafeToString(ReadPropertyValue(relatingClassification, "Name"));
        }

        private static string PromptIfcOpen(Editor docEditor, string message)
        {
            PromptOpenFileOptions opt = new PromptOpenFileOptions(message);
            opt.Filter = "IFC (*.ifc)|*.ifc";
            IfcPavTransferTrace.Write("dialog-open-show", message);
            PromptFileNameResult res = docEditor.GetFileNameForOpen(opt);
            IfcPavTransferTrace.Write("dialog-open-close", res.Status == PromptStatus.OK ? res.StringResult : res.Status.ToString());
            if (res.Status != PromptStatus.OK)
                return string.Empty;

            return res.StringResult;
        }

        private static string PromptIfcSave(Editor docEditor, string message)
        {
            PromptSaveFileOptions opt = new PromptSaveFileOptions(message);
            opt.Filter = "IFC (*.ifc)|*.ifc";
            IfcPavTransferTrace.Write("dialog-save-show", message);
            PromptFileNameResult res = docEditor.GetFileNameForSave(opt);
            IfcPavTransferTrace.Write("dialog-save-close", res.Status == PromptStatus.OK ? res.StringResult : res.Status.ToString());
            if (res.Status != PromptStatus.OK)
                return string.Empty;

            return res.StringResult;
        }

        private static string BuildStationRange(double? startStation, double? endStation)
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

        private static string FormatStation(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " m"
                : string.Empty;
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate.Trim();
            }

            return string.Empty;
        }

        private static string ReadFirstString(IIfcPropertySet pset, params string[] propNames)
        {
            foreach (string propName in propNames)
            {
                string value = TryGetString(pset, propName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static double? ReadFirstDouble(IIfcPropertySet pset, params string[] propNames)
        {
            foreach (string propName in propNames)
            {
                if (TryGetDouble(pset, propName, out double value))
                    return value;
            }

            return null;
        }

        private static string NormalizeCode(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");
        }

        private static bool LooksLikePavementCode(string rawCodeName)
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

        private static string ResolveFriendlyLayerName(string currentValue, string rawCodeName)
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

        private static string ResolveLayerFunction(string rawCodeName)
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

        private static string ResolvePavementType(string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);

            if (key.Contains("SUB_BASE") || key.Contains("SUBBASE")) return "GRANULAR";
            if (key.Contains("BASE")) return "ESTRUTURAL";
            if (key.Contains("PAVIMENTO") || key.Contains("PAVE") || key.Contains("WEARING") || key.Contains("BINDER")) return "ASFALTICO";
            if (key.Contains("PASSEIO") || key.Contains("CALCADA") || key.Contains("GUIA") || key.Contains("MEIO_FIO") || key.Contains("MEIOFIO")) return "CONCRETO";
            if (key.Contains("FRES") || key.Contains("MILL")) return "REABILITACAO";
            return "PAVIMENTO";
        }

        private static string ResolveFriendlyMaterial(string rawCodeName)
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

        private static string InferSide(string rawCodeName)
        {
            string key = NormalizeCode(rawCodeName);

            if (key.Contains("LEFT") || key.Contains("ESQ") || key.Contains("LE"))
                return "Left";

            if (key.Contains("RIGHT") || key.Contains("DIR") || key.Contains("LD"))
                return "Right";

            return string.Empty;
        }

        private static bool IsGenericLayerCode(string value)
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

        private static IIfcPropertySet FindPsetByPrefix(IIfcObject obj, string prefix)
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

        private static string TryGetString(IIfcPropertySet pset, string propName)
        {
            if (pset == null) return string.Empty;

            IIfcPropertySingleValue prop = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propName, StringComparison.OrdinalIgnoreCase));

            if (prop?.NominalValue == null) return string.Empty;

            string raw = SafeToString(prop.NominalValue);
            return raw?.Trim() ?? string.Empty;
        }

        private static bool TryGetDouble(IIfcPropertySet pset, string propName, out double value)
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

        private static bool TryExtractNumber(string raw, out double value)
        {
            value = 0.0;

            string token = Regex.Match(raw ?? string.Empty, @"-?\d[\d\.,]*").Value;
            if (string.IsNullOrWhiteSpace(token)) return false;

            if (token.Contains('.') && !token.Contains(','))
                return double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

            if (token.Contains(',') && !token.Contains('.'))
            {
                CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");
                if (double.TryParse(token, NumberStyles.Any, ptBr, out value)) return true;

                string normComma = token.Replace(',', '.');
                return double.TryParse(normComma, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            }

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

        private static IIfcPropertySet GetOrCreatePropertySet(Xbim.Ifc.IfcStore model, IIfcObject obj, string psetName, string description)
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

            if (model.SchemaVersion == XbimSchemaVersion.Ifc4x3)
            {
                var newPset = model.Instances.New<Xbim.Ifc4x3.Kernel.IfcPropertySet>();
                newPset.Name = psetName;
                newPset.Description = description;

                var relNew = model.Instances.New<Xbim.Ifc4x3.Kernel.IfcRelDefinesByProperties>();
                relNew.RelatingPropertyDefinition = newPset;
                relNew.RelatedObjects.Add((Xbim.Ifc4x3.Kernel.IfcObjectDefinition)obj);

                return newPset;
            }
            else
            {
                var newPset = model.Instances.New<Xbim.Ifc4.Kernel.IfcPropertySet>();
                newPset.Name = psetName;
                newPset.Description = description;

                var relNew = model.Instances.New<Xbim.Ifc4.Kernel.IfcRelDefinesByProperties>();
                relNew.RelatingPropertyDefinition = newPset;
                relNew.RelatedObjects.Add((Xbim.Ifc4.Kernel.IfcObjectDefinition)obj);

                return newPset;
            }
        }

        private static IIfcPropertySingleValue GetOrCreateSingleValueProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName)
        {
            IIfcPropertySingleValue existing = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propertyName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            IIfcPropertySingleValue property;
            if (model.SchemaVersion == XbimSchemaVersion.Ifc4x3)
            {
                var prop = model.Instances.New<Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue>();
                prop.Name = propertyName;
                property = prop;
            }
            else
            {
                var prop = model.Instances.New<Xbim.Ifc4.PropertyResource.IfcPropertySingleValue>();
                prop.Name = propertyName;
                property = prop;
            }

            pset.HasProperties.Add(property);
            return property;
        }

        private static void RemoveProperty(IIfcPropertySet pset, string propertyName)
        {
            IIfcPropertySingleValue existing = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propertyName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                pset.HasProperties.Remove(existing);
        }

        private static void SetTextProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            if (model.SchemaVersion == XbimSchemaVersion.Ifc4x3)
            {
                var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcLabel(value.Trim());
            }
            else
            {
                var property = (Xbim.Ifc4.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4.MeasureResource.IfcLabel(value.Trim());
            }
        }

        private static void SetLengthProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            if (model.SchemaVersion == XbimSchemaVersion.Ifc4x3)
            {
                var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcLengthMeasure(value.Value);
            }
            else
            {
                var property = (Xbim.Ifc4.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4.MeasureResource.IfcLengthMeasure(value.Value);
            }
        }

        private static void SetAreaProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            if (model.SchemaVersion == XbimSchemaVersion.Ifc4x3)
            {
                var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcAreaMeasure(value.Value);
            }
            else
            {
                var property = (Xbim.Ifc4.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4.MeasureResource.IfcAreaMeasure(value.Value);
            }
        }

        private static void SetVolumeProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            if (model.SchemaVersion == XbimSchemaVersion.Ifc4x3)
            {
                var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcVolumeMeasure(value.Value);
            }
            else
            {
                var property = (Xbim.Ifc4.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4.MeasureResource.IfcVolumeMeasure(value.Value);
            }
        }

        private static void SetRealProperty(Xbim.Ifc.IfcStore model, IIfcPropertySet pset, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                RemoveProperty(pset, propertyName);
                return;
            }

            if (model.SchemaVersion == XbimSchemaVersion.Ifc4x3)
            {
                var property = (Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4x3.MeasureResource.IfcReal(value.Value);
            }
            else
            {
                var property = (Xbim.Ifc4.PropertyResource.IfcPropertySingleValue)GetOrCreateSingleValueProperty(model, pset, propertyName);
                property.NominalValue = new Xbim.Ifc4.MeasureResource.IfcReal(value.Value);
            }
        }

        private static object ReadPropertyValue(object target, string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            System.Reflection.PropertyInfo property = target.GetType().GetProperty(propertyName);
            if (property == null)
                return null;

            return property.GetValue(target);
        }

        private static string SafeToString(object value)
        {
            if (value == null) return string.Empty;
            return value.ToString() ?? string.Empty;
        }

        private static string FormatExceptionChain(System.Exception ex)
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

        private static void InitializeIfcServices()
        {
            XbimServiceBootstrap.EnsureInitialized();
        }

        private sealed class TransferResult
        {
            public int SourceObjects { get; set; }
            public int SourceGroups { get; set; }
            public int TargetCandidates { get; set; }
            public int Transferred { get; set; }
            public int Unmatched { get; set; }
            public int Ambiguous { get; set; }
            public string OutputPath { get; set; } = string.Empty;
            public List<string> Warnings { get; } = new List<string>();
        }

        private sealed class SourceRecord
        {
            public string CorridorName { get; set; } = string.Empty;
            public string RegionName { get; set; } = string.Empty;
            public string LaneName { get; set; } = string.Empty;
            public string CodeName { get; set; } = string.Empty;
            public string Side { get; set; } = string.Empty;
            public string Discipline { get; set; } = string.Empty;
            public string FriendlyLayerName { get; set; } = string.Empty;
            public string FunctionLayer { get; set; } = string.Empty;
            public string PavementType { get; set; } = string.Empty;
            public string Material { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Reference { get; set; } = string.Empty;
            public string LrmName { get; set; } = string.Empty;
            public string LrmType { get; set; } = string.Empty;
            public string LrmUnit { get; set; } = string.Empty;
            public double? StartStation { get; set; }
            public double? EndStation { get; set; }
            public double? Length { get; set; }
            public double? Area { get; set; }
            public double? Volume { get; set; }
            public double? Width { get; set; }
            public double? Thickness { get; set; }
            public double? Slope { get; set; }
            public bool HasTransferData { get; set; }
        }

        private sealed class TargetDescriptor
        {
            public string CorridorName { get; set; } = string.Empty;
            public string RegionName { get; set; } = string.Empty;
            public string LaneName { get; set; } = string.Empty;
            public string CodeName { get; set; } = string.Empty;
            public string Side { get; set; } = string.Empty;
            public string DisplayLabel { get; set; } = string.Empty;
            public bool IsCandidate { get; set; }
        }

        private sealed class SourceGroup
        {
            public string FullKey { get; set; } = string.Empty;
            public string RegionLaneCodeSideKey { get; set; } = string.Empty;
            public string RegionLaneCodeKey { get; set; } = string.Empty;
            public string LaneCodeKey { get; set; } = string.Empty;
            public SourceRecord Aggregate { get; set; } = new SourceRecord();
            public List<SourceRecord> Members { get; set; } = new List<SourceRecord>();
        }

        private sealed class MatchOutcome
        {
            public SourceGroup Group { get; private set; }
            public string Warning { get; private set; } = string.Empty;
            public bool IsAmbiguous { get; private set; }

            public static MatchOutcome Success(SourceGroup group)
            {
                return new MatchOutcome { Group = group };
            }

            public static MatchOutcome NotFound(string warning)
            {
                return new MatchOutcome { Warning = warning };
            }

            public static MatchOutcome AmbiguousResult(string warning)
            {
                return new MatchOutcome { Warning = warning, IsAmbiguous = true };
            }
        }
    }

    internal static class IfcPavTransferTrace
    {
        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutomacoesCivil3D",
            "Logs");
        private static readonly string LogPath = Path.Combine(LogDirectory, "ifc-pav-transfer.log");

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
                Write("trace-reset");
            }
        }

        internal static void Write(string stage, string details = null)
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = string.IsNullOrWhiteSpace(details)
                    ? $"{timestamp} | {stage}"
                    : $"{timestamp} | {stage} | {details}";
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
