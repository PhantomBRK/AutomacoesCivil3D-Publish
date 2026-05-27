using AutomacoesCivil3D;
using System;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomacoesCivil3D
{
    public partial class IfcInfraConfigEditorWindow : Window
    {
        private readonly IfcInfraConfigEditorContext _context;

        internal IfcInfraConfigEditorWindow(IfcInfraConfigEditorContext context)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            DataContext = _context;
        }

        private void ChooseConfigFile_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Selecionar arquivo IfcInfraConfiguration.json",
                Filter = "JSON (*.json)|*.json",
                FileName = SafeGetFileName(_context.ConfigFilePath, "IfcInfraConfiguration.json"),
                InitialDirectory = ResolveExistingDirectory(_context.ConfigFilePath, _context.DrawingFolder)
            };

            if (dialog.ShowDialog(this) == true)
            {
                _context.LoadFromPathOrSample(dialog.FileName);
            }
        }

        private void ChooseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog
            {
                Description = "Selecionar pasta padrao para exportacao IFC",
                UseDescriptionForTitle = true,
                SelectedPath = ResolveOutputFolderInitialDirectory()
            };

            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                _context.General.DefaultOutputFolder = MakeRelativeToDrawingFolder(dialog.SelectedPath);
            }
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            _context.ReloadCurrentSource();
        }

        private void LoadSample_Click(object sender, RoutedEventArgs e)
        {
            _context.LoadFromSample(_context.ConfigFilePath);
        }

        private void ApplyPreset_Click(object sender, RoutedEventArgs e)
        {
            _context.ApplyRecommendedPreset();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _context.RunExportAfterSave = false;
            if (_context.TrySave())
            {
                DialogResult = true;
                Close();
            }
        }

        private void SaveAndExport_Click(object sender, RoutedEventArgs e)
        {
            _context.RunExportAfterSave = true;
            if (_context.TrySave())
            {
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private string ResolveOutputFolderInitialDirectory()
        {
            string outputFolder = _context.General.DefaultOutputFolder ?? string.Empty;

            if (Path.IsPathRooted(outputFolder) && Directory.Exists(outputFolder))
            {
                return outputFolder;
            }

            if (!string.IsNullOrWhiteSpace(_context.DrawingFolder) && !string.IsNullOrWhiteSpace(outputFolder))
            {
                string combined = Path.GetFullPath(Path.Combine(_context.DrawingFolder, outputFolder));
                if (Directory.Exists(combined))
                {
                    return combined;
                }
            }

            return ResolveExistingDirectory(_context.ConfigFilePath, _context.DrawingFolder);
        }

        private string MakeRelativeToDrawingFolder(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath) || string.IsNullOrWhiteSpace(_context.DrawingFolder))
            {
                return selectedPath;
            }

            string normalizedBase = AppendDirectorySeparator(_context.DrawingFolder);
            string normalizedTarget = AppendDirectorySeparator(selectedPath);
            Uri baseUri = new Uri(normalizedBase, UriKind.Absolute);
            Uri targetUri = new Uri(normalizedTarget, UriKind.Absolute);

            if (!baseUri.IsBaseOf(targetUri))
            {
                return selectedPath;
            }

            string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString())
                .Replace('/', '\\')
                .TrimEnd('\\');

            return string.IsNullOrWhiteSpace(relative) ? @".\" : $@".\{relative}";
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string ResolveExistingDirectory(string? filePath, string? fallbackDirectory)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    string? directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        return directory;
                    }
                }
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(fallbackDirectory) && Directory.Exists(fallbackDirectory))
            {
                return fallbackDirectory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

  

        private static string SafeGetFileName(string? filePath, string fallbackName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    string fileName = Path.GetFileName(filePath);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        return fileName;
                    }
                }
            }
            catch
            {
            }

            return fallbackName;
        }
    }
}
