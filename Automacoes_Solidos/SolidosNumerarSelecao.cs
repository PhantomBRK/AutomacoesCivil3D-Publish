using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D.AutomacoesPlugInSolidos
{
    public sealed class SolidosNumerarSelecao
    {
        public const string ShortCommandName = "SNUMSEL";
        public const string LongCommandName = "SOL_NUMERAR_SELECAO";
        public const string ParamName = "Name";
        public const string ParamPartNum = "PartNum";
        public const string ParamSubNet = "SubNet";
        public const string ParamConstructor = "Constructor";

        public static readonly string[] PrefixParamCandidates = new string[]
        {
            "Catalogo",
            "Codigo",
            "CodigoCatalogo",
            "Catalog",
            "CatalogCode",
            "Code",
            "FamilyName"
        };

        public static string _lastPrefix = "A";
        public static int _lastStartNumber = 1;
        public static int _lastDigits = 0;

        [CommandMethod("SOL_NUM_SEL")]
        public void NumerarSelecaoCurto()
        {
            Execute();
        }

        [CommandMethod(LongCommandName)]
        public void NumerarSelecaoCompleto()
        {
            Execute();
        }

       
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;

            try
            {
                List<ObjectId> pickedIds = PromptOrderedSelection(ed);
                if (pickedIds.Count == 0)
                {
                    ed.WriteMessage("\n[SOLIDOS] Nenhum dispositivo selecionado.\n");
                    return;
                }

                string suggestedPrefix = TrySuggestPrefix(pickedIds);
                if (!PromptSettings(ed, suggestedPrefix, out NumberingSettings settings))
                {
                    return;
                }

                SolidosInternalInterop interop = SolidosInternalInterop.TryCreate();
                if (!interop.IsReady)
                {
                    ed.WriteMessage(
                        "\n[SOLIDOS] Nao encontrei os metodos internos necessarios do plugin SOLIDOS." +
                        "\nCarregue o SOLIDOS e rode o comando novamente.\n");
                    return;
                }

                ApplyResult result = interop.ApplyNumbering(pickedIds, settings);

                ForceVisualRefresh(doc, ed);

                ed.WriteMessage(
                    "\n[SOLIDOS] Numeracao por selecao concluida." +
                    $"\n  Aplicados: {result.Applied}" +
                    $"\n  Ignorados por nao serem dispositivos: {result.Invalid}" +
                    $"\n  Repetidos ignorados: {result.Duplicates}");

                if (result.AdjustedNames > 0)
                {
                    ed.WriteMessage(
                        $"\n  Nomes ajustados por duplicidade existente: {result.AdjustedNames}");
                }

                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERRO] {Describe(ex)}\n");
            }
        }

        public bool PromptSettings(Editor ed, string suggestedPrefix, out NumberingSettings settings)
        {
            settings = default;
            string defaultPrefix = string.IsNullOrWhiteSpace(suggestedPrefix)
                ? _lastPrefix
                : suggestedPrefix;

            PromptStringOptions prefixOptions = new PromptStringOptions(
                $"\nPrefixo do nome, incluindo hifen se quiser <{defaultPrefix}>: ")
            {
                AllowSpaces = true
            };

            PromptResult prefixResult = ed.GetString(prefixOptions);
            if (prefixResult.Status == PromptStatus.Cancel)
            {
                return false;
            }

            string prefix = prefixResult.Status == PromptStatus.OK
                ? (prefixResult.StringResult ?? string.Empty).Trim()
                : defaultPrefix;

            if (!PromptInteger(
                ed,
                "Numero inicial",
                _lastStartNumber,
                allowZero: false,
                out int startNumber))
            {
                return false;
            }

            if (!PromptInteger(
                ed,
                "Digitos minimos, 0 sem zeros a esquerda",
                _lastDigits,
                allowZero: true,
                out int digits))
            {
                return false;
            }

            if (digits > 12)
            {
                ed.WriteMessage("\n[SOLIDOS] Use no maximo 12 digitos.\n");
                return false;
            }

            _lastPrefix = prefix;
            _lastStartNumber = startNumber;
            _lastDigits = digits;

            settings = new NumberingSettings(prefix, startNumber, digits);
            return true;
        }

        public string TrySuggestPrefix(IReadOnlyList<ObjectId> pickedIds)
        {
            foreach (ObjectId pickedId in pickedIds)
            {
                string direct = TryGetFirstStringParam(pickedId, PrefixParamCandidates);
                if (!string.IsNullOrWhiteSpace(direct))
                {
                    return direct.Trim();
                }

                ObjectId constructorId = TryGetObjectIdParam(pickedId, ParamConstructor);
                if (!constructorId.IsNull)
                {
                    string fromConstructor = TryGetFirstStringParam(constructorId, PrefixParamCandidates);
                    if (!string.IsNullOrWhiteSpace(fromConstructor))
                    {
                        return fromConstructor.Trim();
                    }
                }
            }

            return _lastPrefix;
        }

        public string TryGetFirstStringParam(ObjectId nodeId, IEnumerable<string> propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                object value = TryGetNodeParam(nodeId, propertyName);
                string text = Convert.ToString(value, CultureInfo.CurrentCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return string.Empty;
        }

        public ObjectId TryGetObjectIdParam(ObjectId nodeId, string propertyName)
        {
            object value = TryGetNodeParam(nodeId, propertyName);
            return value is ObjectId id ? id : ObjectId.Null;
        }

        public object TryGetNodeParam(ObjectId nodeId, string propertyName)
        {
            try
            {
                Type propertyType = null;
                return SolidosAPI.GetNodeParam(nodeId, propertyName, null, ref propertyType);
            }
            catch
            {
                return null;
            }
        }

        public bool PromptInteger(
            Editor ed,
            string label,
            int defaultValue,
            bool allowZero,
            out int value)
        {
            value = defaultValue;

            PromptIntegerOptions options = new PromptIntegerOptions(
                $"\n{label} <{defaultValue.ToString(CultureInfo.InvariantCulture)}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = allowZero,
                DefaultValue = defaultValue
            };

            PromptIntegerResult result = ed.GetInteger(options);
            if (result.Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (result.Status == PromptStatus.OK)
            {
                value = result.Value;
            }

            return true;
        }

        public List<ObjectId> PromptOrderedSelection(Editor ed)
        {
            List<ObjectId> ids = new List<ObjectId>();
            HashSet<ObjectId> seen = new HashSet<ObjectId>();

            while (true)
            {
                PromptEntityOptions options = new PromptEntityOptions(
                    $"\nSelecione o dispositivo SOLIDOS #{ids.Count + 1} na ordem, ou Enter para finalizar: ")
                {
                    AllowNone = true
                };
                options.SetRejectMessage("\nSelecione um objeto do SOLIDOS.");

                PromptEntityResult result = ed.GetEntity(options);
                if (result.Status == PromptStatus.OK)
                {
                    if (seen.Add(result.ObjectId))
                    {
                        ids.Add(result.ObjectId);
                        ed.WriteMessage(
                            $"\n  Ordem {ids.Count}: handle {result.ObjectId.Handle}");
                    }
                    else
                    {
                        ed.WriteMessage("\n  Objeto ja selecionado; ignorado.");
                    }

                    continue;
                }

                if (result.Status == PromptStatus.None || ids.Count > 0)
                {
                    break;
                }

                return new List<ObjectId>();
            }

            return ids;
        }

        public void ForceVisualRefresh(Document doc, Editor ed)
        {
            try
            {
                doc.Database.TransactionManager.QueueForGraphicsFlush();
                ed.Regen();
                Application.UpdateScreen();
            }
            catch
            {
                // refresh best effort
            }
        }

        public string Describe(System.Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return ex.Message;
        }

        public readonly struct NumberingSettings
        {
            public NumberingSettings(string prefix, int startNumber, int digits)
            {
                Prefix = prefix ?? string.Empty;
                StartNumber = startNumber;
                Digits = digits;
            }

            public string Prefix { get; }
            public int StartNumber { get; }
            public int Digits { get; }

            public string FormatPartNumber(int index)
            {
                int value = checked(StartNumber + index);
                return Digits <= 0
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : value.ToString("D" + Digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }

            public string FormatName(int index)
            {
                return Prefix + FormatPartNumber(index);
            }
        }

        public sealed class ApplyResult
        {
            public int Applied { get; set; }
            public int Invalid { get; set; }
            public int Duplicates { get; set; }
            public int AdjustedNames { get; set; }
        }

        public sealed class DeviceRecord
        {
            public DeviceRecord(ObjectId selectedId, ObjectId deviceId, object device, string expectedName)
            {
                SelectedId = selectedId;
                DeviceId = deviceId;
                Device = device;
                ExpectedName = expectedName;
            }

            public ObjectId SelectedId { get; }
            public ObjectId DeviceId { get; }
            public object Device { get; }
            public string ExpectedName { get; set; }
        }

        public class SolidosInternalInterop
        {
            public readonly MethodInfo _currentDoc;
            public readonly MethodInfo _startTransaction;
            public readonly MethodInfo _getSolObject;
            public readonly MethodInfo _commitTransaction;
            public readonly MethodInfo _findPartFrom;
            public readonly MethodInfo _setWithoutVerifications;
            public readonly MethodInfo _processKey;
            public readonly MethodInfo _toUpdateXrecord;
            public readonly MethodInfo _removeName;
            public readonly PropertyInfo _idProperty;
            public readonly PropertyInfo _nameProperty;
            public readonly Type _solDeviceType;

            public  SolidosInternalInterop(
                MethodInfo currentDoc,
                MethodInfo startTransaction,
                MethodInfo getSolObject,
                MethodInfo commitTransaction,
                MethodInfo findPartFrom,
                MethodInfo setWithoutVerifications,
                MethodInfo processKey,
                MethodInfo toUpdateXrecord,
                MethodInfo removeName,
                PropertyInfo idProperty,
                PropertyInfo nameProperty,
                Type solDeviceType)
            {
                _currentDoc = currentDoc;
                _startTransaction = startTransaction;
                _getSolObject = getSolObject;
                _commitTransaction = commitTransaction;
                _findPartFrom = findPartFrom;
                _setWithoutVerifications = setWithoutVerifications;
                _processKey = processKey;
                _toUpdateXrecord = toUpdateXrecord;
                _removeName = removeName;
                _idProperty = idProperty;
                _nameProperty = nameProperty;
                _solDeviceType = solDeviceType;
            }

            public bool IsReady =>
                _currentDoc != null
                && _startTransaction != null
                && _getSolObject != null
                && _commitTransaction != null
                && _findPartFrom != null
                && _setWithoutVerifications != null
                && _processKey != null
                && _toUpdateXrecord != null
                && _removeName != null
                && _idProperty != null
                && _nameProperty != null
                && _solDeviceType != null;

            public static SolidosInternalInterop TryCreate()
            {
                Assembly solidosAssembly = typeof(SolidosAPI).Assembly;

                Type docSolidosType = solidosAssembly.GetType("SOLIDOS.DocSolidos", throwOnError: false);
                Type myTransType = solidosAssembly.GetType("SOLIDOS.MyTrans", throwOnError: false);
                Type acadHelperType = solidosAssembly.GetType("SOLIDOS.AcadHelper", throwOnError: false);
                Type solNodeType = solidosAssembly.GetType("SOLIDOS.SolNode", throwOnError: false);
                Type solDeviceType = solidosAssembly.GetType("SOLIDOS.SolDevice", throwOnError: false);
                Type clonavelType = solidosAssembly.GetType("SOLIDOS.Clonavel", throwOnError: false);

                return new SolidosInternalInterop(
                    FindStatic(docSolidosType, "Current"),
                    FindInstance(docSolidosType, "StartTransaction"),
                    FindStatic(myTransType, "GetSolObject"),
                    FindInstance(myTransType, "Commit"),
                    FindStatic(acadHelperType, "FindPartFrom"),
                    FindInstance(clonavelType, "SetWithoutVerifications"),
                    FindInstance(solNodeType, "ProcessaKey"),
                    FindInstance(solNodeType, "ToUpdateXrecord"),
                    FindStatic(acadHelperType, "RemoveName"),
                    solNodeType?.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public),
                    solNodeType?.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public),
                    solDeviceType);
            }

            public ApplyResult ApplyNumbering(IReadOnlyList<ObjectId> pickedIds, NumberingSettings settings)
            {
                ApplyResult result = new ApplyResult();
                object docSolidos = _currentDoc.Invoke(null, null);
                object transaction = _startTransaction.Invoke(docSolidos, new object[] { false });

                try
                {
                    List<DeviceRecord> devices = ResolveDevices(pickedIds, result);
                    if (devices.Count == 0)
                    {
                        return result;
                    }

                    string temporaryPrefix = "__SNUMSEL_TMP_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "_";
                    for (int i = 0; i < devices.Count; i++)
                    {
                        SetRaw(devices[i].Device, ParamName, temporaryPrefix + i.ToString(CultureInfo.InvariantCulture));
                    }

                    for (int i = 0; i < devices.Count; i++)
                    {
                        DeviceRecord record = devices[i];
                        string partNumber = settings.FormatPartNumber(i);
                        string newName = settings.FormatName(i);
                        record.ExpectedName = newName;

                        SetRaw(record.Device, ParamPartNum, partNumber);
                        SetRaw(record.Device, ParamSubNet, settings.Prefix);
                        SetRaw(record.Device, ParamName, newName);
                    }

                    foreach (DeviceRecord record in devices)
                    {
                        _processKey.Invoke(record.Device, new object[] { ParamName });
                        _toUpdateXrecord.Invoke(record.Device, null);
                        _removeName.Invoke(null, new object[] { record.DeviceId });

                        string actualName = Convert.ToString(_nameProperty.GetValue(record.Device), CultureInfo.CurrentCulture) ?? string.Empty;
                        if (!string.Equals(actualName, record.ExpectedName, StringComparison.Ordinal))
                        {
                            result.AdjustedNames++;
                        }
                    }

                    _commitTransaction.Invoke(transaction, null);
                    result.Applied = devices.Count;
                    return result;
                }
                finally
                {
                    (transaction as IDisposable)?.Dispose();
                }
            }

            public List<DeviceRecord> ResolveDevices(IReadOnlyList<ObjectId> pickedIds, ApplyResult result)
            {
                List<DeviceRecord> devices = new List<DeviceRecord>();
                HashSet<ObjectId> seenDeviceIds = new HashSet<ObjectId>();

                foreach (ObjectId pickedId in pickedIds)
                {
                    object node = _getSolObject.Invoke(null, new object[] { pickedId });
                    object device = ResolveDevice(node);
                    if (device == null)
                    {
                        result.Invalid++;
                        continue;
                    }

                    ObjectId deviceId = (ObjectId)_idProperty.GetValue(device);
                    if (deviceId.IsNull)
                    {
                        result.Invalid++;
                        continue;
                    }

                    if (!seenDeviceIds.Add(deviceId))
                    {
                        result.Duplicates++;
                        continue;
                    }

                    string currentName = Convert.ToString(_nameProperty.GetValue(device), CultureInfo.CurrentCulture) ?? string.Empty;
                    devices.Add(new DeviceRecord(pickedId, deviceId, device, currentName));
                }

                return devices;
            }

            public object ResolveDevice(object node)
            {
                if (node == null)
                {
                    return null;
                }

                if (_solDeviceType.IsInstanceOfType(node))
                {
                    return node;
                }

                object device = _findPartFrom.Invoke(null, new[] { node });
                return device != null && _solDeviceType.IsInstanceOfType(device)
                    ? device
                    : null;
            }

            public void SetRaw(object device, string key, object value)
            {
                _setWithoutVerifications.Invoke(device, new[] { key, value });
            }

            public static MethodInfo FindStatic(Type type, string name)
            {
                return FindMethod(type, name, BindingFlags.Static);
            }

            public static MethodInfo FindInstance(Type type, string name)
            {
                return FindMethod(type, name, BindingFlags.Instance);
            }

            public static MethodInfo FindMethod(Type type, string name, BindingFlags scope)
            {
                while (type != null)
                {
                    MethodInfo method = type.GetMethod(
                        name,
                        scope | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (method != null)
                    {
                        return method;
                    }

                    type = type.BaseType;
                }

                return null;
            }
        }
    }
}
