using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.UtilityResource;              // <-- use este
using Xbim.Ifc4x3.Kernel;
using Xbim.Ifc4x3.MeasureResource;
using Xbim.Ifc4x3.ProductExtension;
using Xbim.Ifc4x3.PropertyResource;
using Xbim.Ifc4x3.QuantityResource;
using Xbim.IO;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;



namespace AutomacoesCivil3D

{
    public static class QuantitiesIfcSmec
    {
        private const string PetrobrasPsetPrefix = "Pset_";
        private const string PetrobrasPsetName = "Pset_Petrobras";
        private const string DrainagePetrobrasPsetName = "Pset_DrenagemPetrobras";

        [CommandMethod("SIFC_QTO_SMEC")]
        public static void SifcQtoSmec()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            try
            {
                string inputIfcPath = PromptIfcOpen(docEditor);
                if (string.IsNullOrWhiteSpace(inputIfcPath)) return;

                string outputIfcPath = PromptIfcSave(docEditor);
                if (string.IsNullOrWhiteSpace(outputIfcPath)) return;

                (int updatedQto, int renamed, int fixedTypes, int linkedSystems, int linkedClassifications) = InjectSmecQtoAndRename(inputIfcPath, outputIfcPath);

                docEditor.WriteMessage($"\nOK. QTO: {updatedQto} | Meta: {renamed} | TypeFix: {fixedTypes} | Sistemas: {linkedSystems} | Classif: {linkedClassifications}\nSaída: {outputIfcPath}\n");
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\n[AutoCAD] Erro: {ex.Message}\n");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[.NET] Erro: {FormatExceptionChain(ex)}\n");
            }
        }

        private static string PromptIfcOpen(Editor docEditor)
        {
            PromptOpenFileOptions opt = new PromptOpenFileOptions("\nSelecione o IFC de entrada:");
            opt.Filter = "IFC (*.ifc)|*.ifc";
            PromptFileNameResult res = docEditor.GetFileNameForOpen(opt);
            if (res.Status != PromptStatus.OK) return string.Empty;
            return res.StringResult;
        }

        private static string PromptIfcSave(Editor docEditor)
        {
            PromptSaveFileOptions opt = new PromptSaveFileOptions("\nSalvar IFC de saída (com QTO):");
            opt.Filter = "IFC (*.ifc)|*.ifc";
            PromptFileNameResult res = docEditor.GetFileNameForSave(opt);
            if (res.Status != PromptStatus.OK) return string.Empty;
            return res.StringResult;
        }

        private static (int updatedQto, int renamed, int fixedTypes, int linkedSystems, int linkedClassifications) InjectSmecQtoAndRename(string inputIfcPath, string outputIfcPath)
        {
            InitializeIfcServices();

            Xbim.Ifc.XbimEditorCredentials editor = new Xbim.Ifc.XbimEditorCredentials
            {
                ApplicationDevelopersName = "AutomacoesCivil3D",
                ApplicationFullName = "SMEC QTO Injector",
                ApplicationIdentifier = "AutomacoesCivil3D.SMECQTO",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Gleison",
                EditorsGivenName = "Engenheiro",
                EditorsOrganisationName = "AutomacoesCivil3D"
            };

            using Xbim.Ifc.IfcStore model = Xbim.Ifc.IfcStore.Open(
                inputIfcPath,
                editorDetails: editor,
                accessMode: XbimDBAccess.ReadWrite
            );

            using Xbim.Common.ITransaction tr = model.BeginTransaction("Inject SMEC QTO + Rename from TAG");

            int updated = 0;
            int renamed = 0;

            // ---- 1) QTO para Chambers ----
            IIfcDistributionChamberElement[] chambers = model.Instances
                .OfType<IIfcDistributionChamberElement>()
                .ToArray();

            foreach (IIfcDistributionChamberElement chamber in chambers)
            {
                IIfcPropertySet smecPset = FindPropertySet(chamber, "Qto_SmecCaixas");
                if (smecPset == null) continue;

                IfcElementQuantity qto = GetOrCreateElementQuantity(model, chamber, "Qto_SMEC");
                qto.Quantities.Clear();

                if (TryGetDouble(smecPset, "Volume Concreto Armado", out double volConcArmado))
                {
                    IfcQuantityVolume q = model.Instances.New<IfcQuantityVolume>();
                    q.Name = "ReinforcedConcreteVolume";
                    q.VolumeValue = new IfcVolumeMeasure(volConcArmado);
                    qto.Quantities.Add(q);
                }

                if (TryGetDouble(smecPset, "Volume Concreto Magro", out double volConcMagro))
                {
                    IfcQuantityVolume q = model.Instances.New<IfcQuantityVolume>();
                    q.Name = "LeanConcreteVolume";
                    q.VolumeValue = new IfcVolumeMeasure(volConcMagro);
                    qto.Quantities.Add(q);
                }

                if (TryGetDouble(smecPset, "Volume de Aço", out double steelKg))
                {
                    IfcQuantityWeight q = model.Instances.New<IfcQuantityWeight>();
                    q.Name = "SteelWeight";
                    q.WeightValue = new IfcMassMeasure(steelKg);
                    qto.Quantities.Add(q);
                }

                if (TryGetInt(smecPset, "Quantidade Tampas", out int tampas))
                {
                    IfcQuantityCount q = model.Instances.New<IfcQuantityCount>();
                    q.Name = "CoverCount";
                    q.CountValue = new IfcCountMeasure(tampas);
                    qto.Quantities.Add(q);
                }

                if (TryGetDouble(smecPset, "Espessura Concreto Magro", out double esp))
                {
                    IfcQuantityLength q = model.Instances.New<IfcQuantityLength>();
                    q.Name = "LeanConcreteThickness";
                    q.LengthValue = new IfcLengthMeasure(esp);
                    qto.Quantities.Add(q);
                }

                updated++;
            }

            // Filtra só o que interessa pra rede
            IIfcObject[] targets = model.Instances
                .OfType<IIfcObject>()
                .Where(o =>
                    o is IIfcDistributionChamberElement ||
                    o is IIfcPipeSegment ||
                    o is IIfcPipeFitting ||
                    o is IIfcColumn ||
                    o is IIfcValve
                )
                .ToArray();

            foreach (IIfcObject obj in targets)
            {
                if (ApplyDrainageMetadata(obj))
                    renamed++;
            }

            int fixedTypes =
                ApplyPipeSegmentPredefinedTypeFromMetadata(model) +
                ApplyDistributionChamberPredefinedTypeFromMetadata(model) +
                ApplyPipeFittingPredefinedTypeFromMetadata(model);
            int linkedSystems = ApplyDrainageSystems(model);
            int linkedClassifications = ApplyDrainageClassification(model);

            tr.Commit();
            model.SaveAs(outputIfcPath);

            return (updated, renamed, fixedTypes, linkedSystems, linkedClassifications);
        }

        private static bool ApplyDrainageMetadata(IIfcObject obj)
        {
            bool changed = false;
            string name = TryGetFirstMeaningfulPetrobrasProperty(obj, new[] { "NomeObjeto", "Name", "NAME", "Reference" });
            if (string.IsNullOrWhiteSpace(name))
                name = TryGetFirstMeaningfulProperty(obj, new[] { "NomeObjeto", "NAME", "Name", "Reference" });

            string tag = TryGetFirstStringPropertyFromPetrobrasPset(obj, new[] { "Tag", "CodigoObjeto", "Reference" });
            if (string.IsNullOrWhiteSpace(tag))
                tag = TryGetFirstStringProperty(obj, new[] { "Tag", "CodigoObjeto", "Reference" });

            if (string.IsNullOrWhiteSpace(name) && !LooksLikeNumericReference(tag) && !LooksLikeAnonymousBlockName(tag))
                name = tag?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(name))
            {
                if (!string.Equals(SafeToString(obj.Name), name, StringComparison.Ordinal))
                {
                    obj.Name = name;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                tag = tag.Trim();
                if (obj is IIfcElement element && !string.Equals(SafeToString(element.Tag), tag, StringComparison.Ordinal))
                {
                    element.Tag = tag;
                    changed = true;
                }
            }

            string objectType = TryGetFirstStringProperty(obj, new[] { "IFC::ObjectType", "ObjectType", "FuncaoDrenagem", "ModeloSolidos" });
            if (string.IsNullOrWhiteSpace(objectType))
                objectType = InferFriendlyObjectType(obj);

            if (!string.IsNullOrWhiteSpace(objectType))
            {
                objectType = objectType.Trim();
                if (!string.Equals(SafeToString(obj.ObjectType), objectType, StringComparison.Ordinal))
                {
                    obj.ObjectType = objectType;
                    changed = true;
                }
            }

            string description = TryGetFirstStringProperty(obj, new[] { "Observacoes", "Description", "Catalogo", "FuncaoDrenagem" });
            if (string.IsNullOrWhiteSpace(description))
                description = objectType;

            if (!string.IsNullOrWhiteSpace(description))
            {
                description = description.Trim();
                if (!string.Equals(SafeToString(obj.Description), description, StringComparison.Ordinal))
                {
                    obj.Description = description;
                    changed = true;
                }
            }

            return changed;
        }

        private static string TryGetFirstMeaningfulProperty(IIfcObject obj, string[] names)
        {
            return NormalizeMeaningfulText(TryGetFirstStringProperty(obj, names));
        }

        private static string TryGetFirstStringPropertyFromPetrobrasPset(IIfcObject obj, string[] names)
        {
            return TryGetFirstStringProperty(
                obj,
                names,
                psetName =>
                    string.Equals(psetName, PetrobrasPsetName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(psetName, DrainagePetrobrasPsetName, StringComparison.OrdinalIgnoreCase) ||
                    psetName.StartsWith(PetrobrasPsetPrefix, StringComparison.OrdinalIgnoreCase)
            );
        }

        private static string TryGetFirstMeaningfulPetrobrasProperty(IIfcObject obj, string[] names)
        {
            return NormalizeMeaningfulText(TryGetFirstStringPropertyFromPetrobrasPset(obj, names));
        }

        private static int ApplyDrainageSystems(Xbim.Ifc.IfcStore model)
        {
            int linked = 0;

            IIfcObject[] targets = model.Instances
                .OfType<IIfcObject>()
                .Where(o =>
                    o is IIfcDistributionChamberElement ||
                    o is IIfcPipeSegment ||
                    o is IIfcPipeFitting ||
                    o is IIfcValve
                )
                .ToArray();

            foreach (IIfcObject obj in targets)
            {
                string system = TryGetFirstMeaningfulPetrobrasProperty(obj, new[] { "Sistema", "System", "Rede", "Network", "NetworkName", "NomeRede" });
                string subsystem = TryGetFirstMeaningfulPetrobrasProperty(obj, new[] { "Subsistema", "Subsystem", "Subnetwork", "Ramal", "Branch" });
                string systemName = BuildSystemName(system, subsystem);
                if (string.IsNullOrWhiteSpace(systemName))
                    continue;

                IfcSystem ifcSystem = GetOrCreateSystem(model, systemName, system, subsystem);
                if (ifcSystem != null && EnsureAssignedToSystem(model, obj, ifcSystem))
                    linked++;
            }

            return linked;
        }

        private static string BuildSystemName(string system, string subsystem)
        {
            string main = SafeToString(system).Trim();
            string child = SafeToString(subsystem).Trim();

            if (string.IsNullOrWhiteSpace(main))
                return child;
            if (string.IsNullOrWhiteSpace(child))
                return main;

            return main + " / " + child;
        }

        private static IfcSystem GetOrCreateSystem(Xbim.Ifc.IfcStore model, string systemName, string system, string subsystem)
        {
            IfcSystem existing = model.Instances
                .OfType<IfcSystem>()
                .FirstOrDefault(x => string.Equals(SafeToString(x.Name), systemName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            IfcSystem created = model.Instances.New<IfcSystem>();
            created.Name = systemName;
            created.ObjectType = "Drenagem";
            created.Description = BuildSystemDescription(system, subsystem);
            return created;
        }

        private static string BuildSystemDescription(string system, string subsystem)
        {
            string main = SafeToString(system).Trim();
            string child = SafeToString(subsystem).Trim();

            if (string.IsNullOrWhiteSpace(main))
                return child;
            if (string.IsNullOrWhiteSpace(child))
                return "Rede de drenagem: " + main;

            return "Rede de drenagem: " + main + " | Subsistema: " + child;
        }

        private static bool EnsureAssignedToSystem(Xbim.Ifc.IfcStore model, IIfcObject obj, IfcSystem ifcSystem)
        {
            IfcObjectDefinition objectDefinition = obj as IfcObjectDefinition;
            if (objectDefinition == null)
                return false;

            IfcRelAssignsToGroup relation = model.Instances
                .OfType<IfcRelAssignsToGroup>()
                .FirstOrDefault(x => x.RelatingGroup != null && x.RelatingGroup.EntityLabel == ifcSystem.EntityLabel);

            if (relation == null)
            {
                relation = model.Instances.New<IfcRelAssignsToGroup>();
                relation.Name = ifcSystem.Name;
                relation.RelatingGroup = ifcSystem;
            }

            bool alreadyAssigned = relation.RelatedObjects
                .OfType<IIfcObjectDefinition>()
                .Any(x => x.EntityLabel == objectDefinition.EntityLabel);

            if (alreadyAssigned)
                return false;

            relation.RelatedObjects.Add(objectDefinition);
            return true;
        }

        private static string TryGetFirstStringProperty(IIfcObject obj, string[] names)
        {
            return TryGetFirstStringProperty(obj, names, _ => true);
        }

        private static string TryGetFirstStringProperty(IIfcObject obj, string[] names, Func<string, bool> psetFilter)
        {
            var rels = obj.IsDefinedBy.OfType<IIfcRelDefinesByProperties>();
            foreach (var rel in rels)
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet ps)
                {
                    string psetName = SafeToString(ps.Name);
                    if (psetFilter != null && !psetFilter(psetName))
                        continue;

                    foreach (string n in names)
                    {
                        var p = ps.HasProperties
                            .OfType<IIfcPropertySingleValue>()
                            .FirstOrDefault(x => string.Equals(x.Name.ToString(), n, StringComparison.OrdinalIgnoreCase));

                        if (p?.NominalValue == null) continue;

                        string raw = SafeToString(p.NominalValue).Trim();
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        if (LooksLikeAnonymousBlockName(raw)) continue;

                        return raw;
                    }
                }
            }
            return "";
        }

        private static bool LooksLikeAnonymousBlockName(string value)
        {
            string trimmed = SafeToString(value).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            return Regex.IsMatch(trimmed, @"^\*[A-Z]\d+$", RegexOptions.IgnoreCase);
        }

        private static string NormalizeMeaningfulText(string value)
        {
            string trimmed = SafeToString(value).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            if (LooksLikeAnonymousBlockName(trimmed))
                return string.Empty;

            if (LooksLikeNumericReference(trimmed))
                return string.Empty;

            return trimmed;
        }

        private static bool LooksLikeNumericReference(string value)
        {
            string trimmed = SafeToString(value).Trim();
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

        private static IIfcPropertySet FindPropertySet(IIfcObject obj, string psetName)
        {
            IIfcRelDefinesByProperties[] rels = obj.IsDefinedBy
                .OfType<IIfcRelDefinesByProperties>()
                .ToArray();

            foreach (IIfcRelDefinesByProperties rel in rels)
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet ps)
                {
                    string name = SafeToString(ps.Name);
                    if (string.Equals(name, psetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ps;
                    }
                }
            }

            return null;
        }

        private static IfcElementQuantity GetOrCreateElementQuantity(Xbim.Ifc.IfcStore model, IIfcObject obj, string qtoName)
        {
            IIfcRelDefinesByProperties[] rels = obj.IsDefinedBy
                .OfType<IIfcRelDefinesByProperties>()
                .ToArray();

            foreach (IIfcRelDefinesByProperties rel in rels)
            {
                if (rel.RelatingPropertyDefinition is IIfcElementQuantity eq)
                {
                    string name = SafeToString(eq.Name);
                    if (string.Equals(name, qtoName, StringComparison.OrdinalIgnoreCase))
                    {
                        IfcElementQuantity typed = eq as IfcElementQuantity;
                        if (typed != null) return typed;
                    }
                }
            }

            IfcElementQuantity newQto = model.Instances.New<IfcElementQuantity>();
            newQto.Name = qtoName;
            newQto.Description = "Quantitativo SMEC (gerado a partir do Pset Quantitativo SMEC)";

            IfcRelDefinesByProperties relNew = model.Instances.New<IfcRelDefinesByProperties>();
            relNew.RelatingPropertyDefinition = newQto;
            relNew.RelatedObjects.Add((IfcObjectDefinition)obj);

            return newQto;
        }

        private static bool TryGetDouble(IIfcPropertySet pset, string propName, out double value)
        {
            value = 0.0;

            IIfcPropertySingleValue prop = pset.HasProperties
                .OfType<IIfcPropertySingleValue>()
                .FirstOrDefault(p => string.Equals(SafeToString(p.Name), propName, StringComparison.OrdinalIgnoreCase));

            if (prop == null || prop.NominalValue == null) return false;

            string raw = SafeToString(prop.NominalValue);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            if (!TryExtractNumber(raw, out double parsed)) return false;

            value = parsed;
            return true;
        }

        private static bool TryGetInt(IIfcPropertySet pset, string propName, out int value)
        {
            value = 0;

            if (!TryGetDouble(pset, propName, out double dbl)) return false;

            value = (int)Math.Round(dbl, 0);
            return true;
        }

        private static bool TryExtractNumber(string raw, out double value)
        {
            value = 0.0;

            string token = Regex.Match(raw ?? string.Empty, @"-?\d[\d\.,]*").Value;
            if (string.IsNullOrWhiteSpace(token)) return false;

            // 1) "0.176" / "0.05"
            if (token.Contains('.') && !token.Contains(','))
            {
                if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out double inv))
                {
                    value = inv;
                    return true;
                }
            }

            // 2) "0,176"
            if (token.Contains(',') && !token.Contains('.'))
            {
                CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");
                if (double.TryParse(token, NumberStyles.Any, ptBr, out double pt))
                {
                    value = pt;
                    return true;
                }

                string normComma = token.Replace(',', '.');
                if (double.TryParse(normComma, NumberStyles.Any, CultureInfo.InvariantCulture, out double inv2))
                {
                    value = inv2;
                    return true;
                }
            }

            // 3) "1.234,56" ou "1,234.56"
            if (token.Contains('.') && token.Contains(','))
            {
                int lastDot = token.LastIndexOf('.');
                int lastComma = token.LastIndexOf(',');

                char decimalSep = lastComma > lastDot ? ',' : '.';
                char thousandSep = decimalSep == ',' ? '.' : ',';

                string normalized = token.Replace(thousandSep.ToString(), "");
                normalized = decimalSep == ',' ? normalized.Replace(',', '.') : normalized;

                if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out double mixed))
                {
                    value = mixed;
                    return true;
                }
            }

            return double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static string SafeToString(object value)
        {
            if (value == null) return string.Empty;
            return value.ToString() ?? string.Empty;
        }

        private static int ApplyPipeSegmentPredefinedTypeFromMetadata(Xbim.Ifc.IfcStore model)
        {
            int changed = 0;

            IIfcPipeSegment[] pipes = model.Instances
                .OfType<IIfcPipeSegment>()
                .ToArray();

            foreach (IIfcPipeSegment pipe in pipes)
            {
                if (TryResolvePipeSegmentType(pipe, out string ifcType) &&
                    TrySetEnumProperty(pipe, "PredefinedType", ifcType))
                    changed++;
            }

            return changed;
        }

        private static int ApplyDistributionChamberPredefinedTypeFromMetadata(Xbim.Ifc.IfcStore model)
        {
            int changed = 0;

            IIfcDistributionChamberElement[] chambers = model.Instances
                .OfType<IIfcDistributionChamberElement>()
                .ToArray();

            foreach (IIfcDistributionChamberElement chamber in chambers)
            {
                if (TryResolveDistributionChamberType(chamber, out string ifcType) &&
                    TrySetEnumProperty(chamber, "PredefinedType", ifcType))
                    changed++;
            }

            return changed;
        }

        private static int ApplyPipeFittingPredefinedTypeFromMetadata(Xbim.Ifc.IfcStore model)
        {
            int changed = 0;

            IIfcPipeFitting[] fittings = model.Instances
                .OfType<IIfcPipeFitting>()
                .ToArray();

            foreach (IIfcPipeFitting fitting in fittings)
            {
                if (TryResolvePipeFittingType(fitting, out string ifcType) &&
                    TrySetEnumProperty(fitting, "PredefinedType", ifcType))
                    changed++;
            }

            return changed;
        }

        private static bool TryResolvePipeSegmentType(IIfcPipeSegment pipe, out string ifcType)
        {
            ifcType = TryResolveTypeFromProperties(pipe, "IfcPipeSegment");
            if (!string.IsNullOrWhiteSpace(ifcType))
                return true;

            string text = BuildInferenceText(pipe);
            if (ContainsAny(text, "GUTTER", "CANALETA", "SARJETA", "DRO_CANALETA"))
            {
                ifcType = "GUTTER";
                return true;
            }

            if (ContainsAny(text, "CULVERT", "BUEIRO"))
            {
                ifcType = "CULVERT";
                return true;
            }

            if (ContainsAny(text, "TUBO", "PIPE", "FF", "PEAD", "PVC", "DRENO", "PTB - TUB"))
            {
                ifcType = "RIGIDSEGMENT";
                return true;
            }

            return false;
        }

        private static bool TryResolveDistributionChamberType(IIfcDistributionChamberElement chamber, out string ifcType)
        {
            ifcType = TryResolveTypeFromProperties(chamber, "IfcDistributionChamberElement");
            if (!string.IsNullOrWhiteSpace(ifcType))
                return true;

            string text = BuildInferenceText(chamber);

            if (ContainsAny(text, "VALVECHAMBER", "CAMARA DE VALVULA", "CAIXA DE VALVULA"))
            {
                ifcType = "VALVECHAMBER";
                return true;
            }

            if (ContainsAny(text, "METERCHAMBER", "CAMARA DE MEDICAO", "CAIXA DE MEDICAO", "HIDROMETRO", "MEDIDOR"))
            {
                ifcType = "METERCHAMBER";
                return true;
            }

            if (ContainsAny(text, "SUMP", "SUMIDOURO", "RALO"))
            {
                ifcType = "SUMP";
                return true;
            }

            if (ContainsAny(text, "TRENCH", "VALA"))
            {
                ifcType = "TRENCH";
                return true;
            }

            if (ContainsAny(text, "MANHOLE", "POCO DE VISITA"))
            {
                ifcType = "MANHOLE";
                return true;
            }

            if (ContainsAny(text, "INSPECTIONPIT", "CAIXA DE PASSAGEM", "CAIXA DE AREIA"))
            {
                ifcType = "INSPECTIONPIT";
                return true;
            }

            if (ContainsAny(text, "INSPECTIONCHAMBER", "CAIXA", "CHAMBER", "ESTRUTURA AC", "ESTRUTURA AO", "RCO", "CRC"))
            {
                ifcType = "INSPECTIONCHAMBER";
                return true;
            }

            return false;
        }

        private static bool TryResolvePipeFittingType(IIfcPipeFitting fitting, out string ifcType)
        {
            ifcType = TryResolveTypeFromProperties(fitting, "IfcPipeFitting");
            if (!string.IsNullOrWhiteSpace(ifcType))
                return true;

            string text = BuildInferenceText(fitting);

            if (ContainsAny(text, "JOELHO", "CURVA", "BEND", "ELBOW"))
            {
                ifcType = "BEND";
                return true;
            }

            if (ContainsAny(text, "REDUCAO", "TRANSICAO", "TRANSITION", "REDUCER"))
            {
                ifcType = "TRANSITION";
                return true;
            }

            if (ContainsAny(text, "JUNCAO", "JUNCTION", "TEE", "TE ", "CRUZETA", "CONEX"))
            {
                ifcType = "JUNCTION";
                return true;
            }

            return false;
        }

        private static string TryResolveTypeFromProperties(IIfcObject obj, string expectedIfcClass)
        {
            string exportAs = TryGetFirstStringProperty(obj, new[]
            {
                "IFC::IfcExportAs",
                "IFC::IfxExportAs",
                "IFC::ExportAs",
                "IfcExportAs",
                "IfcExportAsOverride"
            });

            if (!string.IsNullOrWhiteSpace(exportAs) &&
                TryParseIfcClassAndType(exportAs.Trim(), out string ifcClass, out string parsedType) &&
                string.Equals(ifcClass, expectedIfcClass, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeIfcEnumLabel(parsedType);
            }

            string predefinedType = TryGetFirstStringProperty(obj, new[]
            {
                "IFC::PredefinedType",
                "IfcPredefinedType",
                "PredefinedType"
            });

            return NormalizeIfcEnumLabel(predefinedType);
        }

        private static string NormalizeIfcEnumLabel(string value)
        {
            string normalized = NormalizeKeywordText(value).Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            return normalized;
        }

        private static bool TrySetEnumProperty(object entity, string propertyName, string enumName)
        {
            if (entity == null || string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(enumName))
                return false;

            System.Reflection.PropertyInfo property = entity.GetType().GetProperty(propertyName);
            if (property == null)
                return false;

            Type enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!enumType.IsEnum)
                return false;

            string normalized = NormalizeIfcEnumLabel(enumName);
            if (!Enum.GetNames(enumType).Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
                return false;

            object current = property.GetValue(entity);
            object parsed = Enum.Parse(enumType, normalized, true);

            if (current != null && current.Equals(parsed))
                return false;

            property.SetValue(entity, parsed);
            return true;
        }

        private static int ApplyDrainageClassification(Xbim.Ifc.IfcStore model)
        {
            int linked = 0;

            IIfcObject[] targets = model.Instances
                .OfType<IIfcObject>()
                .Where(o =>
                    o is IIfcDistributionChamberElement ||
                    o is IIfcPipeSegment ||
                    o is IIfcPipeFitting ||
                    o is IIfcValve
                )
                .ToArray();

            foreach (IIfcObject obj in targets)
            {
                if (!TryBuildClassificationInfo(obj, out string identification, out string name))
                    continue;

                if (EnsureClassificationAssociation(model, obj, identification, name))
                    linked++;
            }

            return linked;
        }

        private static bool TryBuildClassificationInfo(IIfcObject obj, out string identification, out string name)
        {
            identification = string.Empty;
            name = string.Empty;

            if (obj is IIfcPipeSegment pipe)
            {
                string type = NormalizeIfcEnumLabel(ReadEnumProperty(pipe, "PredefinedType"));
                identification = BuildClassificationCode("IfcPipeSegment", type);
                name = string.IsNullOrWhiteSpace(type) ? "Tubo de drenagem" : "IfcPipeSegment " + type;
                return true;
            }

            if (obj is IIfcDistributionChamberElement chamber)
            {
                string type = NormalizeIfcEnumLabel(ReadEnumProperty(chamber, "PredefinedType"));
                identification = BuildClassificationCode("IfcDistributionChamberElement", type);
                name = string.IsNullOrWhiteSpace(type) ? "Caixa de drenagem" : "IfcDistributionChamberElement " + type;
                return true;
            }

            if (obj is IIfcPipeFitting fitting)
            {
                string type = NormalizeIfcEnumLabel(ReadEnumProperty(fitting, "PredefinedType"));
                identification = BuildClassificationCode("IfcPipeFitting", type);
                name = string.IsNullOrWhiteSpace(type) ? "Conexao de drenagem" : "IfcPipeFitting " + type;
                return true;
            }

            if (obj is IIfcValve)
            {
                identification = "IfcValve";
                name = "IfcValve";
                return true;
            }

            return false;
        }

        private static string BuildClassificationCode(string ifcClass, string predefinedType)
        {
            if (string.IsNullOrWhiteSpace(predefinedType) || string.Equals(predefinedType, "NOTDEFINED", StringComparison.OrdinalIgnoreCase))
                return ifcClass;

            return ifcClass + "." + predefinedType;
        }

        private static bool EnsureClassificationAssociation(Xbim.Ifc.IfcStore model, IIfcObject obj, string identification, string name)
        {
            object reference = GetOrCreateClassificationReference(model, identification, name);
            if (reference == null)
                return false;

            if (HasClassificationAssociation(model, obj, identification))
                return false;

            object relation = CreateSchemaEntity(model, "Kernel.IfcRelAssociatesClassification");
            if (relation == null)
                return false;

            SetPropertyValue(relation, "Name", identification);
            SetPropertyValue(relation, "RelatingClassification", reference);

            object relatedObjects = ReadPropertyValue(relation, "RelatedObjects");
            if (relatedObjects == null || !TryInvokeAdd(relatedObjects, obj))
                return false;

            return true;
        }

        private static bool HasClassificationAssociation(Xbim.Ifc.IfcStore model, IIfcObject obj, string identification)
        {
            return model.Instances
                .OfType<IIfcRelAssociatesClassification>()
                .Any(rel =>
                    string.Equals(GetClassificationIdentification(rel.RelatingClassification), identification, StringComparison.OrdinalIgnoreCase) &&
                    rel.RelatedObjects.OfType<IIfcObjectDefinition>().Any(x => x.EntityLabel == obj.EntityLabel));
        }

        private static object GetOrCreateClassificationReference(Xbim.Ifc.IfcStore model, string identification, string name)
        {
            IIfcClassificationReference existing = model.Instances
                .OfType<IIfcClassificationReference>()
                .FirstOrDefault(x => string.Equals(SafeToString(x.Identification), identification, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            object classification = GetOrCreateClassification(model);
            if (classification == null)
                return null;

            object created = CreateSchemaEntity(model, "ExternalReferenceResource.IfcClassificationReference");
            if (created == null)
                return null;

            SetPropertyValue(created, "Identification", identification);
            SetPropertyValue(created, "Name", name);
            SetPropertyValue(created, "ReferencedSource", classification);
            return created;
        }

        private static object GetOrCreateClassification(Xbim.Ifc.IfcStore model)
        {
            IIfcClassification existing = model.Instances
                .OfType<IIfcClassification>()
                .FirstOrDefault(x => string.Equals(SafeToString(x.Name), "Petrobras Drainage IFC Mapping", StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            object created = CreateSchemaEntity(model, "ExternalReferenceResource.IfcClassification");
            if (created == null)
                return null;

            SetPropertyValue(created, "Name", "Petrobras Drainage IFC Mapping");
            SetPropertyValue(created, "Source", "AutomacoesCivil3D");
            SetPropertyValue(created, "Edition", "2026-03-18");
            return created;
        }

        private static object CreateSchemaEntity(Xbim.Ifc.IfcStore model, string suffix)
        {
            string schemaRoot = ResolveSchemaRoot(model);
            if (string.IsNullOrWhiteSpace(schemaRoot))
                return null;

            string fullTypeName = schemaRoot + "." + suffix;
            Type entityType = model.GetType().Assembly.GetType(fullTypeName) ?? AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(x => x.GetType(fullTypeName))
                .FirstOrDefault(x => x != null);

            if (entityType == null)
                return null;

            System.Reflection.MethodInfo createMethod = model.Instances.GetType()
                .GetMethods()
                .FirstOrDefault(x => x.Name == "New" && x.IsGenericMethodDefinition && x.GetParameters().Length == 0);

            if (createMethod == null)
                return null;

            return createMethod.MakeGenericMethod(entityType).Invoke(model.Instances, null);
        }

        private static string ResolveSchemaRoot(Xbim.Ifc.IfcStore model)
        {
            IIfcProject project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
            string projectNamespace = project?.GetType().Namespace ?? string.Empty;
            int kernelIndex = projectNamespace.IndexOf(".Kernel", StringComparison.Ordinal);
            if (kernelIndex <= 0)
                return string.Empty;

            return projectNamespace.Substring(0, kernelIndex);
        }

        private static string GetClassificationIdentification(object relatingClassification)
        {
            string identification = SafeToString(ReadPropertyValue(relatingClassification, "Identification"));
            if (!string.IsNullOrWhiteSpace(identification))
                return identification;

            return SafeToString(ReadPropertyValue(relatingClassification, "Name"));
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

        private static bool SetPropertyValue(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            System.Reflection.PropertyInfo property = target.GetType().GetProperty(propertyName);
            if (property == null || !property.CanWrite)
                return false;

            if (!TryConvertPropertyValue(property.PropertyType, value, out object converted))
                return false;

            property.SetValue(target, converted);
            return true;
        }

        private static bool TryConvertPropertyValue(Type propertyType, object value, out object converted)
        {
            converted = null;

            if (propertyType == null)
                return false;

            if (value == null)
            {
                if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) != null)
                {
                    converted = null;
                    return true;
                }

                return false;
            }

            Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            Type valueType = value.GetType();

            if (targetType.IsAssignableFrom(valueType))
            {
                converted = value;
                return true;
            }

            if (value is string text)
            {
                if (TryConvertStringValue(targetType, text, out converted))
                    return true;
            }

            try
            {
                converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertStringValue(Type targetType, string value, out object converted)
        {
            converted = null;

            if (targetType == typeof(string))
            {
                converted = value;
                return true;
            }

            System.Reflection.MethodInfo implicitOperator = targetType
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(x =>
                    (x.Name == "op_Implicit" || x.Name == "op_Explicit") &&
                    x.ReturnType == targetType &&
                    x.GetParameters().Length == 1 &&
                    x.GetParameters()[0].ParameterType == typeof(string));

            if (implicitOperator != null)
            {
                converted = implicitOperator.Invoke(null, new object[] { value });
                return true;
            }

            System.Reflection.ConstructorInfo ctor = targetType.GetConstructor(new[] { typeof(string) });
            if (ctor != null)
            {
                converted = ctor.Invoke(new object[] { value });
                return true;
            }

            System.Reflection.MethodInfo parseMethod = targetType.GetMethod(
                "Parse",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            if (parseMethod != null)
            {
                converted = parseMethod.Invoke(null, new object[] { value });
                return true;
            }

            try
            {
                converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeAdd(object collection, object value)
        {
            if (collection == null || value == null)
                return false;

            System.Reflection.MethodInfo addMethod = collection.GetType().GetMethod("Add");
            if (addMethod == null)
                return false;

            addMethod.Invoke(collection, new[] { value });
            return true;
        }

        private static string ReadEnumProperty(object entity, string propertyName)
        {
            object value = ReadPropertyValue(entity, propertyName);
            return SafeToString(value);
        }

        private static string InferFriendlyObjectType(IIfcObject obj)
        {
            string layer = BuildInferenceText(obj);

            if (obj is IIfcDistributionChamberElement)
            {
                if (ContainsAny(layer, "ESTRUTURA AC"))
                    return "Estrutura em concreto armado";
                if (ContainsAny(layer, "ESTRUTURA AO"))
                    return "Estrutura em aco";
                return "Caixa de drenagem";
            }

            if (obj is IIfcPipeSegment pipe && TryResolvePipeSegmentType(pipe, out string pipeType))
            {
                if (string.Equals(pipeType, "GUTTER", StringComparison.OrdinalIgnoreCase))
                    return "Canaleta de drenagem";
                if (string.Equals(pipeType, "CULVERT", StringComparison.OrdinalIgnoreCase))
                    return "Bueiro";
                return "Tubo de drenagem";
            }

            if (obj is IIfcPipeFitting fitting && TryResolvePipeFittingType(fitting, out string fittingType))
            {
                if (string.Equals(fittingType, "BEND", StringComparison.OrdinalIgnoreCase))
                    return "Curva de tubulacao";
                if (string.Equals(fittingType, "TRANSITION", StringComparison.OrdinalIgnoreCase))
                    return "Transicao de tubulacao";
                return "Juncao de tubulacao";
            }

            return string.Empty;
        }

        private static string BuildInferenceText(IIfcObject obj)
        {
            string[] values =
            {
                SafeToString(obj.Name),
                SafeToString(obj.ObjectType),
                SafeToString(obj.Description),
                TryGetFirstStringProperty(obj, new[] { "Layer", "LAYER", "CadLayer", "SourceLayer" }),
                TryGetFirstStringProperty(obj, new[] { "NAME", "Name", "NomeObjeto", "Reference" }),
                TryGetFirstStringProperty(obj, new[] { "Description", "Observacoes", "Catalogo" }),
                TryGetFirstStringProperty(obj, new[] { "IFC::ObjectType", "ObjectType", "FuncaoDrenagem", "ModeloSolidos" })
            };

            return NormalizeKeywordText(string.Join(" | ", values.Where(x => !string.IsNullOrWhiteSpace(x))));
        }

        private static string NormalizeKeywordText(string value)
        {
            string input = SafeToString(value);
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            string normalized = input.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(normalized.Length);

            foreach (char character in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                sb.Append(char.ToUpperInvariant(character));
            }

            return sb.ToString();
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(text) || terms == null || terms.Length == 0)
                return false;

            foreach (string term in terms)
            {
                string normalized = NormalizeKeywordText(term);
                if (!string.IsNullOrWhiteSpace(normalized) && text.Contains(normalized, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool TryParseIfcClassAndType(string value, out string ifcClass, out string ifcType)
        {
            ifcClass = "";
            ifcType = "";

            // aceita:
            // "IfcPipeSegment.GUTTER"
            // "IfcPipeSegment, GUTTER"
            // "IfcPipeSegment - GUTTER"
            string cleaned = value.Replace(" ", "");

            // padrão mais comum
            int dot = cleaned.IndexOf('.');
            if (dot > 0 && dot < cleaned.Length - 1)
            {
                ifcClass = cleaned.Substring(0, dot);
                ifcType = cleaned.Substring(dot + 1);
                return true;
            }

            // fallback
            string[] split = cleaned.Split(new[] { ',', ';', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length >= 2)
            {
                ifcClass = split[0];
                ifcType = split[1];
                return true;
            }

            return false;
        }

        private static void InitializeIfcServices()
        {
            global::AutomacoesCivil3D.XbimServiceBootstrap.EnsureInitialized();
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
    }
}
