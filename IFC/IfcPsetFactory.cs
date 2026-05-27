// ===================== IfcPsetFactory.cs =====================
using Autodesk.Aec.DatabaseServices;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using BlockReference = Autodesk.AutoCAD.DatabaseServices.BlockReference;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    public static class IfcPsetFactory
    {
        private static readonly object SessionCacheLock = new object();
        private static readonly HashSet<int> PreparedDatabases = new HashSet<int>();

        // Chame uma vez por desenho. Retorna os IDs das definições.
        public static Dictionary<string, ObjectId> EnsureDefaultPsets(Database db, Transaction tr, Editor ed)
        {
            Dictionary<string, ObjectId> ids = new Dictionary<string, ObjectId>();
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);
            int databaseKey = RuntimeHelpers.GetHashCode(db);
            bool alreadyPrepared;

            lock (SessionCacheLock)
            {
                alreadyPrepared = PreparedDatabases.Contains(databaseKey);
            }

            if (alreadyPrepared)
            {
                ids["Pset_A - Dados do Projeto"] = dict.Has("Pset_A - Dados do Projeto", tr) ? dict.GetAt("Pset_A - Dados do Projeto") : ObjectId.Null;
                ids["Pset_B - Informações dos Objetos e Elementos"] = dict.Has("Pset_B - Informações dos Objetos e Elementos", tr) ? dict.GetAt("Pset_B - Informações dos Objetos e Elementos") : ObjectId.Null;
                ids["Pset_C - Propriedades Fisicas dos Objetos"] = dict.Has("Pset_C - Propriedades Fisicas dos Objetos", tr) ? dict.GetAt("Pset_C - Propriedades Fisicas dos Objetos") : ObjectId.Null;
                ids["Pset_D - Propriedades Geográficas"] = dict.Has("Pset_D - Propriedades Geográficas", tr) ? dict.GetAt("Pset_D - Propriedades Geográficas") : ObjectId.Null;
                ids["Pset_COORDENAÇÃO"] = dict.Has("Pset_COORDENAÇÃO", tr) ? dict.GetAt("Pset_COORDENAÇÃO") : ObjectId.Null;
                ids["Pset_E - Requisitos Específicos de Projeto"] = dict.Has("Pset_E - Requisitos Específicos de Projeto", tr) ? dict.GetAt("Pset_E - Requisitos Específicos de Projeto") : ObjectId.Null;
                ids["IfcObject Properties"] = dict.Has("IfcObject Properties", tr) ? dict.GetAt("IfcObject Properties") : ObjectId.Null;

                if (ids.Values.All(id => id != ObjectId.Null))
                    return ids;
            }

            // A
            ids["A - Dados do Projeto"] =
                Ensure(defName: "A - Dados do Projeto", dict, db, tr, ed, new (string, PropertyDataType)[] {
                    ("Identificador do Projeto", PropertyDataType.kString),
                    ("NomeProjeto",             PropertyDataType.kString),
                    ("Segmento",                PropertyDataType.kString),
                    ("Trecho",                  PropertyDataType.kString),
                    ("Lote",                    PropertyDataType.kString),
                    ("Rodovia",                 PropertyDataType.kString),
                    ("UF",                      PropertyDataType.kString),
                    ("Municipio",               PropertyDataType.kString),
                    ("EstagioProjeto",          PropertyDataType.kString)
                });

            // B
            ids["B - Informações dos Objetos e Elementos"] =
                Ensure(defName: "B - Informações dos Objetos e Elementos", dict, db, tr, ed, new (string, PropertyDataType)[] {
                    ("Disciplina",            PropertyDataType.kString),
                    ("Localização",           PropertyDataType.kString),
                    ("Localizacao",           PropertyDataType.kString),
                    ("Situação",              PropertyDataType.kString),
                    ("Situacao",              PropertyDataType.kString),
                    ("EstaqueamentoInicial",  PropertyDataType.kString),
                    ("EstaqueamentoFinal",    PropertyDataType.kString),
                    ("Estaqueamento_Inicial", PropertyDataType.kString),
                    ("Estaqueamento_Final",   PropertyDataType.kString),
                    ("Código_do_Objeto",      PropertyDataType.kString),
                    ("CodigoObjeto",          PropertyDataType.kString),
                    ("CodeName",              PropertyDataType.kString),
                    ("SubassemblyName",       PropertyDataType.kString),
                    ("AssemblyName",          PropertyDataType.kString),
                    ("NomeCorredorSolido",    PropertyDataType.kString),
                    ("NomeCorredorSolidos",   PropertyDataType.kString),
                    ("RegionName",            PropertyDataType.kString),
                    ("RegionGUID",            PropertyDataType.kString),
                    ("Comprimento",           PropertyDataType.kString),
                    ("Lado",                  PropertyDataType.kString)
                });

            // C  (nome canonical atual — renomeado de "Pset_C - Propriedades Físicas dos Objetos e Elementos")
            ids["Pset_C - Propriedades Fisicas dos Objetos"] =
                Ensure(defName: "Pset_C - Propriedades Fisicas dos Objetos", dict, db, tr, ed, new (string, PropertyDataType)[] {
                    ("Comprimento",  PropertyDataType.kString),
                    ("Largura",      PropertyDataType.kString),
                    ("Altura",       PropertyDataType.kString),
                    ("Área",         PropertyDataType.kString),
                    ("Area",         PropertyDataType.kString),
                    ("Volume",       PropertyDataType.kString),
                    ("Diâmetro",     PropertyDataType.kString),
                    ("Diametro",     PropertyDataType.kString),
                    ("Inclinação",   PropertyDataType.kString),
                    ("Inclinacao",   PropertyDataType.kString),
                    ("Cota_de_Fundo",PropertyDataType.kString),
                    ("Cota_de_Topo", PropertyDataType.kString),
                });

            // D
            ids["D - Propriedades Geográficas"] =
                Ensure(defName: "D - Propriedades Geográficas", dict, db, tr, ed, new (string, PropertyDataType)[] {
                    ("Coordenada_E", PropertyDataType.kString),
                    ("Coordenada_N", PropertyDataType.kString),
                    ("Coordenada_Z", PropertyDataType.kString),
                });

            // COORDENAÇÃO
            ids["COORDENAÇÃO"] =
                Ensure(defName: "COORDENAÇÃO", dict, db, tr, ed, new (string, PropertyDataType)[] {
                    ("AREA_3D_SUPERFICIE", PropertyDataType.kString),
                    ("COMPRIMENTO_3D_FEATURE_LINES", PropertyDataType.kString),
                    ("COMPRIMENTO_SOLIDOS_CORREDOR", PropertyDataType.kString),
                });

            // E
            ids["E - Requisitos Específicos de Projeto"] =
                Ensure(defName: "E - Requisitos Específicos de Projeto", dict, db, tr, ed, new (string, PropertyDataType)[] {
                    ("Material", PropertyDataType.  kString),
                    ("ClasseMaterial", PropertyDataType.kString)
                });

            // IfcObject Properties
            ids["IfcObject Properties"] =
                Ensure(defName: "IfcObject Properties", dict, db, tr, ed, new (string, PropertyDataType)[] {
                    ("IFC::IfcExportAs",   PropertyDataType.kString),
                    ("IFC::PredefinedType",PropertyDataType.kString),
                    ("IFC::ObjectType",   PropertyDataType.kString),
                    ("IfcGlobalId",        PropertyDataType.kString)
                });

            lock (SessionCacheLock)
            {
                PreparedDatabases.Add(databaseKey);
            }

            return ids;
        }

        // Cria caso não exista. Mantém tudo explícito.
        private static ObjectId Ensure(
            string defName,
            DictionaryPropertySetDefinitions dict,
            Database db,
            Transaction tr,
            Editor ed,
            (string propName, PropertyDataType type)[] fields)
        {
            if (dict.Has(defName, tr))
            {
                ObjectId existingId = dict.GetAt(defName);
                PropertySetDefinition existing = (PropertySetDefinition)tr.GetObject(existingId, OpenMode.ForWrite);
                HashSet<string> existingNames = new HashSet<string>(
                    existing.Definitions.Cast<PropertyDefinition>().Select(p => p.Name ?? string.Empty),
                    System.StringComparer.OrdinalIgnoreCase
                );

                foreach ((string propName, PropertyDataType type) item in fields)
                {
                    if (existingNames.Contains(item.propName))
                        continue;

                    PropertyDefinition prop = new PropertyDefinition();
                    prop.SetToStandard(db);
                    prop.SubSetDatabaseDefaults(db);
                    prop.Name = item.propName;
                    prop.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                    existing.Definitions.Add(prop);
                    existingNames.Add(item.propName);
                }

                return existingId;
            }

            // cria definição
            PropertySetDefinition psd = new PropertySetDefinition();
            psd.SetToStandard(db);
            psd.SubSetDatabaseDefaults(db);
            psd.AppliesToAll = true;
            psd.AlternateName = defName;

            // adiciona propriedades simples
            foreach ((string propName, PropertyDataType type) item in fields)
            {
                PropertyDefinition sp = new PropertyDefinition();
                sp.SetToStandard(db);
                sp.SubSetDatabaseDefaults(db);
                sp.Name = item.propName;
                sp.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                psd.Definitions.Add(sp);
            }

            // posta no desenho e registra no dicionário
            dict.AddNewRecord(defName, psd);
            tr.AddNewlyCreatedDBObject(psd, true);

            ed.WriteMessage($"\n[PSET] Criado: {defName}");
            return psd.ObjectId;
        }
    }

    // Extensões auxiliares para evitar métodos inexistentes em versões antigas.
    internal static class PsetApiCompat
    {
        public static void SetDatabaseDefaults(this PropertySetDefinition psd, Database db)
        {
            TryInvokeInstance(psd, "SubSetDatabaseDefaults", new[] { typeof(Database) }, db);
            TryInvokeInstance(psd, "SetDatabaseDefaults", new[] { typeof(Database) }, db);
        }

        public static void SetAppliesTo(this PropertySetDefinition psd, RXClass rx)
        {
            TryInvokeInstance(psd, "SetAppliesTo", new[] { typeof(RXClass) }, rx);
        }

        public static ObjectId PostToDatabase(this PropertySetDefinition psd, Database db, Transaction tr)
        {
            object result = null;

            if (TryInvokeInstance(psd, "PostToDatabase", new[] { typeof(Database), typeof(Transaction) }, out result, db, tr) && result is ObjectId oidFromPostToDatabase)
                return oidFromPostToDatabase;

            if (TryInvokeInstance(psd, "PostToDb", new[] { typeof(Database), typeof(Transaction) }, out result, db, tr) && result is ObjectId oidFromPostToDb)
                return oidFromPostToDb;

            throw new MissingMethodException("PropertySetDefinition.PostToDatabase/PostToDb não encontrado na API atual.");
        }

        public static void Add(this DictionaryPropertySetDefinitions dict, string name, ObjectId id, Transaction tr)
        {
            TryInvokeInstance(dict, "Add", new[] { typeof(string), typeof(ObjectId), typeof(Transaction) }, name, id, tr);
        }

        private static bool TryInvokeInstance(object target, string methodName, Type[] signature, params object[] args)
        {
            return TryInvokeInstance(target, methodName, signature, out _, args);
        }

        private static bool TryInvokeInstance(object target, string methodName, Type[] signature, out object result, params object[] args)
        {
            result = null;
            if (target == null)
                return false;

            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: signature,
                modifiers: null
            );

            if (method == null)
                return false;

            try
            {
                result = method.Invoke(target, args);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
