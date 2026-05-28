using Autodesk.Aec.Modeler;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.Drawing;
using System.Runtime.InteropServices;
using Surface = Autodesk.Civil.DatabaseServices.Surface;



namespace AutomacoesCivil3D
{
    public class Main
    {
        [CommandMethod("EscavacaoDrenagem")]
        public static void EscavacaoDrenagem()
        {

            //Carrega os domumentos

            CivilDocument DocCivil = Manager.DocCivil;
            Database DocData = Manager.DocData;
            Editor DocEditor = Manager.DocEditor;

            // Inicia uma transação 
            using Transaction TransCad = DocData.TransactionManager.StartTransaction();

            // Solicita ao usuário selecionar as estruturas inicial e final
            PromptEntityOptions options = new PromptEntityOptions("\nSelecione a Primeira Estrutura");
            options.SetRejectMessage("\nO objeto selecionado não é uma estrutura.");
            options.AddAllowedClass(typeof(Structure), true);
            PromptEntityResult result = DocEditor.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                DocEditor.WriteMessage("\nSeleção cancelada.");
                return;
            }

            PromptEntityOptions options2 = new PromptEntityOptions("\nSelecione a Última Estrutura:");
            options2.SetRejectMessage("\nO objeto selecionado não é uma estrutura.");
            options2.AddAllowedClass(typeof(Structure), true);
            PromptEntityResult result2 = DocEditor.GetEntity(options2);
            if (result2.Status != PromptStatus.OK)
            {
                DocEditor.WriteMessage("\nSeleção cancelada.");
                return;
            }

            // Tenta abrir o objeto selecionado como Structure
            Structure Primeira = (Structure)TransCad.GetObject(result.ObjectId, OpenMode.ForRead);
            Structure Ultima = (Structure)TransCad.GetObject(result2.ObjectId, OpenMode.ForRead);

            Network network = (Network)TransCad.GetObject(Primeira.NetworkId, OpenMode.ForRead);

            List<Point3d> point = new List<Point3d>();
            ObjectId EstruturaAlvo = Primeira.Id;
            ObjectId EstruturaAnteriorId = Primeira.Id;
            ObjectId tuboMemoId = ObjectId.Null;



            // Loop para coletar pontos
            while (EstruturaAlvo != Ultima.Id)
            {
                bool encontrouTubo = false;
                

                foreach (ObjectId tuboId in network.GetPipeIds())
                {
                   
                    Pipe tubo = (Pipe)TransCad.GetObject(tuboId, OpenMode.ForRead);
                    

                    if (tubo.StartStructureId == EstruturaAlvo) 
                    { 
                        
                        Structure st = (Structure)EstruturaAlvo.GetObject(OpenMode.ForRead);

                        if (!tuboMemoId.IsNull)
                        {
                            Pipe tuboMemo = (Pipe)TransCad.GetObject(tuboMemoId, OpenMode.ForRead);
                            

                            if (tuboMemo.EndPoint.X > tubo.StartPoint.X)
                            {
                                
                                
                                Point3d pontoIntEstrutura = CalculatePipeAngle(tuboMemo.EndPoint, tubo.StartPoint, 0.6);
                                if (!point.Contains(pontoIntEstrutura))
                                    point.Add(pontoIntEstrutura);
                            }
                            else 
                            {
                                Point3d pontoIntEstrutura = CalculatePipeAngle(tuboMemo.EndPoint, tubo.StartPoint, 0.6);
                                if (!point.Contains(pontoIntEstrutura))
                                    point.Add(pontoIntEstrutura);
                            }
                           
                            DocEditor.WriteMessage("\nEntrar ele entrou, so quero ver o ponto agora!!!!");
                            DocEditor.WriteMessage($"\nTubo atual {tubo.Name} Tubo anterior {tuboMemo.Name}");
                            

                        }

                        Point3d pontoCentralEstrutura = new Point3d(st.Location.X, st.Location.Y, st.SumpElevation);
                        if (!point.Contains(pontoCentralEstrutura))
                            point.Add(pontoCentralEstrutura);


                        Point3d pontoSaidaEstrutura = new Point3d(tubo.StartPoint.X + 0.01, tubo.StartPoint.Y + 0.01, st.SumpElevation);
                        if (!point.Contains(pontoSaidaEstrutura))
                            point.Add(pontoSaidaEstrutura);

                        if (!point.Contains(tubo.StartPoint));
                            point.Add(tubo.StartPoint);

                        if (!point.Contains(tubo.EndPoint))
                            point.Add(tubo.EndPoint);

                        Point3d pontoEntradaEstrutura = new Point3d(tubo.EndPoint.X - 0.01, tubo.EndPoint.Y - 0.01, st.SumpElevation);
                        if (!point.Contains(pontoEntradaEstrutura))
                            point.Add(pontoEntradaEstrutura);


                        





                        //if (!point.Contains(st.Location))
                        //point.Add(st.Location);

                        

                       
                        EstruturaAlvo = tubo.EndStructureId;
                        tuboMemoId = tubo.Id;
                        encontrouTubo = true;
                        break;
                    }

                }

                

                /*foreach (ObjectId obj in network.GetStructureIds())
                {

                    Structure structure = (Structure)TransCad.GetObject(obj, OpenMode.ForWrite);

                    if (structure.Id == EstruturaAlvo)
                    {
                        point.Add(structure.Position);
                        structure.KnownFlow = structure.CatchmentsArea*8.2/1000;
                        
                    }

                }*/

                



                

                if (!encontrouTubo)
                {
                    DocEditor.WriteMessage("\nErro: Não foi possível encontrar um tubo conectado à estrutura-alvo.");
                    break;
                }
            }

            // Ordena os pontos
            Point3dCollection uniquePoints = CreateUniquePointCollection(point);
            Point3dCollection points = OrderPointsByProximity(uniquePoints);

            // Cria a Polyline3d
            ObjectId polylineId;
            using (Polyline3d polyline = new Polyline3d(Poly3dType.SimplePoly, uniquePoints, false))
            {
                BlockTable blockTable = (BlockTable)TransCad.GetObject(DocData.BlockTableId, OpenMode.ForRead);
                BlockTableRecord currentSpace = (BlockTableRecord)TransCad.GetObject(DocData.CurrentSpaceId, OpenMode.ForWrite);

                currentSpace.AppendEntity(polyline);
                TransCad.AddNewlyCreatedDBObject(polyline, true);

                polylineId = polyline.ObjectId;
            }

            /*DoubleCollection elevations = new DoubleCollection();
            elevations.Add(100);

            List<Point2d> point2d = point.ConvertAll(p => new Point2d(p.X, p.Y));


            using (Polyline polyline2d = new Polyline())
            {

                int index = 0;

                foreach(Point2d ponto2d in point2d)
                {
                    polyline2d.AddVertexAt(index, ponto2d, 0, 0, 0);
                    index++;
                }

                BlockTable blockTable = (BlockTable)TransCad.GetObject(DocData.BlockTableId, OpenMode.ForRead);
                BlockTableRecord currentSpace = (BlockTableRecord)TransCad.GetObject(DocData.CurrentSpaceId, OpenMode.ForWrite);
                

                currentSpace.AppendEntity(polyline2d);
                TransCad.AddNewlyCreatedDBObject(polyline2d, true);

                
            }

            PolylineOptions polylineOptions = new PolylineOptions();
            polylineOptions.PlineId = polylineId;

            //ObjectId alignment = Alignment.Create(DocCivil, polylineOptions, "TESTE", "PROJETADO","0" , "EIXO_PRINC_", "_No Label");*/

            



            // Cria a FeatureLine
            ObjectId siteId = SelecionarSite();
            ObjectId featureLineId = FeatureLine.Create("FeatureEscavação", polylineId);
            FeatureLine featureLine = (FeatureLine)TransCad.GetObject(featureLineId, OpenMode.ForWrite);
            featureLine.Name = Primeira.Name + Ultima.Name;
            /*--------------------------------------------------------------------------------------------------------------------------------------------------------*/
            ObjectId assemblyid =  ObjectId.Null;
            CorridorCollection corridors = DocCivil.CorridorCollection;
            
            foreach(ObjectId assemblyId in DocCivil.AssemblyCollection)
            {
                Assembly assembly = (Assembly)TransCad.GetObject(assemblyId, OpenMode.ForRead);

                
                if (assembly.Name == "EscavaçãoDrenagem")
                {
                    assemblyid = assemblyId;
                }
            }

            ObjectId corridor = corridors.Add("corridorName","Baseline Name", featureLine.Id, "baselineRegionName", assemblyid);
            Corridor corredor = (Corridor)TransCad.GetObject(corridor, OpenMode.ForWrite);
           

            
            SetSurfaceTarget(GetSurfaceId(DocCivil, "PÁTIO_INTERNO"), corredor);

            
                foreach (ObjectId surfaceId in DocCivil.GetSurfaceIds())
            {
                TinSurface surface = (TinSurface)TransCad.GetObject(surfaceId, OpenMode.ForRead);
                if (surface.Name == "PÁTIO_INTERNO")
                {
                    //DocEditor.WriteMessage($"\nNome da superficie {surface.Name}");

                   

                    foreach (Baseline baseline in corredor.Baselines)
                    {


                        DocEditor.WriteMessage($"\nNome da Baseline {baseline.Name}");
                        foreach (BaselineRegion region in baseline.BaselineRegions)
                        {
                            
                                
                            DocEditor.WriteMessage($"\nNome da regiao {region.Name}");
                            

                            DocEditor.WriteMessage($"\nEstaca Inicial {region.StartStation}");
                            DocEditor.WriteMessage($"\nEstaca Final {region.EndStation}");


                            

                            for (double i = region.StartStation+.1; i <= region.EndStation-.1; i += 0.1)
                            {
                                // Nome da estaca como string formatada
                                string stationName = $"{Math.Round(i, 2)}";
                                // Adiciona a estaca à região
                                region.AddStation(i, stationName);
                            }

                            corredor.Rebuild();
                            
                        }
                    }                            
                }
            }


            Polyline3d polyline3d = (Polyline3d)TransCad.GetObject(polylineId, OpenMode.ForWrite);
            polyline3d.Erase();

            TransCad.Commit();
        }

        /*--------------------------------------------------------------------------------------------------------------------------------------------------------*/

        private static Point3d CalculatePipeAngle(Point3d p1, Point3d p2, double height)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;

            double length = Math.Sqrt(dx * dx + dy * dy);
            double angulo = Math.Atan2(dx, dy);
            double normX = dx * Math.Sin(angulo);
            double normY = dy * Math.Sin(angulo);

            

            // Gera um ponto perpendicular baseado na altura desejada
            double perpX = p1.X + normY;
            double perpY = p1.Y - normX;
          
            return new Point3d(perpX, perpY, p1.Z);
        }

        public static double ConvertToRadians(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }


        public static Point3dCollection OrderPointsByProximity(Point3dCollection points)
        {
            List<Point3d> unorderedPoints = new List<Point3d>();
            foreach (Point3d point in points)
            {
                unorderedPoints.Add(point);
            }

            List<Point3d> orderedPoints = new List<Point3d>();
            Point3d currentPoint = unorderedPoints[0]; // Comece pelo primeiro ponto
            orderedPoints.Add(currentPoint);
            unorderedPoints.Remove(currentPoint);

            while (unorderedPoints.Count > 0)
            {
                // Encontre o ponto mais próximo
                Point3d closestPoint = FindClosestPoint(currentPoint, unorderedPoints);
                orderedPoints.Add(closestPoint);
                unorderedPoints.Remove(closestPoint);
                currentPoint = closestPoint;
            }

            // Converta de volta para Point3dCollection
            Point3dCollection orderedCollection = new Point3dCollection();
            foreach (Point3d point in orderedPoints)
            {
                orderedCollection.Add(point);
            }

            return orderedCollection;
        }

        private static Point3d FindClosestPoint(Point3d currentPoint, List<Point3d> points)
        {
            Point3d closestPoint = points[0];
            double closestDistance = currentPoint.DistanceTo(closestPoint);

            foreach (Point3d point in points)
            {
                double distance = currentPoint.DistanceTo(point);
                if (distance < closestDistance)
                {
                    closestPoint = point;
                    closestDistance = distance;
                }
            }

            return closestPoint;
        }


        public static Point3dCollection CreateUniquePointCollection(IEnumerable<Point3d> inputPoints)
        {
            HashSet<Point3d> uniquePoints = new HashSet<Point3d>();
            Point3dCollection pointCollection = new Point3dCollection();

            foreach (Point3d point in inputPoints)
            {
                if (!uniquePoints.Contains(point))
                {
                    uniquePoints.Add(point);
                    pointCollection.Add(point);
                }
            }

            return pointCollection;
        }

        public static ObjectId SelecionarSite()
        {
            CivilDocument DocCivil = Manager.DocCivil;
            Database DocData = Manager.DocData;
            Editor DocEditor = Manager.DocEditor;
            ObjectId Id = ObjectId.Null;

            foreach (ObjectId siteId in DocCivil.GetSiteIds())
            {
                Site siteProjetado = (Site)siteId.GetObject(OpenMode.ForRead);
                if (siteProjetado.Name == "PROJETADO")
                {
                    Id = siteId;
                }
            }
            
            return Id;
        }


       
        public static ObjectId GetSurfaceId(CivilDocument civilDoc, string surfaceName)
        {
            // Localiza a superfície pelo nome
            foreach (ObjectId surfaceId in civilDoc.GetSurfaceIds())
            {
                Surface surface = (Surface)surfaceId.GetObject(OpenMode.ForRead);
                if (surface != null && surface.Name == surfaceName)
                {
                    return surfaceId;
                }
            }
            return ObjectId.Null;
        }


        public static SubassemblyTargetInfoCollection SetSurfaceTarget(ObjectId surfaceId, Corridor corredor)
        {
            Editor DocEditor = Manager.DocEditor;
            // Obter a coleção de alvos da região

            
            SubassemblyTargetInfoCollection targets = corredor.GetTargets();
            try
            {
                
                // Iterar sobre cada alvo da subassembly
                foreach (SubassemblyTargetInfo target in targets)
                {
                    // Garantir que o alvo seja do tipo esperado (superfície)
                    if (target.TargetType == SubassemblyLogicalNameType.Surface)
                    {
                        // Atualizar os IDs de alvos com o novo surfaceId
                        /*target.TargetIds.Clear(); // Limpa alvos anteriores*/
                        target.TargetIds.Add(surfaceId); // Adiciona o novo alvo
                        corredor.SetTargets(targets);
                        corredor.Rebuild();
                        //DocEditor.WriteMessage("\nO DIABO DO TREM PASSOU POR AQUI");
                    }
                    
                }

               

                
                
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                DocEditor.WriteMessage($"\nErro ao configurar o alvo de superfície: {ex.Message}");
                
            }

            return targets;


        }


        private ObjectId GetAssemblyByName(CivilDocument civilDoc, Transaction trans, string assemblyName)
        {
            foreach (ObjectId assemblyId in civilDoc.AssemblyCollection)
            {
                Assembly assembly = (Assembly)trans.GetObject(assemblyId, OpenMode.ForRead);
                if (assembly != null && assembly.Name == assemblyName)
                {
                    return assemblyId;
                }
            }
            return ObjectId.Null;
        }

        public static Point3d PontoInterpolado(Point3d p1, Point3d p2, double t)
        {
            double x = p1.X + t * (p2.X - p1.X);
            double y = p1.Y + t * (p2.Y - p1.Y);
            double z = p1.Z;

            return new Point3d(x, y, z);
        }

    }
}



