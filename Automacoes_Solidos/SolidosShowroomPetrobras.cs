using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

using SOLIDOS;

namespace AutomacoesCivil3D.AUTOMAÇÕES_PLUG_IN_SOLIDOS
{
    public class SolidosShowroomPetrobras
    {
        private const string CommandName = "SOL_SHOWROOM_PETROBRAS";
        private const string RootConstructors = "Constructors";
        private const string RootNetworks = "Networks";
        private const string RootPartsLists = "PartsLists";
        private const string PartsListName = "SHOWROOM PETROBRAS";
        private const string PartsListDescription = "Lista de materiais gerada automaticamente para showroom Petrobras.";
        private const string PrefixNetworkName = "SHOWROOM PETROBRAS";
        private const string NetworkDrainage = "DrainageNetwork";
        private const string NetworkSewer = "SewerNetwork";
        private const string NetworkGeneric = "GenericNetwork";
        private const string NetworkPressure = "PressureNetwork";
        private const string ParamName = "Name";
        private const string ParamSection = "Section";
        private const string ParamPartsList = "PartsList";
        private const string ParamCatalogo = "Catalogo";
        private const string ParamConstructor = "Constructor";
        private const string ParamLocation = "Location";
        private const string ParamStartPoint = "StartPoint";
        private const string ParamEndPoint = "EndPoint";
        private const string ParamFromAlign = "FromAlign";
        private const string ParamFromAlignStart = "FromAlignStart";
        private const string ParamFromAlignEnd = "FromAlignEnd";
        private const string ParamDescription = "Description";
        private const double BaseZ = 0.0;
        private const double PointSpacingX = 1.0;
        private const double RowSpacingY = 6.0;
        private const double LinearLengthXY = 2.0;
        private const double LinearVerticalDelta = 2.0;
        private const int MaxReportedFailures = 20;

        private static readonly string[] CatalogParamCandidates = new string[]
        {
            ParamCatalogo,
            "Catálogo",
            "Catalog",
            "CatalogCode"
        };

        private static readonly string[] FamilyParamCandidates = new string[]
        {
            "FamilyName",
            "Family",
            "Familia",
            "NomeFamilia",
            "PartFamilyName",
            "Category",
            "Categoria"
        };

        [CommandMethod(CommandName)]
        public void CriarShowroomPetrobras()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database database = civilDoc.Database;

            SolidosCatalogInterop sol = SolidosCatalogInterop.TryCreate(docEditor, database);
            if (!sol.IsReady)
            {
                docEditor.WriteMessage(
                    "\n[SOLIDOS] Não encontrei os métodos necessários para ler construtores, criar PartsList e criar dispositivos." +
                    "\nCarregue o plug-in do SOLIDOS e rode o comando novamente.\n");
                return;
            }

            try
            {
                using (civilDoc.LockDocument())
                {
                    RunShowroom(civilDoc, docEditor, sol);
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO] {ex.Message}\n");
            }
        }

        private static void RunShowroom(Document civilDoc, Editor docEditor, SolidosCatalogInterop sol)
        {
            List<ConstructorCatalogInfo> items = DiscoverPetrobrasCatalogItems(sol);
            if (items.Count == 0)
            {
                docEditor.WriteMessage(
                    "\n[SOLIDOS] Não encontrei modeladores no nó 'Constructors' com algum valor de catálogo contendo 'PETROBRAS'.\n");
                return;
            }

            string partsListsRoot = sol.GetRootNode(RootPartsLists);
            if (!SolidosCatalogInterop.IsValidHandle(partsListsRoot))
            {
                docEditor.WriteMessage("\n[SOLIDOS] Não encontrei o nó raiz 'PartsLists'.\n");
                return;
            }

            string partsListHandle = EnsurePartsList(sol, partsListsRoot, PartsListName);
            if (!SolidosCatalogInterop.IsValidHandle(partsListHandle))
            {
                docEditor.WriteMessage("\n[SOLIDOS] Falhei ao criar/obter a PartsList do showroom.\n");
                return;
            }

            NetworkHandles networkHandles = EnsureNetworks(sol, partsListHandle);
            if (!networkHandles.HasAny)
            {
                docEditor.WriteMessage("\n[SOLIDOS] Falhei ao criar/obter as redes de showroom.\n");
                return;
            }

            ShowroomLayout layout = new ShowroomLayout(PointSpacingX, RowSpacingY);
            List<string> failures = new List<string>();

            int sectionsReady = 0;
            int sectionsCreated = 0;
            int devicesReady = 0;
            int devicesCreated = 0;
            int placedPoint = 0;
            int placedLinear = 0;
            int unsupportedLong = 0;
            int unsupportedUnknown = 0;

            foreach (ConstructorCatalogInfo item in items
                .OrderBy(i => i.FamilyKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.ConstructorName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.CatalogCode, StringComparer.OrdinalIgnoreCase))
            {
                string sectionHandle = EnsurePartSize(sol, partsListHandle, item, out bool sectionCreated);
                if (!SolidosCatalogInterop.IsValidHandle(sectionHandle))
                {
                    AddFailure(failures, $"Seção não criada para '{item.DisplayName}'.");
                    continue;
                }

                sectionsReady++;
                if (sectionCreated)
                {
                    sectionsCreated++;
                }

                string deviceHandle = EnsureDevice(sol, networkHandles, sectionHandle, item, out bool deviceCreated);
                if (!SolidosCatalogInterop.IsValidHandle(deviceHandle))
                {
                    AddFailure(failures, $"Dispositivo não criado para '{item.DisplayName}'.");
                    continue;
                }

                devicesReady++;
                if (deviceCreated)
                {
                    devicesCreated++;
                }

                string layoutClass = DetectLayoutClass(sol, deviceHandle);
                ShowroomPosition position = layout.Next(item.FamilyKey + " | " + layoutClass);

                switch (layoutClass)
                {
                    case "Linear":
                        if (PlaceLinearDevice(sol, deviceHandle, position))
                        {
                            placedLinear++;
                        }
                        else
                        {
                            AddFailure(failures, $"Falha ao locar linear '{item.DisplayName}'.");
                        }
                        break;

                    case "Point":
                        if (PlacePointDevice(sol, deviceHandle, position))
                        {
                            placedPoint++;
                        }
                        else
                        {
                            AddFailure(failures, $"Falha ao locar pontual '{item.DisplayName}'.");
                        }
                        break;

                    case "Long":
                        unsupportedLong++;
                        AddFailure(failures, $"Longitudinal sem regra automática de locação nesta versão: '{item.DisplayName}'.");
                        break;

                    default:
                        unsupportedUnknown++;
                        AddFailure(failures, $"Tipo não reconhecido para locação: '{item.DisplayName}'.");
                        break;
                }
            }

            sol.Commit(
                "Showroom Petrobras: " +
                $"itens={items.Count}, sections={sectionsReady}, devices={devicesReady}, " +
                $"pontuais={placedPoint}, lineares={placedLinear}");

            ForceVisualRefresh(civilDoc, docEditor);

            docEditor.WriteMessage(
                "\n[SOLIDOS] Showroom Petrobras concluído." +
                $"\n- Modeladores Petrobras encontrados: {items.Count}" +
                $"\n- Seções prontas na PartsList: {sectionsReady} (novas: {sectionsCreated})" +
                $"\n- Dispositivos prontos no showroom: {devicesReady} (novos: {devicesCreated})" +
                $"\n- Pontuais locados: {placedPoint}" +
                $"\n- Lineares locados: {placedLinear}" +
                $"\n- Longitudinais sem regra automática nesta versão: {unsupportedLong}" +
                $"\n- Tipos não reconhecidos: {unsupportedUnknown}" +
                (failures.Count > 0
                    ? "\n- Observações:\n  " + string.Join("\n  ", failures.Take(MaxReportedFailures))
                    : string.Empty) +
                "\n");
        }

        private static List<ConstructorCatalogInfo> DiscoverPetrobrasCatalogItems(SolidosCatalogInterop sol)
        {
            List<ConstructorCatalogInfo> result = new List<ConstructorCatalogInfo>();
            string constructorsRoot = sol.GetRootNode(RootConstructors);
            if (!SolidosCatalogInterop.IsValidHandle(constructorsRoot))
            {
                return result;
            }

            HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string constructorName in sol.GetChildNames(constructorsRoot))
            {
                if (string.IsNullOrWhiteSpace(constructorName))
                {
                    continue;
                }

                string constructorHandle = sol.FindObjectByName(constructorsRoot, constructorName);
                if (!SolidosCatalogInterop.IsValidHandle(constructorHandle))
                {
                    continue;
                }

                List<string> props = sol.ListProperties(constructorHandle);
                string catalogProp = FindCatalogProperty(props);
                if (string.IsNullOrWhiteSpace(catalogProp))
                {
                    continue;
                }

                List<string> catalogCodes = ResolveCatalogCodes(sol, constructorHandle, catalogProp);
                if (catalogCodes.Count == 0)
                {
                    continue;
                }

                string chosenCode = catalogCodes
                    .FirstOrDefault(v => v.IndexOf("PETROBRAS", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(chosenCode))
                {
                    continue;
                }

                string familyKey = ResolveFamilyKey(sol, constructorHandle, constructorName, chosenCode);
                string uniqueKey = constructorName.Trim() + "|" + chosenCode.Trim();
                if (!seenKeys.Add(uniqueKey))
                {
                    continue;
                }

                result.Add(new ConstructorCatalogInfo(
                    constructorHandle,
                    constructorName.Trim(),
                    chosenCode.Trim(),
                    familyKey.Trim()));
            }

            return result;
        }

        private static string EnsurePartsList(SolidosCatalogInterop sol, string partsListsRoot, string partsListName)
        {
            string existing = sol.FindObjectByName(partsListsRoot, partsListName);
            if (SolidosCatalogInterop.IsValidHandle(existing))
            {
                sol.TrySetNodeParams(existing, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [ParamDescription] = PartsListDescription
                });
                return existing;
            }

            return sol.TryCreateNode(partsListsRoot, "PartsList", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ParamName] = partsListName,
                [ParamDescription] = PartsListDescription
            });
        }

        private static NetworkHandles EnsureNetworks(SolidosCatalogInterop sol, string partsListHandle)
        {
            string networksRoot = sol.GetRootNode(RootNetworks);
            if (!SolidosCatalogInterop.IsValidHandle(networksRoot))
            {
                return new NetworkHandles();
            }

            NetworkHandles handles = new NetworkHandles();
            handles.Drainage = EnsureNetwork(sol, networksRoot, PrefixNetworkName + " - DRENAGEM", NetworkDrainage, partsListHandle);
            handles.Sewer = EnsureNetwork(sol, networksRoot, PrefixNetworkName + " - ESGOTO", NetworkSewer, partsListHandle);
            handles.Generic = EnsureNetwork(sol, networksRoot, PrefixNetworkName + " - GENERICA", NetworkGeneric, partsListHandle);
            handles.Pressure = EnsureNetwork(sol, networksRoot, PrefixNetworkName + " - PRESSAO", NetworkPressure, partsListHandle);
            return handles;
        }

        private static string EnsureNetwork(
            SolidosCatalogInterop sol,
            string networksRoot,
            string networkName,
            string networkType,
            string partsListHandle)
        {
            string existing = sol.FindObjectByName(networksRoot, networkName);
            if (SolidosCatalogInterop.IsValidHandle(existing))
            {
                sol.TrySetNodeParams(existing, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [ParamPartsList] = partsListHandle
                });
                return existing;
            }

            string created = sol.TryCreateNode(networksRoot, networkType, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ParamName] = networkName,
                [ParamPartsList] = partsListHandle
            });

            if (!SolidosCatalogInterop.IsValidHandle(created))
            {
                created = sol.TryCreateNode(networksRoot, networkType, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [ParamName] = networkName
                });

                if (SolidosCatalogInterop.IsValidHandle(created))
                {
                    sol.TrySetNodeParams(created, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ParamPartsList] = partsListHandle
                    });
                }
            }

            return created;
        }

        private static string EnsurePartSize(
            SolidosCatalogInterop sol,
            string partsListHandle,
            ConstructorCatalogInfo item,
            out bool created)
        {
            created = false;

            string existing = sol.FindObjectByName(partsListHandle, item.PartSizeName);
            if (SolidosCatalogInterop.IsValidHandle(existing))
            {
                return existing;
            }

            string createdHandle = sol.TryCreateNode(partsListHandle, "PartSize", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ParamName] = item.PartSizeName,
                [ParamConstructor] = item.ConstructorHandle,
                [ParamCatalogo] = item.CatalogCode
            });

            created = SolidosCatalogInterop.IsValidHandle(createdHandle);
            return createdHandle;
        }

        private static string EnsureDevice(
            SolidosCatalogInterop sol,
            NetworkHandles handles,
            string sectionHandle,
            ConstructorCatalogInfo item,
            out bool created)
        {
            created = false;
            string[] order = BuildPreferredNetworkOrder(item);

            foreach (string key in order)
            {
                string networkHandle = handles.Get(key);
                if (!SolidosCatalogInterop.IsValidHandle(networkHandle))
                {
                    continue;
                }

                string existing = sol.FindObjectByName(networkHandle, item.DisplayName);
                if (SolidosCatalogInterop.IsValidHandle(existing))
                {
                    return existing;
                }

                string createdHandle = sol.TryCreateNode(networkHandle, "Device", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [ParamName] = item.DisplayName,
                    [ParamSection] = sectionHandle
                });

                if (SolidosCatalogInterop.IsValidHandle(createdHandle))
                {
                    created = true;
                    return createdHandle;
                }
            }

            return string.Empty;
        }

        private static bool PlacePointDevice(SolidosCatalogInterop sol, string deviceHandle, ShowroomPosition position)
        {
            return sol.TrySetNodeParams(deviceHandle, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ParamLocation] = new GeometryPoint(position.X, position.Y, BaseZ)
            });
        }

        private static bool PlaceLinearDevice(SolidosCatalogInterop sol, string deviceHandle, ShowroomPosition position)
        {
            GeometryPoint start = new GeometryPoint(position.X, position.Y, BaseZ);
            GeometryPoint end = new GeometryPoint(position.X + LinearLengthXY, position.Y, BaseZ + LinearVerticalDelta);

            return sol.TrySetNodeParams(deviceHandle, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ParamStartPoint] = start,
                [ParamEndPoint] = end
            });
        }

        private static string DetectLayoutClass(SolidosCatalogInterop sol, string deviceHandle)
        {
            bool hasStart = sol.HasProperty(deviceHandle, ParamStartPoint);
            bool hasEnd = sol.HasProperty(deviceHandle, ParamEndPoint);
            if (hasStart && hasEnd)
            {
                return "Linear";
            }

            if (sol.HasProperty(deviceHandle, ParamLocation))
            {
                return "Point";
            }

            bool hasAlign = sol.HasProperty(deviceHandle, ParamFromAlign)
                || sol.HasProperty(deviceHandle, ParamFromAlignStart)
                || sol.HasProperty(deviceHandle, ParamFromAlignEnd);

            if (hasAlign)
            {
                return "Long";
            }

            return "Unknown";
        }

        private static string[] BuildPreferredNetworkOrder(ConstructorCatalogInfo item)
        {
            string text = (item.ConstructorName + " " + item.CatalogCode + " " + item.FamilyKey).ToUpperInvariant();

            if (text.Contains("PRESS"))
            {
                return new[] { "pressure", "generic", "drainage", "sewer" };
            }

            if (text.Contains("ESGOTO") || text.Contains("SEWER"))
            {
                return new[] { "sewer", "drainage", "generic", "pressure" };
            }

            if (text.Contains("GENERIC") || text.Contains("GENERICA") || text.Contains("GENÉRICA"))
            {
                return new[] { "generic", "drainage", "sewer", "pressure" };
            }

            return new[] { "drainage", "sewer", "generic", "pressure" };
        }

        private static string ResolveFamilyKey(
            SolidosCatalogInterop sol,
            string constructorHandle,
            string constructorName,
            string chosenCode)
        {
            foreach (string familyProp in FamilyParamCandidates)
            {
                object raw = sol.GetNodeParam(constructorHandle, familyProp);
                string text = FlattenToStrings(raw).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }

            string fromCatalog = DeriveFamilyFromCatalog(chosenCode);
            if (!string.IsNullOrWhiteSpace(fromCatalog))
            {
                return fromCatalog;
            }

            return constructorName;
        }

        private static string DeriveFamilyFromCatalog(string catalogCode)
        {
            if (string.IsNullOrWhiteSpace(catalogCode))
            {
                return string.Empty;
            }

            string text = catalogCode.Trim();
            string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length <= 2)
            {
                return text;
            }

            List<string> keep = new List<string>();
            foreach (string token in tokens)
            {
                if (LooksLikeSizeToken(token))
                {
                    break;
                }
                keep.Add(token);
            }

            return keep.Count > 0 ? string.Join(" ", keep) : text;
        }

        private static bool LooksLikeSizeToken(string token)
        {
            string text = token.Trim().ToUpperInvariant();
            if (text.StartsWith("U", StringComparison.Ordinal) && text.Length > 1)
            {
                return true;
            }

            return text.Any(char.IsDigit);
        }

        private static string FindCatalogProperty(IEnumerable<string> properties)
        {
            foreach (string candidate in CatalogParamCandidates)
            {
                string found = properties.FirstOrDefault(p => string.Equals(p, candidate, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }

            return properties.FirstOrDefault(p =>
                p.IndexOf("catalog", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("catálogo", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? string.Empty;
        }

        private static List<string> ResolveCatalogCodes(SolidosCatalogInterop sol, string constructorHandle, string catalogProp)
        {
            List<string> values = new List<string>();

            Dictionary<string, object> info = sol.GetPropertyInfo(constructorHandle, catalogProp);
            if (info.TryGetValue("Values", out object listedValues) && listedValues != null)
            {
                values.AddRange(FlattenToStrings(listedValues));
            }

            if (values.Count == 0)
            {
                object raw = sol.GetNodeParam(constructorHandle, catalogProp);
                values.AddRange(SplitCatalogText(raw));
            }

            return values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> SplitCatalogText(object raw)
        {
            List<string> flattened = FlattenToStrings(raw);
            List<string> result = new List<string>();

            foreach (string text in flattened)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string[] split = text.Split(new[] { ';', '\r', '\n', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
                result.AddRange(split.Length > 1 ? split.Select(s => s.Trim()) : new[] { text.Trim() });
            }

            return result;
        }

        private static List<string> FlattenToStrings(object raw)
        {
            List<string> result = new List<string>();
            FlattenObject(raw, result);
            return result;
        }

        private static void FlattenObject(object raw, List<string> acc)
        {
            if (raw == null)
            {
                return;
            }

            if (raw is string s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                {
                    acc.Add(s);
                }
                return;
            }

            if (raw is GeometryPoint gp)
            {
                acc.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", gp.X, gp.Y, gp.Z));
                return;
            }

            if (raw is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    FlattenObject(entry.Key, acc);
                    FlattenObject(entry.Value, acc);
                }
                return;
            }

            if (raw is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    FlattenObject(item, acc);
                }
                return;
            }

            string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                acc.Add(text);
            }
        }

        private static void AddFailure(List<string> failures, string text)
        {
            if (failures.Count < MaxReportedFailures)
            {
                failures.Add(text);
            }
        }

        private static void ForceVisualRefresh(Document civilDoc, Editor docEditor)
        {
            try
            {
                civilDoc.Database.TransactionManager.QueueForGraphicsFlush();
                docEditor.Regen();
                Application.UpdateScreen();
            }
            catch
            {
                // não trava o comando
            }
        }

        private sealed class ConstructorCatalogInfo
        {
            public ConstructorCatalogInfo(string constructorHandle, string constructorName, string catalogCode, string familyKey)
            {
                ConstructorHandle = constructorHandle;
                ConstructorName = constructorName;
                CatalogCode = catalogCode;
                FamilyKey = familyKey;
                PartSizeName = constructorName + " - " + catalogCode;
                DisplayName = PartSizeName;
            }

            public string ConstructorHandle { get; }
            public string ConstructorName { get; }
            public string CatalogCode { get; }
            public string FamilyKey { get; }
            public string PartSizeName { get; }
            public string DisplayName { get; }
        }

        private sealed class NetworkHandles
        {
            public string Drainage { get; set; } = string.Empty;
            public string Sewer { get; set; } = string.Empty;
            public string Generic { get; set; } = string.Empty;
            public string Pressure { get; set; } = string.Empty;

            public bool HasAny
            {
                get
                {
                    return SolidosCatalogInterop.IsValidHandle(Drainage)
                        || SolidosCatalogInterop.IsValidHandle(Sewer)
                        || SolidosCatalogInterop.IsValidHandle(Generic)
                        || SolidosCatalogInterop.IsValidHandle(Pressure);
                }
            }

            public string Get(string key)
            {
                switch (key)
                {
                    case "drainage": return Drainage;
                    case "sewer": return Sewer;
                    case "generic": return Generic;
                    case "pressure": return Pressure;
                    default: return string.Empty;
                }
            }
        }

        private sealed class ShowroomLayout
        {
            private readonly double _spacingX;
            private readonly double _spacingY;
            private readonly Dictionary<string, int> _rowIndexByGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, int> _countByGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private int _nextRow;

            public ShowroomLayout(double spacingX, double spacingY)
            {
                _spacingX = spacingX;
                _spacingY = spacingY;
            }

            public ShowroomPosition Next(string groupKey)
            {
                if (!_rowIndexByGroup.TryGetValue(groupKey, out int rowIndex))
                {
                    rowIndex = _nextRow++;
                    _rowIndexByGroup[groupKey] = rowIndex;
                    _countByGroup[groupKey] = 0;
                }

                int itemIndex = _countByGroup[groupKey];
                _countByGroup[groupKey] = itemIndex + 1;
                return new ShowroomPosition(itemIndex * _spacingX, -(rowIndex * _spacingY));
            }
        }

        private readonly struct ShowroomPosition
        {
            public ShowroomPosition(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }
        }
    }

    internal sealed class SolidosCatalogInterop
    {
        private readonly MethodInfo _miGetRootNode;
        private readonly MethodInfo _miGetChildNames;
        private readonly MethodInfo _miFindObjectByName;
        private readonly MethodInfo _miListProperties;
        private readonly MethodInfo _miGetPropertyInfo;
        private readonly MethodInfo _miGetNodeParam;
        private readonly MethodInfo _miGetPropertyType;
        private readonly MethodInfo _miCreateNode;
        private readonly MethodInfo _miSetNodeParams;
        private readonly MethodInfo _miCommit;

        private SolidosCatalogInterop(
            MethodInfo miGetRootNode,
            MethodInfo miGetChildNames,
            MethodInfo miFindObjectByName,
            MethodInfo miListProperties,
            MethodInfo miGetPropertyInfo,
            MethodInfo miGetNodeParam,
            MethodInfo miGetPropertyType,
            MethodInfo miCreateNode,
            MethodInfo miSetNodeParams,
            MethodInfo miCommit)
        {
            _miGetRootNode = miGetRootNode;
            _miGetChildNames = miGetChildNames;
            _miFindObjectByName = miFindObjectByName;
            _miListProperties = miListProperties;
            _miGetPropertyInfo = miGetPropertyInfo;
            _miGetNodeParam = miGetNodeParam;
            _miGetPropertyType = miGetPropertyType;
            _miCreateNode = miCreateNode;
            _miSetNodeParams = miSetNodeParams;
            _miCommit = miCommit;
        }

        public bool IsReady
        {
            get
            {
                return _miGetRootNode != null
                    && _miGetChildNames != null
                    && _miFindObjectByName != null
                    && _miListProperties != null
                    && _miGetNodeParam != null
                    && _miCreateNode != null
                    && _miSetNodeParams != null;
            }
        }

        public static SolidosCatalogInterop TryCreate(Editor ed, Database db)
        {
            List<Type> preferredTypes = new List<Type>();

            try
            {
                Assembly solidosAssembly = typeof(SolidosAPI).Assembly;
                AddPreferred(solidosAssembly.GetType("SOLIDOS.VisualLisp", false));
                AddPreferred(solidosAssembly.GetType("SOLIDOS.SolidosAPI", false));
                AddPreferred(typeof(SolidosAPI));
            }
            catch
            {
                // segue para a busca ampla abaixo
            }

            return new SolidosCatalogInterop(
                FindBest(preferredTypes, "SolidosGetRootNode"),
                FindBest(preferredTypes, "SolidosGetChildNames"),
                FindBest(preferredTypes, "SolidosFindObjectByName"),
                FindBest(preferredTypes, "SolidosListProperties"),
                FindBest(preferredTypes, "SolidosGetPropertyInfo"),
                FindBest(preferredTypes, "SolidosGetNodeParam"),
                FindBest(preferredTypes, "SolidosGetPropertyType"),
                FindBest(preferredTypes, "SolidosCreateNode"),
                FindBest(preferredTypes, "SolidosSetNodeParams"),
                FindBest(preferredTypes, "SolidosCommit"));

            void AddPreferred(Type t)
            {
                if (t == null)
                {
                    return;
                }

                if (!preferredTypes.Contains(t))
                {
                    preferredTypes.Add(t);
                }
            }

            static MethodInfo FindBest(IEnumerable<Type> directTypes, string methodName)
            {
                foreach (Type directType in directTypes)
                {
                    MethodInfo direct = FindOnType(directType, methodName);
                    if (direct != null)
                    {
                        return direct;
                    }
                }

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        Type visualLisp = asm.GetType("SOLIDOS.VisualLisp", false);
                        if (visualLisp != null)
                        {
                            MethodInfo visualLispMethod = FindOnType(visualLisp, methodName);
                            if (visualLispMethod != null)
                            {
                                return visualLispMethod;
                            }
                        }

                        Type solidosApi = asm.GetType("SOLIDOS.SolidosAPI", false);
                        if (solidosApi != null)
                        {
                            MethodInfo solidosApiMethod = FindOnType(solidosApi, methodName);
                            if (solidosApiMethod != null)
                            {
                                return solidosApiMethod;
                            }
                        }

                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(t => t != null).ToArray();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Type t in types)
                    {
                        if (t == null)
                        {
                            continue;
                        }

                        MethodInfo[] methods;
                        try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                        catch { continue; }

                        foreach (MethodInfo m in methods)
                        {
                            if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            ParameterInfo[] ps = m.GetParameters();
                            if (methodName == "SolidosGetRootNode" && ps.Length == 1 && ps[0].ParameterType == typeof(string)) return m;
                            if (methodName == "SolidosGetChildNames" && ps.Length == 1 && ps[0].ParameterType == typeof(string)) return m;
                            if (methodName == "SolidosFindObjectByName" && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                            if (methodName == "SolidosListProperties" && ps.Length == 1) return m;
                            if (methodName == "SolidosGetPropertyInfo" && ps.Length == 2) return m;
                            if (methodName == "SolidosGetNodeParam" && ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                            if (methodName == "SolidosGetPropertyType" && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                            if (methodName == "SolidosCreateNode" && ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                            if (methodName == "SolidosSetNodeParams" && ps.Length == 2 && ps[0].ParameterType == typeof(string)) return m;
                            if (methodName == "SolidosCommit" && ps.Length == 1 && ps[0].ParameterType == typeof(string)) return m;
                        }
                    }
                }

                return null;

                static MethodInfo FindOnType(Type t, string wantedName)
                {
                    if (t == null)
                    {
                        return null;
                    }

                    MethodInfo[] methods;
                    try
                    {
                        methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    }
                    catch
                    {
                        return null;
                    }

                    foreach (MethodInfo m in methods)
                    {
                        if (!string.Equals(m.Name, wantedName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ParameterInfo[] ps = m.GetParameters();
                        if (wantedName == "SolidosGetRootNode" && ps.Length == 1 && ps[0].ParameterType == typeof(string)) return m;
                        if (wantedName == "SolidosGetChildNames" && ps.Length == 1 && ps[0].ParameterType == typeof(string)) return m;
                        if (wantedName == "SolidosFindObjectByName" && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                        if (wantedName == "SolidosListProperties" && ps.Length == 1) return m;
                        if (wantedName == "SolidosGetPropertyInfo" && ps.Length == 2) return m;
                        if (wantedName == "SolidosGetNodeParam" && ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                        if (wantedName == "SolidosGetPropertyType" && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                        if (wantedName == "SolidosCreateNode" && ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string)) return m;
                        if (wantedName == "SolidosSetNodeParams" && ps.Length == 2 && ps[0].ParameterType == typeof(string)) return m;
                        if (wantedName == "SolidosCommit" && ps.Length == 1 && ps[0].ParameterType == typeof(string)) return m;
                    }

                    return null;
                }
            }
        }

        public string GetRootNode(string rootName)
        {
            try { return Convert.ToString(_miGetRootNode.Invoke(null, new object[] { rootName }), CultureInfo.InvariantCulture) ?? string.Empty; }
            catch { return string.Empty; }
        }

        public List<string> GetChildNames(string parentHandle)
        {
            try
            {
                object raw = _miGetChildNames.Invoke(null, new object[] { parentHandle });
                return FlattenToStrings(raw).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public string FindObjectByName(string parentHandle, string objectName)
        {
            try { return Convert.ToString(_miFindObjectByName.Invoke(null, new object[] { parentHandle, objectName }), CultureInfo.InvariantCulture) ?? string.Empty; }
            catch { return string.Empty; }
        }

        public List<string> ListProperties(object nodeObj)
        {
            try
            {
                object raw = _miListProperties.Invoke(null, new[] { nodeObj });
                return FlattenToStrings(raw).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public Dictionary<string, object> GetPropertyInfo(object nodeObj, string propName)
        {
            try
            {
                object raw = _miGetPropertyInfo != null ? _miGetPropertyInfo.Invoke(null, new[] { nodeObj, propName }) : null;
                return ParsePairs(raw);
            }
            catch
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public object GetNodeParam(string nodeHandle, string propName)
        {
            try { return _miGetNodeParam.Invoke(null, new object[] { nodeHandle, propName, null }); }
            catch { return null; }
        }

        public bool HasProperty(string nodeHandle, string propName)
        {
            try
            {
                if (_miGetPropertyType != null)
                {
                    object raw = _miGetPropertyType.Invoke(null, new object[] { nodeHandle, propName });
                    string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return ListProperties(nodeHandle).Any(p => string.Equals(p, propName, StringComparison.OrdinalIgnoreCase));
        }

        public string TryCreateNode(string parentHandle, string nodeType, Dictionary<string, object> values)
        {
            try
            {
                object dict = BuildDsDictionary(_miCreateNode.GetParameters()[2].ParameterType, values);
                object raw = _miCreateNode.Invoke(null, new object[] { parentHandle, nodeType, dict });
                return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool TrySetNodeParams(string nodeHandle, Dictionary<string, object> values)
        {
            try
            {
                object dict = BuildDsDictionary(_miSetNodeParams.GetParameters()[1].ParameterType, values);
                object raw = _miSetNodeParams.Invoke(null, new object[] { nodeHandle, dict });
                return ConvertToBool(raw);
            }
            catch
            {
                return false;
            }
        }

        public void Commit(string msg)
        {
            try
            {
                if (_miCommit != null)
                {
                    _miCommit.Invoke(null, new object[] { msg });
                }
            }
            catch
            {
            }
        }

        public static bool IsValidHandle(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
            {
                return false;
            }

            return !string.Equals(handle.Trim(), "0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ConvertToBool(object raw)
        {
            if (raw is bool b)
            {
                return b;
            }

            string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            return bool.TryParse(text, out bool parsed) && parsed;
        }

        private static object BuildDsDictionary(Type dictionaryType, Dictionary<string, object> values)
        {
            MethodInfo byKeysValues = dictionaryType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => string.Equals(m.Name, "ByKeysValues", StringComparison.Ordinal) && m.GetParameters().Length == 2);

            if (byKeysValues == null)
            {
                throw new InvalidOperationException("DesignScript.Builtin.Dictionary.ByKeysValues não encontrado.");
            }

            ParameterInfo[] argsInfo = byKeysValues.GetParameters();
            object keysArg = ConvertCollectionForParameter(argsInfo[0].ParameterType, values.Keys.Cast<string>().ToArray());
            object valuesArg = ConvertCollectionForParameter(argsInfo[1].ParameterType, values.Values.ToArray());
            return byKeysValues.Invoke(null, new[] { keysArg, valuesArg });
        }

        private static object ConvertCollectionForParameter(Type parameterType, object[] items)
        {
            if (parameterType.IsArray)
            {
                Type elementType = parameterType.GetElementType() ?? typeof(object);
                Array arr = Array.CreateInstance(elementType, items.Length);
                for (int i = 0; i < items.Length; i++)
                {
                    arr.SetValue(ChangeType(items[i], elementType), i);
                }
                return arr;
            }

            if (parameterType.IsAssignableFrom(typeof(object[])))
            {
                return items;
            }

            if (parameterType.IsInterface || parameterType.IsAbstract)
            {
                return items.ToList();
            }

            object instance = Activator.CreateInstance(parameterType);
            MethodInfo add = parameterType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            if (instance != null && add != null)
            {
                Type targetType = add.GetParameters().Length == 1 ? add.GetParameters()[0].ParameterType : typeof(object);
                foreach (object item in items)
                {
                    add.Invoke(instance, new[] { ChangeType(item, targetType) });
                }
                return instance;
            }

            return items;
        }

        private static object ChangeType(object value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            if (targetType.IsInstanceOfType(value) || targetType == typeof(object))
            {
                return value;
            }

            if (targetType == typeof(string))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> ParsePairs(object raw)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (raw == null)
            {
                return result;
            }

            if (raw is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        result[key] = entry.Value;
                    }
                }
                return result;
            }

            if (raw is IEnumerable enumerable && !(raw is string))
            {
                foreach (object item in enumerable)
                {
                    if (item is IDictionary itemDict)
                    {
                        foreach (DictionaryEntry entry in itemDict)
                        {
                            string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                result[key] = entry.Value;
                            }
                        }
                        continue;
                    }

                    if (item is IList list && list.Count >= 2)
                    {
                        string key = Convert.ToString(list[0], CultureInfo.InvariantCulture) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            result[key] = list[1];
                        }
                        continue;
                    }

                    if (item != null)
                    {
                        PropertyInfo keyProp = item.GetType().GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
                        PropertyInfo valueProp = item.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                        if (keyProp != null && valueProp != null)
                        {
                            string key = Convert.ToString(keyProp.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                result[key] = valueProp.GetValue(item);
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static List<string> FlattenToStrings(object raw)
        {
            List<string> result = new List<string>();
            FlattenObject(raw, result);
            return result;
        }

        private static void FlattenObject(object raw, List<string> acc)
        {
            if (raw == null)
            {
                return;
            }

            if (raw is string s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                {
                    acc.Add(s);
                }
                return;
            }

            if (raw is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    FlattenObject(entry.Key, acc);
                    FlattenObject(entry.Value, acc);
                }
                return;
            }

            if (raw is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    FlattenObject(item, acc);
                }
                return;
            }

            string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                acc.Add(text);
            }
        }
    }
}
