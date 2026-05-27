using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    public class QtoSuperficiesCommand
    {
        [CommandMethod("QTO_SUPERFICIES_TRP_PAV")]
        public void QuantitativoSuperficies()
        {
            Editor editor = Manager.DocEditor;

            try
            {
                SurfaceMaterialQuantitiesService service = new SurfaceMaterialQuantitiesService();
                SurfaceMaterialQuantitiesDialogData dialogData = service.BuildDialogData();

                if (dialogData.SurfaceOptions.Count == 0)
                {
                    AcadApp.ShowAlertDialog(dialogData.BlockingIssue);
                    return;
                }

                SurfaceMaterialQuantitiesDialogViewModel viewModel = new SurfaceMaterialQuantitiesDialogViewModel(dialogData);
                QtoSuperficiesWindow window = new QtoSuperficiesWindow(viewModel);

                bool? confirmed = EXTRAIR_SOLIDOS_CORREDORES.AutoCadWpfDialogHost.ShowModal(window);
                if (confirmed != true)
                {
                    editor.WriteMessage("\nQuantitativo por superficies cancelado pelo usuario.");
                    return;
                }

                SurfaceMaterialQuantitiesResult result = service.Execute(window.BuildRequest());
                AcadApp.ShowAlertDialog(result.BuildSummary());
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD: {ex.Message}");
                AcadApp.ShowAlertDialog("Erro AutoCAD no quantitativo por superficies:\n" + ex.Message);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro geral: {ex.Message}");
                AcadApp.ShowAlertDialog("Erro no quantitativo por superficies:\n" + ex.Message);
            }
        }
    }
}