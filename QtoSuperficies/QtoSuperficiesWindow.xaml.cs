using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomacoesCivil3D
{
    public partial class QtoSuperficiesWindow : Window
    {
        private readonly SurfaceMaterialQuantitiesDialogViewModel _viewModel;

        public QtoSuperficiesWindow()
            : this(SurfaceMaterialQuantitiesDesignData.Create())
        {
        }

        public QtoSuperficiesWindow(SurfaceMaterialQuantitiesDialogViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
        }

        public SurfaceMaterialQuantitiesRequest BuildRequest()
        {
            return _viewModel.BuildRequest();
        }

        private void BrowsePav_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = BuildCsvDialog("Salvar CSV de pavimentacao", _viewModel.PavCsvPath, "Quantitativo_PAV.csv");
            if (dialog.ShowDialog(this) == true)
            {
                _viewModel.SetPavCsvPath(dialog.FileName);
            }
        }

        private void BrowseTrp_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = BuildCsvDialog("Salvar CSV de terraplenagem", _viewModel.TrpCsvPath, "Quantitativo_TRP.csv");
            if (dialog.ShowDialog(this) == true)
            {
                _viewModel.SetTrpCsvPath(dialog.FileName);
            }
        }

        private void AddEntry_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddBlankEntry();
        }

        private void RemoveEntries_Click(object sender, RoutedEventArgs e)
        {
            List<SurfaceMaterialQuantityRow> selectedRows = MappingsGrid.SelectedItems
                .OfType<SurfaceMaterialQuantityRow>()
                .ToList();

            _viewModel.RemoveEntries(selectedRows);
        }

        private void SelectAllVisible_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectAllVisible();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearVisibleSelection();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.Validate())
            {
                return;
            }

            DialogResult = true;
            Close();
        }

        private static SaveFileDialog BuildCsvDialog(string title, string currentPath, string fallbackFileName)
        {
            return new SaveFileDialog
            {
                Title = title,
                Filter = "CSV (*.csv)|*.csv",
                FileName = SafeGetFileName(currentPath, fallbackFileName),
                InitialDirectory = SafeGetDirectory(currentPath)
            };
        }

        private static string SafeGetDirectory(string path)
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

        private static string SafeGetFileName(string path, string fallbackFileName)
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

            return fallbackFileName;
        }

        private void MappingsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
    }
}
