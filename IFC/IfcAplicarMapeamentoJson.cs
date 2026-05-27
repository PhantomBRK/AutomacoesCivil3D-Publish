using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Label = Autodesk.Civil.DatabaseServices.Label;

namespace AutomacoesCivil3D
{
    public class IfcAplicarMapeamentoJson
    {
        public const string DictName = "IFC_MAPPING";
        public const string DefaultJsonPath = "C:\\Users\\Gleison Costa\\OneDrive\\Área de Trabalho\\CONSULTORIA PETROBRÁS\\00_TEMPLATE SÓLIDOS\\TEMPLATE SÓLIDOS 2026\\ARQUIVOS VERSAO MAIS RECENTE TEMPLATE SÓLIDOS FEV_2026\\IfcMapping.json";
        public static MethodInfo _solidosGetNodeParamMethod;
        public static readonly object ConfigCacheLock = new object();
        public static string _cachedConfigPath = string.Empty;
        public static DateTime _cachedConfigLastWriteUtc = DateTime.MinValue;
        public static long _cachedConfigLength = -1L;
        private static IfcCompiledMappingConfig _cachedCompiledConfig;

        [CommandMethod("IFC_APLICAR_MAPEAMENTO_JSON")]
        public void AplicarMapeamentoJson()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                string jsonPath = DefaultJsonPath;
                if (string.IsNullOrWhiteSpace(jsonPath))
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                if (!File.Exists(jsonPath))
                {
                    docEditor.WriteMessage("\nArquivo JSON não encontrado: " + jsonPath);
                    return;
                }

                IfcCompiledMappingConfig config = GetOrLoadCompiledConfig(jsonPath);
                if (config == null || config.Rules.Count == 0)
                {
                    docEditor.WriteMessage("\nNenhuma regra válida encontrada no JSON.");
                    return;
                }

                PromptSelectionOptions selOpts = new PromptSelectionOptions();
                selOpts.MessageForAdding = "\nSelecione os objetos para aplicar o mapeamento IFC: ";

                PromptSelectionResult selRes = docEditor.GetSelection(selOpts);
                if (selRes.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nNenhum objeto selecionado.");
                    return;
                }

                int totalSelecionados = 0;
                int totalComRegra = 0;
                int totalGravados = 0;
                int totalSemRegra = 0;
                int totalComErro = 0;

                using (DocumentLock docLock = civilDoc.LockDocument())
                {
                    using (Transaction TransCad = db.TransactionManager.StartTransaction())
                    {
                        SelectionSet selectionSet = selRes.Value;
                        ObjectId[] itens = selectionSet.GetObjectIds();

                        totalSelecionados = itens.Length;

                        foreach (ObjectId objId in itens)
                        {
                            if (objId.IsNull || objId.IsErased)
                            {
                                continue;
                            }

                            try
                            {
                                Entity ent = (Entity)TransCad.GetObject(objId, OpenMode.ForRead);
                                if (ent == null)
                                {
                                    continue;
                                }

                                string layerName = ent.Layer;
                                IfcCompiledMappingRule regra = EncontrarRegra(config, layerName);

                                if (regra == null)
                                {
                                    totalSemRegra++;
                                    continue;
                                }

                                totalComRegra++;

                                IfcResolvedMetadata metadata = ResolverMetadata(ent, regra.Rule, docEditor, null);

                                GravarMetadataNoObjeto(ent, metadata, TransCad);

                                totalGravados++;
                            }
                            catch (System.Exception ex)
                            {
                                totalComErro++;
                                docEditor.WriteMessage(
                                    "\nErro ao processar objeto " + objId.ToString() + ": " + ex.Message);
                            }
                        }

                        TransCad.Commit();
                    }
                }

                docEditor.WriteMessage(
                    "\nMapeamento concluído." +
                    "\nSelecionados: " + totalSelecionados +
                    "\nCom regra: " + totalComRegra +
                    "\nGravados: " + totalGravados +
                    "\nSem regra: " + totalSemRegra +
                    "\nErros: " + totalComErro);
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage("\nErro geral: " + ex.Message);
            }
        }

        public static string SolicitarCaminhoJson(Editor docEditor)
        {
            PromptStringOptions pso = new PromptStringOptions(
                "\nInforme o caminho completo do arquivo JSON de mapeamento: ");
            pso.AllowSpaces = true;

            PromptResult pr = docEditor.GetString(pso);
            if (pr.Status != PromptStatus.OK)
            {
                return null;
            }

            string path = pr.StringResult;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return path.Trim().Trim('"');
        }

        public static IfcMappingConfig CarregarConfig(string jsonPath)
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            options.ReadCommentHandling = JsonCommentHandling.Skip;
            options.AllowTrailingCommas = true;

            string json = File.ReadAllText(jsonPath);
            IfcMappingConfig config = JsonSerializer.Deserialize<IfcMappingConfig>(json, options);

            return config;
        }

        internal static bool TryGetDefaultCompiledConfig(out IfcCompiledMappingConfig config)
        {
            config = null;

            string jsonPath = DefaultJsonPath;
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                return false;

            config = GetOrLoadCompiledConfig(jsonPath);
            return config != null && config.Rules.Count > 0;
        }

        private static IfcCompiledMappingConfig GetOrLoadCompiledConfig(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                return null;

            FileInfo fileInfo = new FileInfo(jsonPath);
            DateTime lastWriteUtc = fileInfo.LastWriteTimeUtc;
            long length = fileInfo.Length;

            lock (ConfigCacheLock)
            {
                if (_cachedCompiledConfig != null &&
                    string.Equals(_cachedConfigPath, jsonPath, StringComparison.OrdinalIgnoreCase) &&
                    _cachedConfigLastWriteUtc == lastWriteUtc &&
                    _cachedConfigLength == length)
                {
                    return _cachedCompiledConfig;
                }

                IfcCompiledMappingConfig compiled = CompileConfig(CarregarConfig(jsonPath));
                _cachedConfigPath = jsonPath;
                _cachedConfigLastWriteUtc = lastWriteUtc;
                _cachedConfigLength = length;
                _cachedCompiledConfig = compiled;
                return compiled;
            }
        }

        private static IfcCompiledMappingConfig CompileConfig(IfcMappingConfig config)
        {
            IfcCompiledMappingConfig compiled = new IfcCompiledMappingConfig();
            if (config?.Mappings == null)
                return compiled;

            foreach (IfcMappingRule rule in config.Mappings)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.LayerPattern))
                    continue;

                try
                {
                    Regex regex = new Regex(rule.LayerPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    compiled.Rules.Add(new IfcCompiledMappingRule(rule, regex));
                }
                catch
                {
                }
            }

            return compiled;
        }

        public static bool TryResolveMetadataForEntity(Entity ent, Editor docEditor, out IfcResolvedMetadata metadata)
        {
            metadata = null;

            if (!TryGetDefaultCompiledConfig(out IfcCompiledMappingConfig config))
                return false;

            return TryResolveMetadataForEntity(ent, config, docEditor, null, out metadata);
        }

        internal static bool TryResolveMetadataForEntity(
            Entity ent,
            IfcCompiledMappingConfig config,
            Editor docEditor,
            Func<string, string> solidosParamResolver,
            out IfcResolvedMetadata metadata)
        {
            metadata = null;

            if (ent == null || string.IsNullOrWhiteSpace(ent.Layer))
                return false;

            IfcCompiledMappingRule regra = EncontrarRegra(config, ent.Layer);
            if (regra == null)
                return false;

            metadata = ResolverMetadata(ent, regra.Rule, docEditor, solidosParamResolver);
            return metadata != null;
        }

        public static void WriteMetadataToObject(Entity ent, IfcResolvedMetadata metadata, Transaction transCad)
        {
            if (ent == null || metadata == null || transCad == null)
                return;

            GravarMetadataNoObjeto(ent, metadata, transCad);
        }

        private static IfcCompiledMappingRule EncontrarRegra(IfcCompiledMappingConfig config, string layerName)
        {
            if (config == null || config.Rules == null || string.IsNullOrWhiteSpace(layerName))
            {
                return null;
            }

            foreach (IfcCompiledMappingRule regra in config.Rules)
            {
                if (regra?.Regex == null)
                {
                    continue;
                }

                if (regra.Regex.IsMatch(layerName))
                {
                    return regra;
                }
            }

            return null;
        }

        public static IfcResolvedMetadata ResolverMetadata(
            Entity ent,
            IfcMappingRule regra,
            Editor docEditor,
            Func<string, string> solidosParamResolver)
        {
            IfcResolvedMetadata metadata = new IfcResolvedMetadata();
            metadata.Layer = ent.Layer;
            metadata.IfcClass = ValorOuVazio(regra.IfcClass);
            metadata.PredefinedType = ValorOuVazio(regra.PredefinedType);
            metadata.ObjectType = ValorOuVazio(regra.ObjectType);

            metadata.Name = ResolverValorPorPrioridade(ent, regra.NameSourcePriority, docEditor, solidosParamResolver);
            metadata.Tag = ResolverValorPorPrioridade(ent, regra.TagSourcePriority, docEditor, solidosParamResolver);
            metadata.Description = ResolverValorPorPrioridade(ent, regra.DescriptionSourcePriority, docEditor, solidosParamResolver);
            metadata.System = ResolverValorPorPrioridade(ent, regra.SystemSourcePriority, docEditor, solidosParamResolver);
            metadata.Subsystem = ResolverValorPorPrioridade(ent, regra.SubsystemSourcePriority, docEditor, solidosParamResolver);

            if (string.IsNullOrWhiteSpace(metadata.Name))
            {
                metadata.Name = ObterHandleTexto(ent);
            }

            if (string.IsNullOrWhiteSpace(metadata.Tag))
            {
                metadata.Tag = ObterHandleTexto(ent);
            }

            if (string.IsNullOrWhiteSpace(metadata.Description))
            {
                metadata.Description = metadata.ObjectType;
            }

            return metadata;
        }

        public static string ResolverValorPorPrioridade(
            Entity ent,
            List<string> sources,
            Editor docEditor,
            Func<string, string> solidosParamResolver)
        {
            if (sources == null || sources.Count == 0)
            {
                return string.Empty;
            }

            foreach (string source in sources)
            {
                string valor = ResolverSource(ent, source, docEditor, solidosParamResolver);
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    return valor.Trim();
                }
            }

            return string.Empty;
        }

        public static string ResolverSource(
            Entity ent,
            string source,
            Editor docEditor,
            Func<string, string> solidosParamResolver)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            if (source.StartsWith("SolidosParam:", StringComparison.OrdinalIgnoreCase))
            {
                string paramName = source.Substring("SolidosParam:".Length).Trim();
                if (solidosParamResolver != null)
                {
                    return solidosParamResolver(paramName) ?? string.Empty;
                }

                return TentarObterParametroSolidos(ent.ObjectId, paramName, docEditor);
            }

            if (source.StartsWith("Template:", StringComparison.OrdinalIgnoreCase))
            {
                return source.Substring("Template:".Length).Trim();
            }

            if (source.Equals("Handle", StringComparison.OrdinalIgnoreCase))
            {
                return ObterHandleTexto(ent);
            }

            if (source.Equals("Layer", StringComparison.OrdinalIgnoreCase))
            {
                return ent.Layer;
            }

            if (source.Equals("ObjectId", StringComparison.OrdinalIgnoreCase))
            {
                return ent.ObjectId.ToString();
            }

            return string.Empty;
        }

        public static string TentarObterParametroSolidos(ObjectId objId, string paramName, Editor docEditor)
        {
            if (objId.IsNull || string.IsNullOrWhiteSpace(paramName))
            {
                return string.Empty;
            }

            try
            {
                MethodInfo method = ObterMetodoSolidosGetNodeParam();
                if (method == null)
                {
                    return string.Empty;
                }

                object retorno = method.Invoke(null, new object[] { objId, paramName });
                if (retorno == null)
                {
                    return string.Empty;
                }

                string texto = retorno.ToString();
                return texto;
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage(
                    "\nFalha ao ler parâmetro SOLIDOS '" + paramName + "' do objeto " +
                    objId.ToString() + ": " + ex.Message);
                return string.Empty;
            }
        }

        public static MethodInfo ObterMetodoSolidosGetNodeParam()
        {
            if (_solidosGetNodeParamMethod != null)
            {
                return _solidosGetNodeParamMethod;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly asm in assemblies)
            {
                Type[] types;

                try
                {
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

                foreach (Type type in types)
                {
                    MethodInfo[] methods = type.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    foreach (MethodInfo method in methods)
                    {
                        object[] attrs = method.GetCustomAttributes(false);
                        foreach (object attr in attrs)
                        {
                            if (attr == null)
                            {
                                continue;
                            }

                            Type attrType = attr.GetType();
                            if (attrType.FullName != "Autodesk.AutoCAD.Runtime.LispFunctionAttribute")
                            {
                                continue;
                            }

                            PropertyInfo globalNameProp = attrType.GetProperty("GlobalName");
                            if (globalNameProp == null)
                            {
                                continue;
                            }

                            object globalNameObj = globalNameProp.GetValue(attr, null);
                            string globalName = globalNameObj == null ? string.Empty : globalNameObj.ToString();

                            if (string.Equals(globalName, "SolidosGetNodeParam", StringComparison.OrdinalIgnoreCase))
                            {
                                ParameterInfo[] pars = method.GetParameters();
                                if (pars.Length == 2 &&
                                    pars[0].ParameterType == typeof(ObjectId) &&
                                    pars[1].ParameterType == typeof(string))
                                {
                                    _solidosGetNodeParamMethod = method;
                                    return _solidosGetNodeParamMethod;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        public static string ObterHandleTexto(Entity ent)
        {
            try
            {
                return ent.Handle.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string ValorOuVazio(string valor)
        {
            return string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim();
        }

        public static void GravarMetadataNoObjeto(Entity ent, IfcResolvedMetadata metadata, Transaction TransCad)
        {
            if (!ent.IsWriteEnabled)
            {
                ent.UpgradeOpen();
            }

            DBDictionary extDict;
            if (ent.ExtensionDictionary.IsNull)
            {
                ent.CreateExtensionDictionary();
            }

            extDict = (DBDictionary)TransCad.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);

            Xrecord xrec;
            if (extDict.Contains(DictName))
            {
                ObjectId xrecId = extDict.GetAt(DictName);
                xrec = (Xrecord)TransCad.GetObject(xrecId, OpenMode.ForWrite);
            }
            else
            {
                xrec = new Xrecord();
                extDict.SetAt(DictName, xrec);
                TransCad.AddNewlyCreatedDBObject(xrec, true);
            }

            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, "IfcClass=" + metadata.IfcClass),
                new TypedValue((int)DxfCode.Text, "PredefinedType=" + metadata.PredefinedType),
                new TypedValue((int)DxfCode.Text, "ObjectType=" + metadata.ObjectType),
                new TypedValue((int)DxfCode.Text, "Name=" + metadata.Name),
                new TypedValue((int)DxfCode.Text, "Tag=" + metadata.Tag),
                new TypedValue((int)DxfCode.Text, "Description=" + metadata.Description),
                new TypedValue((int)DxfCode.Text, "Layer=" + metadata.Layer),
                new TypedValue((int)DxfCode.Text, "System=" + metadata.System),
                new TypedValue((int)DxfCode.Text, "Subsystem=" + metadata.Subsystem)
            );

            xrec.Data = rb;
        }
    }

    public class IfcMappingConfig
    {
        [JsonPropertyName("Mappings")]
        public List<IfcMappingRule> Mappings { get; set; }
    }

    public class IfcMappingRule
    {
        [JsonPropertyName("LayerPattern")]
        public string LayerPattern { get; set; }

        [JsonPropertyName("IfcClass")]
        public string IfcClass { get; set; }

        [JsonPropertyName("PredefinedType")]
        public string PredefinedType { get; set; }

        [JsonPropertyName("ObjectType")]
        public string ObjectType { get; set; }

        [JsonPropertyName("NameSourcePriority")]
        public List<string> NameSourcePriority { get; set; }

        [JsonPropertyName("TagSourcePriority")]
        public List<string> TagSourcePriority { get; set; }

        [JsonPropertyName("DescriptionSourcePriority")]
        public List<string> DescriptionSourcePriority { get; set; }

        [JsonPropertyName("SystemSourcePriority")]
        public List<string> SystemSourcePriority { get; set; }

        [JsonPropertyName("SubsystemSourcePriority")]
        public List<string> SubsystemSourcePriority { get; set; }
    }

    public class IfcResolvedMetadata
    {
        public string IfcClass { get; set; }
        public string PredefinedType { get; set; }
        public string ObjectType { get; set; }
        public string Name { get; set; }
        public string Tag { get; set; }
        public string Description { get; set; }
        public string Layer { get; set; }
        public string System { get; set; }
        public string Subsystem { get; set; }
    }

    internal sealed class IfcCompiledMappingConfig
    {
        public List<IfcCompiledMappingRule> Rules { get; } = new List<IfcCompiledMappingRule>();
    }

    internal sealed class IfcCompiledMappingRule
    {
        public IfcCompiledMappingRule(IfcMappingRule rule, Regex regex)
        {
            Rule = rule;
            Regex = regex;
        }

        public IfcMappingRule Rule { get; }
        public Regex Regex { get; }
    }
}
