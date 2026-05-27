using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using DataType = Autodesk.Aec.PropertyData.DataType;

namespace AutomacoesCivil3D
{
    internal sealed class PsetSnapshotImportResult
    {
        public string SnapshotPath { get; set; } = string.Empty;
        public int TotalDefinicoesSnapshot { get; set; }
        public int DefinicoesCriadas { get; set; }
        public int DefinicoesAtualizadas { get; set; }
        public int PropriedadesCriadas { get; set; }
        public int PropriedadesAtualizadas { get; set; }
        public int FormulasAplicadas { get; set; }
        public int ListasConvertidasParaTexto { get; set; }
        public int Avisos { get; set; }
        public List<string> Detalhes { get; } = new List<string>();
    }

    internal static class PsetSnapshotImportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static PsetSnapshotImportResult Execute(Document doc, string snapshotPath)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (string.IsNullOrWhiteSpace(snapshotPath))
            {
                throw new ArgumentException("Caminho do snapshot não informado.", nameof(snapshotPath));
            }

            string fullPath = Path.GetFullPath(snapshotPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Snapshot JSON não encontrado.", fullPath);
            }

            string json = File.ReadAllText(fullPath);
            PsetSnapshotFile? snapshot = JsonSerializer.Deserialize<PsetSnapshotFile>(json, JsonOptions);
            if (snapshot == null)
            {
                throw new InvalidOperationException("Falha ao desserializar o snapshot de PSet.");
            }

            PsetSnapshotImportResult result = new PsetSnapshotImportResult
            {
                SnapshotPath = fullPath,
                TotalDefinicoesSnapshot = snapshot.Definicoes.Count
            };

            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                Dictionary<string, PropertySetDefinition> resolvedDefinitions = new Dictionary<string, PropertySetDefinition>(StringComparer.OrdinalIgnoreCase);

                foreach (PsetDefinitionSnapshot definitionSnapshot in snapshot.Definicoes.OrderBy(item => item.Nome, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(definitionSnapshot.Nome))
                    {
                        result.Avisos++;
                        result.Detalhes.Add("Definição ignorada por nome vazio no snapshot.");
                        continue;
                    }

                    PropertySetDefinition definition = EnsureDefinition(db, tr, dictionary, definitionSnapshot, result);
                    resolvedDefinitions[definitionSnapshot.Nome] = definition;
                }

                foreach (PsetDefinitionSnapshot definitionSnapshot in snapshot.Definicoes.OrderBy(item => item.Nome, StringComparer.OrdinalIgnoreCase))
                {
                    if (!resolvedDefinitions.TryGetValue(definitionSnapshot.Nome, out PropertySetDefinition? definition))
                    {
                        continue;
                    }

                    ApplyDefinitionSnapshot(db, definition, definitionSnapshot, result);
                }

                tr.Commit();
            }

            return result;
        }

        private static PropertySetDefinition EnsureDefinition(
            Database db,
            Transaction tr,
            DictionaryPropertySetDefinitions dictionary,
            PsetDefinitionSnapshot snapshot,
            PsetSnapshotImportResult result)
        {
            ObjectId definitionId = TryGetDefinitionId(dictionary, tr, snapshot.Nome);
            PropertySetDefinition definition;

            if (definitionId.IsNull || !definitionId.IsValid)
            {
                definition = new PropertySetDefinition();
                definition.SetToStandard(db);
                definition.SubSetDatabaseDefaults(db);
                TrySetWritableProperty(definition, "Name", snapshot.Nome);
                TrySetWritableProperty(definition, "AlternateName", GetScalar(snapshot.PropriedadesEscalares, "AlternateName") ?? snapshot.Nome);
                TrySetWritableProperty(definition, "Description", GetScalar(snapshot.PropriedadesEscalares, "Description") ?? string.Empty);
                ApplyAppliesToFilter(definition, snapshot);

                dictionary.AddNewRecord(snapshot.Nome, definition);
                tr.AddNewlyCreatedDBObject(definition, true);
                result.DefinicoesCriadas++;
                result.Detalhes.Add($"Definição criada: {snapshot.Nome}");
                return definition;
            }

            definition = (PropertySetDefinition)tr.GetObject(definitionId, OpenMode.ForWrite);
            result.DefinicoesAtualizadas++;
            return definition;
        }

        private static void ApplyDefinitionSnapshot(
            Database db,
            PropertySetDefinition definition,
            PsetDefinitionSnapshot snapshot,
            PsetSnapshotImportResult result)
        {
            TrySetWritableProperty(definition, "Name", snapshot.Nome);
            TrySetWritableProperty(definition, "AlternateName", GetScalar(snapshot.PropriedadesEscalares, "AlternateName") ?? snapshot.Nome);
            TrySetWritableProperty(definition, "Description", GetScalar(snapshot.PropriedadesEscalares, "Description") ?? string.Empty);
            ApplyAppliesToFilter(definition, snapshot);

            Dictionary<string, PropertyDefinition> existingProperties = new Dictionary<string, PropertyDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyDefinition existingProperty in definition.Definitions)
            {
                string? propertyName = TryGetPropertyText(existingProperty, "Name");
                if (!string.IsNullOrWhiteSpace(propertyName))
                {
                    existingProperties[propertyName] = existingProperty;
                }
            }

            foreach (PsetPropertySnapshot propertySnapshot in snapshot.Propriedades)
            {
                if (string.IsNullOrWhiteSpace(propertySnapshot.Nome))
                {
                    result.Avisos++;
                    result.Detalhes.Add($"Propriedade ignorada por nome vazio em {snapshot.Nome}.");
                    continue;
                }

                bool requiresFormula = IsFormulaProperty(propertySnapshot);
                if (!existingProperties.TryGetValue(propertySnapshot.Nome, out PropertyDefinition? property))
                {
                    property = CreateProperty(db, propertySnapshot);
                    ConfigureProperty(property, propertySnapshot, result, isNewProperty: true);
                    definition.Definitions.Add(property);
                    existingProperties[propertySnapshot.Nome] = property;
                    result.PropriedadesCriadas++;
                    continue;
                }

                if (!IsCompatiblePropertyType(property, requiresFormula))
                {
                    result.Avisos++;
                    result.Detalhes.Add($"Propriedade mantida sem alteração por incompatibilidade de tipo: {snapshot.Nome}.{propertySnapshot.Nome}");
                    continue;
                }

                ConfigureProperty(property, propertySnapshot, result, isNewProperty: false);
                result.PropriedadesAtualizadas++;
            }
        }

        private static PropertyDefinition CreateProperty(Database db, PsetPropertySnapshot snapshot)
        {
            PropertyDefinition property = IsFormulaProperty(snapshot)
                ? new PropertyDefinitionFormula()
                : new PropertyDefinition();

            property.SetToStandard(db);
            property.SubSetDatabaseDefaults(db);
            return property;
        }

        private static void ConfigureProperty(
            PropertyDefinition property,
            PsetPropertySnapshot snapshot,
            PsetSnapshotImportResult result,
            bool isNewProperty)
        {
            DataType effectiveDataType = ResolveDataType(snapshot, out bool listConvertedToText);
            if (listConvertedToText && isNewProperty)
            {
                result.ListasConvertidasParaTexto++;
                result.Detalhes.Add($"Lista convertida para texto: {snapshot.Nome}");
            }

            TrySetWritableProperty(property, "Name", snapshot.Nome);
            TrySetWritableProperty(property, "Description", GetScalar(snapshot.PropriedadesEscalares, "Description") ?? string.Empty);
            TrySetWritableProperty(property, "DataType", effectiveDataType);
            TrySetWritableProperty(property, "DisplayOrder", ParseInt(GetScalar(snapshot.PropriedadesEscalares, "DisplayOrder")));
            TrySetWritableProperty(property, "IsVisible", ParseBool(GetScalar(snapshot.PropriedadesEscalares, "IsVisible")));
            TrySetWritableProperty(property, "IsReadOnly", ParseBool(GetScalar(snapshot.PropriedadesEscalares, "IsReadOnly")));
            TrySetWritableProperty(property, "Automatic", ParseBool(GetScalar(snapshot.PropriedadesEscalares, "Automatic")));
            TrySetWritableProperty(property, "DefaultIsUnspecified", ParseBool(GetScalar(snapshot.PropriedadesEscalares, "DefaultIsUnspecified")));
            TrySetWritableProperty(property, "UseFormulaForDescription", ParseBool(GetScalar(snapshot.PropriedadesEscalares, "UseFormulaForDescription")));
            TrySetWritableProperty(property, "FormatString", GetScalar(snapshot.PropriedadesEscalares, "FormatString"));

            string? globalName = GetScalar(snapshot.PropriedadesEscalares, "GlobalName");
            if (!string.IsNullOrWhiteSpace(globalName))
            {
                TrySetWritableProperty(property, "GlobalName", globalName);
            }

            ApplyDefaultData(property, snapshot, effectiveDataType);
            ApplyFormula(property, snapshot, result);
        }

        private static void ApplyAppliesToFilter(PropertySetDefinition definition, PsetDefinitionSnapshot snapshot)
        {
            if (!snapshot.Colecoes.TryGetValue("AppliesToFilter", out List<string>? appliesToFilter) || appliesToFilter == null)
            {
                return;
            }

            StringCollection appliesTo = new StringCollection();
            foreach (string className in appliesToFilter.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                appliesTo.Add(className);
            }

            if (appliesTo.Count == 0)
            {
                return;
            }

            try
            {
                definition.SetAppliesToFilter(appliesTo, false);
            }
            catch
            {
            }
        }

        private static void ApplyDefaultData(PropertyDefinition property, PsetPropertySnapshot snapshot, DataType effectiveDataType)
        {
            if (property is PropertyDefinitionFormula)
            {
                return;
            }

            string? defaultData = GetScalar(snapshot.PropriedadesEscalares, "DefaultData");
            if (defaultData == null)
            {
                return;
            }

            object? convertedDefault = ConvertDefaultData(defaultData, effectiveDataType);
            if (convertedDefault == null)
            {
                return;
            }

            TrySetWritableProperty(property, "DefaultData", convertedDefault);
        }

        private static void ApplyFormula(PropertyDefinition property, PsetPropertySnapshot snapshot, PsetSnapshotImportResult result)
        {
            string? formula = GetPreferredFormula(snapshot);
            if (string.IsNullOrWhiteSpace(formula))
            {
                return;
            }

            if (property is PropertyDefinitionFormula formulaProperty)
            {
                try
                {
                    formulaProperty.SetFormulaString(formula);
                    result.FormulasAplicadas++;
                    return;
                }
                catch
                {
                }
            }

            if (TryInvokeStringSetter(property, "SetFormulaString", formula)
                || TrySetWritableProperty(property, "FormulaString", formula)
                || TrySetWritableProperty(property, "Source", formula)
                || TrySetWritableProperty(property, "Expression", formula))
            {
                result.FormulasAplicadas++;
            }
        }

        private static string? GetPreferredFormula(PsetPropertySnapshot snapshot)
        {
            if (snapshot.FormulaOuOrigem.TryGetValue("GetFormulaString", out string? formula) && !string.IsNullOrWhiteSpace(formula))
            {
                return formula;
            }

            foreach (string key in new[] { "FormulaString", "Source", "Formula", "Expression", "GetExpression", "GetSourceString" })
            {
                if (snapshot.FormulaOuOrigem.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            string? displayString = GetScalar(snapshot.PropriedadesEscalares, "DisplayString");
            if (!string.IsNullOrWhiteSpace(displayString) && displayString.Contains("[", StringComparison.Ordinal))
            {
                return displayString;
            }

            return null;
        }

        private static bool IsFormulaProperty(PsetPropertySnapshot snapshot)
        {
            return snapshot.TipoClr.IndexOf("PropertyDefinitionFormula", StringComparison.OrdinalIgnoreCase) >= 0
                || snapshot.FormulaOuOrigem.Count > 0;
        }

        private static bool IsCompatiblePropertyType(PropertyDefinition property, bool requiresFormula)
        {
            bool currentIsFormula = property.GetType().Name.IndexOf("Formula", StringComparison.OrdinalIgnoreCase) >= 0;
            return currentIsFormula == requiresFormula;
        }

        private static DataType ResolveDataType(PsetPropertySnapshot snapshot, out bool listConvertedToText)
        {
            listConvertedToText = false;

            string? dataTypeName = GetScalar(snapshot.PropriedadesEscalares, "DataType");
            if (string.IsNullOrWhiteSpace(dataTypeName))
            {
                return DataType.Text;
            }

            if (dataTypeName.Equals("List", StringComparison.OrdinalIgnoreCase))
            {
                listConvertedToText = true;
                return DataType.Text;
            }

            if (Enum.TryParse(dataTypeName, ignoreCase: true, out DataType parsed))
            {
                return parsed;
            }

            return DataType.Text;
        }

        private static object? ConvertDefaultData(string defaultData, DataType dataType)
        {
            if (string.IsNullOrEmpty(defaultData))
            {
                return dataType == DataType.Text ? string.Empty : null;
            }

            switch (dataType)
            {
                case DataType.Integer:
                    return int.TryParse(defaultData, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)
                        ? intValue
                        : null;
                case DataType.Real:
                    return double.TryParse(defaultData, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue)
                        ? doubleValue
                        : null;
                case DataType.TrueFalse:
                    return ParseBool(defaultData);
                default:
                    return defaultData;
            }
        }

        private static string? GetScalar(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string? value) ? value : null;
        }

        private static string? TryGetPropertyText(object target, string propertyName)
        {
            PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

        private static bool TryInvokeStringSetter(object target, string methodName, string value)
        {
            MethodInfo? method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(target, new object[] { value });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetWritableProperty(object target, string propertyName, object? value)
        {
            if (target == null || value == null)
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
                object? converted = ConvertValueForProperty(value, property.PropertyType);
                property.SetValue(target, converted, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object? ConvertValueForProperty(object value, Type propertyType)
        {
            Type actualType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (value == null)
            {
                return null;
            }

            if (actualType.IsInstanceOfType(value))
            {
                return value;
            }

            if (actualType == typeof(string))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            if (actualType == typeof(bool))
            {
                return ParseBool(text);
            }

            if (actualType == typeof(int))
            {
                return ParseInt(text);
            }

            if (actualType == typeof(double))
            {
                return double.Parse(text, CultureInfo.InvariantCulture);
            }

            if (actualType == typeof(float))
            {
                return float.Parse(text, CultureInfo.InvariantCulture);
            }

            if (actualType == typeof(decimal))
            {
                return decimal.Parse(text, CultureInfo.InvariantCulture);
            }

            if (actualType.IsEnum)
            {
                return Enum.Parse(actualType, text, ignoreCase: true);
            }

            return Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }

            if (value == "1")
            {
                return true;
            }

            if (value == "0")
            {
                return false;
            }

            return false;
        }

        private static int ParseInt(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
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
    }
}
