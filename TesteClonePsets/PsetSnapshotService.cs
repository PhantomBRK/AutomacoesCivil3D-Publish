using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutomacoesCivil3D
{
    internal sealed class PsetSnapshotResult
    {
        public string OutputPath { get; set; } = string.Empty;
        public int TotalDefinitions { get; set; }
        public int TotalProperties { get; set; }
    }

    internal sealed class PsetSnapshotFile
    {
        public string GeradoEm { get; set; } = string.Empty;
        public string NomeDocumento { get; set; } = string.Empty;
        public string CaminhoDocumento { get; set; } = string.Empty;
        public List<PsetDefinitionSnapshot> Definicoes { get; set; } = new List<PsetDefinitionSnapshot>();
    }

    internal sealed class PsetDefinitionSnapshot
    {
        public string Nome { get; set; } = string.Empty;
        public string TipoClr { get; set; } = string.Empty;
        public Dictionary<string, string> PropriedadesEscalares { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> MetodosSemParametros { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Colecoes { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public List<PsetPropertySnapshot> Propriedades { get; set; } = new List<PsetPropertySnapshot>();
    }

    internal sealed class PsetPropertySnapshot
    {
        public string Nome { get; set; } = string.Empty;
        public string TipoClr { get; set; } = string.Empty;
        public Dictionary<string, string> PropriedadesEscalares { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> MetodosSemParametros { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Colecoes { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FormulaOuOrigem { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public AuxiliaryObjectSnapshot? DefinicaoLista { get; set; }
    }

    internal sealed class AuxiliaryObjectSnapshot
    {
        public string Nome { get; set; } = string.Empty;
        public string ObjectIdTexto { get; set; } = string.Empty;
        public string TipoClr { get; set; } = string.Empty;
        public Dictionary<string, string> PropriedadesEscalares { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> MetodosSemParametros { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Colecoes { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public List<string> AssinaturasMetodos { get; set; } = new List<string>();
        public Dictionary<string, List<AuxiliaryObjectSnapshot>> ObjetosRelacionados { get; set; } = new Dictionary<string, List<AuxiliaryObjectSnapshot>>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class PsetSnapshotService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static PsetSnapshotResult Execute(Document doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            Database db = doc.Database;
            PsetSnapshotFile snapshot = new PsetSnapshotFile
            {
                GeradoEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                NomeDocumento = Path.GetFileName(doc.Name ?? string.Empty),
                CaminhoDocumento = SafeGetDocumentPath(doc)
            };

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                IReadOnlyList<KeyValuePair<string, ObjectId>> definitions = TesteClonePsetsService.GetDefinitionEntries(db, tr);
                foreach (KeyValuePair<string, ObjectId> entry in definitions.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (entry.Value.IsNull || !entry.Value.IsValid)
                    {
                        continue;
                    }

                    PropertySetDefinition? definition = null;
                    try
                    {
                        definition = (PropertySetDefinition)tr.GetObject(entry.Value, OpenMode.ForRead);
                    }
                    catch
                    {
                        definition = null;
                    }

                    if (definition == null)
                    {
                        continue;
                    }

                    snapshot.Definicoes.Add(BuildDefinitionSnapshot(entry.Key, definition, tr));
                }

                tr.Commit();
            }

            string outputPath = BuildOutputPath(snapshot.CaminhoDocumento);
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(outputPath, json);

            return new PsetSnapshotResult
            {
                OutputPath = outputPath,
                TotalDefinitions = snapshot.Definicoes.Count,
                TotalProperties = snapshot.Definicoes.Sum(item => item.Propriedades.Count)
            };
        }

        private static PsetDefinitionSnapshot BuildDefinitionSnapshot(string fallbackName, PropertySetDefinition definition, Transaction tr)
        {
            PsetDefinitionSnapshot snapshot = new PsetDefinitionSnapshot
            {
                Nome = ResolveName(definition, fallbackName),
                TipoClr = definition.GetType().FullName ?? definition.GetType().Name
            };

            CaptureObjectState(definition, snapshot.PropriedadesEscalares, snapshot.MetodosSemParametros, snapshot.Colecoes, skipDefinitionsCollection: true);

            foreach (PropertyDefinition property in definition.Definitions)
            {
                snapshot.Propriedades.Add(BuildPropertySnapshot(property, tr));
            }

            return snapshot;
        }

        private static PsetPropertySnapshot BuildPropertySnapshot(PropertyDefinition property, Transaction tr)
        {
            PsetPropertySnapshot snapshot = new PsetPropertySnapshot
            {
                Nome = ResolveName(property, property.Name ?? string.Empty),
                TipoClr = property.GetType().FullName ?? property.GetType().Name
            };

            CaptureObjectState(property, snapshot.PropriedadesEscalares, snapshot.MetodosSemParametros, snapshot.Colecoes, skipDefinitionsCollection: false);

            foreach (string memberName in new[] { "Source", "Formula", "Expression", "Reference", "ReferencePath", "DataSource" })
            {
                string? value = TryReadMemberAsString(property, memberName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    snapshot.FormulaOuOrigem[memberName] = value;
                }
            }

            foreach ((string Getter, string Key) method in new[] { ("GetFormulaString", "GetFormulaString"), ("GetExpression", "GetExpression"), ("GetSourceString", "GetSourceString") })
            {
                string? value = TryInvokeStringMethod(property, method.Getter);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    snapshot.FormulaOuOrigem[method.Key] = value;
                }
            }

            ObjectId listDefinitionId = TryGetObjectIdProperty(property, "ListDefinitionId");
            if (!listDefinitionId.IsNull && listDefinitionId.IsValid)
            {
                snapshot.DefinicaoLista = BuildAuxiliarySnapshot(listDefinitionId, tr);
            }

            return snapshot;
        }

        private static AuxiliaryObjectSnapshot? BuildAuxiliarySnapshot(ObjectId objectId, Transaction tr, bool includeRelatedObjects = true)
        {
            DBObject? dbObject = null;
            try
            {
                dbObject = tr.GetObject(objectId, OpenMode.ForRead);
            }
            catch
            {
                dbObject = null;
            }

            if (dbObject == null)
            {
                return null;
            }

            AuxiliaryObjectSnapshot snapshot = new AuxiliaryObjectSnapshot
            {
                Nome = ResolveName(dbObject, dbObject.GetType().Name),
                ObjectIdTexto = objectId.ToString(),
                TipoClr = dbObject.GetType().FullName ?? dbObject.GetType().Name
            };

            CaptureObjectState(dbObject, snapshot.PropriedadesEscalares, snapshot.MetodosSemParametros, snapshot.Colecoes, skipDefinitionsCollection: false);
            CaptureMethodSignatures(dbObject, snapshot.AssinaturasMetodos);
            CaptureEnumerableMethods(dbObject, snapshot.Colecoes);
            if (includeRelatedObjects)
            {
                CaptureRelatedObjects(dbObject, snapshot, tr);
            }

            return snapshot;
        }

        private static void CaptureRelatedObjects(DBObject dbObject, AuxiliaryObjectSnapshot snapshot, Transaction tr)
        {
            CaptureRelatedObjectIdsFromMethod(dbObject, snapshot, tr, "GetListItems");
        }

        private static void CaptureRelatedObjectIdsFromMethod(DBObject dbObject, AuxiliaryObjectSnapshot snapshot, Transaction tr, string methodName)
        {
            MethodInfo? method = dbObject.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (method == null)
            {
                return;
            }

            object? result;
            try
            {
                result = method.Invoke(dbObject, null);
            }
            catch
            {
                return;
            }

            if (result is not IEnumerable enumerable || result is string)
            {
                return;
            }

            List<AuxiliaryObjectSnapshot> relatedObjects = new List<AuxiliaryObjectSnapshot>();
            foreach (object? item in enumerable)
            {
                if (item is not ObjectId relatedId || relatedId.IsNull || !relatedId.IsValid)
                {
                    continue;
                }

                AuxiliaryObjectSnapshot? relatedSnapshot = BuildAuxiliarySnapshot(relatedId, tr, includeRelatedObjects: false);
                if (relatedSnapshot != null)
                {
                    relatedObjects.Add(relatedSnapshot);
                }
            }

            if (relatedObjects.Count > 0)
            {
                snapshot.ObjetosRelacionados["Method:" + methodName] = relatedObjects;
            }
        }

        private static void CaptureObjectState(
            object target,
            Dictionary<string, string> scalarProperties,
            Dictionary<string, string> methodValues,
            Dictionary<string, List<string>> collections,
            bool skipDefinitionsCollection)
        {
            foreach (PropertyInfo property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (ShouldSkipMember(property.Name))
                {
                    continue;
                }

                if (skipDefinitionsCollection && property.Name.Equals("Definitions", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(target, null);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                if (TryConvertSimpleValue(value, out string? simple))
                {
                    scalarProperties[property.Name] = simple;
                    continue;
                }

                if (value is IEnumerable enumerable && value is not string)
                {
                    List<string> items = SummarizeEnumerable(enumerable);
                    if (items.Count > 0)
                    {
                        collections[property.Name] = items;
                    }
                }
            }

            foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsSpecialName || method.GetParameters().Length != 0)
                {
                    continue;
                }

                if (!ShouldCaptureMethod(method))
                {
                    continue;
                }

                try
                {
                    object? value = method.Invoke(target, null);
                    if (value != null && TryConvertSimpleValue(value, out string? simple))
                    {
                        methodValues[method.Name] = simple;
                    }
                }
                catch
                {
                }
            }
        }

        private static bool ShouldCaptureMethod(MethodInfo method)
        {
            if (method.DeclaringType == typeof(object))
            {
                return false;
            }

            Type returnType = method.ReturnType;
            return method.Name.StartsWith("Get", StringComparison.Ordinal)
                && returnType != typeof(void)
                && IsSimpleType(returnType);
        }

        private static void CaptureMethodSignatures(object target, List<string> signatures)
        {
            foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsSpecialName)
                {
                    continue;
                }

                string signature = method.Name + "(" + string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name + " " + parameter.Name)) + ") : " + method.ReturnType.Name;
                if (!signatures.Contains(signature))
                {
                    signatures.Add(signature);
                }
            }

            signatures.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static void CaptureEnumerableMethods(object target, Dictionary<string, List<string>> collections)
        {
            foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsSpecialName || method.GetParameters().Length != 0)
                {
                    continue;
                }

                if (!typeof(IEnumerable).IsAssignableFrom(method.ReturnType) || method.ReturnType == typeof(string))
                {
                    continue;
                }

                try
                {
                    object? result = method.Invoke(target, null);
                    if (result is IEnumerable enumerable)
                    {
                        List<string> items = SummarizeEnumerable(enumerable);
                        if (items.Count > 0)
                        {
                            collections["Method:" + method.Name] = items;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static bool ShouldSkipMember(string memberName)
        {
            return memberName.Equals("AutoDelete", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("Database", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("Document", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("ExtensionDictionary", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("XData", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryConvertSimpleValue(object value, out string simple)
        {
            simple = string.Empty;
            if (value == null)
            {
                return false;
            }

            Type type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
            if (value is ObjectId objectId)
            {
                simple = objectId.IsNull ? "ObjectId.Null" : objectId.ToString();
                return true;
            }

            if (value is RXClass rxClass)
            {
                simple = rxClass.Name;
                return true;
            }

            if (type.IsEnum || IsSimpleType(type))
            {
                simple = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            }

            return false;
        }

        private static ObjectId TryGetObjectIdProperty(object target, string propertyName)
        {
            PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
            {
                return ObjectId.Null;
            }

            try
            {
                object? value = property.GetValue(target, null);
                return value is ObjectId objectId ? objectId : ObjectId.Null;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static bool IsSimpleType(Type type)
        {
            Type actualType = Nullable.GetUnderlyingType(type) ?? type;
            return actualType.IsEnum
                || actualType == typeof(string)
                || actualType == typeof(bool)
                || actualType == typeof(byte)
                || actualType == typeof(short)
                || actualType == typeof(int)
                || actualType == typeof(long)
                || actualType == typeof(float)
                || actualType == typeof(double)
                || actualType == typeof(decimal)
                || actualType == typeof(Guid)
                || actualType == typeof(DateTime)
                || actualType == typeof(TimeSpan);
        }

        private static List<string> SummarizeEnumerable(IEnumerable enumerable)
        {
            List<string> items = new List<string>();
            foreach (object? item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                if (TryConvertSimpleValue(item, out string? simple))
                {
                    items.Add(simple);
                }
                else
                {
                    string summary = ResolveName(item, item.GetType().Name);
                    items.Add(summary);
                }

                if (items.Count >= 50)
                {
                    break;
                }
            }

            return items;
        }

        private static string ResolveName(object target, string fallback)
        {
            string? name = TryReadMemberAsString(target, "Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            string? alternateName = TryReadMemberAsString(target, "AlternateName");
            if (!string.IsNullOrWhiteSpace(alternateName))
            {
                return alternateName;
            }

            return fallback;
        }

        private static string? TryReadMemberAsString(object target, string memberName)
        {
            PropertyInfo? property = target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    object? value = property.GetValue(target, null);
                    return value?.ToString();
                }
                catch
                {
                }
            }

            return null;
        }

        private static string? TryInvokeStringMethod(object target, string methodName)
        {
            MethodInfo? method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (method == null)
            {
                return null;
            }

            try
            {
                object? value = method.Invoke(target, null);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string SafeGetDocumentPath(Document doc)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(doc.Name) && Path.IsPathRooted(doc.Name))
                {
                    return Path.GetFullPath(doc.Name);
                }
            }
            catch
            {
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DesenhoSemNome.dwg");
        }

        private static string BuildOutputPath(string drawingPath)
        {
            string directory = Path.GetDirectoryName(drawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string baseName = Path.GetFileNameWithoutExtension(drawingPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            return Path.Combine(directory, $"{baseName}_PSET_SNAPSHOT_{timestamp}.json");
        }
    }
}
