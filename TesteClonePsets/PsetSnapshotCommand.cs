using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    public class PsetSnapshotCommand
    {
        [CommandMethod("EXPORTAR_SNAPSHOT_PSETS")]
        public void ExportarSnapshotPsets()
        {
            Document? doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[EXPORTAR_SNAPSHOT_PSETS] Iniciando...");
                PsetSnapshotResult result = PsetSnapshotService.Execute(doc);
                ed.WriteMessage($"\n[EXPORTAR_SNAPSHOT_PSETS] Arquivo={result.OutputPath} Definicoes={result.TotalDefinitions} Propriedades={result.TotalProperties}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFalha no comando EXPORTAR_SNAPSHOT_PSETS: {ex.Message}");
            }
        }
    }
}
