using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D
{
    public class IfcMappingReader
    {
        public const string IfcMappingRecordName = "IFC_MAPPING";

        [CommandMethod("IFC_LER_MAPPING_APLICADO")]
        public void LerMappingAplicado()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione o objeto para ler o IFC_MAPPING: ");
                peo.SetRejectMessage("\nSelecione uma entidade válida.");
                peo.AddAllowedClass(typeof(Entity), true);

                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                using (Transaction TransCad = db.TransactionManager.StartTransaction())
                {
                    IfcStoredMetadata metadata;
                    bool encontrou = TryReadIfcMapping(per.ObjectId, TransCad, out metadata);

                    if (!encontrou)
                    {
                        docEditor.WriteMessage("\nO objeto não possui IFC_MAPPING no ExtensionDictionary.");
                        return;
                    }

                    docEditor.WriteMessage("\n--- IFC_MAPPING ---");
                    docEditor.WriteMessage("\nIfcClass: " + metadata.IfcClass);
                    docEditor.WriteMessage("\nPredefinedType: " + metadata.PredefinedType);
                    docEditor.WriteMessage("\nObjectType: " + metadata.ObjectType);
                    docEditor.WriteMessage("\nName: " + metadata.Name);
                    docEditor.WriteMessage("\nTag: " + metadata.Tag);
                    docEditor.WriteMessage("\nDescription: " + metadata.Description);
                    docEditor.WriteMessage("\nLayer: " + metadata.Layer);
                    docEditor.WriteMessage("\nSystem: " + metadata.System);
                    docEditor.WriteMessage("\nSubsystem: " + metadata.Subsystem);

                    TransCad.Commit();
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage("\nErro ao ler IFC_MAPPING: " + ex.Message);
            }
        }

        public static bool TryReadIfcMapping(ObjectId entityId, Transaction TransCad, out IfcStoredMetadata metadata)
        {
            metadata = null;

            if (entityId.IsNull || entityId.IsErased)
            {
                return false;
            }

            Entity ent = (Entity)TransCad.GetObject(entityId, OpenMode.ForRead);
            if (ent == null)
            {
                return false;
            }

            if (ent.ExtensionDictionary.IsNull)
            {
                return false;
            }

            DBDictionary extDict = (DBDictionary)TransCad.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
            if (extDict == null)
            {
                return false;
            }

            if (!extDict.Contains(IfcMappingRecordName))
            {
                return false;
            }

            ObjectId xrecId = extDict.GetAt(IfcMappingRecordName);
            if (xrecId.IsNull || xrecId.IsErased)
            {
                return false;
            }

            Xrecord xrec = (Xrecord)TransCad.GetObject(xrecId, OpenMode.ForRead);
            if (xrec == null || xrec.Data == null)
            {
                return false;
            }

            metadata = ParseResultBuffer(xrec.Data);
            return metadata != null;
        }

        public static IfcStoredMetadata ReadIfcMappingOrDefault(ObjectId entityId, Transaction TransCad)
        {
            IfcStoredMetadata metadata;
            bool encontrou = TryReadIfcMapping(entityId, TransCad, out metadata);

            if (encontrou)
            {
                return metadata;
            }

            Entity ent = (Entity)TransCad.GetObject(entityId, OpenMode.ForRead);

            IfcStoredMetadata fallback = new IfcStoredMetadata();
            fallback.IfcClass = string.Empty;
            fallback.PredefinedType = string.Empty;
            fallback.ObjectType = string.Empty;
            fallback.Name = ent != null ? ent.Handle.ToString() : string.Empty;
            fallback.Tag = ent != null ? ent.Handle.ToString() : string.Empty;
            fallback.Description = string.Empty;
            fallback.Layer = ent != null ? ent.Layer : string.Empty;
            fallback.System = string.Empty;
            fallback.Subsystem = string.Empty;

            return fallback;
        }

        private static IfcStoredMetadata ParseResultBuffer(ResultBuffer rb)
        {
            IfcStoredMetadata metadata = new IfcStoredMetadata();

            TypedValue[] values = rb.AsArray();
            if (values == null || values.Length == 0)
            {
                return null;
            }

            foreach (TypedValue tv in values)
            {
                if (tv.Value == null)
                {
                    continue;
                }

                string raw = tv.Value.ToString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                int idx = raw.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                string key = raw.Substring(0, idx).Trim();
                string value = raw.Substring(idx + 1).Trim();

                if (key.Equals("IfcClass", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.IfcClass = value;
                    continue;
                }

                if (key.Equals("PredefinedType", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.PredefinedType = value;
                    continue;
                }

                if (key.Equals("ObjectType", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.ObjectType = value;
                    continue;
                }

                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.Name = value;
                    continue;
                }

                if (key.Equals("Tag", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.Tag = value;
                    continue;
                }

                if (key.Equals("Description", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.Description = value;
                    continue;
                }

                if (key.Equals("Layer", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.Layer = value;
                    continue;
                }

                if (key.Equals("System", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.System = value;
                    continue;
                }

                if (key.Equals("Subsystem", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.Subsystem = value;
                    continue;
                }
            }

            return metadata;
        }
    }

    public class IfcStoredMetadata
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
}
