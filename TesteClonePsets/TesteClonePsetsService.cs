using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace AutomacoesCivil3D
{
    public class ClonePsetsCopyResult
    {
        public int TotalEncontrados { get; set; }
        public int Criados { get; set; }
        public int Atualizados { get; set; }
        public int IgnoradosPrefixo { get; set; }
        public int IgnoradosExistentes { get; set; }
        public int Falhas { get; set; }
        public int FormulaLikeOrigem { get; set; }
        public int FormulaLikeClone { get; set; }
        public List<string> Detalhes { get; } = new List<string>();
    }

    public class TesteClonePsetsService
    {
        private const string PrefixoClone = "copy_";

        internal static IReadOnlyList<KeyValuePair<string, ObjectId>> GetDefinitionEntries(Database db, Transaction tr)
        {
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            return EnumerateDefinitions(db, dictionary, tr)
                .Select(entry => new KeyValuePair<string, ObjectId>(entry.Name, entry.Id))
                .ToList();
        }

        public static ClonePsetsCopyResult Execute(Document doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            Database db = doc.Database;
            ClonePsetsCopyResult result = new ClonePsetsCopyResult();

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                List<PropertySetDefinitionEntry> entries = EnumerateDefinitions(db, dictionary, tr)
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                result.TotalEncontrados = entries.Count;

                if (entries.Count == 0)
                {
                    result.Detalhes.Add("Nenhuma PropertySetDefinition encontrada no desenho.");
                    tr.Commit();
                    return result;
                }

                foreach (PropertySetDefinitionEntry entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                    {
                        continue;
                    }

                    if (entry.Name.StartsWith(PrefixoClone, StringComparison.OrdinalIgnoreCase))
                    {
                        result.IgnoradosPrefixo++;
                        continue;
                    }

                    string cloneName = PrefixoClone + entry.Name;
                    if (DefinitionExists(dictionary, tr, cloneName))
                    {
                        PropertySetDefinition source = (PropertySetDefinition)tr.GetObject(entry.Id, OpenMode.ForRead);
                        ObjectId cloneId = TryGetDefinitionId(dictionary, tr, cloneName);
                        PropertySetDefinition existingClone = (PropertySetDefinition)tr.GetObject(cloneId, OpenMode.ForWrite);
                        UpdateDefinition(db, entry.Name, cloneName, source, existingClone, result);
                        result.Atualizados++;
                        continue;
                    }

                    try
                    {
                        PropertySetDefinition source = (PropertySetDefinition)tr.GetObject(entry.Id, OpenMode.ForRead);
                        CloneDefinition(db, tr, dictionary, entry.Name, cloneName, source, result);
                        result.Criados++;
                    }
                    catch (System.Exception ex)
                    {
                        result.Falhas++;
                        result.Detalhes.Add($"{entry.Name}: falha ao clonar ({ex.Message}).");
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private static void CloneDefinition(
            Database db,
            Transaction tr,
            DictionaryPropertySetDefinitions dictionary,
            string sourceName,
            string cloneName,
            PropertySetDefinition source,
            ClonePsetsCopyResult result)
        {
            PropertySetDefinition clone = new PropertySetDefinition();
            clone.SetToStandard(db);
            clone.SubSetDatabaseDefaults(db);

            bool definitionCopied = TryCopyFrom(clone, source);

            SetDefinitionNames(clone, cloneName);
            SyncProperties(db, source, clone);

            dictionary.AddNewRecord(cloneName, clone);
            tr.AddNewlyCreatedDBObject(clone, true);

            PropertySetDefinition persisted = (PropertySetDefinition)tr.GetObject(clone.ObjectId, OpenMode.ForWrite);
            SetDefinitionNames(persisted, cloneName);

            int sourceFormulaLike = CountFormulaLikeProperties(source);
            int cloneFormulaLike = CountFormulaLikeProperties(persisted);

            result.FormulaLikeOrigem += sourceFormulaLike;
            result.FormulaLikeClone += cloneFormulaLike;

            string metodo = definitionCopied ? "CopyFrom" : "Manual";
            result.Detalhes.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} -> {1}: criado com {2}; props origem={3}; props clone={4}; formulaLike origem={5}; formulaLike clone={6}",
                    sourceName,
                    cloneName,
                    metodo,
                    source.Definitions.Count,
                    persisted.Definitions.Count,
                    sourceFormulaLike,
                    cloneFormulaLike));
        }

        private static void UpdateDefinition(
            Database db,
            string sourceName,
            string cloneName,
            PropertySetDefinition source,
            PropertySetDefinition existingClone,
            ClonePsetsCopyResult result)
        {
            CopyWritableScalarProperties(source, existingClone);
            SetDefinitionNames(existingClone, cloneName);
            SyncProperties(db, source, existingClone);

            int sourceFormulaLike = CountFormulaLikeProperties(source);
            int cloneFormulaLike = CountFormulaLikeProperties(existingClone);

            result.FormulaLikeOrigem += sourceFormulaLike;
            result.FormulaLikeClone += cloneFormulaLike;

            result.Detalhes.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} -> {1}: atualizado; props origem={2}; props clone={3}; formulaLike origem={4}; formulaLike clone={5}",
                    sourceName,
                    cloneName,
                    source.Definitions.Count,
                    existingClone.Definitions.Count,
                    sourceFormulaLike,
                    cloneFormulaLike));
        }

        private static void SyncProperties(Database db, PropertySetDefinition source, PropertySetDefinition clone)
        {
            Dictionary<string, PropertyDefinition> existingByName = new Dictionary<string, PropertyDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyDefinition existing in clone.Definitions)
            {
                string? existingName = TryGetPropertyText(existing, "Name");
                if (!string.IsNullOrWhiteSpace(existingName))
                {
                    existingByName[existingName] = existing;
                }
            }

            foreach (PropertyDefinition sourceProperty in source.Definitions)
            {
                string sourceName = TryGetPropertyText(sourceProperty, "Name") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sourceName) && existingByName.TryGetValue(sourceName, out PropertyDefinition? existingClone))
                {
                    SynchronizePropertyClone(sourceProperty, existingClone);
                    continue;
                }

                PropertyDefinition cloneProperty = CreatePropertyClone(db, sourceProperty);
                if (cloneProperty == null)
                {
                    continue;
                }

                clone.Definitions.Add(cloneProperty);

                string cloneName = TryGetPropertyText(cloneProperty, "Name") ?? sourceName;
                if (!string.IsNullOrWhiteSpace(cloneName))
                {
                    existingByName[cloneName] = cloneProperty;
                }
            }
        }

        public static PropertyDefinition CreatePropertyClone(Database db, PropertyDefinition sourceProperty)
        {
            PropertyDefinition cloneProperty = CreateCompatiblePropertyDefinition(sourceProperty) ?? new PropertyDefinition();
            cloneProperty.SetToStandard(db);
            cloneProperty.SubSetDatabaseDefaults(db);

            TryCopyFrom(cloneProperty, sourceProperty);
            CopyWritableScalarProperties(sourceProperty, cloneProperty);
            CopySpecialFormulaMembers(sourceProperty, cloneProperty);

            string? sourceName = TryGetPropertyText(sourceProperty, "Name");
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                TrySetWritableProperty(cloneProperty, "Name", sourceName);
            }

            return cloneProperty;
        }

        private static PropertyDefinition? CreateCompatiblePropertyDefinition(PropertyDefinition sourceProperty)
        {
            Type sourceType = sourceProperty.GetType();

            try
            {
                if (!sourceType.IsAbstract && typeof(PropertyDefinition).IsAssignableFrom(sourceType))
                {
                    object? instance = Activator.CreateInstance(sourceType, nonPublic: true);
                    if (instance is PropertyDefinition propertyDefinition)
                    {
                        return propertyDefinition;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static void SynchronizePropertyClone(PropertyDefinition sourceProperty, PropertyDefinition cloneProperty)
        {
            CopyWritableScalarProperties(sourceProperty, cloneProperty);
            CopySpecialFormulaMembers(sourceProperty, cloneProperty);
        }

        private static List<PropertySetDefinitionEntry> EnumerateDefinitions(Database db, DictionaryPropertySetDefinitions dictionary, Transaction tr)
        {
            List<PropertySetDefinitionEntry> entries = new List<PropertySetDefinitionEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectDefinitionEntries(entries, seen, dictionary, dictionary, tr);

            try
            {
                Autodesk.Aec.DatabaseServices.Dictionary raw = (Autodesk.Aec.DatabaseServices.Dictionary)dictionary;
                CollectDefinitionEntries(entries, seen, raw, dictionary, tr);
            }
            catch
            {
            }

            if (entries.Count == 0)
            {
                CollectDefinitionsFromNamedObjects(entries, seen, db, tr);
            }

            if (entries.Count == 0)
            {
                CollectDefinitionsFromAttachedPropertySets(entries, seen, db, tr);
            }

            return entries;
        }

        private static void CollectDefinitionsFromNamedObjects(
            List<PropertySetDefinitionEntry> entries,
            HashSet<string> seen,
            Database db,
            Transaction tr)
        {
            HashSet<ObjectId> visited = new HashSet<ObjectId>();
            TraverseDictionary(db.NamedObjectsDictionaryId, "NOD", entries, seen, visited, tr, 0);
        }

        private static void TraverseDictionary(
            ObjectId dictionaryId,
            string currentPath,
            List<PropertySetDefinitionEntry> entries,
            HashSet<string> seen,
            HashSet<ObjectId> visited,
            Transaction tr,
            int depth)
        {
            if (dictionaryId.IsNull || !dictionaryId.IsValid || visited.Contains(dictionaryId) || depth > 12)
            {
                return;
            }

            visited.Add(dictionaryId);

            DBDictionary? dictionary = null;
            try
            {
                dictionary = tr.GetObject(dictionaryId, OpenMode.ForRead) as DBDictionary;
            }
            catch
            {
                return;
            }

            if (dictionary == null)
            {
                return;
            }

            foreach (DictionaryEntry entry in dictionary)
            {
                string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                ObjectId childId = ExtractObjectId(entry.Value);
                if (childId.IsNull || !childId.IsValid)
                {
                    continue;
                }

                DBObject? child = null;
                try
                {
                    child = tr.GetObject(childId, OpenMode.ForRead);
                }
                catch
                {
                    continue;
                }

                if (child == null)
                {
                    continue;
                }

                if (child is PropertySetDefinition propertySetDefinition)
                {
                    string name = ResolveDefinitionName(propertySetDefinition, key);
                    if (!string.IsNullOrWhiteSpace(name) && !seen.Contains(name))
                    {
                        seen.Add(name);
                        entries.Add(new PropertySetDefinitionEntry(name, childId));
                    }

                    continue;
                }

                if (child is DBDictionary)
                {
                    string nextPath = string.IsNullOrWhiteSpace(key) ? currentPath : currentPath + "\\" + key;
                    TraverseDictionary(childId, nextPath, entries, seen, visited, tr, depth + 1);
                }
            }
        }

        private static void CollectDefinitionsFromAttachedPropertySets(
            List<PropertySetDefinitionEntry> entries,
            HashSet<string> seen,
            Database db,
            Transaction tr)
        {
            BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in blockTable)
            {
                BlockTableRecord? btr = null;
                try
                {
                    btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                }
                catch
                {
                    continue;
                }

                if (btr == null)
                {
                    continue;
                }

                foreach (ObjectId entityId in btr)
                {
                    Entity? entity = null;
                    try
                    {
                        entity = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                    }
                    catch
                    {
                        continue;
                    }

                    if (entity == null)
                    {
                        continue;
                    }

                    ObjectIdCollection? propertySetIds = null;
                    try
                    {
                        propertySetIds = PropertyDataServices.GetPropertySets(entity);
                    }
                    catch
                    {
                        propertySetIds = null;
                    }

                    if (propertySetIds == null)
                    {
                        continue;
                    }

                    foreach (ObjectId propertySetId in propertySetIds)
                    {
                        if (propertySetId.IsNull || !propertySetId.IsValid)
                        {
                            continue;
                        }

                        PropertySet? propertySet = null;
                        try
                        {
                            propertySet = (PropertySet)tr.GetObject(propertySetId, OpenMode.ForRead);
                        }
                        catch
                        {
                            continue;
                        }

                        if (propertySet == null || propertySet.PropertySetDefinition.IsNull || !propertySet.PropertySetDefinition.IsValid)
                        {
                            continue;
                        }

                        PropertySetDefinition? definition = null;
                        try
                        {
                            definition = (PropertySetDefinition)tr.GetObject(propertySet.PropertySetDefinition, OpenMode.ForRead);
                        }
                        catch
                        {
                            definition = null;
                        }

                        if (definition == null)
                        {
                            continue;
                        }

                        string name = ResolveDefinitionName(definition, string.Empty);
                        if (string.IsNullOrWhiteSpace(name) || seen.Contains(name))
                        {
                            continue;
                        }

                        seen.Add(name);
                        entries.Add(new PropertySetDefinitionEntry(name, propertySet.PropertySetDefinition));
                    }
                }
            }
        }

        private static void CollectDefinitionEntries(
            List<PropertySetDefinitionEntry> entries,
            HashSet<string> seen,
            object source,
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr)
        {
            if (source is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    TryAddDefinitionEntry(entries, seen, item, dictionary, tr);
                }
            }

            MethodInfo? getEnumerator = source.GetType().GetMethod(
                "GetEnumerator",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (getEnumerator == null)
            {
                return;
            }

            object? enumerator = null;
            try
            {
                enumerator = getEnumerator.Invoke(source, null);
            }
            catch
            {
                return;
            }

            if (enumerator == null)
            {
                return;
            }

            MethodInfo? moveNext = enumerator.GetType().GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo? current = enumerator.GetType().GetProperty("Current", BindingFlags.Instance | BindingFlags.Public);
            if (moveNext == null || current == null)
            {
                return;
            }

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = Convert.ToBoolean(moveNext.Invoke(enumerator, null), CultureInfo.InvariantCulture);
                }
                catch
                {
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                object? item;
                try
                {
                    item = current.GetValue(enumerator, null);
                }
                catch
                {
                    break;
                }

                TryAddDefinitionEntry(entries, seen, item, dictionary, tr);
            }
        }

        private static void TryAddDefinitionEntry(
            List<PropertySetDefinitionEntry> entries,
            HashSet<string> seen,
            object? item,
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr)
        {
            if (!TryExtractEntry(item, dictionary, tr, out string name, out ObjectId id))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(name) || id.IsNull || !id.IsValid || seen.Contains(name))
            {
                return;
            }

            seen.Add(name);
            entries.Add(new PropertySetDefinitionEntry(name, id));
        }

        private static bool TryExtractEntry(
            object? item,
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr,
            out string name,
            out ObjectId id)
        {
            name = string.Empty;
            id = ObjectId.Null;

            if (item == null)
            {
                return false;
            }

            if (item is DictionaryEntry dictionaryEntry)
            {
                name = Convert.ToString(dictionaryEntry.Key, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                id = ExtractObjectId(dictionaryEntry.Value);
                return !string.IsNullOrWhiteSpace(name) && !id.IsNull;
            }

            if (item is string directName)
            {
                name = directName.Trim();
                id = TryGetDefinitionId(dictionary, tr, name);
                return !string.IsNullOrWhiteSpace(name) && !id.IsNull;
            }

            Type itemType = item.GetType();
            PropertyInfo? keyProperty = itemType.GetProperty("Key") ?? itemType.GetProperty("Name");
            PropertyInfo? valueProperty = itemType.GetProperty("Value") ?? itemType.GetProperty("ObjectId") ?? itemType.GetProperty("Id");

            if (keyProperty != null)
            {
                try
                {
                    name = Convert.ToString(keyProperty.GetValue(item, null), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                }
                catch
                {
                    name = string.Empty;
                }
            }

            if (valueProperty != null)
            {
                try
                {
                    id = ExtractObjectId(valueProperty.GetValue(item, null));
                }
                catch
                {
                    id = ObjectId.Null;
                }
            }

            if (!string.IsNullOrWhiteSpace(name) && id.IsNull)
            {
                id = TryGetDefinitionId(dictionary, tr, name);
            }

            return !string.IsNullOrWhiteSpace(name) && !id.IsNull;
        }

        private static ObjectId ExtractObjectId(object? value)
        {
            if (value is ObjectId objectId)
            {
                return objectId;
            }

            if (value == null)
            {
                return ObjectId.Null;
            }

            Type type = value.GetType();
            PropertyInfo? objectIdProperty = type.GetProperty("ObjectId") ?? type.GetProperty("Id") ?? type.GetProperty("Value");
            if (objectIdProperty == null)
            {
                return ObjectId.Null;
            }

            try
            {
                object? nested = objectIdProperty.GetValue(value, null);
                return nested is ObjectId nestedId ? nestedId : ObjectId.Null;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static string ResolveDefinitionName(PropertySetDefinition definition, string fallbackKey)
        {
            string? name = TryGetPropertyText(definition, "Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            string? alternateName = TryGetPropertyText(definition, "AlternateName");
            if (!string.IsNullOrWhiteSpace(alternateName))
            {
                return alternateName.Trim();
            }

            return fallbackKey?.Trim() ?? string.Empty;
        }

        private static ObjectId TryGetDefinitionId(DictionaryPropertySetDefinitions dictionary, Transaction tr, string name)
        {
            try
            {
                Autodesk.Aec.DatabaseServices.Dictionary raw = (Autodesk.Aec.DatabaseServices.Dictionary)dictionary;
                if (raw.Has(name, tr))
                {
                    return raw.GetAt(name);
                }
            }
            catch
            {
            }

            try
            {
                if (dictionary.Has(name, tr))
                {
                    return dictionary.GetAt(name);
                }
            }
            catch
            {
            }

            return ObjectId.Null;
        }

        private static bool DefinitionExists(DictionaryPropertySetDefinitions dictionary, Transaction tr, string name)
        {
            return !TryGetDefinitionId(dictionary, tr, name).IsNull;
        }

        private static bool TryCopyFrom(object target, object source)
        {
            if (target == null || source == null)
            {
                return false;
            }

            try
            {
                MethodInfo? copyFrom = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method =>
                    {
                        if (!method.Name.Equals("CopyFrom", StringComparison.Ordinal))
                        {
                            return false;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(source);
                    });

                if (copyFrom == null)
                {
                    return false;
                }

                copyFrom.Invoke(target, new[] { source });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetDefinitionNames(PropertySetDefinition definition, string name)
        {
            TrySetWritableProperty(definition, "Name", name);
            TrySetWritableProperty(definition, "AlternateName", name);
            TrySetWritableProperty(definition, "Description", $"Clone de teste de {name}");
        }

        private static string? TryGetPropertyText(object target, string propertyName)
        {
            if (target == null)
            {
                return null;
            }

            PropertyInfo? property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
            {
                return null;
            }

            try
            {
                object? value = property.GetValue(target, null);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetWritableProperty(object target, string propertyName, object value)
        {
            if (target == null)
            {
                return false;
            }

            PropertyInfo? property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property == null || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                return false;
            }

            try
            {
                object? safeValue = value;
                Type targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                if (safeValue != null && !targetType.IsInstanceOfType(safeValue))
                {
                    safeValue = Convert.ChangeType(safeValue, targetType, CultureInfo.InvariantCulture);
                }

                property.SetValue(target, safeValue, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CopyWritableScalarProperties(object source, object target)
        {
            foreach (PropertyInfo sourceProperty in source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!sourceProperty.CanRead || sourceProperty.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!IsCloneableScalar(sourceProperty.PropertyType))
                {
                    continue;
                }

                if (ShouldSkipProperty(sourceProperty.Name))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = sourceProperty.GetValue(source, null);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                TrySetWritableProperty(target, sourceProperty.Name, value);
            }
        }

        private static void CopySpecialFormulaMembers(object source, object target)
        {
            string[] textualMembers =
            {
                "Source",
                "Formula",
                "Expression",
                "Reference",
                "ReferencePath",
                "DataSource"
            };

            foreach (string memberName in textualMembers)
            {
                object? value = TryGetReadablePropertyValue(source, memberName);
                if (value != null)
                {
                    TrySetWritableProperty(target, memberName, value);
                }
            }

            CopyMethodStringValue(source, target, "GetFormulaString", "SetFormulaString");
            CopyMethodStringValue(source, target, "GetExpression", "SetExpression");
            CopyMethodStringValue(source, target, "GetSourceString", "SetSourceString");
        }

        private static object? TryGetReadablePropertyValue(object target, string propertyName)
        {
            if (target == null)
            {
                return null;
            }

            PropertyInfo? property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
            {
                return null;
            }

            try
            {
                return property.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static void CopyMethodStringValue(object source, object target, string getterName, string setterName)
        {
            MethodInfo? getter = source.GetType().GetMethod(
                getterName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            MethodInfo? setter = target.GetType().GetMethod(
                setterName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            if (getter == null || setter == null)
            {
                return;
            }

            try
            {
                object? value = getter.Invoke(source, null);
                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    setter.Invoke(target, new object[] { text });
                }
            }
            catch
            {
            }
        }

        private static bool IsCloneableScalar(Type type)
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

        private static bool ShouldSkipProperty(string propertyName)
        {
            return propertyName.Equals("AutoDelete", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("Database", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("Document", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("ExtensionDictionary", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("Handle", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("IsDisposed", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("ObjectId", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("OwnerId", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("XData", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountFormulaLikeProperties(PropertySetDefinition definition)
        {
            int count = 0;
            foreach (PropertyDefinition property in definition.Definitions)
            {
                if (LooksFormulaLike(property))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool LooksFormulaLike(PropertyDefinition property)
        {
            Type type = property.GetType();
            if (type.Name.IndexOf("Formula", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return type.GetProperty("Formula", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null
                || type.GetProperty("Expression", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null
                || type.GetMethod("GetFormulaString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null
                || type.GetMethod("SetFormulaString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
        }

        private readonly struct PropertySetDefinitionEntry
        {
            public PropertySetDefinitionEntry(string name, ObjectId id)
            {
                Name = name;
                Id = id;
            }

            public string Name { get; }
            public ObjectId Id { get; }
        }
    }
}
