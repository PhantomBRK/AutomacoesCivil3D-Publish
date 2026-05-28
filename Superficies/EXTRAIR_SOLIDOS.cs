using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Linq;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace AutomacoesCivil3D.Superficies
{
    public class VolumeSurfaceAutomator
    {
        [CommandMethod("C3D_VOL_BOUNDARY_AUTOMATE")]
        public void Run()
        {
            Document doc = Manager.DocCad;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            CivilDocument cdoc = CivilApplication.ActiveDocument;

            try
            {
                using (var docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1) Escolha do CORREDOR e da sua Surface de corredor (CorridorSurface)
                    var corridors = GetAllCorridors(cdoc, tr);
                    if (corridors.Count == 0)
                        throw new InvalidOperationException("Não há corredores no desenho.");

                    ObjectId corridorId = PromptCorridorByName(ed, "Selecione o CORREDOR", corridors);
                    if (corridorId.IsNull) throw new OperationCanceledException("Cancelado.");

                    var corr = (Corridor)tr.GetObject(corridorId, OpenMode.ForRead);
                    var corrSurfMap = GetCorridorSurfaces(corr, tr);
                    if (corrSurfMap.Count == 0)
                        throw new InvalidOperationException($"O corredor '{corr.Name}' não possui superfícies.");

                    ObjectId corridorSurfaceId = PromptCorridorSurfaceByName(ed, $"Selecione a Superfície do Corredor de '{corr.Name}'", corrSurfMap);
                    if (corridorSurfaceId.IsNull) throw new OperationCanceledException("Cancelado.");

                    // 2) Escolha da EG (TinSurface) por nome (coleção global de surfaces)
                    var tinSurfaces = GetAllTinSurfaces(cdoc, tr);
                    if (tinSurfaces.Count == 0)
                        throw new InvalidOperationException("Não há TinSurfaces no desenho (EG).");

                    ObjectId egSurfId = PromptSurfaceByName(ed, "Selecione a superfície do TERRENO (EG)", tinSurfaces);
                    if (egSurfId.IsNull) throw new OperationCanceledException("Cancelado.");

                    // 3) Boundary interno (Polyline 2D fechada) para recortar o corredor
                    var innerBoundaryId = PromptClosedPolyline(ed, tr, "Selecione a polilinha FECHADA da borda do corredor (boundary interno)");
                    if (innerBoundaryId.IsNull) throw new OperationCanceledException("Cancelado.");

                    // 4) Parâmetros de processo
                    double offsetDist = PromptDouble(ed, "Informe o offset para o boundary do EG (padrão 5.0):", 5.0);
                    double midOrdinate = PromptDouble(ed, "Mid-ordinate distance para boundary (padrão 0.25):", 0.25);

                    // 5) Export settings
                    var exportChoice = Prompt3dFaceExportChoice(ed);
                    double gridStep = 2.0; // usado no fallback e para "Volume" (par de malhas)
                    if (exportChoice != ExportChoice.Nenhum)
                        gridStep = PromptDouble(ed, "Passo da grade para malha (fallback e Volume) <2.0m>:", 2.0);

                    // 6) Extrair a Superfície de Corredor para uma TinSurface de trabalho
                    // (Create TinSurface e PasteSurface da CorridorSurface -> vira uma TIN independente)
                    string corrWorkName = MakeUniqueName(cdoc, $"CORR_Work_{corr.Name}");
                    ObjectId corrWorkId = TinSurface.Create(db, corrWorkName);
                    var corrWork = (TinSurface)tr.GetObject(corrWorkId, OpenMode.ForWrite);
                    corrWork.PasteSurface(corridorSurfaceId); // cola a CorridorSurface na TIN de trabalho

                    // 7) Aplicar boundary EXTERNO na TinSurface de trabalho (corredor recortado)
                    AddOuterBoundaryFromPolyline(corrWork, innerBoundaryId, midOrdinate);

                    // 8) Criar cópia do EG + boundary offset (externo)
                    string egCopyName = MakeUniqueName(cdoc, "EG_Copy");
                    var egCopyId = TinSurface.Create(db, egCopyName);
                    var egCopy = (TinSurface)tr.GetObject(egCopyId, OpenMode.ForWrite);
                    egCopy.PasteSurface(egSurfId);

                    var offsetBoundaryId = CreateBestOffsetPolyline(db, tr, innerBoundaryId, Math.Abs(offsetDist));
                    AddOuterBoundaryFromPolyline(egCopy, offsetBoundaryId, midOrdinate);

                    // 9) Rebuild das superfícies recortadas
                    SafeRebuild(corrWork);
                    SafeRebuild(egCopy);

                    // 10) Criar TinVolumeSurface (entre as duas TINs recortadas)
                    string volName = MakeUniqueName(cdoc, $"VOL_{corrWorkName}_VS_{egCopyName}");
                    ObjectId volId = TinVolumeSurface.Create(volName, corrWorkId, egCopyId);
                    var volSurf = (TinVolumeSurface)tr.GetObject(volId, OpenMode.ForRead);

                    // 11) Exportar 3D Faces (opcional)
                    if (exportChoice != ExportChoice.Nenhum)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                        if (exportChoice == ExportChoice.Volume || exportChoice == ExportChoice.Todos)
                        {
                            ed.WriteMessage($"\nExportando 3D Faces do VOLUME (par de malhas TOP/BOTTOM) com grade {gridStep}m ...");
                            // Volume: TinVolumeSurface NÃO tem triângulos; geramos par de malhas casadas (corrWork x egCopy)
                            ExportBetweenSurfacesAsFaces(ed, tr, btr, corrWork, egCopy, gridStep);
                        }

                        if (exportChoice == ExportChoice.TopBottom || exportChoice == ExportChoice.Todos)
                        {
                            ed.WriteMessage($"\nExportando 3D Faces do CORREDOR recortado: {corrWorkName} ...");
                            Export3DFacesFromTinOrGrid(ed, tr, btr, corrWork, gridStep);

                            ed.WriteMessage($"\nExportando 3D Faces do EG recortado: {egCopyName} ...");
                            Export3DFacesFromTinOrGrid(ed, tr, btr, egCopy, gridStep);
                        }
                    }

                    // 12) Mensagens finais
                    ed.WriteMessage($"\n✔ Superfície de corredor extraída para TIN: {corrWorkName}");
                    ed.WriteMessage($"\n✔ Boundary aplicado em: {corrWorkName}");
                    ed.WriteMessage($"\n✔ EG copiado e recortado: {egCopyName}");
                    ed.WriteMessage($"\n✔ TinVolumeSurface criada: {volName}");
                    if (exportChoice != ExportChoice.Nenhum)
                        ed.WriteMessage("\n✔ 3D Faces exportadas (ver Model Space).");

                    tr.Commit();
                }
            }
            catch (OperationCanceledException)
            {
                // cancelado
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERRO] {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ========================= EXPORT 3D FACES =========================

        private enum ExportChoice { Nenhum, Volume, TopBottom, Todos }

        private static ExportChoice Prompt3dFaceExportChoice(Editor ed)
        {
            var pko = new PromptKeywordOptions("\nExportar 3D Faces? [Nenhum/Volume/TopBottom/Todos] <Nenhum>: ");
            pko.AllowNone = true;
            pko.Keywords.Add("Nenhum");
            pko.Keywords.Add("Volume");    // gera par de malhas casadas (corridor_work x EG_Copy)
            pko.Keywords.Add("TopBottom"); // exporta individualmente as TINs recortadas
            pko.Keywords.Add("Todos");

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(res.StringResult))
                return ExportChoice.Nenhum;

            switch (res.StringResult)
            {
                case "Volume": return ExportChoice.Volume;
                case "TopBottom": return ExportChoice.TopBottom;
                case "Todos": return ExportChoice.Todos;
                default: return ExportChoice.Nenhum;
            }
        }

        private static void Export3DFacesFromTinOrGrid(Editor ed, Transaction tr, BlockTableRecord btr, TinSurface tin, double gridStep)
        {
            try
            {
                // Tenta triângulos nativos
                ExportTinTriangles(tr, btr, tin.Triangles);
                return;
            }
            catch
            {
                ed.WriteMessage("\nColeção de triângulos indisponível — usando fallback por grade...");
                ExportGridAsFaces(tr, btr, tin, gridStep);
            }
        }

        private static void ExportBetweenSurfacesAsFaces(Editor ed, Transaction tr, BlockTableRecord btr, TinSurface top, TinSurface bottom, double gridStep)
        {
            if (gridStep <= 0.0) gridStep = 2.0;

            if (!TryGetIntersectionExtents(top, bottom, out var ext)) return;

            double minX = ext.MinPoint.X, maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y, maxY = ext.MaxPoint.Y;

            for (double x = minX; x < maxX - gridStep; x += gridStep)
            {
                for (double y = minY; y < maxY - gridStep; y += gridStep)
                {
                    var t00 = Sample(top, x, y);
                    var t10 = Sample(top, x + gridStep, y);
                    var t01 = Sample(top, x, y + gridStep);
                    var t11 = Sample(top, x + gridStep, y + gridStep);

                    var b00 = Sample(bottom, x, y);
                    var b10 = Sample(bottom, x + gridStep, y);
                    var b01 = Sample(bottom, x, y + gridStep);
                    var b11 = Sample(bottom, x + gridStep, y + gridStep);

                    if (Invalid(t00, t10, t01, t11) || Invalid(b00, b10, b01, b11))
                        continue;

                    AddFace(tr, btr, t00, t10, t11);
                    AddFace(tr, btr, t00, t11, t01);

                    // invertido para normal oposta
                    AddFace(tr, btr, b00, b11, b10);
                    AddFace(tr, btr, b00, b01, b11);
                }
            }
        }

        private static bool TryGetIntersectionExtents(Surface s1, Surface s2, out Extents3d inter)
        {
            inter = default;
            Extents3d e1, e2;
            try { e1 = s1.GeometricExtents; } catch { return false; }
            try { e2 = s2.GeometricExtents; } catch { return false; }

            double minX = Math.Max(e1.MinPoint.X, e2.MinPoint.X);
            double minY = Math.Max(e1.MinPoint.Y, e2.MinPoint.Y);
            double maxX = Math.Min(e1.MaxPoint.X, e2.MaxPoint.X);
            double maxY = Math.Min(e1.MaxPoint.Y, e2.MaxPoint.Y);

            if (minX >= maxX || minY >= maxY) return false;
            inter = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
            return true;
        }

        private static bool Invalid(params Point3d[] pts)
        {
            foreach (var p in pts) if (double.IsNaN(p.Z)) return true;
            return false;
        }

        private static void SafeRebuild(TinSurface s)
        {
            try { s.Rebuild(); } catch { /* ignora */ }
        }

        private static void ExportTinTriangles(Transaction tr, BlockTableRecord btr, TinSurfaceTriangleCollection triangles)
        {
            int counter = 0;
            foreach (TinSurfaceTriangle tri in triangles)
            {
                Point3d p1 = tri.Vertex1.Location;
                Point3d p2 = tri.Vertex2.Location;
                Point3d p3 = tri.Vertex3.Location;

                using (var face = new Face(p1, p2, p3, p3, true, true, true, true))
                {
                    btr.AppendEntity(face);
                    tr.AddNewlyCreatedDBObject(face, true);
                }
                if ((++counter % 50000) == 0)
                    tr.TransactionManager.QueueForGraphicsFlush();
            }
        }

        private static void ExportGridAsFaces(Transaction tr, BlockTableRecord btr, Surface surface, double gridStep)
        {
            if (gridStep <= 0.0) gridStep = 2.0;

            Extents3d ext;
            try { ext = surface.GeometricExtents; } catch { return; }

            double minX = ext.MinPoint.X, maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y, maxY = ext.MaxPoint.Y;

            for (double x = minX; x < maxX - gridStep; x += gridStep)
            {
                for (double y = minY; y < maxY - gridStep; y += gridStep)
                {
                    var p00 = Sample(surface, x, y);
                    var p10 = Sample(surface, x + gridStep, y);
                    var p01 = Sample(surface, x, y + gridStep);
                    var p11 = Sample(surface, x + gridStep, y + gridStep);

                    if (Invalid(p00, p10, p01, p11)) continue;

                    AddFace(tr, btr, p00, p10, p11);
                    AddFace(tr, btr, p00, p11, p01);
                }
            }
        }

        private static void AddFace(Transaction tr, BlockTableRecord btr, Point3d a, Point3d b, Point3d c)
        {
            using (var f = new Face(a, b, c, c, true, true, true, true))
            {
                btr.AppendEntity(f);
                tr.AddNewlyCreatedDBObject(f, true);
            }
        }

        private static Point3d Sample(Surface s, double x, double y)
        {
            try
            {
                double z = s.FindElevationAtXY(x, y);
                return new Point3d(x, y, z);
            }
            catch
            {
                return new Point3d(x, y, double.NaN);
            }
        }

        // ========================= COLETORES DE OBJETOS =========================

        private static Dictionary<string, ObjectId> GetAllCorridors(CivilDocument cdoc, Transaction tr)
        {
            var dict = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in cdoc.CorridorCollection)
            {
                var c = tr.GetObject(id, OpenMode.ForRead) as Corridor;
                if (c != null && !dict.ContainsKey(c.Name))
                    dict.Add(c.Name, id);
            }
            return dict;
        }

        private static Dictionary<string, ObjectId> GetCorridorSurfaces(Corridor corr, Transaction tr)
        {
            var dict = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);

            // Algumas versões expõem 'CorridorSurfaces' como coleção de ObjectId;
            // noutras, como coleção de objetos; este foreach cobre ObjectId.
            foreach (CorridorSurface sid in corr.CorridorSurfaces)
            {
                var cs = tr.GetObject(sid.SurfaceId, OpenMode.ForRead) as Surface; // CorridorSurface herda Surface
                if (cs != null && !dict.ContainsKey(cs.Name))
                    dict.Add(cs.Name, sid.SurfaceId);
            }
            return dict;
        }

        private static Dictionary<string, ObjectId> GetAllTinSurfaces(CivilDocument cdoc, Transaction tr)
        {
            var dict = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
            {
                var s = tr.GetObject(sid, OpenMode.ForRead) as TinSurface;
                if (s != null)
                {
                    var nm = s.Name;
                    if (!dict.ContainsKey(nm))
                        dict.Add(nm, sid);
                }
            }
            return dict;
        }

        // ========================= PROMPTS =========================

        private static ObjectId PromptCorridorByName(Editor ed, string prompt, Dictionary<string, ObjectId> corridors)
        {
            var names = corridors.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) return ObjectId.Null;
            if (names.Count == 1) return corridors[names[0]];

            var pko = new PromptKeywordOptions($"\n{prompt}:");
            foreach (var n in names) pko.Keywords.Add(n);
            pko.AllowNone = false;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK) return ObjectId.Null;

            return corridors.TryGetValue(res.StringResult, out var id) ? id : ObjectId.Null;
        }

        private static ObjectId PromptCorridorSurfaceByName(Editor ed, string prompt, Dictionary<string, ObjectId> corrSurfaces)
        {
            var names = corrSurfaces.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) return ObjectId.Null;
            if (names.Count == 1) return corrSurfaces[names[0]];

            var pko = new PromptKeywordOptions($"\n{prompt}:");
            foreach (var n in names) pko.Keywords.Add(n);
            pko.AllowNone = false;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK) return ObjectId.Null;

            return corrSurfaces.TryGetValue(res.StringResult, out var id) ? id : ObjectId.Null;
        }

        private static ObjectId PromptSurfaceByName(Editor ed, string prompt, Dictionary<string, ObjectId> surfaces, ObjectId exclude = new ObjectId())
        {
            var names = surfaces.Keys
                                .Where(n => exclude.IsNull || surfaces[n] != exclude)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ToList();

            if (names.Count == 0) return ObjectId.Null;
            if (names.Count == 1) return surfaces[names[0]];

            var pko = new PromptKeywordOptions($"\n{prompt}:");
            foreach (var n in names) pko.Keywords.Add(n);
            pko.AllowNone = false;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK) return ObjectId.Null;

            return surfaces.TryGetValue(res.StringResult, out var id) ? id : ObjectId.Null;
        }

        // ========================= BOUNDARY & OFFSET =========================

        private static void AddOuterBoundaryFromPolyline(TinSurface surf, ObjectId plId, double midOrdinate)
        {
            var ids = new ObjectIdCollection(new[] { plId });
            surf.BoundariesDefinition.AddBoundaries(ids, midOrdinate, Autodesk.Civil.SurfaceBoundaryType.Outer, true);
        }

        private static ObjectId CreateBestOffsetPolyline(Database db, Transaction tr, ObjectId basePolyId, double d)
        {
            var pl = tr.GetObject(basePolyId, OpenMode.ForRead) as Polyline;
            if (pl == null || !pl.Closed)
                throw new InvalidOperationException("A entidade selecionada não é uma Polyline FECHADA.");

            var offPlus = CreateOffsetCopy(db, tr, pl, +d);
            var offMinus = CreateOffsetCopy(db, tr, pl, -d);

            var plusPl = tr.GetObject(offPlus, OpenMode.ForRead) as Polyline;
            var minusPl = tr.GetObject(offMinus, OpenMode.ForRead) as Polyline;

            double aPlus = (plusPl != null && plusPl.Closed) ? SafeArea(plusPl) : -1.0;
            double aMinus = (minusPl != null && minusPl.Closed) ? SafeArea(minusPl) : -1.0;

            if (aPlus >= aMinus && aPlus > 0) return offPlus;
            if (aMinus > 0) return offMinus;

            if (aPlus > 0) return offPlus;
            if (aMinus > 0) return offMinus;

            throw new InvalidOperationException("Falha ao gerar polilinha de offset. Verifique se a polilinha base é válida.");
        }

        private static double SafeArea(Polyline p)
        {
            try { return p.Area; } catch { return -1.0; }
        }

        private static ObjectId CreateOffsetCopy(Database db, Transaction tr, Polyline pl, double d)
        {
            var coll = pl.GetOffsetCurves(d);
            ObjectId resultId = ObjectId.Null;

            using (coll)
            {
                foreach (Entity ent in coll)
                {
                    var plOff = ent as Polyline;
                    if (plOff == null) { ent.Dispose(); continue; }

                    if (!plOff.Closed && plOff.GetPoint3dAt(0).DistanceTo(plOff.GetPoint3dAt(plOff.NumberOfVertices - 1)) < 1e-6)
                        plOff.Closed = true;

                    var btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    resultId = btr.AppendEntity(plOff);
                    tr.AddNewlyCreatedDBObject(plOff, true);
                    break;
                }
            }
            if (resultId.IsNull)
                throw new InvalidOperationException("Não foi possível criar o offset da polilinha.");

            return resultId;
        }

        // ========================= PROMPTS GENÉRICOS =========================

        private static ObjectId PromptClosedPolyline(Editor ed, Transaction tr, string msg)
        {
            var peo = new PromptEntityOptions($"\n{msg}:");
            peo.SetRejectMessage("\nSelecione uma Polyline 2D fechada.");
            peo.AddAllowedClass(typeof(Polyline), exactMatch: false);

            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return ObjectId.Null;

            var pl = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;
            if (pl == null || !pl.Closed)
            {
                ed.WriteMessage("\nA entidade não é uma Polyline FECHADA.");
                return ObjectId.Null;
            }
            return per.ObjectId;
        }

        private static double PromptDouble(Editor ed, string msg, double defVal)
        {
            var pdo = new PromptDoubleOptions($"\n{msg} <{defVal}>");
            pdo.DefaultValue = defVal;
            pdo.AllowNone = true;
            pdo.AllowZero = false;
            pdo.AllowNegative = false;

            var pdr = ed.GetDouble(pdo);
            return (pdr.Status == PromptStatus.OK) ? pdr.Value : defVal;
        }

        private static string MakeUniqueName(CivilDocument cdoc, string baseName)
        {
            string name = baseName;
            int i = 1;
            var existing = new HashSet<string>(GetAllSurfaceNames(cdoc), StringComparer.OrdinalIgnoreCase);
            while (existing.Contains(name))
                name = $"{baseName}_{i++}";
            return name;
        }

        private static IEnumerable<string> GetAllSurfaceNames(CivilDocument cdoc)
        {
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
            {
                using (var tr = sid.Database.TransactionManager.StartTransaction())
                {
                    var s = tr.GetObject(sid, OpenMode.ForRead) as Surface;
                    if (s != null) yield return s.Name;
                    tr.Commit();
                }
            }
        }
    }
}
