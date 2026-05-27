// BlocksToSolids.cs
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.Collections.Generic;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;

namespace AutomacoesCivil3D
{
    public class BlocksToSolids
    {
        [CommandMethod("BLOCO2SOLIDO")]
        public void ConverterBlocosParaSolidos()
        {
            Document docCad = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database db = docCad.Database;

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelecione blocos (BlockReference): ";
            PromptSelectionResult psr = docEditor.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdo = new PromptDoubleOptions("\nAltura de extrusão (m): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = 0.2;
            PromptDoubleResult pdr = docEditor.GetDouble(pdo);
            if (pdr.Status != PromptStatus.OK) return;
            double altura = pdr.Value;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    BlockReference br = (BlockReference)tr.GetObject(so.ObjectId, OpenMode.ForWrite);
                    if (br == null) continue;

                    // Explode do bloco para obter geometrias instanciadas (transformadas)
                    DBObjectCollection exploded = new DBObjectCollection();
                    br.Explode(exploded);

                    foreach (DBObject dbo in exploded)
                    {
                        Entity ent = dbo as Entity;
                        if (ent == null) { dbo.Dispose(); continue; }

                        // Já é sólido 3D? Apenas anexa ao modelspace
                        if (ent is Solid3d)
                        {
                            ent.UpgradeOpen();
                            ms.AppendEntity(ent);
                            tr.AddNewlyCreatedDBObject(ent, true);
                            continue;
                        }

                        // Region pronta?
                        if (ent is Region)
                        {
                            Region reg = (Region)ent;
                            Solid3d sol = new Solid3d();
                            sol.SetDatabaseDefaults();
                            sol.Extrude(reg, altura, 0.0);
                            ms.AppendEntity(sol);
                            tr.AddNewlyCreatedDBObject(sol, true);
                            reg.Dispose();
                            continue;
                        }

                        // Curvas fechadas: Polyline, Polyline2d, Circle, Ellipse fechada, Spline fechada
                        Curve cv = ent as Curve;
                        if (cv != null && IsClosedCurve(cv))
                        {
                            DBObjectCollection curvas = new DBObjectCollection();
                            curvas.Add(cv); // Region.CreateFromCurves consome a entidade
                            DBObjectCollection regs = Region.CreateFromCurves(curvas);

                            foreach (DBObject r in regs)
                            {
                                Region reg = (Region)r;
                                Solid3d sol = new Solid3d();
                                sol.SetDatabaseDefaults();
                                sol.Extrude(reg, altura, 0.0);
                                ms.AppendEntity(sol);
                                tr.AddNewlyCreatedDBObject(sol, true);
                                reg.Dispose();
                            }
                            // Region.CreateFromCurves já assumiu a curva; descartar referência local
                            continue;
                        }

                        // Outras entidades: descarte seguro (evita lixo no desenho)
                        ent.Dispose();
                    }
                }

                tr.Commit();
            }
        }

        private bool IsClosedCurve(Curve cv)
        {
            if (cv.Closed) return true;
            // Círculo e elipse fechada
            if (cv is Circle) return true;
            if (cv is Ellipse && ((Ellipse)cv).StartAngle == 0.0 && ((Ellipse)cv).EndAngle == 0.0) return true;
            // Spline com flag de fechado
            if (cv is Spline && ((Spline)cv).Closed) return true;
            return false;
        }
    }
}
