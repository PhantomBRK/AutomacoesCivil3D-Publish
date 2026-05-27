using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.IO;
using System.Linq;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
//Classe que cria a exportação dos XML reports
namespace AutomacoesCivil3D.Rotinas_Petrobras
{
    public class TesteReportQuantities
    {
        [CommandMethod("ExportSampleLineVolumes")]
        public void ExportSampleLineVolumes()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                // Configurações globais
                string programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string styleSheetPath = @"C:\ProgramData\Autodesk\C3D 2026\enu\Data\Quantities Report Style Sheets\xsl\MATERIAIS.XSL";

                // Loop por todos os alinhamentos
                foreach (ObjectId alignmentId in civilDoc.GetAlignmentIds())
                {
                    Alignment alignment = (Alignment)tr.GetObject(alignmentId, OpenMode.ForRead);

                    // Pular alinhamentos sem grupos de seções transversais
                    if (alignment.GetSampleLineGroupIds().Count == 0) continue;

                    // Loop por todos os grupos de seções transversais do alinhamento
                    foreach (ObjectId sampleGroupId in alignment.GetSampleLineGroupIds())
                    {
                        SampleLineGroup sampleGroup = (SampleLineGroup)tr.GetObject(sampleGroupId, OpenMode.ForRead);

                        // Buscar listas de materiais que começam com "PAVIMENTAÇÃO"
                        QTOMaterialListCollection materialLists = sampleGroup.MaterialLists;
                        foreach (QTOMaterialList materialList1 in materialLists) {
                            
                            string materialList = materialList1.Name;
                            if (materialList.StartsWith("PAVIMENTO") || materialList.StartsWith("TERRAPLENAGEM")) {

                                // Configurar caminho do relatório
                                string reportsDir = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                    "Relatorios_Civil3D",
                                    alignment.Name);

                                string reportPath = Path.Combine(
                                    reportsDir,
                                    $"Volumes_{alignment.Name}_{materialList}.xml");

                                // Validar caminhos e gerar relatório
                                if (ValidatePaths(reportPath, styleSheetPath, ed))
                                {
                                    SampleLineGroup.ReportQuantities(
                                        sampleGroup.ObjectId,
                                        materialList,
                                        reportPath,
                                        styleSheetPath
                                    );
                                    ed.WriteMessage($"\nRelatório gerado: {reportPath}");
                                }

                            };         

                        }


                    }
                           
                }
                tr.Commit();
                ed.WriteMessage("\nProcessamento concluído!");
            }

               
        }
        

        private static bool ValidatePaths(string reportPath, string styleSheetPath, Editor ed)
        {
            try
            {
                // Criar diretório se não existir
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));

                // Verificar arquivo XSL
                if (!File.Exists(styleSheetPath))
                {
                    ed.WriteMessage($"\nERRO: Folha de estilo não encontrada: {styleSheetPath}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nERRO: {ex.Message}");
                return false;
            }
        }
    }
}