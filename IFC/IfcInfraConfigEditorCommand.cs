using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    public class IfcInfraConfigEditorCommand
    {
        [CommandMethod("IFC_CONFIG_INFRA")]
        [CommandMethod("IFCEDITCONFIG")]
        public void OpenIfcInfraConfigEditor()
        {
            Editor editor = Manager.DocEditor;

            try
            {
                IfcInfraConfigEditorContext context = new IfcInfraConfigEditorContext();
                context.Initialize();

                IfcInfraConfigEditorWindow window = new IfcInfraConfigEditorWindow(context);
                bool? confirmed = AutoCadWpfDialogHost.ShowModal(window);

                if (confirmed != true)
                {
                    editor.WriteMessage("\nEdicao da configuracao IFC cancelada pelo usuario.");
                    return;
                }

                editor.WriteMessage($"\nConfiguracao IFC salva em: {context.ConfigFilePath}");

                if (context.RunExportAfterSave)
                {
                    editor.WriteMessage("\nAbrindo o comando IFCINFRAEXPORT...");
                    AcadApp.DocumentManager.MdiActiveDocument.SendStringToExecute("IFCINFRAEXPORT ", true, false, false);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD: {ex.Message}");
                AcadApp.ShowAlertDialog("Erro AutoCAD ao abrir o editor IFC:\n" + ex.Message);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro geral: {ex.Message}");
                AcadApp.ShowAlertDialog("Falha ao abrir o editor da configuracao IFC:\n" + ex.Message);
            }
        }
    }
}
