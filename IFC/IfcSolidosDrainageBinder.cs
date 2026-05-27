using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.Aec.DatabaseServices;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using SOLIDOS;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    public class IfcSolidosDrainageBinder
    {
        private static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
        private const string DrainageCustomPsetName = "Pset_DrenagemPetrobras";
        private static readonly string[] FamilyParamCandidates =
        {
            "FamilyName",
            "Family",
            "Familia",
            "NomeFamilia",
            "PartFamilyName",
            "Family Name",
            "Family_Name"
        };
        private static readonly string[] CotaSaidaParamCandidates =
        {
            "cotaSaida",
            "CotaSaida",
            "cota_saida",
            "Cota_Saida",
            "Cota Saida",
            "SaidaCota"
        };
        private static readonly string[] NameParamCandidates = { "Nome", "Name" };
        private static readonly string[] CodeParamCandidates = { "Codigo", "Code" };
        private static readonly string[] IdentificationParamCandidates = { "Identificacao", "Identification", "ID" };
        private static readonly string[] DescriptionParamCandidates = { "Descricao", "Description" };
        private static readonly string[] SystemParamCandidates = { "Sistema", "System", "Rede", "Network", "NetworkName", "NomeRede" };
        private static readonly string[] SubsystemParamCandidates = { "Subsistema", "SubRede", "Subsystem", "Subnetwork", "Ramal", "Branch" };
        private static readonly string[] AreaOperacionalParamCandidates = { "AreaOperacional", "Area Operacional", "Area" };

        private static readonly FamilyRule[] FamilyRules =
        {
            new FamilyRule(
                keywords: new[] { "CANALETA", "VALETA", "DESCIDA" },
                ifcClass: "IfcPipeSegment",
                predefinedType: "GUTTER",
                category: ElementCategory.Gutter,
                psetNames: new[] { "Pset_PipeSegmentTypeCommon", "Pset_PipeSegmentOccurrence", "Pset_PipeSegmentTypeGutter", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "BUEIRO", "CULVERT" },
                ifcClass: "IfcPipeSegment",
                predefinedType: "CULVERT",
                category: ElementCategory.Culvert,
                psetNames: new[] { "Pset_PipeSegmentTypeCommon", "Pset_PipeSegmentOccurrence", "Pset_PipeSegmentTypeCulvert", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "TUBO", "PIPE" },
                ifcClass: "IfcPipeSegment",
                predefinedType: "RIGIDSEGMENT",
                category: ElementCategory.Pipe,
                psetNames: new[] { "Pset_PipeSegmentTypeCommon", "Pset_PipeSegmentOccurrence", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "JOELHO", "CURVA", "BEND" },
                ifcClass: "IfcPipeFitting",
                predefinedType: "BEND",
                category: ElementCategory.Bend,
                psetNames: new[] { "Pset_PipeFittingTypeCommon", "Pset_PipeFittingOccurrence", "Pset_FittingBend", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "TEE", "JUNCTION", "JUNCAO", "CRUZETA" },
                ifcClass: "IfcPipeFitting",
                predefinedType: "JUNCTION",
                category: ElementCategory.Junction,
                psetNames: new[] { "Pset_PipeFittingTypeCommon", "Pset_PipeFittingOccurrence", "Pset_FittingJunction", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "REDU", "TRANSICAO", "TRANSITION" },
                ifcClass: "IfcPipeFitting",
                predefinedType: "TRANSITION",
                category: ElementCategory.Transition,
                psetNames: new[] { "Pset_PipeFittingTypeCommon", "Pset_PipeFittingOccurrence", "Pset_FittingTransition", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "CAIXA COLETORA", "COLETORA", "RALO", "FUNIL", "BOCA DE LOBO", "BL", "SUMP" },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "SUMP",
                category: ElementCategory.Sump,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", "Pset_DistributionChamberElementTypeSump", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "CAIXA DE PASSAGEM", "CAIXA PASSAGEM", "PASSAGEM", "INSPECAO", "INSPECTION CHAMBER", "CPO", "CP " },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "INSPECTIONCHAMBER",
                category: ElementCategory.InspectionChamber,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", "Pset_DistributionChamberElementTypeInspectionChamber", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "POCO DE INSPECAO", "POCO INSPECAO", "INSPECTION PIT", "PIT" },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "INSPECTIONPIT",
                category: ElementCategory.InspectionPit,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", "Pset_DistributionChamberElementTypeInspectionPit", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "METER", "MEDICAO" },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "METERCHAMBER",
                category: ElementCategory.MeterChamber,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", "Pset_DistributionChamberElementTypeMeterChamber", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "VALVE", "VALVULA" },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "VALVECHAMBER",
                category: ElementCategory.ValveChamber,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", "Pset_DistributionChamberElementTypeValveChamber", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "TRENCH" },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "TRENCH",
                category: ElementCategory.Trench,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", "Pset_DistributionChamberElementTypeTrench", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "PV", "MANHOLE" },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "MANHOLE",
                category: ElementCategory.Manhole,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", "Pset_DistributionChamberElementTypeManhole", DrainageCustomPsetName }
            ),
            new FamilyRule(
                keywords: new[] { "CAIXA", "CHAMBER", "POCO" },
                ifcClass: "IfcDistributionChamberElement",
                predefinedType: "NOTDEFINED",
                category: ElementCategory.Chamber,
                psetNames: new[] { "Pset_DistributionChamberElementCommon", DrainageCustomPsetName }
            )
        };

        [CommandMethod("IFC_VINCULAR_PSETS_SOLIDOS_DRENAGEM")]
        public void VincularPsetsSolidosDrenagem()
        {
            Document docCad = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database db = docCad.Database;

            try
            {
                PromptSelectionResult selection = GetSelection(docEditor);
                if (selection.Status != PromptStatus.OK || selection.Value == null || selection.Value.Count == 0)
                {
                    docEditor.WriteMessage("\n[PSET] Nenhum objeto selecionado.");
                    return;
                }

                IfcDrainagePsetSeeder.EnsureDrainagePsets(db);

                using Transaction tr = db.TransactionManager.StartTransaction();
                IfcPsetFactory.EnsureDefaultPsets(db, tr, docEditor);

                DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);
                IfcAplicarMapeamentoJson.TryGetDefaultCompiledConfig(out IfcCompiledMappingConfig mappingConfig);
                BindingContext bindingContext = new BindingContext(mappingConfig);
                int total = 0;
                int solidosFound = 0;
                int mapped = 0;
                int skipped = 0;
                int failures = 0;
                int writtenValues = 0;

                foreach (SelectedObject selected in selection.Value)
                {
                    total++;
                    if (selected == null || selected.ObjectId.IsNull)
                    {
                        skipped++;
                        continue;
                    }

                    Entity entity = tr.GetObject(selected.ObjectId, OpenMode.ForWrite, false) as Entity;
                    if (entity == null)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        SolidosParamReader paramReader = new SolidosParamReader(entity.ObjectId);
                        bool hasFamilyName = TryGetFamilyName(paramReader, out string familyName);
                        IfcResolvedMetadata layerMetadata = null;
                        bool hasLayerMetadata = bindingContext.HasMappingConfig &&
                            IfcAplicarMapeamentoJson.TryResolveMetadataForEntity(
                                entity,
                                bindingContext.MappingConfig,
                                docEditor,
                                paramReader.GetString,
                                out layerMetadata
                            );

                        if (!hasFamilyName && !hasLayerMetadata)
                        {
                            skipped++;
                            continue;
                        }

                        solidosFound++;

                        FamilyRule baseRule = hasFamilyName ? ResolveRule(familyName) : null;
                        if (baseRule == null && !hasLayerMetadata)
                        {
                            skipped++;
                            continue;
                        }

                        SolidosNodeData data = ReadNodeData(entity, familyName, paramReader);
                        IfcResolvedMetadata metadata = ResolveMetadata(entity, baseRule, data, layerMetadata);
                        FamilyRule effectiveRule = ResolveEffectiveRule(baseRule, metadata);
                        if (effectiveRule == null)
                        {
                            skipped++;
                            continue;
                        }

                        IfcAplicarMapeamentoJson.WriteMetadataToObject(entity, metadata, tr);
                        ApplyIfcObjectProperties(entity, tr, dict, bindingContext, effectiveRule, metadata, ref writtenValues);
                        ApplyMappedPsets(entity, tr, dict, bindingContext, effectiveRule, data, metadata, ref writtenValues);
                        mapped++;
                    }
                    catch (System.Exception ex)
                    {
                        failures++;
                        docEditor.WriteMessage($"\n[PSET] Falha ao processar {entity.Handle}: {ex.Message}");
                    }
                }

                tr.Commit();

                docEditor.WriteMessage(
                    $"\n[PSET] Vinculacao SOLIDOS -> IFC concluida. Selecionados: {total} | SOLIDOS reconhecidos: {solidosFound} | Mapeados: {mapped} | Ignorados: {skipped} | Falhas: {failures} | Valores gravados: {writtenValues}"
                );
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                docEditor.WriteMessage($"\n[AutoCAD] Erro ao vincular PSETs IFC de drenagem: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[.NET] Erro ao vincular PSETs IFC de drenagem: {FormatExceptionChain(ex)}");
            }
        }

        private static PromptSelectionResult GetSelection(Editor editor)
        {
            PromptSelectionResult implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
                return implied;

            PromptSelectionOptions options = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione objetos do SOLIDOS para vincular aos PSETs IFC: "
            };

            return editor.GetSelection(options);
        }

        private static void ApplyIfcObjectProperties(
            Entity entity,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            BindingContext bindingContext,
            FamilyRule rule,
            IfcResolvedMetadata metadata,
            ref int writtenValues)
        {
            PropertySet pset = EnsureAttachedPropertySet(entity, tr, dict, bindingContext, "IfcObject Properties");
            if (pset == null)
                return;

            writtenValues += SetPsetValue(pset, bindingContext, "IFC::IfcExportAs", rule.IfcClass);
            writtenValues += SetPsetValue(pset, bindingContext, "IFC::PredefinedType", rule.PredefinedType);
            writtenValues += SetPsetValue(pset, bindingContext, "IFC::ObjectType", metadata?.ObjectType);
        }

        private static void ApplyMappedPsets(
            Entity entity,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            BindingContext bindingContext,
            FamilyRule rule,
            SolidosNodeData data,
            IfcResolvedMetadata metadata,
            ref int writtenValues)
        {
            Dictionary<string, Dictionary<string, string>> assignments = BuildAssignments(rule, data, metadata);
            Dictionary<string, PropertySet> attachedPsets =
                new Dictionary<string, PropertySet>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Dictionary<string, string>> assignment in assignments)
            {
                if (assignment.Value == null || assignment.Value.Count == 0)
                    continue;

                PropertySet attached = EnsureAttachedPropertySet(entity, tr, dict, bindingContext, assignment.Key);
                if (attached != null)
                    attachedPsets[assignment.Key] = attached;
            }

            foreach (KeyValuePair<string, Dictionary<string, string>> assignment in assignments)
            {
                string psetName = assignment.Key;
                Dictionary<string, string> values = assignment.Value;
                if (values == null || values.Count == 0)
                    continue;

                if (!attachedPsets.TryGetValue(psetName, out PropertySet pset) || pset == null)
                    continue;

                foreach (KeyValuePair<string, string> pair in values)
                {
                    writtenValues += SetPsetValue(pset, bindingContext, pair.Key, pair.Value);
                }
            }
        }

        private static IfcResolvedMetadata ResolveMetadata(
            Entity entity,
            FamilyRule baseRule,
            SolidosNodeData data,
            IfcResolvedMetadata layerMetadata)
        {
            string handleText = entity?.Handle.ToString() ?? string.Empty;
            string name = FirstMeaningfulText(
                data?.Name,
                data?.Description,
                layerMetadata?.ObjectType,
                baseRule?.DefaultFunction,
                data?.FamilyName
            );

            if (LooksLikeAnonymousBlockName(name))
                name = string.Empty;

            if (string.IsNullOrWhiteSpace(name))
                name = FirstNonEmpty(data?.Code, data?.Identification, handleText);

            string tag = FirstNonEmpty(layerMetadata?.Tag, data?.Code, data?.Identification, data?.Name, data?.Tag, handleText);
            string objectType = FirstMeaningfulText(layerMetadata?.ObjectType, data?.Description, baseRule?.DefaultFunction, data?.FamilyName);
            if (string.IsNullOrWhiteSpace(objectType))
                objectType = FirstNonEmpty(layerMetadata?.ObjectType, data?.Description, baseRule?.DefaultFunction, data?.FamilyName);

            string description = FirstMeaningfulText(layerMetadata?.Description, data?.Description, objectType, baseRule?.DefaultFunction);
            if (string.IsNullOrWhiteSpace(description))
                description = FirstNonEmpty(layerMetadata?.Description, data?.Description, objectType, baseRule?.DefaultFunction);

            string system = FirstMeaningfulText(layerMetadata?.System, data?.System);
            string subsystem = FirstMeaningfulText(layerMetadata?.Subsystem, data?.Subsystem);

            return new IfcResolvedMetadata
            {
                IfcClass = FirstNonEmpty(layerMetadata?.IfcClass, baseRule?.IfcClass),
                PredefinedType = FirstNonEmpty(layerMetadata?.PredefinedType, baseRule?.PredefinedType),
                ObjectType = objectType,
                Name = name,
                Tag = tag,
                Description = description,
                Layer = FirstNonEmpty(layerMetadata?.Layer, entity?.Layer),
                System = system,
                Subsystem = subsystem
            };
        }

        private static FamilyRule ResolveEffectiveRule(FamilyRule baseRule, IfcResolvedMetadata metadata)
        {
            string ifcClass = FirstNonEmpty(metadata?.IfcClass, baseRule?.IfcClass);
            string predefinedType = FirstNonEmpty(metadata?.PredefinedType, baseRule?.PredefinedType);
            if (string.IsNullOrWhiteSpace(ifcClass))
                return baseRule;

            ElementCategory fallbackCategory = baseRule != null ? baseRule.Category : ElementCategory.Chamber;
            ElementCategory category = ResolveCategory(ifcClass, predefinedType, fallbackCategory);
            return new FamilyRule(Array.Empty<string>(), ifcClass, predefinedType, category, baseRule?.PsetNames ?? Array.Empty<string>());
        }

        private static ElementCategory ResolveCategory(string ifcClass, string predefinedType, ElementCategory fallbackCategory)
        {
            string normalizedClass = NormalizeForMatch(ifcClass);
            string normalizedType = NormalizeForMatch(predefinedType);

            if (normalizedClass == "IFCPIPESEGMENT")
            {
                if (normalizedType == "GUTTER")
                    return ElementCategory.Gutter;
                if (normalizedType == "CULVERT")
                    return ElementCategory.Culvert;
                return ElementCategory.Pipe;
            }

            if (normalizedClass == "IFCPIPEFITTING")
            {
                if (normalizedType == "BEND")
                    return ElementCategory.Bend;
                if (normalizedType == "JUNCTION")
                    return ElementCategory.Junction;
                if (normalizedType == "TRANSITION")
                    return ElementCategory.Transition;
            }

            if (normalizedClass == "IFCDISTRIBUTIONCHAMBERELEMENT")
            {
                if (normalizedType == "INSPECTIONCHAMBER")
                    return ElementCategory.InspectionChamber;
                if (normalizedType == "INSPECTIONPIT")
                    return ElementCategory.InspectionPit;
                if (normalizedType == "MANHOLE")
                    return ElementCategory.Manhole;
                if (normalizedType == "METERCHAMBER")
                    return ElementCategory.MeterChamber;
                if (normalizedType == "VALVECHAMBER")
                    return ElementCategory.ValveChamber;
                if (normalizedType == "TRENCH")
                    return ElementCategory.Trench;
                if (normalizedType == "SUMP")
                    return ElementCategory.Sump;
                return ElementCategory.Chamber;
            }

            return fallbackCategory;
        }

        private static Dictionary<string, Dictionary<string, string>> BuildAssignments(FamilyRule rule, SolidosNodeData data, IfcResolvedMetadata metadata)
        {
            Dictionary<string, Dictionary<string, string>> assignments =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            string reference = FirstNonEmpty(metadata?.Tag, data.Reference, data.Code, data.Identification, data.Catalogo);

            AddValue(assignments, DrainageCustomPsetName, "AreaOperacional", data.AreaOperacional);
            AddValue(assignments, DrainageCustomPsetName, "Sistema", FirstNonEmpty(metadata?.System, data.System));
            AddValue(assignments, DrainageCustomPsetName, "Subsistema", FirstNonEmpty(metadata?.Subsystem, data.Subsystem));
            AddValue(assignments, DrainageCustomPsetName, "NomeObjeto", FirstMeaningfulText(metadata?.Name, data.Name, data.Description, data.FamilyName));
            AddValue(assignments, DrainageCustomPsetName, "Catalogo", FirstNonEmpty(data.Catalogo, data.Reference, data.FamilyName));
            AddValue(assignments, DrainageCustomPsetName, "CodigoObjeto", FirstNonEmpty(data.Code, data.Identification, metadata?.Tag, data.Catalogo, data.Reference));
            AddValue(assignments, DrainageCustomPsetName, "Tag", FirstNonEmpty(metadata?.Tag, data.Tag, data.Code));
            AddValue(assignments, DrainageCustomPsetName, "FamilyNameSolidos", data.FamilyName);
            AddValue(assignments, DrainageCustomPsetName, "ModeloSolidos", data.FamilyName);
            AddValue(assignments, DrainageCustomPsetName, "FuncaoDrenagem", rule.DefaultFunction);
            AddValue(assignments, DrainageCustomPsetName, "DiametroNominalProjeto", FormatDouble(data.Diameter));
            AddValue(assignments, DrainageCustomPsetName, "DeclividadeProjeto", FormatDouble(data.Gradient));
            AddValue(assignments, DrainageCustomPsetName, "ComprimentoProjeto", FormatDouble(data.NominalLength));
            AddValue(assignments, DrainageCustomPsetName, "LarguraProjeto", FormatDouble(data.Width));
            AddValue(assignments, DrainageCustomPsetName, "BaseProjeto", FormatDouble(data.Base));
            AddValue(assignments, DrainageCustomPsetName, "CotaFundoMontante", FormatDouble(data.UpstreamInvert));
            AddValue(assignments, DrainageCustomPsetName, "CotaFundoJusante", FormatDouble(data.DownstreamInvert));
            AddValue(assignments, DrainageCustomPsetName, "CotaTampa", FormatDouble(data.TopElevation));
            AddValue(assignments, DrainageCustomPsetName, "ProfundidadeUtil", FormatDouble(data.UsefulDepth));
            AddValue(assignments, DrainageCustomPsetName, "CoefManning", FormatDouble(data.Manning));
            AddValue(assignments, DrainageCustomPsetName, "CoefHazenWilliams", FormatDouble(data.HazenWilliams));
            AddValue(assignments, DrainageCustomPsetName, "CoefDarcyWeisbach", FormatDouble(data.DarcyWeisbach));
            AddValue(assignments, DrainageCustomPsetName, "CoverHeight", FormatDouble(data.CoverHeight));
            AddValue(assignments, DrainageCustomPsetName, "AlturaTampa", FormatDouble(data.CoverThickness));
            AddValue(assignments, DrainageCustomPsetName, "AlturaGrelha", FormatDouble(data.GrateHeight));
            AddValue(assignments, DrainageCustomPsetName, "AlturaPiso", FormatDouble(data.FloorHeight));
            AddValue(assignments, DrainageCustomPsetName, "EspessuraParede", FormatDouble(data.WallThickness));
            AddValue(assignments, DrainageCustomPsetName, "EspessuraLaje", FormatDouble(data.SlabThickness));
            AddValue(assignments, DrainageCustomPsetName, "ComprimentoGrelha", FormatDouble(data.GrateLength));
            AddValue(assignments, DrainageCustomPsetName, "LarguraGrelha", FormatDouble(data.GrateWidth));
            AddValue(assignments, DrainageCustomPsetName, "TipoTampaSolidos", data.CoverType);
            AddValue(assignments, DrainageCustomPsetName, "TipoGrelhaSolidos", data.GrateType);
            AddValue(assignments, DrainageCustomPsetName, "QuantidadeTampas", FormatDouble(data.CoverCount));
            AddValue(assignments, DrainageCustomPsetName, "QuantidadeAco", FormatDouble(data.SteelQuantity));
            AddValue(assignments, DrainageCustomPsetName, "VolumeEstrutura", FormatDouble(data.StructureVolume));
            AddValue(assignments, DrainageCustomPsetName, "VolumeExterno", FormatDouble(data.ExternalVolume));
            AddValue(assignments, DrainageCustomPsetName, "VolumeConcretoArmado", FormatDouble(data.ConcreteArmadoVolume));
            AddValue(assignments, DrainageCustomPsetName, "VolumeConcretoMagro", FormatDouble(data.ConcreteMagroVolume));
            AddValue(assignments, DrainageCustomPsetName, "ElevacaoMaximaConexao", FormatDouble(data.MaxConnectionElevation));
            AddValue(assignments, DrainageCustomPsetName, "ElevacaoMinimaConexao", FormatDouble(data.MinConnectionElevation));
            AddValue(assignments, DrainageCustomPsetName, "MaiorTuboConectado", data.MaxPipe);
            AddValue(assignments, DrainageCustomPsetName, "FolgaTopo", FormatDouble(data.TopClearance));
            AddValue(assignments, DrainageCustomPsetName, "FolgaTampa", FormatDouble(data.CoverClearance));
            AddValue(assignments, DrainageCustomPsetName, "Deflexao", FormatDouble(data.DeflectionAngle));
            AddValue(assignments, DrainageCustomPsetName, "Observacoes", metadata?.Description);

            switch (rule.Category)
            {
                case ElementCategory.Gutter:
                case ElementCategory.Culvert:
                case ElementCategory.Pipe:
                    AddValue(assignments, "Pset_PipeSegmentTypeCommon", "Reference", reference);
                    AddValue(assignments, "Pset_PipeSegmentTypeCommon", "Status", data.Status);
                    AddValue(assignments, "Pset_PipeSegmentTypeCommon", "NominalDiameter", FormatDouble(data.Diameter));
                    AddValue(assignments, "Pset_PipeSegmentTypeCommon", "Length", FormatDouble(data.NominalLength));
                    AddValue(assignments, "Pset_PipeSegmentOccurrence", "Gradient", FormatDouble(data.Gradient));
                    AddValue(assignments, "Pset_PipeSegmentOccurrence", "InvertElevation", FormatDouble(data.DownstreamInvert));
                    AddValue(assignments, "Qto_PipeSegmentBaseQuantities", "Length", FormatDouble(data.NominalLength));
                    AddValue(assignments, "Qto_PipeSegmentBaseQuantities", "GrossCrossSectionArea", FormatDouble(ResolvePipeGrossCrossSectionArea(data, rule.Category)));
                    AddValue(assignments, "Qto_PipeSegmentBaseQuantities", "NetCrossSectionArea", FormatDouble(ResolveNetCrossSectionArea(data)));
                    AddValue(assignments, "Qto_PipeSegmentBaseQuantities", "OuterSurfaceArea", FormatDouble(ResolvePipeOuterSurfaceArea(data, rule.Category)));
                    AddValue(assignments, "Qto_PipeSegmentBaseQuantities", "FootPrintArea", FormatDouble(ResolvePipeFootprintArea(data, rule.Category)));

                    if (rule.Category == ElementCategory.Gutter)
                    {
                        AddValue(assignments, "Pset_PipeSegmentTypeGutter", "Slope", FormatDouble(data.Gradient));
                        AddValue(assignments, "Pset_PipeSegmentTypeGutter", "Complementaryfunction", data.FamilyName);
                        AddValue(assignments, "Pset_PipeSegmentTypeGutter", "OrthometricHeight", FormatDouble(data.TopElevation));
                        AddValue(assignments, "Pset_PipeSegmentTypeGutter", "IsCovered", FormatBool(data.IsCovered));
                    }

                    if (rule.Category == ElementCategory.Culvert)
                    {
                        AddValue(assignments, "Pset_PipeSegmentTypeCulvert", "InternalWidth", FormatDouble(FirstNonNull(data.Base, data.Width)));
                        AddValue(assignments, "Pset_PipeSegmentTypeCulvert", "ClearDepth", FormatDouble(data.UsefulDepth));
                    }

                    break;

                case ElementCategory.Bend:
                case ElementCategory.Junction:
                case ElementCategory.Transition:
                    AddValue(assignments, "Pset_PipeFittingTypeCommon", "Reference", reference);
                    AddValue(assignments, "Pset_PipeFittingTypeCommon", "Status", data.Status);
                    AddValue(assignments, "Qto_PipeFittingBaseQuantities", "Length", FormatDouble(data.NominalLength));
                    AddValue(assignments, "Qto_PipeFittingBaseQuantities", "GrossCrossSectionArea", FormatDouble(ResolvePipeGrossCrossSectionArea(data, rule.Category)));
                    AddValue(assignments, "Qto_PipeFittingBaseQuantities", "NetCrossSectionArea", FormatDouble(ResolveNetCrossSectionArea(data)));
                    AddValue(assignments, "Qto_PipeFittingBaseQuantities", "OuterSurfaceArea", FormatDouble(ResolvePipeOuterSurfaceArea(data, rule.Category)));

                    if (rule.Category == ElementCategory.Bend)
                    {
                        AddValue(assignments, "Pset_FittingBend", "BendAngle", FormatDouble(data.DeflectionAngle));
                    }
                    else if (rule.Category == ElementCategory.Junction)
                    {
                        AddValue(assignments, "Pset_FittingJunction", "JunctionType", data.FamilyName);
                    }
                    else if (rule.Category == ElementCategory.Transition)
                    {
                        AddValue(assignments, "Pset_FittingTransition", "NominalLength", FormatDouble(data.NominalLength));
                    }

                    break;

                case ElementCategory.Sump:
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Reference", reference);
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Status", data.Status);
                    AddValue(assignments, "Pset_DistributionChamberElementTypeSump", "Length", FormatDouble(data.NominalLength));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeSump", "Width", FormatDouble(data.Width));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeSump", "SumpInvertLevel", FormatDouble(data.BottomElevation));
                    AddChamberBaseQuantities(assignments, data);
                    break;

                case ElementCategory.InspectionChamber:
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Reference", reference);
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Status", data.Status);
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "ChamberLengthOrRadius", FormatDouble(ResolveChamberLengthOrRadius(data)));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "ChamberWidth", FormatDouble(data.Width));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "InspectionChamberInvertLevel", FormatDouble(data.BottomElevation));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "SoffitLevel", FormatDouble(data.TopElevation));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "WallThickness", FormatDouble(data.WallThickness));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "BaseThickness", FormatDouble(data.FloorHeight));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "AccessLengthOrRadius", FormatDouble(ResolveAccessLengthOrRadius(data)));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "AccessWidth", FormatDouble(ResolveAccessWidth(data)));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionChamber", "AccessCoverLoadRating", FirstNonEmpty(data.CoverType, data.GrateType));
                    AddChamberBaseQuantities(assignments, data);
                    break;

                case ElementCategory.InspectionPit:
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Reference", reference);
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Status", data.Status);
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionPit", "Length", FormatDouble(data.NominalLength));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionPit", "Width", FormatDouble(data.Width));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeInspectionPit", "Depth", FormatDouble(data.UsefulDepth));
                    AddChamberBaseQuantities(assignments, data);
                    break;

                case ElementCategory.MeterChamber:
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Reference", reference);
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Status", data.Status);
                    AddValue(assignments, "Pset_DistributionChamberElementTypeMeterChamber", "ChamberLengthOrRadius", FormatDouble(ResolveChamberLengthOrRadius(data)));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeMeterChamber", "ChamberWidth", FormatDouble(data.Width));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeMeterChamber", "WallThickness", FormatDouble(data.WallThickness));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeMeterChamber", "BaseThickness", FormatDouble(data.FloorHeight));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeMeterChamber", "AccessCoverMaterial", FirstNonEmpty(data.CoverType, data.GrateType));
                    AddChamberBaseQuantities(assignments, data);
                    break;

                case ElementCategory.ValveChamber:
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Reference", reference);
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Status", data.Status);
                    AddValue(assignments, "Pset_DistributionChamberElementTypeValveChamber", "ChamberLengthOrRadius", FormatDouble(ResolveChamberLengthOrRadius(data)));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeValveChamber", "ChamberWidth", FormatDouble(data.Width));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeValveChamber", "WallThickness", FormatDouble(data.WallThickness));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeValveChamber", "BaseThickness", FormatDouble(data.FloorHeight));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeValveChamber", "AccessCoverMaterial", FirstNonEmpty(data.CoverType, data.GrateType));
                    AddChamberBaseQuantities(assignments, data);
                    break;

                case ElementCategory.Trench:
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Reference", reference);
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Status", data.Status);
                    AddValue(assignments, "Pset_DistributionChamberElementTypeTrench", "Width", FormatDouble(FirstNonNull(data.Width, data.Base)));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeTrench", "Depth", FormatDouble(data.UsefulDepth));
                    AddValue(assignments, "Pset_DistributionChamberElementTypeTrench", "InvertLevel", FormatDouble(data.BottomElevation));
                    AddChamberBaseQuantities(assignments, data);
                    break;

                case ElementCategory.Manhole:
                case ElementCategory.Chamber:
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Reference", reference);
                    AddValue(assignments, "Pset_DistributionChamberElementCommon", "Status", data.Status);
                    AddChamberBaseQuantities(assignments, data);

                    if (rule.Category == ElementCategory.Manhole)
                    {
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "InvertLevel", FormatDouble(data.BottomElevation));
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "SoffitLevel", FormatDouble(data.TopElevation));
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "WallThickness", FormatDouble(data.WallThickness));
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "BaseThickness", FormatDouble(data.FloorHeight));
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "AccessLengthOrRadius", FormatDouble(ResolveAccessLengthOrRadius(data)));
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "AccessWidth", FormatDouble(ResolveAccessWidth(data)));
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "AccessCoverLoadRating", FirstNonEmpty(data.CoverType, data.GrateType));
                        AddValue(assignments, "Pset_DistributionChamberElementTypeManhole", "NumberOfManholeCovers", FormatDouble(data.CoverCount));
                    }

                    break;
            }

            return assignments;
        }

        private static SolidosNodeData ReadNodeData(Entity entity, string familyName, SolidosParamReader paramReader)
        {
            SolidosNodeData data = new SolidosNodeData
            {
                FamilyName = familyName,
                Name = paramReader.ReadFirstString(NameParamCandidates),
                Code = paramReader.ReadFirstString(CodeParamCandidates),
                Identification = paramReader.ReadFirstString(IdentificationParamCandidates),
                Description = paramReader.ReadFirstString(DescriptionParamCandidates),
                System = paramReader.ReadFirstString(SystemParamCandidates),
                Subsystem = paramReader.ReadFirstString(SubsystemParamCandidates),
                AreaOperacional = paramReader.ReadFirstString(AreaOperacionalParamCandidates),
                Catalogo = paramReader.ReadFirstString("Catalogo"),
                Tag = paramReader.ReadFirstString("Tag", "TAG", "Nome", "Name"),
                Status = paramReader.ReadFirstString("Status"),
                Diameter = paramReader.ReadFirstDouble("Diametro", "Diameter", "DN"),
                Width = paramReader.ReadFirstDouble("Largura", "Width"),
                Base = paramReader.ReadFirstDouble("Base"),
                Height = paramReader.ReadFirstDouble("Altura", "Depth"),
                CoverThickness = paramReader.ReadFirstDouble("AltTampa"),
                GrateHeight = paramReader.ReadFirstDouble("AltGrelha"),
                FloorHeight = paramReader.ReadFirstDouble("AltPiso"),
                CoverHeight = paramReader.ReadFirstDouble("CoverHeight"),
                DeflectionAngle = FirstNonNull(paramReader.ReadFirstDouble("Deflexao"), paramReader.ReadFirstDouble("Angle")),
                Manning = paramReader.ReadFirstDouble("ACMan"),
                HazenWilliams = paramReader.ReadFirstDouble("ACHW"),
                DarcyWeisbach = paramReader.ReadFirstDouble("ACDW"),
                Declivity = paramReader.ReadFirstDouble("Declividade", "Gradient", "Slope"),
                BottomElevation = paramReader.ReadFirstDouble("SumpElevation"),
                CotaSaida = paramReader.ReadFirstDouble(CotaSaidaParamCandidates),
                LengthFromProperty = paramReader.ReadFirstDouble("Comprimento", "Length"),
                ArcLength = paramReader.ReadFirstDouble("ArcLength"),
                LengthL = paramReader.ReadFirstDouble("L"),
                LengthL1 = paramReader.ReadFirstDouble("L1"),
                WallThickness = paramReader.ReadFirstDouble("Parede"),
                SlabThickness = paramReader.ReadFirstDouble("AltLaje"),
                GrateLength = paramReader.ReadFirstDouble("ComprGrelha"),
                GrateWidth = paramReader.ReadFirstDouble("LargGrelha"),
                CoverCount = paramReader.ReadFirstDouble("QuantTampa"),
                SteelQuantity = paramReader.ReadFirstDouble("QuantAco"),
                StructureVolume = paramReader.ReadFirstDouble("Volume"),
                ExternalVolume = paramReader.ReadFirstDouble("SolidVolume"),
                ConcreteArmadoVolume = paramReader.ReadFirstDouble("VolumeConcretoArmado"),
                ConcreteMagroVolume = paramReader.ReadFirstDouble("VolumeConcretoMagro"),
                MaxConnectionElevation = paramReader.ReadFirstDouble("MaxElev"),
                MinConnectionElevation = paramReader.ReadFirstDouble("MinElev"),
                TopClearance = paramReader.ReadFirstDouble("Folga"),
                CoverClearance = paramReader.ReadFirstDouble("FolgaTampa"),
                CoverType = paramReader.ReadFirstString("TipoTampa"),
                GrateType = paramReader.ReadFirstString("TipoGrelha"),
                MaxPipe = paramReader.ReadFirstString("MaxPipe"),
                CoverNominalDiameter = paramReader.ReadFirstDouble("DNTampaFoFo"),
                IsCovered = familyName.IndexOf("GRELHA", StringComparison.OrdinalIgnoreCase) >= 0
            };

            if (paramReader.TryGetPoint("StartPoint", out GeometryPoint startPoint))
            {
                data.HasStartPoint = true;
                data.StartPoint = startPoint;
            }

            if (paramReader.TryGetPoint("EndPoint", out GeometryPoint endPoint))
            {
                data.HasEndPoint = true;
                data.EndPoint = endPoint;
            }

            if (paramReader.TryGetPoint("Location", out GeometryPoint location))
            {
                data.HasLocation = true;
                data.Location = location;
            }

            TryPopulateEntityMetrics(entity, data);
            data.Reference = FirstNonEmpty(data.Code, data.Identification, data.Catalogo);
            data.NominalLength = ResolveLength(data);
            data.Gradient = ResolveGradient(data);
            data.UpstreamInvert = ResolveUpstreamInvert(data);
            data.DownstreamInvert = ResolveDownstreamInvert(data);
            data.TopElevation = ResolveTopElevation(data);
            data.UsefulDepth = ResolveUsefulDepth(data);
            data.IsCovered = data.IsCovered || HasPositiveValue(data.CoverThickness) || HasPositiveValue(data.GrateHeight);

            return data;
        }

        private static double? ResolveLength(SolidosNodeData data)
        {
            double? directLength = FirstNonNull(data.LengthFromProperty, data.ArcLength, data.LengthL, data.LengthL1);
            if (directLength.HasValue)
                return directLength;

            if (data.HasStartPoint && data.HasEndPoint)
                return Distance3D(data.StartPoint, data.EndPoint);

            return ResolveMaxBoundDimension(data);
        }

        private static double? ResolveGradient(SolidosNodeData data)
        {
            if (data.Declivity.HasValue)
                return Math.Abs(data.Declivity.Value);

            if (!data.HasStartPoint || !data.HasEndPoint)
                return null;

            double planLength = Math.Sqrt(
                Math.Pow(data.StartPoint.X - data.EndPoint.X, 2) +
                Math.Pow(data.StartPoint.Y - data.EndPoint.Y, 2)
            );

            if (planLength <= 1e-9)
                return null;

            return Math.Abs(data.StartPoint.Z - data.EndPoint.Z) / planLength;
        }

        private static double? ResolveUpstreamInvert(SolidosNodeData data)
        {
            if (data.BottomElevation.HasValue)
                return data.BottomElevation.Value;

            if (data.HasStartPoint && data.HasEndPoint)
                return Math.Max(data.StartPoint.Z, data.EndPoint.Z);

            if (data.CotaSaida.HasValue)
                return data.CotaSaida.Value;

            return data.HasLocation ? data.Location.Z : (double?)null;
        }

        private static double? ResolveDownstreamInvert(SolidosNodeData data)
        {
            if (data.BottomElevation.HasValue)
                return data.BottomElevation.Value;

            if (data.HasStartPoint && data.HasEndPoint)
                return Math.Min(data.StartPoint.Z, data.EndPoint.Z);

            if (data.CotaSaida.HasValue)
                return data.CotaSaida.Value;

            return data.HasLocation ? data.Location.Z : (double?)null;
        }

        private static double? ResolveTopElevation(SolidosNodeData data)
        {
            if (data.BottomElevation.HasValue && data.Height.HasValue)
                return data.BottomElevation.Value + data.Height.Value;

            if (data.HasLocation)
                return data.Location.Z;

            if (data.CotaSaida.HasValue && data.Height.HasValue)
                return data.CotaSaida.Value + data.Height.Value;

            return null;
        }

        private static double? ResolveUsefulDepth(SolidosNodeData data)
        {
            if (data.Height.HasValue)
                return data.Height.Value;

            if (data.TopElevation.HasValue && data.DownstreamInvert.HasValue)
                return Math.Abs(data.TopElevation.Value - data.DownstreamInvert.Value);

            return null;
        }

        private static double? ResolveChamberLengthOrRadius(SolidosNodeData data)
        {
            if (data.NominalLength.HasValue)
                return data.NominalLength.Value;

            if (data.Width.HasValue)
                return data.Width.Value * 0.5;

            if (data.BoundingWidth.HasValue)
                return data.BoundingWidth.Value * 0.5;

            return null;
        }

        private static double? ResolveAccessLengthOrRadius(SolidosNodeData data)
        {
            return FirstNonNull(data.GrateLength, data.CoverNominalDiameter);
        }

        private static double? ResolveAccessWidth(SolidosNodeData data)
        {
            return FirstNonNull(data.GrateWidth, data.CoverNominalDiameter);
        }

        private static void AddChamberBaseQuantities(Dictionary<string, Dictionary<string, string>> assignments, SolidosNodeData data)
        {
            AddValue(assignments, "Qto_DistributionChamberElementBaseQuantities", "GrossSurfaceArea", FormatDouble(ResolveChamberGrossSurfaceArea(data)));
            AddValue(assignments, "Qto_DistributionChamberElementBaseQuantities", "NetSurfaceArea", FormatDouble(ResolveChamberNetSurfaceArea(data)));
            AddValue(assignments, "Qto_DistributionChamberElementBaseQuantities", "GrossVolume", FormatDouble(ResolveChamberGrossVolume(data)));
            AddValue(assignments, "Qto_DistributionChamberElementBaseQuantities", "NetVolume", FormatDouble(ResolveChamberNetVolume(data)));
            AddValue(assignments, "Qto_DistributionChamberElementBaseQuantities", "Depth", FormatDouble(ResolveChamberDepth(data)));
        }

        private static void TryPopulateEntityMetrics(Entity entity, SolidosNodeData data)
        {
            if (entity == null || data == null)
                return;

            try
            {
                Extents3d extents = entity.GeometricExtents;
                data.BoundingLength = Math.Abs(extents.MaxPoint.X - extents.MinPoint.X);
                data.BoundingWidth = Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y);
                data.BoundingHeight = Math.Abs(extents.MaxPoint.Z - extents.MinPoint.Z);
            }
            catch
            {
            }

            if (TryGetMassPropertyDouble(entity, "Volume", out double measuredVolume))
                data.MeasuredVolume = measuredVolume;

            if (TryGetEntityArea(entity, out double measuredSurfaceArea))
                data.MeasuredSurfaceArea = measuredSurfaceArea;
        }

        private static bool TryGetMassPropertyDouble(Entity entity, string propertyName, out double value)
        {
            value = 0d;
            if (entity == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            try
            {
                object massProperties = entity.GetType().GetProperty("MassProperties")?.GetValue(entity);
                if (massProperties == null)
                    return false;

                object raw = massProperties.GetType().GetProperty(propertyName)?.GetValue(massProperties);
                return TryConvertToDouble(raw, out value);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetEntityArea(Entity entity, out double value)
        {
            value = 0d;
            if (entity == null)
                return false;

            try
            {
                object raw = entity.GetType().GetProperty("Area")?.GetValue(entity);
                if (TryConvertToDouble(raw, out value))
                    return true;
            }
            catch
            {
            }

            return TryGetMassPropertyDouble(entity, "Area", out value) ||
                   TryGetMassPropertyDouble(entity, "SurfaceArea", out value);
        }

        private static double? ResolvePipeGrossCrossSectionArea(SolidosNodeData data, ElementCategory category)
        {
            if (data == null)
                return null;

            if (category == ElementCategory.Gutter || category == ElementCategory.Culvert)
            {
                double? span = FirstNonNull(data.Base, data.Width, ResolveMinorBoundDimension(data));
                double? depth = FirstNonNull(data.UsefulDepth, data.Height, ResolveVerticalBoundDimension(data));
                if (span.HasValue && depth.HasValue)
                    return Math.Abs(span.Value * depth.Value);
            }

            double? diameter = FirstNonNull(data.Diameter, data.Width, data.Base);
            if (diameter.HasValue)
                return Math.PI * Math.Pow(Math.Abs(diameter.Value), 2) / 4d;

            return ResolveBoundCrossSectionArea(data);
        }

        private static double? ResolveNetCrossSectionArea(SolidosNodeData data)
        {
            return DivideIfPositive(ResolveNetVolume(data), data?.NominalLength);
        }

        private static double? ResolvePipeOuterSurfaceArea(SolidosNodeData data, ElementCategory category)
        {
            if (data == null)
                return null;

            if (HasPositiveValue(data.MeasuredSurfaceArea))
                return data.MeasuredSurfaceArea;

            double? perimeter;
            if (category == ElementCategory.Gutter || category == ElementCategory.Culvert)
            {
                double? span = FirstNonNull(data.Base, data.Width, ResolveMinorBoundDimension(data));
                double? depth = FirstNonNull(data.UsefulDepth, data.Height, ResolveVerticalBoundDimension(data));
                perimeter = span.HasValue && depth.HasValue
                    ? 2d * (Math.Abs(span.Value) + Math.Abs(depth.Value))
                    : ResolveBoundCrossSectionPerimeter(data);
            }
            else
            {
                double? diameter = FirstNonNull(data.Diameter, data.Width, data.Base);
                perimeter = diameter.HasValue
                    ? Math.PI * Math.Abs(diameter.Value)
                    : ResolveBoundCrossSectionPerimeter(data);
            }

            return perimeter.HasValue && data.NominalLength.HasValue
                ? perimeter.Value * Math.Abs(data.NominalLength.Value)
                : (double?)null;
        }

        private static double? ResolvePipeFootprintArea(SolidosNodeData data, ElementCategory category)
        {
            if (data == null || !data.NominalLength.HasValue)
                return null;

            double? width = category == ElementCategory.Gutter || category == ElementCategory.Culvert
                ? FirstNonNull(data.Base, data.Width, ResolveMinorPlanDimension(data))
                : FirstNonNull(data.Diameter, data.Width, data.Base, ResolveMinorPlanDimension(data));

            return width.HasValue
                ? Math.Abs(width.Value) * Math.Abs(data.NominalLength.Value)
                : (double?)null;
        }

        private static double? ResolveChamberGrossSurfaceArea(SolidosNodeData data)
        {
            double? area = CalculateBoxSurfaceArea(ResolveChamberOuterLength(data), ResolveChamberOuterWidth(data), ResolveChamberDepth(data));
            return FirstNonNull(area, data?.MeasuredSurfaceArea);
        }

        private static double? ResolveChamberNetSurfaceArea(SolidosNodeData data)
        {
            if (data == null)
                return null;

            double? outerLength = ResolveChamberOuterLength(data);
            double? outerWidth = ResolveChamberOuterWidth(data);
            double? depth = ResolveChamberDepth(data);
            double? wall = data.WallThickness;
            double? baseThickness = FirstNonNull(data.SlabThickness, data.FloorHeight);

            if (outerLength.HasValue && outerWidth.HasValue && depth.HasValue && wall.HasValue)
            {
                double innerLength = Math.Max(outerLength.Value - 2d * Math.Abs(wall.Value), 0d);
                double innerWidth = Math.Max(outerWidth.Value - 2d * Math.Abs(wall.Value), 0d);
                double innerDepth = Math.Max(depth.Value - Math.Abs(baseThickness ?? 0d), 0d);
                double? area = CalculateBoxSurfaceArea(innerLength, innerWidth, innerDepth);
                if (area.HasValue)
                    return area.Value;
            }

            return ResolveChamberGrossSurfaceArea(data);
        }

        private static double? ResolveChamberGrossVolume(SolidosNodeData data)
        {
            return FirstNonNull(
                data?.ExternalVolume,
                CalculateBoxVolume(ResolveChamberOuterLength(data), ResolveChamberOuterWidth(data), ResolveChamberDepth(data)),
                data?.MeasuredVolume
            );
        }

        private static double? ResolveNetVolume(SolidosNodeData data)
        {
            return FirstNonNull(data?.StructureVolume, data?.MeasuredVolume);
        }

        private static double? ResolveChamberNetVolume(SolidosNodeData data)
        {
            return ResolveNetVolume(data);
        }

        private static double? ResolveChamberDepth(SolidosNodeData data)
        {
            return FirstNonNull(data?.UsefulDepth, data?.Height, data?.BoundingHeight);
        }

        private static double? ResolveChamberOuterLength(SolidosNodeData data)
        {
            return FirstNonNull(data?.NominalLength, data?.GrateLength, data?.BoundingLength, data?.Width, data?.Base);
        }

        private static double? ResolveChamberOuterWidth(SolidosNodeData data)
        {
            return FirstNonNull(data?.Width, data?.Base, data?.GrateWidth, data?.BoundingWidth, data?.NominalLength);
        }

        private static double? ResolveBoundCrossSectionArea(SolidosNodeData data)
        {
            List<double> dims = GetPositiveDimensions(data?.BoundingLength, data?.BoundingWidth, data?.BoundingHeight);
            return dims.Count >= 2 ? dims[0] * dims[1] : (double?)null;
        }

        private static double? ResolveBoundCrossSectionPerimeter(SolidosNodeData data)
        {
            List<double> dims = GetPositiveDimensions(data?.BoundingLength, data?.BoundingWidth, data?.BoundingHeight);
            return dims.Count >= 2 ? 2d * (dims[0] + dims[1]) : (double?)null;
        }

        private static double? ResolveMinorPlanDimension(SolidosNodeData data)
        {
            List<double> dims = GetPositiveDimensions(data?.BoundingLength, data?.BoundingWidth);
            return dims.Count > 0 ? dims[0] : (double?)null;
        }

        private static double? ResolveMinorBoundDimension(SolidosNodeData data)
        {
            List<double> dims = GetPositiveDimensions(data?.BoundingLength, data?.BoundingWidth, data?.BoundingHeight);
            return dims.Count > 0 ? dims[0] : (double?)null;
        }

        private static double? ResolveVerticalBoundDimension(SolidosNodeData data)
        {
            return HasPositiveValue(data?.BoundingHeight) ? data.BoundingHeight : ResolveMinorBoundDimension(data);
        }

        private static double? ResolveMaxBoundDimension(SolidosNodeData data)
        {
            List<double> dims = GetPositiveDimensions(data?.BoundingLength, data?.BoundingWidth, data?.BoundingHeight);
            return dims.Count > 0 ? dims[dims.Count - 1] : (double?)null;
        }

        private static double? CalculateBoxSurfaceArea(double? length, double? width, double? depth)
        {
            if (!length.HasValue || !width.HasValue || !depth.HasValue)
                return null;

            double l = Math.Abs(length.Value);
            double w = Math.Abs(width.Value);
            double d = Math.Abs(depth.Value);
            return 2d * ((l * w) + (l * d) + (w * d));
        }

        private static double? CalculateBoxVolume(double? length, double? width, double? depth)
        {
            if (!length.HasValue || !width.HasValue || !depth.HasValue)
                return null;

            return Math.Abs(length.Value) * Math.Abs(width.Value) * Math.Abs(depth.Value);
        }

        private static double? DivideIfPositive(double? numerator, double? denominator)
        {
            if (!numerator.HasValue || !denominator.HasValue || Math.Abs(denominator.Value) <= 1e-9)
                return null;

            return numerator.Value / Math.Abs(denominator.Value);
        }

        private static List<double> GetPositiveDimensions(params double?[] dims)
        {
            List<double> values = new List<double>();
            if (dims == null)
                return values;

            foreach (double? dim in dims)
            {
                if (dim.HasValue && Math.Abs(dim.Value) > 1e-9)
                    values.Add(Math.Abs(dim.Value));
            }

            values.Sort();
            return values;
        }

        private static bool TryGetFamilyName(SolidosParamReader paramReader, out string familyName)
        {
            familyName = string.Empty;
            if (paramReader == null)
                return false;

            foreach (string candidate in FamilyParamCandidates)
            {
                if (paramReader.TryGetString(candidate, out string value) && !string.IsNullOrWhiteSpace(value))
                {
                    familyName = value.Trim();
                    return true;
                }
            }

            return false;
        }

        private static FamilyRule ResolveRule(string familyName)
        {
            string normalized = NormalizeForMatch(familyName);

            foreach (FamilyRule rule in FamilyRules)
            {
                foreach (string keyword in rule.Keywords)
                {
                    if (MatchesKeyword(normalized, keyword))
                        return rule;
                }
            }

            return null;
        }

        private static bool MatchesKeyword(string normalizedValue, string keyword)
        {
            string normalizedKeyword = NormalizeForMatch(keyword);
            if (normalizedKeyword.Length <= 2)
                return (" " + normalizedValue + " ").Contains(" " + normalizedKeyword + " ", StringComparison.Ordinal);

            return normalizedValue.Contains(normalizedKeyword, StringComparison.Ordinal);
        }

        private static bool LooksLikeAnonymousBlockName(string value)
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length < 3 || trimmed[0] != '*')
                return false;

            if (!char.IsLetter(trimmed[1]))
                return false;

            for (int index = 2; index < trimmed.Length; index++)
            {
                if (!char.IsDigit(trimmed[index]))
                    return false;
            }

            return true;
        }

        private static string NormalizeForMatch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string formD = value.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(formD.Length);

            foreach (char ch in formD)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToUpperInvariant(ch));
                else
                    sb.Append(' ');
            }

            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool TryReadRawParam(ObjectId nodeId, string propName, out object value)
        {
            value = null;

            try
            {
                Type propertyType = null;
                object raw = SolidosAPI.GetNodeParam(nodeId, propName, null, ref propertyType);
                if (raw == null && propertyType == null)
                    return false;

                value = raw;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertToDouble(object raw, out double value)
        {
            value = 0d;
            if (raw == null)
                return false;

            switch (raw)
            {
                case double d:
                    value = d;
                    return true;
                case float f:
                    value = f;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
                case decimal m:
                    value = (double)m;
                    return true;
            }

            string text = Convert.ToString(raw, InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return double.TryParse(text, NumberStyles.Any, PtBrCulture, out value) ||
                   double.TryParse(text, NumberStyles.Any, InvariantCulture, out value);
        }

        private static ObjectId TryGetPsetDefinitionId(
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr,
            BindingContext bindingContext,
            string name)
        {
            if (bindingContext != null && bindingContext.TryGetDefinitionId(name, out ObjectId cachedDefinitionId))
                return cachedDefinitionId;

            ObjectId definitionId = ObjectId.Null;
            try
            {
                if (dictionary.Has(name, tr))
                    definitionId = dictionary.GetAt(name);
            }
            catch
            {
            }

            bindingContext?.RememberDefinitionId(name, definitionId);
            return definitionId;
        }

        private static PropertySet EnsureAttachedPropertySet(
            Entity entity,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            BindingContext bindingContext,
            string psetName)
        {
            if (entity == null || string.IsNullOrWhiteSpace(psetName))
                return null;

            ObjectId definitionId = TryGetPsetDefinitionId(dict, tr, bindingContext, psetName);
            if (definitionId == ObjectId.Null)
                return null;

            if (bindingContext != null &&
                bindingContext.TryGetAttachedPropertySet(entity.ObjectId, definitionId, out PropertySet cachedPropertySet))
            {
                return cachedPropertySet;
            }

            PropertySet propertySet = GetOrCreatePropertySet(entity, definitionId, tr);
            bindingContext?.RememberAttachedPropertySet(entity.ObjectId, definitionId, propertySet);
            return propertySet;
        }

        private static PropertySet GetOrCreatePropertySet(Entity entity, ObjectId propertySetDefinitionId, Transaction tr)
        {
            try
            {
                ObjectId currentId = PropertyDataServices.GetPropertySet(entity, propertySetDefinitionId);
                if (currentId == ObjectId.Null)
                {
                    PropertyDataServices.AddPropertySet(entity, propertySetDefinitionId);
                    currentId = PropertyDataServices.GetPropertySet(entity, propertySetDefinitionId);
                }

                if (currentId == ObjectId.Null)
                    return null;

                return tr.GetObject(currentId, OpenMode.ForWrite, false) as PropertySet;
            }
            catch
            {
                return null;
            }
        }

        private static int SetPsetValue(PropertySet pset, BindingContext bindingContext, string propertyName, string value)
        {
            if (pset == null || string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(value))
                return 0;

            int propertyId = ResolvePropertyId(pset, bindingContext, propertyName);

            if (propertyId < 0)
                return 0;

            try
            {
                object current = null;
                try
                {
                    current = pset.GetAt(propertyId);
                }
                catch
                {
                }

                string currentText = Convert.ToString(current, InvariantCulture) ?? string.Empty;
                if (string.Equals(currentText, value, StringComparison.Ordinal))
                    return 0;

                pset.SetAt(propertyId, value);
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private static int ResolvePropertyId(PropertySet pset, BindingContext bindingContext, string propertyName)
        {
            if (bindingContext != null && bindingContext.TryGetPropertyId(pset.ObjectId, propertyName, out int cachedPropertyId))
                return cachedPropertyId;

            int propertyId;
            try
            {
                propertyId = pset.PropertyNameToId(propertyName);
            }
            catch
            {
                propertyId = -1;
            }

            bindingContext?.RememberPropertyId(pset.ObjectId, propertyName, propertyId);
            return propertyId;
        }

        private static void AddValue(
            Dictionary<string, Dictionary<string, string>> assignments,
            string psetName,
            string propertyName,
            string value)
        {
            if (string.IsNullOrWhiteSpace(psetName) || string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(value))
                return;

            if (!assignments.TryGetValue(psetName, out Dictionary<string, string> psetValues))
            {
                psetValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                assignments[psetName] = psetValues;
            }

            psetValues[propertyName] = value;
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", InvariantCulture) : string.Empty;
        }

        private static string FormatBool(bool value)
        {
            return value ? "True" : "False";
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

        private static string FirstMeaningfulText(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                string trimmed = candidate?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (LooksLikeAnonymousBlockName(trimmed))
                    continue;

                if (LooksLikeNumericReference(trimmed))
                    continue;

                return trimmed;
            }

            return string.Empty;
        }

        private static bool LooksLikeNumericReference(string value)
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
                return false;

            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal) && trimmed.Length > 2)
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();

            if (trimmed.Length < 5)
                return false;

            for (int index = 0; index < trimmed.Length; index++)
            {
                if (!char.IsDigit(trimmed[index]))
                    return false;
            }

            return true;
        }

        private static double? FirstNonNull(params double?[] candidates)
        {
            foreach (double? candidate in candidates)
            {
                if (candidate.HasValue)
                    return candidate;
            }

            return null;
        }

        private static bool HasPositiveValue(double? value)
        {
            return value.HasValue && Math.Abs(value.Value) > 1e-9;
        }

        private static double Distance3D(GeometryPoint a, GeometryPoint b)
        {
            return Math.Sqrt(
                Math.Pow(a.X - b.X, 2) +
                Math.Pow(a.Y - b.Y, 2) +
                Math.Pow(a.Z - b.Z, 2)
            );
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

        private sealed class BindingContext
        {
            private readonly Dictionary<string, ObjectId> _definitionIds =
                new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<ObjectId, Dictionary<ObjectId, PropertySet>> _attachedPropertySets =
                new Dictionary<ObjectId, Dictionary<ObjectId, PropertySet>>();
            private readonly Dictionary<ObjectId, Dictionary<string, int>> _propertyIds =
                new Dictionary<ObjectId, Dictionary<string, int>>();

            public BindingContext(IfcCompiledMappingConfig mappingConfig)
            {
                MappingConfig = mappingConfig;
            }

            public IfcCompiledMappingConfig MappingConfig { get; }
            public bool HasMappingConfig => MappingConfig != null && MappingConfig.Rules.Count > 0;

            public bool TryGetDefinitionId(string name, out ObjectId definitionId)
            {
                definitionId = ObjectId.Null;
                return !string.IsNullOrWhiteSpace(name) && _definitionIds.TryGetValue(name, out definitionId);
            }

            public void RememberDefinitionId(string name, ObjectId definitionId)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    _definitionIds[name] = definitionId;
            }

            public bool TryGetAttachedPropertySet(ObjectId entityId, ObjectId definitionId, out PropertySet propertySet)
            {
                propertySet = null;
                return _attachedPropertySets.TryGetValue(entityId, out Dictionary<ObjectId, PropertySet> entitySets) &&
                       entitySets.TryGetValue(definitionId, out propertySet) &&
                       propertySet != null;
            }

            public void RememberAttachedPropertySet(ObjectId entityId, ObjectId definitionId, PropertySet propertySet)
            {
                if (propertySet == null)
                    return;

                if (!_attachedPropertySets.TryGetValue(entityId, out Dictionary<ObjectId, PropertySet> entitySets))
                {
                    entitySets = new Dictionary<ObjectId, PropertySet>();
                    _attachedPropertySets[entityId] = entitySets;
                }

                entitySets[definitionId] = propertySet;
            }

            public bool TryGetPropertyId(ObjectId propertySetId, string propertyName, out int propertyId)
            {
                propertyId = -1;
                return !string.IsNullOrWhiteSpace(propertyName) &&
                       _propertyIds.TryGetValue(propertySetId, out Dictionary<string, int> idsByName) &&
                       idsByName.TryGetValue(propertyName, out propertyId);
            }

            public void RememberPropertyId(ObjectId propertySetId, string propertyName, int propertyId)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    return;

                if (!_propertyIds.TryGetValue(propertySetId, out Dictionary<string, int> idsByName))
                {
                    idsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    _propertyIds[propertySetId] = idsByName;
                }

                idsByName[propertyName] = propertyId;
            }
        }

        private sealed class SolidosParamReader
        {
            private readonly ObjectId _nodeId;
            private readonly Dictionary<string, RawParamCacheEntry> _rawParams =
                new Dictionary<string, RawParamCacheEntry>(StringComparer.OrdinalIgnoreCase);

            public SolidosParamReader(ObjectId nodeId)
            {
                _nodeId = nodeId;
            }

            public string GetString(string propName)
            {
                return TryGetString(propName, out string value) ? value : string.Empty;
            }

            public bool TryGetString(string propName, out string value)
            {
                value = string.Empty;
                if (!TryGetRawParam(propName, out object raw) || raw == null)
                    return false;

                string text = Convert.ToString(raw, InvariantCulture);
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                value = text.Trim();
                return true;
            }

            public bool TryGetPoint(string propName, out GeometryPoint value)
            {
                value = default;
                if (!TryGetRawParam(propName, out object raw) || !(raw is GeometryPoint point))
                    return false;

                value = point;
                return true;
            }

            public double? ReadFirstDouble(params string[] candidates)
            {
                if (candidates == null)
                    return null;

                foreach (string candidate in candidates)
                {
                    if (TryGetRawParam(candidate, out object raw) && TryConvertToDouble(raw, out double value))
                        return value;
                }

                return null;
            }

            public string ReadFirstString(params string[] candidates)
            {
                if (candidates == null)
                    return string.Empty;

                foreach (string candidate in candidates)
                {
                    if (TryGetString(candidate, out string value))
                        return value;
                }

                return string.Empty;
            }

            private bool TryGetRawParam(string propName, out object value)
            {
                value = null;
                if (string.IsNullOrWhiteSpace(propName))
                    return false;

                if (_rawParams.TryGetValue(propName, out RawParamCacheEntry cached))
                {
                    value = cached.Value;
                    return cached.Found;
                }

                bool found = TryReadRawParam(_nodeId, propName, out object rawValue);
                _rawParams[propName] = new RawParamCacheEntry(found, rawValue);
                value = rawValue;
                return found;
            }
        }

        private readonly struct RawParamCacheEntry
        {
            public RawParamCacheEntry(bool found, object value)
            {
                Found = found;
                Value = value;
            }

            public bool Found { get; }
            public object Value { get; }
        }

        private enum ElementCategory
        {
            Pipe,
            Culvert,
            Gutter,
            Sump,
            InspectionChamber,
            InspectionPit,
            MeterChamber,
            ValveChamber,
            Trench,
            Chamber,
            Manhole,
            Bend,
            Junction,
            Transition
        }

        private sealed class FamilyRule
        {
            public FamilyRule(string[] keywords, string ifcClass, string predefinedType, ElementCategory category, string[] psetNames)
            {
                Keywords = keywords ?? Array.Empty<string>();
                IfcClass = ifcClass ?? string.Empty;
                PredefinedType = predefinedType ?? string.Empty;
                Category = category;
                PsetNames = psetNames ?? Array.Empty<string>();
            }

            public string[] Keywords { get; }
            public string IfcClass { get; }
            public string PredefinedType { get; }
            public ElementCategory Category { get; }
            public string[] PsetNames { get; }

            public string DefaultFunction
            {
                get
                {
                    switch (Category)
                    {
                        case ElementCategory.Gutter:
                            return "Conducao superficial";
                        case ElementCategory.Culvert:
                        case ElementCategory.Pipe:
                            return "Conducao fechada";
                        case ElementCategory.Sump:
                            return "Coleta e acumulacao";
                        case ElementCategory.InspectionChamber:
                        case ElementCategory.InspectionPit:
                        case ElementCategory.MeterChamber:
                        case ElementCategory.ValveChamber:
                        case ElementCategory.Trench:
                        case ElementCategory.Manhole:
                        case ElementCategory.Chamber:
                            return "Coleta e inspecao";
                        case ElementCategory.Bend:
                        case ElementCategory.Junction:
                        case ElementCategory.Transition:
                            return "Conexao hidraulica";
                        default:
                            return string.Empty;
                    }
                }
            }
        }

        private sealed class SolidosNodeData
        {
            public string FamilyName { get; set; }
            public string Name { get; set; }
            public string Code { get; set; }
            public string Identification { get; set; }
            public string Description { get; set; }
            public string System { get; set; }
            public string Subsystem { get; set; }
            public string AreaOperacional { get; set; }
            public string Catalogo { get; set; }
            public string Tag { get; set; }
            public string Status { get; set; }
            public string Reference { get; set; }
            public double? Diameter { get; set; }
            public double? Width { get; set; }
            public double? Base { get; set; }
            public double? Height { get; set; }
            public double? CoverThickness { get; set; }
            public double? GrateHeight { get; set; }
            public double? FloorHeight { get; set; }
            public double? CoverHeight { get; set; }
            public double? DeflectionAngle { get; set; }
            public double? Manning { get; set; }
            public double? HazenWilliams { get; set; }
            public double? DarcyWeisbach { get; set; }
            public double? Declivity { get; set; }
            public double? BottomElevation { get; set; }
            public double? CotaSaida { get; set; }
            public bool HasStartPoint { get; set; }
            public bool HasEndPoint { get; set; }
            public bool HasLocation { get; set; }
            public GeometryPoint StartPoint { get; set; }
            public GeometryPoint EndPoint { get; set; }
            public GeometryPoint Location { get; set; }
            public double? NominalLength { get; set; }
            public double? Gradient { get; set; }
            public double? UpstreamInvert { get; set; }
            public double? DownstreamInvert { get; set; }
            public double? TopElevation { get; set; }
            public double? UsefulDepth { get; set; }
            public bool IsCovered { get; set; }
            public double? LengthFromProperty { get; set; }
            public double? ArcLength { get; set; }
            public double? LengthL { get; set; }
            public double? LengthL1 { get; set; }
            public double? WallThickness { get; set; }
            public double? SlabThickness { get; set; }
            public double? GrateLength { get; set; }
            public double? GrateWidth { get; set; }
            public double? CoverCount { get; set; }
            public double? SteelQuantity { get; set; }
            public double? StructureVolume { get; set; }
            public double? ExternalVolume { get; set; }
            public double? ConcreteArmadoVolume { get; set; }
            public double? ConcreteMagroVolume { get; set; }
            public double? MaxConnectionElevation { get; set; }
            public double? MinConnectionElevation { get; set; }
            public double? TopClearance { get; set; }
            public double? CoverClearance { get; set; }
            public string CoverType { get; set; }
            public string GrateType { get; set; }
            public string MaxPipe { get; set; }
            public double? CoverNominalDiameter { get; set; }
            public double? MeasuredVolume { get; set; }
            public double? MeasuredSurfaceArea { get; set; }
            public double? BoundingLength { get; set; }
            public double? BoundingWidth { get; set; }
            public double? BoundingHeight { get; set; }
        }
    }
}
