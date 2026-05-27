using System;
using System.IO;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using AutomacoesCivil3D;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    public class Civil3DObjectCopier2
    {
        public void CopyObjectsBetweenDrawings(ObjectIdCollection objetos, string caminhoDestino, Database _ignore, Database srcDb)
        {
            if (objetos == null || objetos.Count == 0)
            {
                return;
            }

            Editor ed = Manager.DocEditor;
            string alvo = Path.GetFullPath(caminhoDestino);

            if (!File.Exists(alvo))
            {
                Database novoDb = new Database(true, true);
                novoDb.SaveAs(alvo, DwgVersion.Current);
                novoDb.Dispose();
            }

            bool jaEstavaAberto = TryGetOpenDocument(alvo, out Document destDoc);

            if (!jaEstavaAberto)
            {
                destDoc = Application.DocumentManager.Open(alvo, false);
            }

            Database destDb = destDoc.Database;

            using (DocumentLock docLock = destDoc.LockDocument())
            using (Transaction destTr = destDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)destTr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms =
                    (BlockTableRecord)destTr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                IdMapping map = new IdMapping();
                destDb.WblockCloneObjects(objetos, ms.ObjectId, map, DuplicateRecordCloning.Replace, false);

                destTr.Commit();
            }

            try
            {
                destDb.SaveAs(alvo, DwgVersion.Current);
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nFalha ao salvar DWG destino: {ex.Message}\nSalve manualmente: {alvo}");
            }
        }

        private static bool TryGetOpenDocument(string fullPath, out Document doc)
        {
            doc = null;

            DocumentCollection docs = Application.DocumentManager;
            foreach (Document d in docs)
            {
                try
                {
                    string docPath = Path.GetFullPath(d.Name);
                    if (string.Equals(docPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        doc = d;
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }
    }
}


