using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Aec.DatabaseServices;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using ClosedXML.Excel;
using AcadBlockReference = Autodesk.AutoCAD.DatabaseServices.BlockReference;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    public class IfcSolidModelPsetImporter
    {
        private const string DefaultWorkbookPath = @"C:\Users\Gleison Costa\OneDrive\Área de Trabalho\PARAMETROS DE MODELOS SOLIDOS.xlsx";
        private const string PsetPrefix = "PSET_";

        [CommandMethod("IFC_IMPORTAR_PSETS_MODELOS_SOLIDOS")]
        public void ImportarPsetsModelosSolidos()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                string workbookPath = ResolveWorkbookPath(docEditor);
                if (string.IsNullOrWhiteSpace(workbookPath))
                    return;

                using XLWorkbook workbook = new XLWorkbook(workbookPath);
                using Transaction tr = db.TransactionManager.StartTransaction();

                DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);

                int processedSheets = 0;
                int createdPsets = 0;
                int updatedPsets = 0;
                int addedFields = 0;

                foreach (IXLWorksheet worksheet in workbook.Worksheets)
                {
                    SolidModelPsetDefinition definition = ParseWorksheet(worksheet);
                    if (definition.Fields.Count == 0)
                        continue;

                    EnsureOrUpdatePropertySetDefinition(db, tr, dict, definition, ref createdPsets, ref updatedPsets, ref addedFields);
                    processedSheets++;
                }

                tr.Commit();

                docEditor.WriteMessage(
                    $"\n[PSET] Importação concluída. Abas processadas: {processedSheets} | Psets criados: {createdPsets} | Psets atualizados: {updatedPsets} | Campos adicionados: {addedFields}"
                );
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\n[AutoCAD] Erro ao importar PSETs IFC: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[.NET] Erro ao importar PSETs IFC: {ex.Message}");
            }
        }

        private static string ResolveWorkbookPath(Editor docEditor)
        {
            if (File.Exists(DefaultWorkbookPath))
            {
                docEditor.WriteMessage($"\n[PSET] Usando planilha padrão: {DefaultWorkbookPath}");
                return DefaultWorkbookPath;
            }

            PromptOpenFileOptions options = new PromptOpenFileOptions("\nSelecione a planilha de parâmetros dos modelos sólidos:");
            options.Filter = "Excel (*.xlsx)|*.xlsx";

            PromptFileNameResult result = docEditor.GetFileNameForOpen(options);
            if (result.Status != PromptStatus.OK)
                return string.Empty;

            return result.StringResult;
        }

        private static SolidModelPsetDefinition ParseWorksheet(IXLWorksheet worksheet)
        {
            string sourceName = worksheet.Name?.Trim() ?? string.Empty;
            string psetName = BuildPsetName(sourceName);

            List<SolidModelPsetField> fields = new List<SolidModelPsetField>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            for (int row = 1; row <= lastRow; row++)
            {
                string propertyName = worksheet.Cell(row, 2).GetString().Trim();
                if (string.IsNullOrWhiteSpace(propertyName))
                    continue;

                if (string.Equals(propertyName, "Nome da Propriedade", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add(propertyName))
                    continue;

                string category = worksheet.Cell(row, 3).GetString().Trim();
                string friendlyName = worksheet.Cell(row, 4).GetString().Trim();
                string description = worksheet.Cell(row, 5).GetString().Trim();

                fields.Add(new SolidModelPsetField(propertyName, category, friendlyName, description));
            }

            return new SolidModelPsetDefinition(psetName, sourceName, fields);
        }

        private static void EnsureOrUpdatePropertySetDefinition(
            Database db,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            SolidModelPsetDefinition definition,
            ref int createdPsets,
            ref int updatedPsets,
            ref int addedFields)
        {
            bool created = false;
            PropertySetDefinition psd = GetOrCreatePropertySetDefinition(db, tr, dict, definition, ref created);

            HashSet<string> existingNames = new HashSet<string>(
                psd.Definitions.Cast<PropertyDefinition>().Select(p => p.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase
            );

            foreach (SolidModelPsetField field in definition.Fields)
            {
                if (existingNames.Contains(field.PropertyName))
                    continue;

                PropertyDefinition prop = new PropertyDefinition();
                prop.SetToStandard(db);
                prop.SubSetDatabaseDefaults(db);
                prop.Name = field.PropertyName;
                prop.DataType = Autodesk.Aec.PropertyData.DataType.Text;

                TrySetStringProperty(prop, "AlternateName", string.IsNullOrWhiteSpace(field.FriendlyName) ? field.PropertyName : field.FriendlyName);
                TrySetStringProperty(prop, "Description", BuildPropertyDescription(field));

                psd.Definitions.Add(prop);
                existingNames.Add(field.PropertyName);
                addedFields++;
            }

            if (created)
                createdPsets++;
            else
                updatedPsets++;
        }

        private static PropertySetDefinition GetOrCreatePropertySetDefinition(
            Database db,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            SolidModelPsetDefinition definition,
            ref bool created)
        {
            if (dict.Has(definition.PsetName, tr))
            {
                ObjectId existingId = dict.GetAt(definition.PsetName);
                PropertySetDefinition existing = (PropertySetDefinition)tr.GetObject(existingId, OpenMode.ForWrite);
                TrySetStringProperty(existing, "AlternateName", definition.SourceSheetName);
                return existing;
            }

            PropertySetDefinition psd = new PropertySetDefinition();
            psd.SetToStandard(db);
            psd.SubSetDatabaseDefaults(db);
            psd.AppliesToAll = true;
            psd.AlternateName = definition.SourceSheetName;
            dict.AddNewRecord(definition.PsetName, psd);
            tr.AddNewlyCreatedDBObject(psd, true);
            created = true;
            return psd;
        }

        private static string BuildPsetName(string sourceName)
        {
            string normalized = RemoveDiacritics(sourceName ?? string.Empty).ToUpperInvariant();
            normalized = Regex.Replace(normalized, @"[^A-Z0-9]+", "_").Trim('_');

            if (string.IsNullOrWhiteSpace(normalized))
                normalized = "MODELO_SOLIDO";

            if (!normalized.StartsWith(PsetPrefix, StringComparison.Ordinal))
                normalized = PsetPrefix + normalized;

            return normalized;
        }

        private static string BuildPropertyDescription(SolidModelPsetField field)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(field.Category))
                parts.Add("Categoria: " + field.Category);

            if (!string.IsNullOrWhiteSpace(field.Description))
                parts.Add(field.Description);

            return string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static void TrySetStringProperty(object target, string propertyName, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(value))
                return;

            try
            {
                var property = target.GetType().GetProperty(propertyName);
                if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                    return;

                property.SetValue(target, value);
            }
            catch
            {
            }
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

        private sealed class SolidModelPsetDefinition
        {
            public SolidModelPsetDefinition(string psetName, string sourceSheetName, List<SolidModelPsetField> fields)
            {
                PsetName = psetName;
                SourceSheetName = sourceSheetName;
                Fields = fields ?? new List<SolidModelPsetField>();
            }

            public string PsetName { get; }
            public string SourceSheetName { get; }
            public List<SolidModelPsetField> Fields { get; }
        }

        private sealed class SolidModelPsetField
        {
            public SolidModelPsetField(string propertyName, string category, string friendlyName, string description)
            {
                PropertyName = propertyName;
                Category = category;
                FriendlyName = friendlyName;
                Description = description;
            }

            public string PropertyName { get; }
            public string Category { get; }
            public string FriendlyName { get; }
            public string Description { get; }
        }
    }
}
