using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using EscavacaoDRE;
using System.Globalization;
using System.IO;
using System.Text;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ObjectIdCollection = Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Site = Autodesk.Civil.DatabaseServices.Site;

namespace AutomacoesCivil3D
{


    public class SurfaceData
    {
        public TinSurface Superficie { get; set; }
        public List<Polyline> Polylines { get; set; }

        public List<List<Point3d>> PontosVertices { get; set; }

        public string Nomes { get; set; }

        public SurfaceData(TinSurface superficie)
        {
            Superficie = superficie;
            Polylines = new List<Polyline>();
            PontosVertices = new List<List<Point3d>>();
            Nomes = "";
        }
    
    }


    public class VolumeSurfaceData
    {
        public string SurfaceId { get; set; }
        public Point3dCollection Points { get; set; }

        public VolumeSurfaceData(string surfaceId, Point3dCollection points)
        {
            SurfaceId = surfaceId;
            Points = points;
        }
    }








    public class RotinaSuperficieEscavacaoDRE
    {
        const double OFFSET_TOPO = 0;
        const double OFFSET_BASE = 0;
        const int LADOS_CIRCULO = 24;
        Polyline polyTopo = new Polyline();
        List<Polyline> Polylines = new List<Polyline>();
        List<Point3dCollection> valaAte1_5 = new List<Point3dCollection>();
        Point3dCollection valaDe1_5A3 = new Point3dCollection();
        Point3dCollection valaAte_3 = new Point3dCollection();
        string compacManual = "";
        int cont = 0;
        List<Point3d> refPolyline = new List<Point3d>();
        List<Point3d> verticesTopo = new List<Point3d>();

        // Criação do dicionário
        Dictionary<ObjectId, SurfaceData> superficiesDict = new Dictionary<ObjectId, SurfaceData>();

        [CommandMethod("CriarEscavacaoDRE")]
        public void CriarFeatureLinePorRede()
        {
            Document docCad = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            CivilDocument docCivil = CivilApplication.ActiveDocument;
            Editor docEditor = docCad.Editor;
            Database docData = docCad.Database;

            //Chama o Formulario de Estilos
            InterfaceRotinaEscavacao Janela = new InterfaceRotinaEscavacao();
            //Abre o Formulario
            Janela.ShowDialog();
            


            // Seleção da rede de drenagem
            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione Qualquer Tubo da rede de drenagem (PipeNetwork):");
            peo.SetRejectMessage("\nPor favor, selecione apenas Tubos da Rede.");
            peo.AddAllowedClass(typeof(Pipe), false);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nNão foi possível selecionar uma rede de drenagem.");
                return;
            }
            if (Janela.OK)
            {
                using (Transaction tr = docData.TransactionManager.StartTransaction())
                {
                    try
                    {
                        Pipe tuboSelect = (Pipe)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        ObjectId networkId = tuboSelect.NetworkId;
                        Network net = (Network)tr.GetObject(networkId, OpenMode.ForRead);
                        ObjectId surfaceBaseId = net.ReferenceSurfaceId;
                        TinSurface superficieBase = (TinSurface)surfaceBaseId.GetObject(OpenMode.ForRead);
                        List<Point2d> points = new List<Point2d>();
                        string nomeTopo = "";

                        double espConc = Janela.EspessuraConcreto;
                        double espCompc = Janela.EspessuraCompact;

                        // Cria e obtém sites exclusivos para as features de escavação
                        ObjectId siteTopoId = GarantirSiteCivil(tr, docCivil, "ESCAVAÇÃO REDES DRENAGEM");
                        ObjectId siteTopoCaixaId = siteTopoId;
                        ObjectId siteFundoId = siteTopoId;
                        ObjectId siteFundoCaixaId = siteTopoId;
                        ObjectId siteConcretoMagroId = siteTopoId;
                        ObjectId sitecCompactacaoManualId = siteTopoId;
                        ObjectId sitecCompactacaoMecanicalId = siteTopoId;
                        ObjectId siteConcretoMagroCaixaId = siteTopoId;
                        ObjectId sitecCompactacaoManualCaixaId = siteTopoId;
                        ObjectId sitecCompactacaoMecanicaCaixaId = siteTopoId;

                        // Selecionar o estilo de superfície utilizado
                        ObjectId surfaceStyleId = ObterStyleId(docCivil, tr, "TRI_PTO_BRD");
                        ObjectId surfaceStyleInvId = ObterStyleId(docCivil, tr, "INVISIVEL");

                        /*ObjectId surfaceId = TinSurface.Create($"01-ESC TUBOS {net.Name}",
                            ObterStyleId(docCivil, tr, "02-SUPERFÍCIE ESCAVAÇÃO TUBOS DRENAGEM"));
                        TinSurface superficie = (TinSurface)surfaceId.GetObject(OpenMode.ForRead);
                        
                        superficie.AutoRebuild = true;*/

                        ObjectId surfaceCaixasId = TinSurface.Create($"01-ESC CAIXAS {net.Name}",
                            ObterStyleId(docCivil, tr, "03-SUPERFICIE CAIXAS DRENAGEM"));
                        TinSurface superficieCaixas = (TinSurface)surfaceCaixasId.GetObject(OpenMode.ForRead);
                        superficieCaixas.AutoRebuild = true;

                        /*ObjectId surfaceConcretoId = TinSurface.Create($"03-EMBASAMENTO CAIXAS {net.Name}",
                            ObterStyleId(docCivil, tr, "04-SUPERFÍCIE EMBASAMENTO DRENAGEM"));
                        TinSurface superficieConcreto = (TinSurface)surfaceConcretoId.GetObject(OpenMode.ForRead);
                        superficieConcreto.AutoRebuild = true;*/

                        /*ObjectId surfaceConcretoTuboId = TinSurface.Create($"03-EMBASAMENTO TUBOS {net.Name}",
                            ObterStyleId(docCivil, tr, "04-SUPERFÍCIE EMBASAMENTO DRENAGEM"));
                        TinSurface superficieConcretoTubo = (TinSurface)surfaceConcretoTuboId.GetObject(OpenMode.ForRead);
                        superficieConcreto.AutoRebuild = true;

                        ObjectId surfaceCompactacaoManualId = TinSurface.Create($"02-COMP MANUAL CAIXA {net.Name}",
                            ObterStyleId(docCivil, tr, "03-SUPERFÍCIE COMPACTAÇÃO MANUAL DRENAGEM"));
                        TinSurface superficieCompactacaoManual = (TinSurface)surfaceCompactacaoManualId.GetObject(OpenMode.ForRead);
                        superficieCompactacaoManual.AutoRebuild = true;
                        compacManual = superficieCompactacaoManual.Name;



                        ObjectId surfaceCompactacaoManualTuboId = TinSurface.Create($"02-COMP MANUAL TUBO {net.Name}",
                            ObterStyleId(docCivil, tr, "03-SUPERFÍCIE COMPACTAÇÃO MANUAL DRENAGEM"));
                        TinSurface superficieCompactacaoManualTubo = (TinSurface)surfaceCompactacaoManualTuboId.GetObject(OpenMode.ForRead);
                        superficieCompactacaoManualTubo.AutoRebuild = true;*/


                        List<ObjectId> estruturasId = new List<ObjectId>();



                        // ==== Estruturas ====
                        foreach (ObjectId structureId in net.GetStructureIds())
                        {
                            Structure structure = (Structure)tr.GetObject(structureId, OpenMode.ForRead);
                            estruturasId.Add(structureId);
                            Point3d centro = structure.Location;
                            centro = new Point3d(centro.X, centro.Y, structure.SurfaceElevationAtInsertionPoint);
                            // Ajustar centro para Boca de Lobo, se necessário
                            //if (structure.PartDescription != null && structure.PartDescription.ToUpper().Contains("BOCA BSTC"))
                            //{
                                centro = CalcularCentroGeometricoBocaDeLobo(structure);
                            //}

                            if (structure.BoundingShape == BoundingShapeType.Box)
                            {
                                double profundidade = structure.Height; //structure.RimToSumpHeight + structure.FloorThickness + espConc;
                                //double profundidadeConcreto = structure.RimToSumpHeight + structure.FloorThickness;
                                //double profundidadeReaterroManual = structure.SurfaceElevationAtInsertionPoint;



                                if (profundidade > 1.25 && profundidade < 1.75)
                                {
                                    double profundidadeMeio = profundidade - 1.25;                                  
                                    double elevacaoSuperficie = structure.SurfaceElevationAtInsertionPoint;
                                    double elevacaoMeio = structure.SurfaceElevationAtInsertionPoint - profundidadeMeio;

                                    double offsetMeio = elevacaoSuperficie - elevacaoMeio;
                                    var verticesMeioCaixa = VerticesCaixas(structure, OFFSET_BASE + 0.01, profundidadeMeio, centro, superficieCaixas, nomeTopo);
                                    nomeTopo = "TOPO";
                                    var verticesTopoCaixa = VerticesCaixas(structure, OFFSET_TOPO + offsetMeio/2, 0, centro, superficieCaixas, nomeTopo);

                                    

                                    //AdionaVerticesNaSuperficie(verticesMeioCaixa, $"Caixa {structure.Name} Meio", siteTopoCaixaId, superficieCaixas);
                                    //AdionaVerticesNaSuperficie(verticesTopoCaixa, $"Caixa {structure.Name} Top", siteTopoCaixaId, superficieCaixas);
                                    //AdionaVerticesNaSuperficie(verticesTopoCaixa, $"Caixa {structure.Name} Top", siteTopoCaixaId, superficieCompactacaoManual);
                                    verticesTopo = verticesTopoCaixa;
                                    nomeTopo = "";

                                }
                                else
                                {
                                    nomeTopo = "TOPO";
                                    verticesTopo = VerticesCaixas(structure, OFFSET_TOPO, 0, centro, superficieCaixas, nomeTopo);
                                    //AdionaVerticesNaSuperficie(verticesTopo, $"Caixa {structure.Name} Top", siteTopoCaixaId, superficieCaixas);
                                    nomeTopo = "";
                                }

                                


                                var verticesFundo = VerticesCaixas(structure, OFFSET_BASE, profundidade, centro, superficieCaixas, nomeTopo);
                                /*var verticesConcreto = VerticesCaixas(structure, OFFSET_BASE, profundidadeConcreto, centro, superficieConcreto, nomeTopo);
                                var verticesReaterroManual = VerticesCaixas(structure, 0.0, 0.0, centro, superficieCompactacaoManual, nomeTopo);
                                var verticesReaterroManualFundo = VerticesCaixas(structure, 0.0, structure.Height, centro, superficieCompactacaoManual, nomeTopo);
                                */
                                AdionaVerticesNaSuperficie(verticesFundo, $"Caixa {structure.Name} Fundo", siteFundoCaixaId, superficieCaixas);

                               /* AdionaVerticesNaSuperficie(verticesConcreto, $"Caixa {structure.Name} Concreto",
                                        siteConcretoMagroCaixaId, superficieConcreto);

                                AdionaVerticesNaSuperficie(verticesReaterroManual, $"Caixa {structure.Name} ReaterroManual",
                                    sitecCompactacaoManualCaixaId, superficieCompactacaoManual);

                                AdionaVerticesNaSuperficie(verticesTopo, $"Caixa {structure.Name} ReaterroManual",
                                    sitecCompactacaoManualCaixaId, superficieCompactacaoManual);

                                if (!structure.PartFamilyName.Contains("BOCA DE LOBO"))
                                {
                                    Plane pl1 = new Plane();
                                    TryDeleteEdge(superficieCompactacaoManual, structure.Location.Convert2d(pl1), docEditor);
                                }
                                else
                                {
                                    Plane pl = new Plane();
                                    TryDeleteEdge(superficieCompactacaoManual, CalcularCentroGeometricoBocaDeLobo(structure).Convert2d(pl), docEditor);

                                }


                                */




                            }
                            else if (structure.BoundingShape == BoundingShapeType.Cylinder)
                            {
                                double profundidadeReaterroManual = structure.SurfaceElevationAtInsertionPoint;
                                double profundidade = structure.Height + espConc;
                                double profundidadeConcreto = structure.Height;
                                double raioTopo = structure.DiameterOrWidth / 2 + OFFSET_TOPO;
                                double raioFundo = structure.DiameterOrWidth / 2 + OFFSET_BASE;
                                double raioEstrutura = structure.DiameterOrWidth / 2;
                                
                               // if(structure.PartSizeName.Contains("CURVA") || structure.PartSizeName.Contains("COLETOR") || structure.PartSizeName.Contains("TÊ"))
                                        //profundidade = profundidade + (centro.Z - structure.Location.Z);


                                
                                



                                if (profundidade > 1.25 && profundidade < 1.75)
                                {
                                    double profundidadeMeio = profundidade - 1.25;
                                    double elevacaoSuperficie = structure.SurfaceElevationAtInsertionPoint;
                                    double elevacaoMeio = elevacaoSuperficie - profundidadeMeio;                                   
                                    double offsetMeio = elevacaoSuperficie - elevacaoMeio;
                                    

                                    

                                    nomeTopo = "TOPO";
                                    List<Point3d> circuloTopo = VerticesCaixasCirculares(centro, raioTopo + offsetMeio / 2, 0, superficieCaixas, nomeTopo);
                                    nomeTopo = "";
                                    List<Point3d> verticesMeioCaixa = VerticesCaixasCirculares(centro - new Vector3d(0, 0, profundidadeMeio), raioFundo + 0.01, 0, superficieCaixas, nomeTopo);
                                    
                                    AdionaVerticesNaSuperficie(verticesMeioCaixa, $"Caixa {structure.Name} Meio", siteTopoCaixaId, superficieCaixas);
                                    AdionaVerticesNaSuperficie(circuloTopo, $"Caixa {structure.Name} Top", siteTopoCaixaId, superficieCaixas);
                                    //AdionaVerticesNaSuperficie(circuloTopo, $"Caixa {structure.Name} Topo", siteTopoCaixaId, superficieCompactacaoManual);

                                }
                                else
                                {
                                    nomeTopo = "TOPO";
                                    List<Point3d> circuloTopo = VerticesCaixasCirculares(centro, raioTopo, 0, superficieCaixas, nomeTopo);
                                    nomeTopo = "";
                                    AdionaVerticesNaSuperficie(circuloTopo, $"Caixa {structure.Name} Top", siteTopoCaixaId, superficieCaixas);
                                    //AdionaVerticesNaSuperficie(circuloTopo, $"Caixa {structure.Name} Topo", siteTopoCaixaId, superficieCompactacaoManual);
                                }


                                List<Point3d> circuloFundo = VerticesCaixasCirculares(centro - new Vector3d(0, 0, profundidade), raioFundo, 0, superficieCaixas, nomeTopo);
                               // List<Point3d> circuloConcreto = VerticesCaixasCirculares(centro - new Vector3d(0, 0, profundidade - espConc), raioFundo, 0, superficieConcreto, nomeTopo);
                               // List<Point3d> circuloCompacManualInt = VerticesCaixasCirculares(centro, raioEstrutura, 0, superficieCompactacaoManual, nomeTopo);



                                AdionaVerticesNaSuperficie(circuloFundo, $"Caixa {structure.Name} Fundo", siteFundoCaixaId, superficieCaixas);                        
                               // AdionaVerticesNaSuperficie(circuloCompacManualInt, $"Caixa {structure.Name} ReaterroManualInt", siteFundoCaixaId, superficieCompactacaoManual);
                               // AdionaVerticesNaSuperficie(circuloConcreto, $"Caixa {structure.Name} Concreto", siteFundoCaixaId, superficieConcreto);
                                Plane pl1 = new Plane();
                               // TryDeleteEdge(superficieCompactacaoManual, structure.Location.Convert2d(pl1), docEditor);

                            }
                            else if (structure.BoundingShape == BoundingShapeType.Undefined)
                            {
                                double profundidade = structure.DiameterOrWidth + espConc;
                                double profundidadeConcreto = structure.DiameterOrWidth;
                                double raioTopo = structure.DiameterOrWidth / 2 + OFFSET_TOPO;
                                double raioFundo = structure.DiameterOrWidth / 2 + OFFSET_BASE;
                                nomeTopo = "TOPO";
                                List<Point3d> circuloTopo = VerticesCaixasCirculares(centro, raioTopo, 0, superficieCaixas, nomeTopo);
                                nomeTopo = "";
                                List<Point3d> circuloFundo = VerticesCaixasCirculares(centro - new Vector3d(0, 0, profundidade), raioFundo, 0, 
                                    superficieCaixas, nomeTopo);

                                AdionaVerticesNaSuperficie(circuloTopo, $"Caixa {structure.Name} Top", siteTopoCaixaId, superficieCaixas);
                                AdionaVerticesNaSuperficie(circuloFundo, $"Caixa {structure.Name} Fundo", siteFundoCaixaId, superficieCaixas);

                            }

                            
                        }

                        double volume = 0;
                        /*/ ==== Tubos ====
                        foreach (ObjectId pipeId in net.GetPipeIds())
                        {
                            Pipe tubo = (Pipe)tr.GetObject(pipeId, OpenMode.ForRead);
                            double afastamento = CalcularLarguraVala(tubo.OuterDiameterOrWidth);
                            
                            Structure endStructure = (Structure)tubo.EndStructureId.GetObject(OpenMode.ForRead);
                            Structure startStructure = (Structure)tubo.StartStructureId.GetObject(OpenMode.ForRead);

                            double startDiametroOrWidth = startStructure.DiameterOrWidth;
                            double endDiametroOrWidth = endStructure.DiameterOrWidth;

                            double offsetLongStart = startStructure.BoundingShape == BoundingShapeType.Box ? startDiametroOrWidth / 2 - 0.3 :
                                startStructure.BoundingShape == BoundingShapeType.Cylinder && 
                                !startStructure.PartSizeName.Contains("CURVA") ? startDiametroOrWidth / 2 + 0.3 :

                                startStructure.BoundingShape == BoundingShapeType.Cylinder && 
                                startStructure.PartSizeName.Contains("CURVA") ? startDiametroOrWidth / 2 + 0.2 :

                                                     startStructure.BoundingShape != BoundingShapeType.Box &&
                                                     startStructure.PartSizeName.Contains("CURVA") ? 0.25 : 0.1;

                            double offsetLongEnd = endStructure.BoundingShape == BoundingShapeType.Box ? endDiametroOrWidth / 2 - 0.3 :

                                endStructure.BoundingShape == BoundingShapeType.Cylinder &&
                                endStructure.PartSizeName.Contains("CURVA") ? endDiametroOrWidth / 2 + 0.2 : 

                                endStructure.BoundingShape == BoundingShapeType.Cylinder && 
                                !endStructure.PartSizeName.Contains("CURVA") ? endDiametroOrWidth / 2 + 0.3 :

                                                   endStructure.BoundingShape != BoundingShapeType.Box && 
                                                   endStructure.PartSizeName.Contains("CURVA") ? 0.2 : 0.1;

                            double startX = tubo.StartPoint.X, startY = tubo.StartPoint.Y, startZ = tubo.StartPoint.Z;
                            double endX = tubo.EndPoint.X, endY = tubo.EndPoint.Y, endZ = tubo.EndPoint.Z;

                            double profundidadeInicio = superficieBase.FindElevationAtXY(startX, startY) - (startZ + tubo.OuterDiameterOrWidth / 2 + espConc);
                            double profundidadeFim = superficieBase.FindElevationAtXY(endX, endY) - endZ - (tubo.OuterDiameterOrWidth / 2 + espConc);
                            double profundidade = (profundidadeInicio + profundidadeFim) / 2;


                            if (profundidade < 1.25 || profundidade > 1.75)
                            {
                                double elevacaoInicio = startZ - tubo.OuterDiameterOrWidth / 2 - espConc;
                                double elevacaoFinal = endZ - tubo.OuterDiameterOrWidth / 2 - espConc;
                                double elevacaoInicioConcreto = startZ - tubo.OuterDiameterOrWidth / 2;
                                double elevacaoFinalConcreto = endZ - tubo.OuterDiameterOrWidth / 2;
                                double elevacaoInicioManual = startZ + tubo.OuterDiameterOrWidth / 2 + espCompc;
                                double elevacaoFinalManual = endZ + tubo.OuterDiameterOrWidth / 2 + espCompc;
                                
                               
                                
                               
                                VerticesTubos(tubo, $"{tubo.Name} Fundo", afastamento - 0.02, offsetLongStart + 0.05,
                                    offsetLongEnd + 0.05, false, elevacaoInicio, elevacaoFinal, null, superficie, profundidade, siteFundoId);

                                

                                VerticesTubos(tubo, $"{tubo.Name} Concreto", afastamento - 0.03, offsetLongStart + 0.045,
                                    offsetLongEnd + 0.045, false, elevacaoInicioConcreto, elevacaoFinalConcreto, null, superficieConcretoTubo, profundidade, siteConcretoMagroId);

                                

                                VerticesTubos(tubo, $"{tubo.Name} Manual", afastamento - 0.04, offsetLongStart + 0.04,
                                    offsetLongEnd + 0.04, false, elevacaoInicioManual, elevacaoFinalManual, null, 
                                    superficieCompactacaoManualTubo, profundidade, sitecCompactacaoManualId);


                                VerticesTubos(tubo, $"{tubo.Name} Superior", afastamento, offsetLongStart,
                                    offsetLongEnd, true, 0.0, 0.0, superficieBase, superficie, profundidade, siteTopoId);


                                



                            }
                            else if (profundidade > 1.25 && profundidade < 1.75)
                            {
                                double elevacaoMeioInicio = startZ - tubo.OuterDiameterOrWidth / 2 - espConc + 1.25;
                                double elevacaoMeioFinal = endZ - tubo.OuterDiameterOrWidth / 2 - espConc + 1.25;
                                double elevacaoMedia = (elevacaoMeioInicio + elevacaoMeioFinal) / 2;
                             
                                double elevacaoSuperficie = (superficieBase.FindElevationAtXY(startX, startY) + superficieBase.FindElevationAtXY(endX, endY)) / 2;
                              
                                double afastamentoMeio = elevacaoSuperficie - elevacaoMedia;

                                

                                VerticesTubos(tubo, $"{tubo.Name} Meio", afastamento - 0.01, offsetLongStart + 0.03,
                                    offsetLongEnd + 0.03, false, elevacaoMeioInicio, elevacaoMeioFinal, null, superficie, profundidade, siteFundoId);
                                double elevacaoFundoInicio = startZ - tubo.OuterDiameterOrWidth / 2 - espConc;
                                double elevacaoFundoFinal = endZ - tubo.OuterDiameterOrWidth / 2 - espConc;
                                double elevacaoInicioConcreto = startZ - tubo.OuterDiameterOrWidth / 2;
                                double elevacaoFinalConcreto = endZ - tubo.OuterDiameterOrWidth / 2;
                                double elevacaoInicioManual = startZ + tubo.OuterDiameterOrWidth / 2 + espCompc;
                                double elevacaoFinalManual = endZ + tubo.OuterDiameterOrWidth / 2 + espCompc;
                                

                                VerticesTubos(tubo, $"{tubo.Name} Fundo", afastamento - 0.02, offsetLongStart + 0.05,
                                    offsetLongEnd + 0.05, false, elevacaoFundoInicio, elevacaoFundoFinal, null, superficie, profundidade, siteFundoId);

                                VerticesTubos(tubo, $"{tubo.Name} FundoConcreto", afastamento - 0.03, offsetLongStart + 0.04,
                                    offsetLongEnd + 0.04, false, elevacaoInicioConcreto, elevacaoFinalConcreto, null, superficieConcretoTubo, profundidade, siteConcretoMagroId);

                                VerticesTubos(tubo, $"{tubo.Name} Manual", afastamento - 0.04, offsetLongStart + 0.04,
                                    offsetLongEnd + 0.04, false, elevacaoInicioManual, elevacaoFinalManual, null,
                                    superficieCompactacaoManualTubo, profundidade, sitecCompactacaoManualId);

                                VerticesTubos(tubo, $"{tubo.Name} Superior", afastamentoMeio, offsetLongStart,
                                    offsetLongEnd, true, 0.0, 0.0, superficieBase, superficie, profundidade, siteTopoId);
                            }

                            

                            

                        }*/




                        string materialReaterro = Janela.ReaterroSelecionado;
                        string materialEmbasamento = Janela.EmbasamentoSelecionado;


                        /*/=========== CRIAÇÃO SUPERFÍCIES DE VOLUME =============//
                        //VOLUME ESCAVAÇÃO TUBOS
                        ObjectId volumeEscavacaoTubosId = TinVolumeSurface.Create($"VOL ESC TUBOS {net.Name}", 
                            surfaceBaseId, superficie.Id, surfaceStyleInvId);
                        TinVolumeSurface volumeEscavacaoTubos = (TinVolumeSurface)volumeEscavacaoTubosId.GetObject(OpenMode.ForRead);
                        volumeEscavacaoTubos.Description = "";
                        
                        
                        


                        //VOLUME ESCAVAÇÃO CAIXAS
                        ObjectId volumeEscavacaoCaixasId = TinVolumeSurface.Create($"VOL ESC CAIXAS {net.Name}", 
                            surfaceBaseId, superficieCaixas.Id, surfaceStyleInvId);
                        TinVolumeSurface volumeEscavacaoCaixas = (TinVolumeSurface)volumeEscavacaoCaixasId.GetObject(OpenMode.ForRead);
                        volumeEscavacaoCaixas.Description = "";
                        
                        


                        //VOLUME EMBASAMENTO TUBOS
                        ObjectId volumeConcretoMagroTubosId = TinVolumeSurface.Create($"VOL EMB TUBOS {net.Name}",
                           superficie.Id, superficieConcretoTubo.Id, surfaceStyleInvId);
                        TinVolumeSurface volumeConcretoMagroTubos = (TinVolumeSurface)volumeConcretoMagroTubosId.GetObject(OpenMode.ForRead);
                        volumeConcretoMagroTubos.Description = materialEmbasamento;
                        


                        //VOLUME EMBASAMENTO CAIXAS
                        ObjectId volumeConcretoMagroCaixasId = TinVolumeSurface.Create($"VOL EMB CAIXAS {net.Name}",
                              superficieCaixas.Id, superficieConcreto.Id, surfaceStyleInvId);
                        TinVolumeSurface volumeConcretoMagroCaixas = (TinVolumeSurface)volumeConcretoMagroCaixasId.GetObject(OpenMode.ForRead);
                        volumeConcretoMagroCaixas.Description = materialEmbasamento;
                        volumeConcretoMagroCaixas.AutoRebuild=true;
                        


                        //VOLUME COMPACTAÇÃO MANUAL TUBOS
                        ObjectId volumeCompactacaoManualTubosId = TinVolumeSurface.Create($"VOL COMP MANUAL TUBOS {net.Name}",
                             superficie.Id, superficieCompactacaoManualTubo.Id, surfaceStyleInvId);
                        TinVolumeSurface volumeCompactacaoManualTubos = (TinVolumeSurface)volumeCompactacaoManualTubosId.GetObject(OpenMode.ForRead);
                        volumeCompactacaoManualTubos.Description = materialReaterro;
                        
                        




                        //VOLUME COMPACTAÇÃO MANUAL CAIXAS
                        ObjectId volumeCompactacaoManualCaixasId = TinVolumeSurface.Create($"VOL COMP MANUAL CAIXAS {net.Name}",
                            superficieCaixas.Id, superficieCompactacaoManual.Id, surfaceStyleInvId);
                        TinVolumeSurface volumeCompactacaoManualCaixas = (TinVolumeSurface)volumeCompactacaoManualCaixasId.GetObject(OpenMode.ForRead);
                        volumeCompactacaoManualCaixas.Description = materialReaterro;
                        
                        



                        ObjectId volumeCompactacaoMecanicaTubosId = TinVolumeSurface.Create($"VOL COMP MECÂNICA TUBOS {net.Name}",
                            superficieCompactacaoManualTubo.Id, superficieBase.Id, surfaceStyleInvId);
                        TinVolumeSurface volumeCompactacaoMecanicaTubos = (TinVolumeSurface)volumeCompactacaoMecanicaTubosId.GetObject(OpenMode.ForRead);
                        volumeCompactacaoMecanicaTubos.Description = materialReaterro;
                        
                        */


                        
                   
                       


                         //Solicita o ponto de inserção dos Profile Views
                       /* PromptPointResult pointResult = docEditor.GetPoint("\nEspecifique o ponto de inserção do Profile View:");
                        if (pointResult.Status != PromptStatus.OK)
                        {
                            docEditor.WriteMessage("\nOperação cancelada pelo usuário.");
                            return;
                        }
                        Point3d basePoint = pointResult.Value;

                        */


                        //---------- DEFINIÇÃO DOS PROFILES DE SUPERFICIES --------------//

                        ObjectId alinhamentoRedeId = net.ReferenceAlignmentId;
                        ObjectId styleTubosId = ObterProfileStyleId(docCivil, "ESCAVAÇÃO_TUBOS");
                        ObjectId labelStyleId = ObterLabelStyleId(docCivil, "VAZIO");
                       // ObjectId profileEscTubosId = Profile.CreateFromSurface($"ESC TUBOS {net.Name}", alinhamentoRedeId, superficie.Id, superficie.LayerId, styleTubosId, labelStyleId);

                        //---------- CRIA PROFILE VIEW --------------//

                        Point3d pontoPV = new Point3d(281602.8478, 7484625.2047, 0);
                        ObjectId profileViewStyleId = ObterProfileViewStyleId(docCivil, "PROFILEVIEW_DRENAGEM");
                        ObjectId profileViewBandId = ObterProfileViewBandStyleId(docCivil, "00-BAND_STYLE_DRENAGEM");


                        //ObjectId profileView = ProfileView.Create(alinhamentoRedeId, basePoint, $"PROFILE VIEW {net.Name}", profileViewBandId, profileViewStyleId);




                        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                        ///
                        foreach (ObjectId superficiesId in superficiesDict.Keys)
                        {
                            TinSurface superficies = superficiesDict[superficiesId].Superficie;

                            if (superficies.Name.Contains("TUBO"))
                            {
                                List<List<Point3d>> pontos = superficiesDict[superficiesId].PontosVertices;
                                string nome = superficiesDict[superficiesId].Nomes;
                                foreach (List<Point3d> pontosVertices in pontos)
                                {
                                    LigarVertices(pontosVertices, superficies, nome, 0, 1);

                                }

                                //docEditor.WriteMessage($"\nSUPERFICIE {superficies.Name} RECONECTADA");
                            }
                            
                        }


                        foreach (ObjectId superficiesId in superficiesDict.Keys)
                        {
                            TinSurface superficies = superficiesDict[superficiesId].Superficie;

                            if (superficies.Name.Contains("TUBO"))
                            {
                                List<List<Point3d>> pontos = superficiesDict[superficiesId].PontosVertices;
                                string nome = superficiesDict[superficiesId].Nomes;
                                foreach (List<Point3d> pontosVertices in pontos)
                                {
                                    LigarVertices(pontosVertices, superficies, nome, 1, 2);

                                }

                                //docEditor.WriteMessage($"\nSUPERFICIE {superficies.Name} RECONECTADA");
                            }

                        }



                        foreach (ObjectId superficiesId in superficiesDict.Keys)
                        {
                            TinSurface superficies = superficiesDict[superficiesId].Superficie;

                            if (superficies.Name.Contains("TUBO"))
                            {
                                List<List<Point3d>> pontos = superficiesDict[superficiesId].PontosVertices;
                                string nome = superficiesDict[superficiesId].Nomes;
                                foreach (List<Point3d> pontosVertices in pontos)
                                {
                                    LigarVertices(pontosVertices, superficies, nome, 2, 3);

                                }

                                //docEditor.WriteMessage($"\nSUPERFICIE {superficies.Name} RECONECTADA");
                            }

                        }


                        foreach (ObjectId superficiesId in superficiesDict.Keys)
                        {
                            TinSurface superficies = superficiesDict[superficiesId].Superficie;
                            
                            

                            if (superficies.Name.Contains("TUBO"))
                            {
                                List<List<Point3d>> pontos = superficiesDict[superficiesId].PontosVertices;
                                string nome = superficiesDict[superficiesId].Nomes;
                                foreach (List<Point3d> pontosVertices in pontos)
                                {
                                    LigarVertices(pontosVertices, superficies, nome, 3, 0);

                                }

                                //docEditor.WriteMessage($"\nSUPERFICIE {superficies.Name} RECONECTADA");
                            }

                        }

                       
                        foreach (ObjectId superficiesId in superficiesDict.Keys)
                        {
                            TinSurface superficies = superficiesDict[superficiesId].Superficie;

                            //docEditor.WriteMessage($"\nSUPERFICIE {superficies.Name} ITERADA");
                            IntesecaoSurfacePoly(superficiesDict[superficiesId].Polylines, superficies);
                        }

                        foreach (var polys in Polylines)
                        {
                             polys.Erase();
 
                        }

                        

                        //double volume1 = volumeEscavacaoTubos.GetBoundedVolumes(valaAte1_5, 0.0).Cut;
                        //double volume2 = volumeEscavacaoTubos.GetBoundedVolumes(valaAte1_5, 0.0).Fill;
                        //double volume3 = volumeEscavacaoTubos.GetBoundedVolumes(valaAte1_5, 0.0).Net;

                        //docEditor.WriteMessage($"\nO Numero de Pontos até 1.5 é: {volume1}");
                        //docEditor.WriteMessage($"\nO Numero de Pontos até 3 é: {volume2}");
                        //docEditor.WriteMessage($"\nO Numero de Pontos até 1.5 a 3 é: {volume3}\n");


                        string caminhoBounderies = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Bounderies Tubos {net.Name}.csv");

                        // Construir CSV
                        var csv = new StringBuilder();

                        foreach (var collection in valaAte1_5)
                        {
                            foreach (Point3d point in collection)
                            {
                                csv.AppendLine($"{point.X.ToString(CultureInfo.InvariantCulture)},{point.Y.ToString(CultureInfo.InvariantCulture)},{point.Z.ToString(CultureInfo.InvariantCulture)}");
                            }
                            csv.AppendLine(); // Linha em branco para separar coleções
                        }

                        // Salvar para arquivo
                        File.WriteAllText(caminhoBounderies, csv.ToString());

                        


                        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


                        //MessageBox.Show("USE O COMANDO REGEN PARA VER AS SUPERFÍCIES CRIADAS");
                        RebuidarSuperficies rebuidar = new RebuidarSuperficies();
                        rebuidar.RebuidaSuperficies(tr);


                        Manager.DocEditor.Regen();
                        tr.Commit();
                    }
                    catch (System.Exception e)
                    {
                        docEditor.WriteMessage("\nErro: " + e.Message + "\nStack: " + e.StackTrace);

                    }
                }
            }
          
        }

        private void TryDeleteEdge(TinSurface superficie, Point2d ponto, Editor docEditor, bool rebuild = false)
        {
            try
            {
                var edge = superficie.FindEdgeAtXY(ponto.X, ponto.Y);
                if (edge != null)
                {
                    superficie.DeleteLine(edge);
                    if (rebuild) superficie.Rebuild();
                }
            }
            catch (System.Exception ex)
            {
                //docEditor.WriteMessage($"\nProblema ao apagar edge em {ponto.X},{ponto.Y}: {ex.Message}");
            }
        }

        

        private Polyline PolyCaixas(List<Point3d> plOrdenados, double profundidade = 0.1)
        {
            Polyline poly = new Polyline();
            

            using (Transaction trans = Manager.DocData.TransactionManager.StartTransaction())
            {


                if (profundidade > 0.0) {
                    Point3dCollection vala = new Point3dCollection();
                    foreach (Point3d ponto in plOrdenados)
                    {
                        vala.Add(ponto);  
                    }
                    valaAte1_5.Add(vala);
                }

                List<Point2d> pontosSt = new List<Point2d>();
                
                // Adiciona os vértices
                foreach (Point3d ponto in plOrdenados)
                {
                    Plane pl = new Plane();
                    Point3d pontoOffset = new Point3d(ponto.X, ponto.Y, ponto.Z);
                    pontosSt.Add(pontoOffset.Convert2d(pl));
                }

                

                // Abre Model Space
                BlockTable bt = (BlockTable)trans.GetObject(Manager.DocData.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);


                for (int i = 0; i < pontosSt.Count; i++)
                    poly.AddVertexAt(i, pontosSt[i], 0, 0, 0);
                poly.Closed = true;
                poly.Layer = "U-HZ-TABELA";
                ms.AppendEntity(poly);
                trans.AddNewlyCreatedDBObject(poly, true);
                trans.Commit();

             
            }

            polyTopo = poly;

            return poly;

        }



        // ---------------- AUXILIARES GEOMÉTRICOS ----------------

        private List<Point3d> VerticesCaixas(Structure structure, double offset, double deslocamentoVertical, Point3d centro, TinSurface tinSurface, string nome)
        {

            double largura = structure.Length + 2 * offset;

           
           
            double comprimento = (structure.DiameterOrWidth + 2) + offset;
            double thetaRad = structure.Rotation;
            //Polyline polyTopo = new Polyline();
            List<Point3d> plOrdenados = new List<Point3d>();    

            Vector3d dirComp = new Vector3d(Math.Cos(thetaRad), Math.Sin(thetaRad), 0).GetNormal();
            Vector3d dirLarg = dirComp.CrossProduct(Vector3d.ZAxis).GetNormal();

            Vector3d metadeComp = dirComp * (comprimento / 2);
            Vector3d metadeLarg = dirLarg * (largura / 2);

            Vector3d metadeCompPl = dirComp * (comprimento / 2 + 0.1);
            Vector3d metadeLargPl = dirLarg * (largura / 2 + 0.1);

            Point3d d1 = centro + metadeCompPl + metadeLargPl;
            Point3d d2 = centro + metadeCompPl - metadeLargPl;
            Point3d d3 = centro - metadeCompPl - metadeLargPl;
            Point3d d4 = centro - metadeCompPl + metadeLargPl;

            plOrdenados = new List<Point3d> { d1, d2, d3, d4, d1 }; // fecha polígono

            if (nome == "TOPO")
            {
                polyTopo = PolyCaixas(plOrdenados);
                Polylines.Add(polyTopo);

            }


           centro = centro - new Vector3d(0, 0, deslocamentoVertical);

            Point3d c1 = centro + metadeComp + metadeLarg;
            Point3d c2 = centro + metadeComp - metadeLarg;
            Point3d c3 = centro - metadeComp - metadeLarg;
            Point3d c4 = centro - metadeComp + metadeLarg;
           
            /*using (Transaction trans = Manager.DocData.TransactionManager.StartTransaction())
            {
               
                List<Point2d> pontosSt = new List<Point2d>();
                // Adiciona os vértices
                foreach (Point3d ponto in plOrdenados)
                {
                    Plane pl = new Plane();
                    Point3d pontoOffset = new Point3d(ponto.X, ponto.Y, ponto.Z);
                    pontosSt.Add(pontoOffset.Convert2d(pl));
                }

                // Abre Model Space
                BlockTable bt = (BlockTable)trans.GetObject(Manager.DocData.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);


                for (int i = 0; i < pontosSt.Count; i++)
                    polyTopo.AddVertexAt(i, pontosSt[i], 0, 0, 0);
                polyTopo.Closed = true;
                polyTopo.Layer = "U-HZ-TABELA";
                ms.AppendEntity(polyTopo);
                trans.AddNewlyCreatedDBObject(polyTopo, true);
                trans.Commit();

            }*/

            if (!superficiesDict.ContainsKey(tinSurface.Id))
                superficiesDict[tinSurface.Id] = new SurfaceData(tinSurface);

            // Adicionando uma polyline 
            superficiesDict[tinSurface.Id].Polylines.Add(polyTopo);

            return new List<Point3d> { c1, c2, c3, c4,}; // fecha polígono
        }






        private Point3d CalcularCentroGeometricoBocaDeLobo(Structure estrutura)
        {
            double thetaRad = estrutura.Rotation;
            double largura = estrutura.DiameterOrWidth;
            Point3d centro = estrutura.Location;
            Vector3d dirComp = new Vector3d(Math.Sin(thetaRad), Math.Cos(thetaRad), 0).GetNormal();
            Vector3d dirLarg = dirComp.CrossProduct(Vector3d.ZAxis).GetNormal();

            Point3d d1 = centro + dirLarg * largura;
            // Idealmente, calcule nesta função caso haja deslocamento real. Por padrão, retorna o centro atual:
            return d1;
        }








        private List<Point3d> VerticesCaixasCirculares(Point3d centro, double raio, double deslocamentoVertical, TinSurface superficie, string nome)
        {
            //Polyline polyTopo = new Polyline();
            List<Point3d> plOrdenados = new List<Point3d>();
            var pontos = new List<Point3d>();
            for (int i = 0; i <= LADOS_CIRCULO; i++)
            {
                double ang = 2 * Math.PI * i / LADOS_CIRCULO;
                double x = centro.X + raio * Math.Cos(ang);
                double y = centro.Y + raio * Math.Sin(ang);
                double z = centro.Z - deslocamentoVertical;
                double x1 = centro.X + (raio + 0.1) * Math.Cos(ang);
                double y1 = centro.Y + (raio + 0.1) * Math.Sin(ang);
                double z1 = centro.Z - deslocamentoVertical;
                pontos.Add(new Point3d(x, y, z));
                plOrdenados.Add(new Point3d(x1, y1, z1));
            }

            if (nome == "TOPO")
            {
                polyTopo = PolyCaixas(plOrdenados);
                Polylines.Add(polyTopo);

            }
            /*using (Transaction trans = Manager.DocData.TransactionManager.StartTransaction())
                {

                    List<Point2d> pontosSt = new List<Point2d>();
                    // Adiciona os vértices
                    foreach (Point3d ponto in plOrdenados)
                    {
                        Plane pl = new Plane();
                        Point3d pontoOffset = new Point3d(ponto.X, ponto.Y, ponto.Z);
                        pontosSt.Add(pontoOffset.Convert2d(pl));
                    }

                    // Abre Model Space
                    BlockTable bt = (BlockTable)trans.GetObject(Manager.DocData.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);


                    for (int i = 0; i < pontosSt.Count; i++)
                        polyTopo.AddVertexAt(i, pontosSt[i], 0, 0, 0);
                    polyTopo.Closed = true;
                    polyTopo.Layer = "U-HZ-TABELA";
                    ms.AppendEntity(polyTopo);
                    trans.AddNewlyCreatedDBObject(polyTopo, true);
                    trans.Commit();
                }*/

                if (!superficiesDict.ContainsKey(superficie.Id))
                    superficiesDict[superficie.Id] = new SurfaceData(superficie);

                // Adicionando uma polyline 
                superficiesDict[superficie.Id].Polylines.Add(polyTopo);

            return pontos;
        }

       


        private double CalcularLarguraVala(double D)
        {
            if (D <= 0.40) return 0.40;
            if (D > 0.40 && D <= 0.80) return D / 2 + 0.30;
            return D / 2 + 0.20;
        }




        // -- CRIAÇÃO DE FEATURE LINES CORRIGIDA
        public void AdionaVerticesNaSuperficie(List<Point3d> vertices, string nome, ObjectId siteId, TinSurface superficie)
        {
            Document docCad = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Database docData = docCad.Database;
            CivilDocument docCivil = CivilApplication.ActiveDocument;
            ObjectIdCollection featLineColl = new ObjectIdCollection();

            // Remove duplicados consecutivos
            List<Point3d> verticesDistinct = FiltrarPontosDuplicados(vertices);



            Point3dCollection pontosCollection = new Point3dCollection();
            foreach (Point3d pt in verticesDistinct)
                pontosCollection.Add(pt);

            superficie.AddVertices(pontosCollection);

        }
        

        public static bool PontoIgual(Point3d a, Point3d b, double tol = 1e-6)
        {
            return a.DistanceTo(b) < tol;
        }




        public void LigarVertices(List<Point3d> verticesDistinct, TinSurface superficie, string nome, int v1, int v2)
        {

            if (nome.Contains("Superior") || nome.Contains("Fundo"))
            {
                TinSurfaceVertexCollection verticesCollection = superficie.Vertices;

                TinSurfaceVertex primeiroVertice = BuscarVerticePorCoordenada(verticesCollection, verticesDistinct[v1]);
                TinSurfaceVertex segundoVertice = BuscarVerticePorCoordenada(verticesCollection, verticesDistinct[v2]);
                //TinSurfaceVertex terceiroVertice = BuscarVerticePorCoordenada(verticesCollection, verticesDistinct[2]);
                //TinSurfaceVertex quartoVertice = BuscarVerticePorCoordenada(verticesCollection, verticesDistinct[3]);

                if (primeiroVertice == null || segundoVertice == null)
                {
                    // Exiba mensagem de erro amigável
                    //Manager.DocEditor.WriteMessage("\nNão foi possível localizar todos os vértices!");
                    return; // Não tenta adicionar linhas com nulos!
                }

                superficie.AddLine(primeiroVertice, segundoVertice);
                
                //superficie.AddLine(segundoVertice, terceiroVertice);
                //Manager.DocEditor.WriteMessage("\nSEGUNDO ADD");
                //superficie.AddLine(quartoVertice, terceiroVertice);
                //Manager.DocEditor.WriteMessage("\nTERCEIRO ADD");
                //superficie.AddLine(quartoVertice, primeiroVertice);
                //Manager.DocEditor.WriteMessage("\nQUARTO ADD");

                superficie.Rebuild();
            } 




        }



       

        TinSurfaceVertex BuscarVerticePorCoordenada(
            TinSurfaceVertexCollection lista, Point3d referencia, double tol = 1e-6)
        {
            foreach (TinSurfaceVertex vt in lista)
            {
                if (PontoIgual(vt.Location, referencia, tol))
                    return vt;
            }
            return null;
        }


        private List<Point3d> FiltrarPontosDuplicados(List<Point3d> pontos, double tolerancia = 0.001)
        {
            List<Point3d> pontosFiltrados = new List<Point3d>();
            foreach (var pt in pontos)
            {
                if (pontosFiltrados.Count == 0 || !pt.IsEqualTo(pontosFiltrados[pontosFiltrados.Count - 1], new Tolerance(tolerancia, tolerancia)))
                    pontosFiltrados.Add(pt);
            }
            return pontosFiltrados;
        }

        
        // -- SITES
        private ObjectId GarantirSiteCivil(Transaction tr, CivilDocument docCivil, string nomeSite)
        {
            foreach (ObjectId siteId in docCivil.GetSiteIds())
            {
                Site siteExistente = (Site)tr.GetObject(siteId, OpenMode.ForRead);
                if (siteExistente.Name == nomeSite)
                    return siteExistente.Id;
            }
            return Site.Create(docCivil, nomeSite);
        }

        // -- ESTILO DE SUPERFÍCIE
        private ObjectId ObterStyleId(CivilDocument docCivil, Transaction tr, string nome)
        {
            foreach (ObjectId styleId in docCivil.Styles.SurfaceStyles)
            {
                SurfaceStyle surfaceStyle = (SurfaceStyle)styleId.GetObject(OpenMode.ForRead);
                if (surfaceStyle.Name == nome)
                    return styleId;
            }
            return ObjectId.Null;
        }


        //|||||||||||||||||||||||| PROFILE STYLES |||||||||||||||||||||||||||//


        private ObjectId ObterProfileStyleId(CivilDocument docCivil, string nome)
        {
            foreach (ObjectId styleId in docCivil.Styles.ProfileStyles)
            {
                var surfaceStyle = (StyleBase)styleId.GetObject(OpenMode.ForRead);
                if (surfaceStyle.Name == nome)
                    return styleId;
            }
            return ObjectId.Null;
        }


        private ObjectId ObterLabelStyleId(CivilDocument docCivil, string nome)
        {
            var enumerator = docCivil.Styles.LabelSetStyles.ProfileLabelSetStyles.GetEnumerator();
            while (enumerator.MoveNext())
            {
                // Cada item é um ObjectId
                ObjectId BSstyleId = enumerator.Current;

                // Abrir o estilo para leitura
                ProfileLabelSetStyle style = (ProfileLabelSetStyle)BSstyleId.GetObject(OpenMode.ForRead);


                if (style.Name == nome)
                {
                    return style.Id;
                }
    
            }

            return ObjectId.Null;
        }


        //|||||||||||||||||||||||| PROFILE VIEWS STYLES |||||||||||||||||||||||||||//

        private ObjectId ObterProfileViewStyleId(CivilDocument docCivil, string nome)
        {
            foreach (ObjectId styleId in docCivil.Styles.ProfileViewStyles)
            {
                var style = (StyleBase)styleId.GetObject(OpenMode.ForRead);
                if (style.Name == nome)
                {
                    return style.Id;
                }
                    
            }
            return ObjectId.Null;
        }

        private ObjectId ObterProfileViewBandStyleId(CivilDocument docCivil, string nome)
        {
            var enumerator = docCivil.Styles.ProfileViewBandSetStyles.GetEnumerator();
            while (enumerator.MoveNext())
            {
                // Cada item é um ObjectId
                ObjectId BSstyleId = enumerator.Current;

                // Abrir o estilo para leitura
                var style = (StyleBase)BSstyleId.GetObject(OpenMode.ForRead);
                

                if (style.Name == nome)
                {
                    return style.Id;
                }

                
            }

            return ObjectId.Null;
        }




        // -- POLÍGONO PARA CADA TUBO
        public void VerticesTubos(
            Pipe tubo,
            string nome,
            double afastamentoLateral,
            double offsetLongitudinalStart,
            double offsetLongitudinalEnd,
            bool cotaPorSuperficie,
            double elevacaoInicio,
            double elevacaoFinal,
            TinSurface superficieBase,
            TinSurface tinSurface,
            double profundidade,
            ObjectId siteId = default



        )
        {

            Polyline poly = new Polyline();
            List<Point3d> plOrdenados = new List<Point3d>();
            Point3d start = tubo.StartPoint;
            Point3d end = tubo.EndPoint;
            Vector3d dirTubo = (end - start).GetNormal();
            Vector3d dirLateral = dirTubo.CrossProduct(Vector3d.ZAxis).GetNormal();

            Point3d startOffset = start + dirTubo * offsetLongitudinalStart;
            Point3d endOffset = end - dirTubo * offsetLongitudinalEnd;

            Point3d p1 = startOffset + dirLateral * afastamentoLateral; // Início direita
            Point3d p2 = endOffset + dirLateral * afastamentoLateral;   // Fim direita
            Point3d p3 = endOffset - dirLateral * afastamentoLateral;   // Fim esquerda
            Point3d p4 = startOffset - dirLateral * afastamentoLateral; // Início esquerda


            if (cotaPorSuperficie && superficieBase != null)
            {
                p1 = new Point3d(p1.X, p1.Y, superficieBase.FindElevationAtXY(p1.X, p1.Y));
                p2 = new Point3d(p2.X, p2.Y, superficieBase.FindElevationAtXY(p2.X, p2.Y));
                p3 = new Point3d(p3.X, p3.Y, superficieBase.FindElevationAtXY(p3.X, p3.Y));
                p4 = new Point3d(p4.X, p4.Y, superficieBase.FindElevationAtXY(p4.X, p4.Y));

                Point3d sffset = start + dirTubo * (offsetLongitudinalStart - 0.1);
                Point3d eOffset = end - dirTubo * (offsetLongitudinalEnd - 0.1);

                Point3d pl1 = sffset + dirLateral * (afastamentoLateral + 0.01);
                Point3d pl2 = eOffset + dirLateral * (afastamentoLateral + 0.01);
                Point3d pl3 = eOffset - dirLateral * (afastamentoLateral + 0.01);
                Point3d pl4 = sffset - dirLateral * (afastamentoLateral + 0.01);

                plOrdenados = new List<Point3d> { pl1, pl2, pl3, pl4, pl1 };
                refPolyline = plOrdenados;

                if (nome == "TOPO")
                {
                    PolyCaixas(plOrdenados, profundidade);
                    
                }



            }
        
            else
            {
                p1 = new Point3d(p1.X, p1.Y, elevacaoInicio);
                p2 = new Point3d(p2.X, p2.Y, elevacaoFinal);
                p3 = new Point3d(p3.X, p3.Y, elevacaoFinal);
                p4 = new Point3d(p4.X, p4.Y, elevacaoInicio);
            }

            List<Point3d> pontosOrdenados = new List<Point3d> { p1, p2, p3, p4, p1 };
            //Filtra a existencia de pontos duplicados
            pontosOrdenados = FiltrarPontosDuplicados(pontosOrdenados);

            if(plOrdenados.Count == 0)
                plOrdenados = refPolyline;

            using (Transaction trans = Manager.DocData.TransactionManager.StartTransaction())
            {
                List<Point2d> pontos = new List<Point2d>();
                // Adiciona os vértices
                foreach (Point3d ponto in plOrdenados)
                {
                    Plane pl = new Plane();
                    Point3d pontoOffset = new Point3d(ponto.X, ponto.Y, ponto.Z);
                    pontos.Add(pontoOffset.Convert2d(pl));
                }

                // Abre Model Space
                BlockTable bt = (BlockTable)trans.GetObject(Manager.DocData.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                for (int i = 0; i < pontos.Count; i++)
                    poly.AddVertexAt(i, pontos[i], 0, 0, 0);
                poly.Closed = true;
                poly.Layer = "U-HZ-TABELA";
                ms.AppendEntity(poly);
                trans.AddNewlyCreatedDBObject(poly, true);

                trans.Commit();
            }

            //Chamando o dicionario criando um objeto da classe surfaceData
            if (!superficiesDict.ContainsKey(tinSurface.Id))
                superficiesDict[tinSurface.Id] = new SurfaceData(tinSurface);

            // Adicionando uma polyline 
            superficiesDict[tinSurface.Id].Polylines.Add(poly);
            Polylines.Add(poly);

            superficiesDict[tinSurface.Id].PontosVertices.Add(pontosOrdenados);

            if(nome.Contains("Superior"))
                superficiesDict[tinSurface.Id].Nomes = nome;

            //Adiciona os vertices à superficie
            AdionaVerticesNaSuperficie(pontosOrdenados, nome, siteId, tinSurface);
        }


        /*|||||||||||||||||||||||||||||| EXCLUSAO DE EDGES ||||||||||||||||||||||||||||||*/
       
        public void IntesecaoSurfacePoly(List<Polyline> listExterior2D, TinSurface superficie)
        {
            Document docCad = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            CivilDocument docCivil = CivilApplication.ActiveDocument;
            Editor docEditor = docCad.Editor;
            Database docData = docCad.Database;

            try
            {
                using (Transaction tr = docData.TransactionManager.StartTransaction())
                {

                    

                    // 1. Coletar todos os pontos de intersecção possíveis
                    var intersecoes = new List<Point2d>();
                    foreach(Polyline exterior2D in listExterior2D) {
                        foreach (TinSurfaceTriangle tri in superficie.Triangles)
                        {
                            Point2d[] triPts = new Point2d[] {
                            new Point2d(tri.Vertex1.Location.X, tri.Vertex1.Location.Y),
                            new Point2d(tri.Vertex2.Location.X, tri.Vertex2.Location.Y),
                            new Point2d(tri.Vertex3.Location.X, tri.Vertex3.Location.Y),
                        };

                            

                            // Checa cada edge do triângulo
                            for (int t = 0; t < 3; t++)
                            {
                                Point2d A = triPts[t];
                                Point2d B = triPts[(t + 1) % 3];



                                // Para cada segmento da polyline...
                                int nSegments = exterior2D.NumberOfVertices - (exterior2D.Closed ? 0 : 1);
                                for (int j = 0; j < nSegments; j++)
                                {
                                    Point2d C = exterior2D.GetPoint2dAt(j);
                                    Point2d D = exterior2D.GetPoint2dAt((j + 1) % exterior2D.NumberOfVertices);

                                    // Calcula interseção
                                    if (SegmentIntersect(A, B, C, D, out Point2d interPt))
                                    {
                                        // Adiciona SE ainda não existe (por causa da tolerância)
                                        bool jaExiste = false;
                                        foreach (Point2d ep in intersecoes)
                                        {
                                            if (ep.GetDistanceTo(interPt) < 0.001)
                                            {
                                                jaExiste = true;
                                                break;
                                            }
                                        }
                                        if (!jaExiste)
                                            intersecoes.Add(interPt);


                                    }
                                }

                            }
                        }
                    }

                    // 2. Só agora, apague todas as edges de uma vez
                    int apagadas = 0;
                    foreach (var ponto in intersecoes)
                    {
                        TryDeleteEdge(superficie, ponto, docEditor);
                        apagadas++;
                    }

                    
                    //docEditor.WriteMessage($"\nTotal de edges apagadas: {apagadas}");

                    

                    tr.Commit();
                }
            }
            catch (System.Exception e)
            {
                docEditor.WriteMessage("\nErro: " + e.Message + "\nStack: " + e.StackTrace);
            }
        }

        // Função de interseção entre segmentos 2D
        public bool SegmentIntersect(Point2d A, Point2d B, Point2d C, Point2d D, out Point2d intersection)
        {
            intersection = new Point2d();
            double denom = (B.X - A.X) * (D.Y - C.Y) - (B.Y - A.Y) * (D.X - C.X);
            if (Math.Abs(denom) < 1e-10)
                return false; // Paralelos ou coincidentes

            double num1 = (A.Y - C.Y) * (D.X - C.X) - (A.X - C.X) * (D.Y - C.Y);
            double num2 = (A.Y - C.Y) * (B.X - A.X) - (A.X - C.X) * (B.Y - A.Y);

            double t1 = num1 / denom;
            double t2 = num2 / denom;

            if (t1 < 0 || t1 > 1 || t2 < 0 || t2 > 1)
                return false; // Fora dos segmentos

            // Interseção encontrada
            intersection = new Point2d(
                A.X + t1 * (B.X - A.X),
                A.Y + t1 * (B.Y - A.Y)
            );
            return true;
        }

        

    }
}
