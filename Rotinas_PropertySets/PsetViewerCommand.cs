using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using System.Drawing;
using System.Windows.Forms;

// Certifique-se de que este namespace corresponde ao seu projeto
namespace AutomacoesCivil3D.Rotinas_PropertySets
{
    public class PsetViewerCommand
    {
        private static PaletteSet _viewerPalette;

        [CommandMethod("AWPViewer")] // Novo comando para abrir a interface do visualizador
        public void ShowPsetViewer()
        {
            if (_viewerPalette == null)
            {
                _viewerPalette = new PaletteSet("Visualizador de Psets AWP");
                _viewerPalette.Style = PaletteSetStyles.ShowAutoHideButton
                                     | PaletteSetStyles.ShowCloseButton
                                     | PaletteSetStyles.NameEditable
                                     | PaletteSetStyles.ShowPropertiesMenu;
                // Defina um tamanho mínimo razoável para a janela do visualizador
                _viewerPalette.MinimumSize = new Size(800, 600);

                PsetViewerControl viewerControl = new PsetViewerControl();
                _viewerPalette.Add("Objetos com Psets", viewerControl);
                _viewerPalette.Visible = true;
            }
            else
            {
                if (!_viewerPalette.Visible)
                {
                    _viewerPalette.Visible = true;
                }
            }

            _viewerPalette.Activate(0);
        }
    }
}