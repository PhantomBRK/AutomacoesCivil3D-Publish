using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;

namespace AutomacoesCivil3D
{
    #region Modelos JSON

    public class PsetExportRoot
    {
        [JsonPropertyName("exportDate")]
        public string ExportDate { get; set; }

        [JsonPropertyName("sourceFile")]
        public string SourceFile { get; set; }

        [JsonPropertyName("propertySetDefinitions")]
        public List<PsetDefJson> PropertySetDefinitions { get; set; } = new();
    }

    public class PsetDefJson
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("appliesToAll")]
        public bool AppliesToAll { get; set; }

        [JsonPropertyName("properties")]
        public List<PsetPropJson> Properties { get; set; } = new();
    }

    public class PsetPropJson
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("dataType")]
        public string DataType { get; set; }

        [JsonPropertyName("defaultData")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string DefaultData { get; set; }

        [JsonPropertyName("isVisible")]
        public bool IsVisible { get; set; } = true;

        [JsonPropertyName("isReadOnly")]
        public bool IsReadOnly { get; set; }

        [JsonPropertyName("automatic")]
        public bool Automatic { get; set; }

        [JsonPropertyName("isFormula")]
        public bool IsFormula { get; set; }

        [JsonPropertyName("formulaExpression")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string FormulaExpression { get; set; }

        [JsonPropertyName("unitType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string UnitType { get; set; }
    }

    #endregion

    public class PsetJsonExportImport
    {
        // PSets bloqueados do Corridor – ignorar na exportação direta,
        // mas manter referências em fórmulas de outros PSets
        private static readonly HashSet<string> LockedPsetNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Corridor Identity",
            "Corridor Model Information",
            "Corridor Property Data \u2013 User Defined",   // en-dash U+2013
            "Corridor Property Data - User Defined",         // traço comum (fallback)
            "Corridor Property Data \u2014 User Defined",   // em-dash U+2014 (fallback)
            "Corridor Shape Information"
        };

        private static readonly Dictionary<string, string> LegacyFormulaExpressions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CodeName"] = "[Corridor Shape Information:CodeName]",
            ["Comprimento"] = "[COORDENAÇÃO:COMPRIMENTO_SOLIDOS_CORREDOR]",
            ["Estaqueamento_Final"] = "[Corridor Identity:EndStation]",
            ["Estaqueamento_Inicial"] = "[Corridor Identity:StartStation]",
            ["NomeCorredorSolido"] = "[Corridor Model Information:CorridorName]",
            ["RegionName"] = "[Corridor Identity:RegionGuid]",
            ["SubassemblyName"] = "[Corridor Identity:SubassemblyName]"
        };

        private static readonly Dictionary<string, Autodesk.Aec.PropertyData.DataType> LegacyFormulaDataTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Comprimento"] = Autodesk.Aec.PropertyData.DataType.Real,
            ["Estaqueamento_Final"] = Autodesk.Aec.PropertyData.DataType.Real,
            ["Estaqueamento_Inicial"] = Autodesk.Aec.PropertyData.DataType.Real
        };

        #region Exportar

        [CommandMethod("PSET_EXPORTAR_JSON")]
        public void ExportarPsetsJson()
        {
            Editor ed = Manager.DocEditor;
            Database db = Manager.DocData;
            Document doc = Manager.DocCad;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DictionaryPropertySetDefinitions dictWrapper = new DictionaryPropertySetDefinitions(db);
                    List<(string Name, ObjectId Id)> allDefs = EnumeratePsetDefinitions(dictWrapper, tr, db, ed);

                    if (allDefs.Count == 0)
                    {
                        ed.WriteMessage("\nNenhum Property Set Definition encontrado no documento.");
                        tr.Commit();
                        return;
                    }

                    PsetExportRoot export = new PsetExportRoot
                    {
                        ExportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        SourceFile = Path.GetFileName(doc.Name)
                    };

                    int skipped = 0;
                    foreach (var (name, id) in allDefs)
                    {
                        if (LockedPsetNames.Contains(name))
                        {
                            skipped++;
                            ed.WriteMessage($"\n[PSET] Ignorado (bloqueado): {name}");
                            continue;
                        }

                        try
                        {
                            PropertySetDefinition psd = (PropertySetDefinition)tr.GetObject(id, OpenMode.ForRead);
                            PsetDefJson defJson = SerializePsd(psd, name);
                            export.PropertySetDefinitions.Add(defJson);
                            ed.WriteMessage($"\n[PSET] Exportado: {name} ({defJson.Properties.Count} propriedades)");
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\n[AVISO] Erro ao ler PSet '{name}': {ex.Message}");
                        }
                    }

                    tr.Commit();

                    if (export.PropertySetDefinitions.Count == 0)
                    {
                        ed.WriteMessage("\nNenhum Property Set exportável encontrado.");
                        return;
                    }

                    SaveFileDialog sfd = new SaveFileDialog
                    {
                        Title = "Salvar Property Sets como JSON",
                        Filter = "JSON (*.json)|*.json",
                        FileName = Path.GetFileNameWithoutExtension(doc.Name) + "_PSets.json",
                        DefaultExt = "json"
                    };

                    if (sfd.ShowDialog() != DialogResult.OK)
                        return;

                    JsonSerializerOptions opts = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };

                    string json = JsonSerializer.Serialize(export, opts);
                    File.WriteAllText(sfd.FileName, json, System.Text.Encoding.UTF8);

                    ed.WriteMessage($"\n\n===== EXPORTAÇÃO CONCLUÍDA =====");
                    ed.WriteMessage($"\nPSets exportados: {export.PropertySetDefinitions.Count}");
                    ed.WriteMessage($"\nPSets ignorados (bloqueados): {skipped}");
                    ed.WriteMessage($"\nArquivo: {sfd.FileName}");

                    MessageBox.Show(
                        $"Exportação concluída!\n\n" +
                        $"PSets exportados: {export.PropertySetDefinitions.Count}\n" +
                        $"PSets ignorados (bloqueados): {skipped}\n" +
                        $"Arquivo: {Path.GetFileName(sfd.FileName)}",
                        "Exportar PSets", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro ao exportar PSets: {ex.Message}");
                MessageBox.Show($"Erro ao exportar:\n{ex.Message}", "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Importar

        [CommandMethod("PSET_IMPORTAR_JSON")]
        public void ImportarPsetsJson()
        {
            Editor ed = Manager.DocEditor;
            Database db = Manager.DocData;
            Document doc = Manager.DocCad;

            try
            {
                OpenFileDialog ofd = new OpenFileDialog
                {
                    Title = "Selecionar arquivo JSON de Property Sets",
                    Filter = "JSON (*.json)|*.json",
                    DefaultExt = "json"
                };

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                string json = File.ReadAllText(ofd.FileName, System.Text.Encoding.UTF8);
                PsetExportRoot importData = JsonSerializer.Deserialize<PsetExportRoot>(json);

                if (importData?.PropertySetDefinitions == null || importData.PropertySetDefinitions.Count == 0)
                {
                    ed.WriteMessage("\nArquivo JSON não contém Property Sets.");
                    return;
                }

                ed.WriteMessage($"\nImportando {importData.PropertySetDefinitions.Count} PSets " +
                                $"de '{importData.SourceFile}' ({importData.ExportDate})...");

                using (DocumentLock dl = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DictionaryPropertySetDefinitions dictWrapper = new DictionaryPropertySetDefinitions(db);
                    int created = 0, updated = 0, errors = 0;

                    foreach (PsetDefJson defJson in importData.PropertySetDefinitions)
                    {
                        try
                        {
                            if (dictWrapper.Has(defJson.Name, tr))
                            {
                                ObjectId existingId = dictWrapper.GetAt(defJson.Name);
                                PropertySetDefinition existing =
                                    (PropertySetDefinition)tr.GetObject(existingId, OpenMode.ForWrite);

                                MergePsdProperties(existing, defJson, db, ed);
                                updated++;
                                ed.WriteMessage($"\n[PSET] Atualizado: {defJson.Name}");
                            }
                            else
                            {
                                CreatePsd(dictWrapper, defJson, db, tr, ed);
                                created++;
                                ed.WriteMessage($"\n[PSET] Criado: {defJson.Name}");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            errors++;
                            ed.WriteMessage($"\n[ERRO] Falha no PSet '{defJson.Name}': {ex.Message}");
                        }
                    }

                    tr.Commit();

                    ed.WriteMessage($"\n\n===== IMPORTAÇÃO CONCLUÍDA =====");
                    ed.WriteMessage($"\nCriados: {created}  |  Atualizados: {updated}  |  Erros: {errors}");

                    MessageBox.Show(
                        $"Importação concluída!\n\nCriados: {created}\nAtualizados: {updated}\nErros: {errors}",
                        "Importar PSets", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro ao importar PSets: {ex.Message}");
                MessageBox.Show($"Erro ao importar:\n{ex.Message}", "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Enumeração do Dicionário

        private static List<(string Name, ObjectId Id)> EnumeratePsetDefinitions(
            DictionaryPropertySetDefinitions dictWrapper, Transaction tr, Database db, Editor ed)
        {
            var result = new List<(string, ObjectId)>();

            // Estratégia 1: abrir o ObjectId do wrapper como DBDictionary
            try
            {
                Autodesk.Aec.DatabaseServices.Dictionary rawDict =
                    (Autodesk.Aec.DatabaseServices.Dictionary)dictWrapper;

                if (rawDict is IEnumerable rawEnumerable)
                {
                    foreach (object entry in rawEnumerable)
                    {
                        if (entry is DictionaryEntry de)
                        {
                            result.Add(((string)de.Key, (ObjectId)de.Value));
                        }
                        else
                        {
                            ExtractEntryViaReflection(entry, result);
                        }
                    }

                    if (result.Count > 0) return result;
                }
            }
            catch { /* prossegue para próxima estratégia */ }

            // Estratégia 2: wrapper pode ser IEnumerable
            try
            {
                if (dictWrapper is IEnumerable enumerable)
                {
                    foreach (object entry in enumerable)
                    {
                        if (entry is DictionaryEntry de)
                        {
                            result.Add(((string)de.Key, (ObjectId)de.Value));
                        }
                        else
                        {
                            ExtractEntryViaReflection(entry, result);
                        }
                    }
                    if (result.Count > 0) return result;
                }
            }
            catch { }

            // Estratégia 3: propriedade Records via reflexão
            try
            {
                PropertyInfo recordsProp = dictWrapper.GetType().GetProperty("Records",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (recordsProp != null)
                {
                    object records = recordsProp.GetValue(dictWrapper);
                    if (records is IEnumerable recEnum)
                    {
                        foreach (object entry in recEnum)
                        {
                            if (entry is DictionaryEntry de)
                                result.Add(((string)de.Key, (ObjectId)de.Value));
                            else
                                ExtractEntryViaReflection(entry, result);
                        }
                        if (result.Count > 0) return result;
                    }
                }
            }
            catch { }

            // Estratégia 4: varrer NOD procurando PropertySetDefinition
            try
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                ScanForPsds(nod, tr, result, 0);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[DEBUG] Falha ao varrer NOD: {ex.Message}");
            }

            return result;
        }

        private static void ExtractEntryViaReflection(object entry, List<(string, ObjectId)> result)
        {
            try
            {
                Type t = entry.GetType();
                PropertyInfo keyProp = t.GetProperty("Key") ?? t.GetProperty("SearchName") ?? t.GetProperty("Name");
                PropertyInfo valProp = t.GetProperty("Value") ?? t.GetProperty("ObjectId") ?? t.GetProperty("Id");

                if (keyProp != null && valProp != null)
                {
                    string key = keyProp.GetValue(entry)?.ToString();
                    object val = valProp.GetValue(entry);
                    if (val is ObjectId oid && !string.IsNullOrEmpty(key))
                        result.Add((key, oid));
                }
            }
            catch { }
        }

        private static void ScanForPsds(DBDictionary dict, Transaction tr,
            List<(string, ObjectId)> result, int depth)
        {
            if (depth > 5) return;

            foreach (DictionaryEntry entry in (IDictionary)dict)
            {
                ObjectId id = (ObjectId)entry.Value;
                if (id.IsNull || id.IsErased) continue;

                try
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is PropertySetDefinition psd)
                    {
                        string name = !string.IsNullOrEmpty(SafeGet(() => psd.AlternateName))
                            ? psd.AlternateName
                            : (string)entry.Key;
                        result.Add((name, id));
                    }
                    else if (obj is DBDictionary subDict)
                    {
                        ScanForPsds(subDict, tr, result, depth + 1);
                    }
                }
                catch { }
            }
        }

        #endregion

        #region Serialização (Export)

        private static PsetDefJson SerializePsd(PropertySetDefinition psd, string name)
        {
            PsetDefJson def = new PsetDefJson
            {
                Name = name,
                Description = SafeGet(() => psd.Description) ?? "",
                AppliesToAll = SafeGet(() => psd.AppliesToAll, true)
            };

            foreach (PropertyDefinition propDef in psd.Definitions.OfType<PropertyDefinition>())
            {
                def.Properties.Add(SerializeProperty(propDef));
            }

            return def;
        }

        private static PsetPropJson SerializeProperty(PropertyDefinition propDef)
        {
            string formulaExpression = GetPropertyFormulaExpression(propDef);
            bool isFormula = IsFormulaProperty(propDef, formulaExpression);

            PsetPropJson p = new PsetPropJson
            {
                Name = propDef.Name,
                Description = SafeGet(() => propDef.Description) ?? "",
                DataType = propDef.DataType.ToString(),
                DefaultData = SafeGet(() => propDef.DefaultData?.ToString()),
                IsVisible = ReflectBool(propDef, "IsVisible", true),
                IsReadOnly = ReflectBool(propDef, "IsReadOnly", false),
                Automatic = isFormula ? false : ReflectBool(propDef, "Automatic", false),
                IsFormula = isFormula,
            };

            // Buscar expressão de fórmula
            p.FormulaExpression = formulaExpression;

            // Se IsFormula mas sem expressão explícita, verificar DefaultData
            if (p.IsFormula && string.IsNullOrEmpty(p.FormulaExpression)
                && !string.IsNullOrEmpty(p.DefaultData)
                && p.DefaultData.Contains('[') && p.DefaultData.Contains(':'))
            {
                p.FormulaExpression = p.DefaultData;
            }

            // UnitType (se existir)
            p.UnitType = ReflectString(propDef, "UnitType");

            return p;
        }

        #endregion

        #region Desserialização (Import)

        private static void CreatePsd(DictionaryPropertySetDefinitions dictWrapper,
            PsetDefJson defJson, Database db, Transaction tr, Editor ed)
        {
            PropertySetDefinition psd = new PropertySetDefinition();
            psd.SetToStandard(db);
            psd.SubSetDatabaseDefaults(db);
            psd.AppliesToAll = defJson.AppliesToAll;
            psd.AlternateName = defJson.Name;

            TrySet(() => psd.Description = defJson.Description);

            foreach (PsetPropJson propJson in defJson.Properties)
            {
                try
                {
                    PropertyDefinition prop = BuildPropertyDefinition(propJson, db);
                    psd.Definitions.Add(prop);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n  [AVISO] Propriedade '{propJson.Name}': {ex.Message}");
                }
            }

            dictWrapper.AddNewRecord(defJson.Name, psd);
            tr.AddNewlyCreatedDBObject(psd, true);
        }

        private static void MergePsdProperties(PropertySetDefinition existing,
            PsetDefJson defJson, Database db, Editor ed)
        {
            Dictionary<string, PropertyDefinition> existingProps = existing.Definitions
                .OfType<PropertyDefinition>()
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (PsetPropJson propJson in defJson.Properties)
            {
                if (existingProps.TryGetValue(propJson.Name, out PropertyDefinition existingProp))
                {
                    // Atualizar propriedade existente (fórmula, default, etc.)
                    try
                    {
                        if (!IsCompatiblePropertyType(existingProp, propJson))
                        {
                            if (!TryReplacePropertyDefinition(existing, existingProp, propJson, db))
                            {
                                ed.WriteMessage($"\n  [AVISO] '{propJson.Name}' mantida sem alteraÃ§Ã£o por incompatibilidade de tipo.");
                                continue;
                            }

                            existingProps[propJson.Name] = existing.Definitions
                                .OfType<PropertyDefinition>()
                                .First(p => string.Equals(p.Name, propJson.Name, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            UpdatePropertyDefinition(existingProp, propJson, db);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n  [AVISO] Atualizar '{propJson.Name}': {ex.Message}");
                    }
                    continue;
                }

                // Adicionar nova propriedade
                try
                {
                    PropertyDefinition prop = BuildPropertyDefinition(propJson, db);
                    existing.Definitions.Add(prop);
                    existingProps[propJson.Name] = prop;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n  [AVISO] Adicionar '{propJson.Name}': {ex.Message}");
                }
            }
        }

        private static PropertyDefinition BuildPropertyDefinition(PsetPropJson propJson, Database db)
        {
            string formulaExpression = GetImportFormulaExpression(propJson);
            bool isFormula = RequiresFormulaProperty(propJson, formulaExpression);

            PropertyDefinition prop = isFormula
                ? new PropertyDefinitionFormula()
                : new PropertyDefinition();
            prop.SetToStandard(db);
            prop.SubSetDatabaseDefaults(db);
            prop.Name = propJson.Name;
            prop.DataType = ResolveImportDataType(propJson, isFormula);

            TrySet(() => prop.Description = propJson.Description);
            ReflectSetBool(prop, "IsVisible", propJson.IsVisible);
            ReflectSetBool(prop, "IsReadOnly", propJson.IsReadOnly);

            // Fórmula
            if (isFormula && !string.IsNullOrEmpty(formulaExpression))
            {
                ApplyFormula(prop, formulaExpression);
            }
            else
            {
                SetDefaultData(prop, propJson);
            }

            // Automático
            if (!isFormula && propJson.Automatic)
                ReflectSetBool(prop, "Automatic", true);
            else if (isFormula)
                ReflectSetBool(prop, "Automatic", false);

            return prop;
        }

        private static void UpdatePropertyDefinition(PropertyDefinition prop,
            PsetPropJson propJson, Database db)
        {
            string formulaExpression = GetImportFormulaExpression(propJson);
            bool isFormula = RequiresFormulaProperty(propJson, formulaExpression);

            TrySet(() => prop.Name = propJson.Name);
            TrySet(() => prop.DataType = ResolveImportDataType(propJson, isFormula));
            ReflectSetBool(prop, "IsVisible", propJson.IsVisible);
            ReflectSetBool(prop, "IsReadOnly", propJson.IsReadOnly);

            // Atualizar fórmula se necessário
            if (isFormula && !string.IsNullOrEmpty(formulaExpression))
            {
                ApplyFormula(prop, formulaExpression);
                ReflectSetBool(prop, "Automatic", false);
            }
            else if (!string.IsNullOrEmpty(propJson.DefaultData))
            {
                SetDefaultData(prop, propJson);
                if (propJson.Automatic)
                    ReflectSetBool(prop, "Automatic", true);
            }

            TrySet(() => prop.Description = propJson.Description);
        }

        private static void ApplyFormula(PropertyDefinition prop, string formula)
        {
            // Tentar múltiplas abordagens para definir a fórmula
            bool set = ReflectInvoke(prop, "SetIsFormula", new object[] { true });
            if (!set) ReflectSetBool(prop, "IsFormula", true);

            bool formulaSet = ReflectInvoke(prop, "SetFormulaString", new object[] { formula })
                              || ReflectSetString(prop, "FormulaString", formula)
                              || ReflectSetString(prop, "Source", formula)
                              || ReflectSetString(prop, "Formula", formula)
                              || ReflectSetString(prop, "FormulaExpression", formula)
                              || ReflectSetString(prop, "Expression", formula);

            ReflectSetBool(prop, "Automatic", false);

            // Se nenhum método de fórmula funcionou, armazenar no DefaultData
            if (!formulaSet && prop is not PropertyDefinitionFormula)
                TrySet(() => prop.DefaultData = formula);
        }

        private static void SetDefaultData(PropertyDefinition prop, PsetPropJson propJson)
        {
            if (prop is PropertyDefinitionFormula) return;
            if (string.IsNullOrEmpty(propJson.DefaultData)) return;

            try
            {
                switch (prop.DataType)
                {
                    case Autodesk.Aec.PropertyData.DataType.Integer:
                        if (int.TryParse(propJson.DefaultData, out int i))
                            prop.DefaultData = i;
                        break;
                    case Autodesk.Aec.PropertyData.DataType.Real:
                        if (double.TryParse(propJson.DefaultData,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double d))
                            prop.DefaultData = d;
                        break;
                    case Autodesk.Aec.PropertyData.DataType.TrueFalse:
                        if (bool.TryParse(propJson.DefaultData, out bool b))
                            prop.DefaultData = b;
                        break;
                    default:
                        prop.DefaultData = propJson.DefaultData;
                        break;
                }
            }
            catch { }
        }

        private static Autodesk.Aec.PropertyData.DataType ParseDataType(string s)
        {
            if (string.IsNullOrEmpty(s))
                return Autodesk.Aec.PropertyData.DataType.Text;

            if (Enum.TryParse<Autodesk.Aec.PropertyData.DataType>(s, true, out var dt))
                return dt;

            return s.ToLowerInvariant() switch
            {
                "string" or "kstring" => Autodesk.Aec.PropertyData.DataType.Text,
                "int" or "kinteger" => Autodesk.Aec.PropertyData.DataType.Integer,
                "double" or "number" or "kreal" => Autodesk.Aec.PropertyData.DataType.Real,
                "bool" or "boolean" or "kboolean" => Autodesk.Aec.PropertyData.DataType.TrueFalse,
                _ => Autodesk.Aec.PropertyData.DataType.Text
            };
        }

        private static bool TryReplacePropertyDefinition(PropertySetDefinition psd, PropertyDefinition existingProp,
            PsetPropJson propJson, Database db)
        {
            if (!TryRemoveDefinition(psd.Definitions, existingProp))
                return false;

            PropertyDefinition replacement = BuildPropertyDefinition(propJson, db);
            psd.Definitions.Add(replacement);
            return true;
        }

        private static bool TryRemoveDefinition(object definitions, PropertyDefinition prop)
        {
            if (definitions is IList list)
            {
                int index = list.IndexOf(prop);
                if (index >= 0)
                {
                    list.RemoveAt(index);
                    return true;
                }
            }

            if (ReflectInvoke(definitions, "Remove", new object[] { prop }))
                return true;

            try
            {
                MethodInfo removeAt = definitions.GetType().GetMethod("RemoveAt",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(int) }, null);

                if (removeAt != null && definitions is IEnumerable enumerable)
                {
                    int index = 0;
                    foreach (object item in enumerable)
                    {
                        if (ReferenceEquals(item, prop))
                        {
                            removeAt.Invoke(definitions, new object[] { index });
                            return true;
                        }
                        index++;
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsCompatiblePropertyType(PropertyDefinition existingProp, PsetPropJson propJson)
        {
            bool currentIsFormula = IsFormulaProperty(existingProp, GetPropertyFormulaExpression(existingProp));
            bool incomingIsFormula = RequiresFormulaProperty(propJson, GetImportFormulaExpression(propJson));
            return currentIsFormula == incomingIsFormula;
        }

        private static bool RequiresFormulaProperty(PsetPropJson propJson, string formulaExpression)
        {
            return propJson.IsFormula
                || !string.IsNullOrWhiteSpace(formulaExpression);
        }

        private static bool IsFormulaProperty(PropertyDefinition propDef, string formulaExpression)
        {
            return propDef.GetType().Name.IndexOf("Formula", StringComparison.OrdinalIgnoreCase) >= 0
                || ReflectBool(propDef, "IsFormula", false)
                || !string.IsNullOrWhiteSpace(formulaExpression);
        }

        private static string GetPropertyFormulaExpression(PropertyDefinition propDef)
        {
            string formula = TryInvokeStringGetter(propDef, "GetFormulaString")
                             ?? ReflectString(propDef, "FormulaString")
                             ?? ReflectString(propDef, "Source")
                             ?? ReflectString(propDef, "Formula")
                             ?? ReflectString(propDef, "FormulaExpression")
                             ?? ReflectString(propDef, "Expression")
                             ?? TryInvokeStringGetter(propDef, "GetExpression")
                             ?? TryInvokeStringGetter(propDef, "GetSourceString");

            if (!string.IsNullOrWhiteSpace(formula))
                return formula;

            string description = SafeGet(() => propDef.Description);
            if (LooksLikeFormulaExpression(description))
                return description.Trim();

            string defaultData = SafeGet(() => propDef.DefaultData?.ToString());
            if (LooksLikeFormulaExpression(defaultData))
                return defaultData.Trim();

            return null;
        }

        private static string GetImportFormulaExpression(PsetPropJson propJson)
        {
            if (LooksLikeFormulaExpression(propJson.FormulaExpression))
                return propJson.FormulaExpression.Trim();

            if (LooksLikeFormulaExpression(propJson.Description))
                return propJson.Description.Trim();

            if (LooksLikeFormulaExpression(propJson.DefaultData))
                return propJson.DefaultData.Trim();

            if (propJson.Automatic
                && LegacyFormulaExpressions.TryGetValue(propJson.Name ?? string.Empty, out string legacyFormula))
            {
                return legacyFormula;
            }

            return null;
        }

        private static Autodesk.Aec.PropertyData.DataType ResolveImportDataType(PsetPropJson propJson, bool isFormula)
        {
            Autodesk.Aec.PropertyData.DataType parsed = ParseDataType(propJson.DataType);
            if (isFormula
                && parsed == Autodesk.Aec.PropertyData.DataType.Text
                && LegacyFormulaDataTypes.TryGetValue(propJson.Name ?? string.Empty, out Autodesk.Aec.PropertyData.DataType legacyType))
            {
                return legacyType;
            }

            return parsed;
        }

        private static bool LooksLikeFormulaExpression(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            return trimmed.IndexOf('[') >= 0
                && trimmed.IndexOf(':') >= 0
                && trimmed.IndexOf(']') >= 0;
        }

        #endregion

        #region Helpers de Reflexão

        private static string SafeGet(Func<string> getter)
        {
            try { return getter(); }
            catch { return null; }
        }

        private static T SafeGet<T>(Func<T> getter, T fallback)
        {
            try { return getter(); }
            catch { return fallback; }
        }

        private static void TrySet(Action setter)
        {
            try { setter(); } catch { }
        }

        private static bool ReflectBool(object obj, string propName, bool fallback)
        {
            try
            {
                PropertyInfo pi = obj.GetType().GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null && pi.CanRead && pi.GetValue(obj) is bool b) return b;
            }
            catch { }
            return fallback;
        }

        private static string ReflectString(object obj, string propName)
        {
            try
            {
                PropertyInfo pi = obj.GetType().GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null && pi.CanRead) return pi.GetValue(obj)?.ToString();
            }
            catch { }
            return null;
        }

        private static string TryInvokeStringGetter(object obj, string methodName)
        {
            try
            {
                MethodInfo mi = obj.GetType().GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);

                if (mi != null)
                    return mi.Invoke(obj, null)?.ToString();
            }
            catch { }
            return null;
        }

        private static bool ReflectSetBool(object obj, string propName, bool value)
        {
            try
            {
                PropertyInfo pi = obj.GetType().GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null && pi.CanWrite) { pi.SetValue(obj, value); return true; }
            }
            catch { }
            return false;
        }

        private static bool ReflectSetString(object obj, string propName, string value)
        {
            try
            {
                PropertyInfo pi = obj.GetType().GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null && pi.CanWrite) { pi.SetValue(obj, value); return true; }
            }
            catch { }
            return false;
        }

        private static bool ReflectInvoke(object obj, string methodName, object[] args)
        {
            try
            {
                Type[] argTypes = args.Select(a => a.GetType()).ToArray();
                MethodInfo mi = obj.GetType().GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic, null, argTypes, null);
                if (mi != null) { mi.Invoke(obj, args); return true; }
            }
            catch { }
            return false;
        }

        #endregion
    }
}
