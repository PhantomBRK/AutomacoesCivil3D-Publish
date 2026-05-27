using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    public class PsetSnapshotImportCommand
    {
        [CommandMethod("IMPORTAR_SNAPSHOT_PSETS")]
        public void ImportarSnapshotPsets()
        {
            Document? doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;

            PromptStringOptions options = new PromptStringOptions("\nCaminho completo do snapshot JSON: ")
            {
                AllowSpaces = true
            };

            PromptResult prompt = ed.GetString(options);
            if (prompt.Status != PromptStatus.OK)
            {
                return;
            }

            string snapshotPath = (prompt.StringResult ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(snapshotPath))
            {
                ed.WriteMessage("\n[IMPORTAR_SNAPSHOT_PSETS] Caminho do arquivo não informado.");
                return;
            }

            try
            {
                ed.WriteMessage("\n[IMPORTAR_SNAPSHOT_PSETS] Iniciando...");
                PsetSnapshotImportResult result = PsetSnapshotImportService.Execute(doc, snapshotPath);
                ed.WriteMessage(
                    $"\n[IMPORTAR_SNAPSHOT_PSETS] Arquivo={result.SnapshotPath} Definicoes={result.TotalDefinicoesSnapshot} Criadas={result.DefinicoesCriadas} Atualizadas={result.DefinicoesAtualizadas} PropsCriadas={result.PropriedadesCriadas} PropsAtualizadas={result.PropriedadesAtualizadas} Formulas={result.FormulasAplicadas} ListasComoTexto={result.ListasConvertidasParaTexto} Avisos={result.Avisos}");

                foreach (string detalhe in result.Detalhes)
                {
                    ed.WriteMessage("\n- " + detalhe);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFalha no comando IMPORTAR_SNAPSHOT_PSETS: {ex.Message}");
            }
        }
    }
}
