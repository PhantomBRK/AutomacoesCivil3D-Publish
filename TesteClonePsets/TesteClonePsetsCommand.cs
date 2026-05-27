using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    public class TesteClonePsetsCommand
    {
        [CommandMethod("CLONAR_PSETS_COPY")]
        public void ClonarPsetsCopy()
        {
            Document? doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[CLONAR_PSETS_COPY] Iniciando...");
                ClonePsetsCopyResult result = TesteClonePsetsService.Execute(doc);

                ed.WriteMessage(
                    $"\n[CLONAR_PSETS_COPY] Total={result.TotalEncontrados} Criados={result.Criados} Atualizados={result.Atualizados} IgnoradosPrefixo={result.IgnoradosPrefixo} IgnoradosExistentes={result.IgnoradosExistentes} Falhas={result.Falhas} FormulaLikeOrigem={result.FormulaLikeOrigem} FormulaLikeClone={result.FormulaLikeClone}");

                foreach (string detalhe in result.Detalhes)
                {
                    ed.WriteMessage($"\n  - {detalhe}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFalha no comando CLONAR_PSETS_COPY: {ex.Message}");
            }
        }
    }
}
