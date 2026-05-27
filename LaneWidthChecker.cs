using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.Runtime;
using System;
using System.Drawing;
using System.Windows.Forms.Design;
using static System.Collections.Specialized.BitVector32;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Section = Autodesk.Civil.DatabaseServices.Section;


namespace AutomacoesCivil3D
{


    public class VerificaLargura
    {

        [CommandMethod("Largura Pista")]
        public static void VerificaLarguraPista()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            CivilDocument civilDoc = Manager.DocCivil;
            
            


            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 1. Selecionar o alinhamento
                PromptEntityOptions opt = new PromptEntityOptions("\nSelecione um alinhamento: ");
                opt.SetRejectMessage("\nEntidade não é um alinhamento!");
                opt.AddAllowedClass(typeof(Alignment), true);
                
                PromptEntityResult res = ed.GetEntity(opt);
                if (res.Status != PromptStatus.OK) return;

                ObjectId alignmentId = res.ObjectId;
                Alignment alignment = (Alignment)tr.GetObject(alignmentId, OpenMode.ForWrite);
                LaneWidthChecker laneWidthChecker = new ();
                laneWidthChecker.GetLaneWidthFromAlignment(alignment.Name);

                tr.Commit();
            }
        }

    }

    public class LaneAreas
    {
        public double Pintura { get; set; }
        public double Imprimacao { get; set; }
        public List<string> Regions { get; set; }
        public List<string> Alinhamentos { get; set; }
    }

    class LaneWidthChecker
    {


        public LaneAreas GetLaneWidthFromAlignment(string alignmentName)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            CivilDocument docCivil = CivilApplication.ActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            double areaTotalPista = 0.0;
            double areaTotalPasseio = 0.0;
            List<string>  regions = new List<string>();
            List<string> alinhamentos = new List<string>();


            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId corridorId in docCivil.CorridorCollection)
                    {
                        Corridor corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForRead);

                        foreach (Baseline baseline in corridor.Baselines)
                        {
                            Alignment currentAlignment = (Alignment)tr.GetObject(baseline.AlignmentId, OpenMode.ForRead);

                            if (currentAlignment.Name == alignmentName && !alinhamentos.Contains(currentAlignment.Name))
                            {
                                alinhamentos.Add(currentAlignment.Name);

                                foreach (BaselineRegion regiao in baseline.BaselineRegions)
                                {
                                    if (!regions.Contains(regiao.Name))
                                    {
                                        regions.Add(regiao.Name);

                                       
                                        
                                        double startStation = regiao.StartStation;
                                        double endStation = regiao.EndStation;
                                        double lengthRegiao = endStation - startStation;


                                       

                                        


                                        try
                                        {

                                            AppliedAssembly assembly = baseline.GetAppliedAssemblyAtStation(regiao.StartStation);

                                            // Verificação de assembly nulo
                                            if (assembly == null)
                                            {

                                                continue;
                                            }

                                            foreach (AppliedSubassembly appliedSub in assembly.GetAppliedSubassemblies())
                                            {
                                                Autodesk.Civil.DatabaseServices.Subassembly sub = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(appliedSub.SubassemblyId, OpenMode.ForRead);

                                                // Cálculo seguro com tratamento de nulos
                                                if (sub?.Name != null)
                                                {
                                                    double width = GetSubassemblyWidth(sub);
                                                    switch (sub.Name.ToUpper())
                                                    {
                                                        case "PASSEIO":
                                                            areaTotalPasseio += lengthRegiao * width;
                                                            break;
                                                        case "LANESUPERELEVATIONAOR":
                                                            areaTotalPista += lengthRegiao * width;
                                                            break;
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            ed.WriteMessage($"Erro na estação {startStation:N2}: {ex.Message}\n");
                                        }

                                    }
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
               
                
            }

            return new LaneAreas
            {
                Pintura = areaTotalPista,
                Imprimacao = areaTotalPasseio,
                Regions = regions,
                Alinhamentos = alinhamentos
            };
        }

        private double GetSubassemblyWidth(Autodesk.Civil.DatabaseServices.Subassembly sub)
        {
            // Garantir o índice correto do parâmetro Width
            const int widthParamIndex = 3; // Ajuste conforme necessário
            return sub.ParamsDouble[widthParamIndex].Value;
        }

        

    }
}
