// IfcConfigurator.cs
using Autodesk.Aec.DatabaseServices;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.Collections.Generic;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using DataType = Autodesk.Aec.PropertyData.DataType;

namespace AutomacoesCivil3D
{
    public static class IfcConfigurator
    {
        // Regra → (IfcClass, PredefinedType, Layer)
        private static readonly Dictionary<string, (string IfcClass, string PreType, string Layer)> Rules =
            new Dictionary<string, (string, string, string)>
            {
                { "BUEIRO",   ("IfcPipeSegment", "CULVERT",       "DREN_BUEIRO_IFC") },
                { "TUBO",     ("IfcPipeSegment", "RIGIDSEGMENT",  "DREN_TUBO_IFC") },
                { "JOELHO",   ("IfcPipeFitting", "BEND",          "DREN_CONEXAO_IFC") },
                { "CURVA",    ("IfcPipeFitting", "BEND",          "DREN_CONEXAO_IFC") },
                { "TEE",      ("IfcPipeFitting", "JUNCTION",      "DREN_CONEXAO_IFC") },
                { "JUNCTION", ("IfcPipeFitting", "JUNCTION",      "DREN_CONEXAO_IFC") },
                { "REDU",     ("IfcPipeFitting", "TRANSITION",    "DREN_CONEXAO_IFC") },
                { "TRANSITION",("IfcPipeFitting","TRANSITION",    "DREN_CONEXAO_IFC") },
                { "VALETA",   ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "CANALETA", ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "DESCIDA",  ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "PV",       ("IfcDistributionChamberElement","MANHOLE","DREN_ESTRUTURA_IFC") },
                { "MANHOLE",  ("IfcDistributionChamberElement","MANHOLE","DREN_ESTRUTURA_IFC") },
                { "BL",       ("IfcDistributionChamberElement","INLET",  "DREN_ESTRUTURA_IFC") },
                { "INLET",    ("IfcDistributionChamberElement","INLET",  "DREN_ESTRUTURA_IFC") },
            };

        // Cores sugeridas p/ filtros (pode ajustar)
        private static readonly Dictionary<string, short> LayerColors = new Dictionary<string, short>
        {
            { "DREN_TUBO_IFC",        3 },   // Verde
            { "DREN_BUEIRO_IFC",      130 },
            { "DREN_CONEXAO_IFC",     6 },   // Magenta
            { "DREN_ABERTA_IFC",      1 },   // Vermelho
            { "DREN_ESTRUTURA_IFC",   5 },   // Azul
            { "IFC_PROXY",            8 }    // Cinza
        };

        // 1) Cria TODOS os layers IFC padronizados (chamar 1x por transação)
        public static void EnsureIfcLayers(Database db, Transaction tr)
        {
            // Sempre inclui PROXY
            HashSet<string> set = new HashSet<string>(LayerColors.Keys);
            foreach (KeyValuePair<string, (string IfcClass, string PreType, string Layer)> kv in Rules)
                set.Add(kv.Value.Layer);

            foreach (string layerName in set)
                EnsureLayer(db, tr, layerName, LayerColors.ContainsKey(layerName) ? LayerColors[layerName] : (short)7);
        }

        // NOVO: garante PSETs A..E e "IfcObject Properties"
        private static ObjectId EnsureIfcObjectProps(Database db, Transaction tr, Editor ed)
        {
            // Reusa sua fábrica para criar quando faltar
            System.Collections.Generic.Dictionary<string, ObjectId> ids =
                AutomacoesCivil3D.IfcPsetFactory.EnsureDefaultPsets(db, tr, ed);

            if (ids != null && ids.ContainsKey("IfcObject Properties"))
                return ids["IfcObject Properties"];
            return ObjectId.Null;
        }



        
        // 2) Classifica e aplica layer + Pset IFC
        public static void ApplyToEntity(Entity entity, string codeName, Database db, Transaction tr)
        {
            Document docCad = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            // garante layers prontos
            EnsureIfcLayers(db, tr);


            string ifcClass = "IfcPavement";
            string predef = "USERDEFINED";
            string tgtLayer = "IFC_PROXY";

            string key = (codeName ?? "").ToUpperInvariant();
            foreach (KeyValuePair<string, (string IfcClass, string PreType, string Layer)> kv in Rules)
            {
                if (key.Contains(kv.Key))
                {
                    ifcClass = kv.Value.IfcClass;
                    predef = kv.Value.PreType;
                    tgtLayer = kv.Value.Layer;
                    break;
                }
            }

            // aplica layer
            SetEntityLayer(entity, db, tr, tgtLayer);
            // injeta PSET IFC:: se existir no DWG
            string psetIfc = "IfcObject Properties";
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);
            PropertySetDefinition novo = new PropertySetDefinition();

            if (dict.Has(psetIfc, tr))
            {
                ObjectId defId = dict.GetAt(psetIfc);
                PropertyDataServices.AddPropertySet(entity, defId);
                PropertySet pset = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(entity, defId), OpenMode.ForWrite);


                int idExportAs = pset.PropertyNameToId("IFC::IfcExportAs");
                if (idExportAs != -1) pset.SetAt(idExportAs, ifcClass);

                int idPreType = pset.PropertyNameToId("IFC::PredefinedType");
                if (idPreType != -1) pset.SetAt(idPreType, predef);
            }
           
        }

        // Helpers
        private static void EnsureLayer(Database db, Transaction tr, string layerName, short aci)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName)) return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            ObjectId newId = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void SetEntityLayer(Entity ent, Database db, Transaction tr, string layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
                EnsureLayer(db, tr, layerName, (short)7);

            ent.UpgradeOpen();
            ent.Layer = layerName;
        }
    }
}
