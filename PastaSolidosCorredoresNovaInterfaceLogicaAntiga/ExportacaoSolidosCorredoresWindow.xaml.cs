using System;
using System.IO;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

using AutomacoesCivil3D;
using AutomacoesCivil3D.loin;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    public partial class ExportacaoSolidosCorredoresWindow : Window
    {
        private readonly ExportacaoSolidosCorredoresDialogViewModel _viewModel;

        public ExportacaoSolidosCorredoresWindow(ExportacaoSolidosCorredoresDialogViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
        }

        public ExportacaoSolidosCorredoresRequest BuildRequest()
        {
            return _viewModel.BuildRequest();
        }

        public void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Selecionar DWG de destino",
                Filter = "Arquivos DWG (*.dwg)|*.dwg",
                CheckFileExists = false,
                FileName = SafeGetFileName(_viewModel.DestinationPath),
                InitialDirectory = SafeGetDirectory(_viewModel.DestinationPath)
            };

            if (dialog.ShowDialog(this) == true)
            {
                _viewModel.SetDestinationPath(dialog.FileName);
            }
        }

        public void BrowseReport_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Salvar relatório CSV",
                Filter = "CSV (*.csv)|*.csv",
                FileName = SafeGetFileName(_viewModel.ReportPath),
                InitialDirectory = SafeGetDirectory(_viewModel.ReportPath)
            };

            if (dialog.ShowDialog(this) == true)
            {
                _viewModel.ReportPath = dialog.FileName;
            }
        }

        public void SelectAllVisible_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectAllVisible();
        }

        public void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearVisibleSelection();
        }

        public void InvertVisibleSelection_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.InvertVisibleSelection();
        }

        public void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public void Export_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.Validate())
            {
                return;
            }

            DialogResult = true;
            Close();
        }

        public static string SafeGetDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    string? directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        return directory;
                    }
                }
            }
            catch
            {
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public static string SafeGetFileName(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    string fileName = Path.GetFileName(path);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        return fileName;
                    }
                }
            }
            catch
            {
            }

            return "Exportacao_Solidos.dwg";
        }
    }
}



