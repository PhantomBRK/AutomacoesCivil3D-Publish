using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

using AutomacoesCivil3D;

namespace AutomacoesCivil3D.loin
{
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class CorridorExportItem : BindableBase
    {
        private bool _isSelected;

        public CorridorExportItem(ObjectId corridorId, string name, int shapeCodeCount, int linkCodeCount, string sourceTypeLabel)
        {
            CorridorId = corridorId;
            Name = name;
            ShapeCodeCount = shapeCodeCount;
            LinkCodeCount = linkCodeCount;
            SourceTypeLabel = sourceTypeLabel;
            _isSelected = true;
        }

        public ObjectId CorridorId { get; }
        public string Name { get; }
        public int ShapeCodeCount { get; }
        public int LinkCodeCount { get; }
        public int TotalCodeCount => ShapeCodeCount + LinkCodeCount;
        public string SourceTypeLabel { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public sealed class PropertySetStatusInfo
    {
        public PropertySetStatusInfo(string name, bool isRequired, bool isAvailable)
        {
            Name = name;
            IsRequired = isRequired;
            IsAvailable = isAvailable;
            Caption = isAvailable
                ? (isRequired ? "essencial" : "complementar")
                : (isRequired ? "ausente" : "não encontrado");

            Background = isAvailable
                ? BrushFrom(isRequired ? "#DDF1F2" : "#EAF1F7")
                : BrushFrom(isRequired ? "#F9E3E3" : "#F7EFE5");

            Foreground = isAvailable
                ? BrushFrom(isRequired ? "#145B64" : "#355166")
                : BrushFrom(isRequired ? "#8C3535" : "#7B583F");
        }

        public string Name { get; }
        public bool IsRequired { get; }
        public bool IsAvailable { get; }
        public string Caption { get; }
        public Brush Background { get; }
        public Brush Foreground { get; }

        public static SolidColorBrush BrushFrom(string color)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        }
    }

    public sealed class ExportacaoSolidosCorredoresDialogData
    {
        public ExportacaoSolidosCorredoresDialogData(
            IReadOnlyList<CorridorExportItem> corridors,
            IReadOnlyList<PropertySetStatusInfo> propertySetStatuses,
            string activeDrawingPath,
            string suggestedDestinationPath,
            string suggestedReportPath,
            string? blockingIssue)
        {
            Corridors = corridors;
            PropertySetStatuses = propertySetStatuses;
            ActiveDrawingPath = activeDrawingPath;
            SuggestedDestinationPath = suggestedDestinationPath;
            SuggestedReportPath = suggestedReportPath;
            BlockingIssue = blockingIssue ?? string.Empty;
        }

        public IReadOnlyList<CorridorExportItem> Corridors { get; }
        public IReadOnlyList<PropertySetStatusInfo> PropertySetStatuses { get; }
        public string ActiveDrawingPath { get; }
        public string SuggestedDestinationPath { get; }
        public string SuggestedReportPath { get; }
        public string BlockingIssue { get; }
        public bool HasBlockingIssue => !string.IsNullOrWhiteSpace(BlockingIssue);
    }

    public sealed class ExportacaoSolidosCorredoresRequest
    {
        public string DestinationPath { get; set; } = string.Empty;
        public string ReportPath { get; set; } = string.Empty;
        public bool GenerateReport { get; set; }
        public bool ExportShapes { get; set; }
        public bool ExportLinks { get; set; }
        public bool RemoveSourceSolidsAfterCopy { get; set; }
        public IReadOnlyList<ObjectId> CorridorIds { get; set; } = Array.Empty<ObjectId>();
    }

    public sealed class ExportacaoSolidosCorredoresDialogViewModel : BindableBase
    {
        private readonly ObservableCollection<CorridorExportItem> _corridors;
        private readonly string _activeDrawingPath;
        private string _corridorFilterText = string.Empty;
        private string _destinationPath;
        private string _reportPath;
        private string _lastSuggestedReportPath;
        private bool _generateReport;
        private bool _exportShapes = true;
        private bool _exportLinks = true;
        // Default true por decisão de UX: limpar sólidos temporários é o comportamento
        // esperado depois da cópia para o DWG destino (a cópia já aconteceu antes).
        private bool _removeSourceSolidsAfterCopy = true;
        private string _statusMessage = string.Empty;
        private Brush _statusBackground;
        private Brush _statusForeground;

        public ExportacaoSolidosCorredoresDialogViewModel(ExportacaoSolidosCorredoresDialogData data)
        {
            _corridors = new ObservableCollection<CorridorExportItem>(data.Corridors);
            PropertySetStatuses = new ReadOnlyCollection<PropertySetStatusInfo>(data.PropertySetStatuses.ToList());
            CorridorsView = CollectionViewSource.GetDefaultView(_corridors);
            CorridorsView.Filter = FilterCorridor;

            _activeDrawingPath = data.ActiveDrawingPath;
            _destinationPath = data.SuggestedDestinationPath;
            _reportPath = data.SuggestedReportPath;
            _lastSuggestedReportPath = data.SuggestedReportPath;
            _statusBackground = BrushFrom("#EEF4FA");
            _statusForeground = BrushFrom("#173042");

            foreach (CorridorExportItem item in _corridors)
            {
                item.PropertyChanged += CorridorOnPropertyChanged;
            }

            if (data.HasBlockingIssue)
            {
                SetStatus(data.BlockingIssue, "#F9E3E3", "#8C3535");
            }
            else
            {
                SetStatus("Selecione os corredores que deseja exportar e confirme os caminhos de saída.", "#EEF4FA", "#173042");
            }
            OnValidationStateChanged();
        }

        public ICollectionView CorridorsView { get; }
        public ReadOnlyCollection<PropertySetStatusInfo> PropertySetStatuses { get; }

        public string DestinationPath
        {
            get => _destinationPath;
            set
            {
                if (SetProperty(ref _destinationPath, value))
                {
                    SuggestReportPath();
                    OnValidationStateChanged();
                }
            }
        }

        public string ReportPath
        {
            get => _reportPath;
            set
            {
                if (SetProperty(ref _reportPath, value))
                {
                    OnValidationStateChanged();
                }
            }
        }

        public string CorridorFilterText
        {
            get => _corridorFilterText;
            set
            {
                if (SetProperty(ref _corridorFilterText, value))
                {
                    CorridorsView.Refresh();
                    OnPropertyChanged(nameof(FilteredCorridorsLabel));
                }
            }
        }

        public bool GenerateReport
        {
            get => _generateReport;
            set
            {
                if (SetProperty(ref _generateReport, value))
                {
                    OnValidationStateChanged();
                }
            }
        }

        public bool ExportShapes
        {
            get => _exportShapes;
            set
            {
                if (SetProperty(ref _exportShapes, value))
                {
                    OnValidationStateChanged();
                }
            }
        }

        public bool ExportLinks
        {
            get => _exportLinks;
            set
            {
                if (SetProperty(ref _exportLinks, value))
                {
                    OnValidationStateChanged();
                }
            }
        }

        public bool RemoveSourceSolidsAfterCopy
        {
            get => _removeSourceSolidsAfterCopy;
            set => SetProperty(ref _removeSourceSolidsAfterCopy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public Brush StatusBackground
        {
            get => _statusBackground;
            private set => SetProperty(ref _statusBackground, value);
        }

        public Brush StatusForeground
        {
            get => _statusForeground;
            private set => SetProperty(ref _statusForeground, value);
        }

        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
        public int SelectedCorridorsCount => _corridors.Count(c => c.IsSelected);
        public int SelectedCodeEstimate => _corridors.Where(c => c.IsSelected).Sum(c => c.TotalCodeCount);
        public int SelectedShapeCorridorsCount => _corridors.Count(c => c.IsSelected && c.ShapeCodeCount > 0);
        public int SelectedLinkCorridorsCount => _corridors.Count(c => c.IsSelected && c.LinkCodeCount > 0);
        public string FilteredCorridorsLabel => $"{GetVisibleCount()} de {_corridors.Count} corredores";
        public bool CanExport => ComputeCanExport();

        public void SetDestinationPath(string path)
        {
            DestinationPath = path;
        }

        public ExportacaoSolidosCorredoresRequest BuildRequest()
        {
            return new ExportacaoSolidosCorredoresRequest
            {
                DestinationPath = DestinationPath.Trim(),
                ReportPath = ReportPath.Trim(),
                GenerateReport = GenerateReport,
                ExportShapes = ExportShapes,
                ExportLinks = ExportLinks,
                RemoveSourceSolidsAfterCopy = RemoveSourceSolidsAfterCopy,
                CorridorIds = _corridors.Where(c => c.IsSelected).Select(c => c.CorridorId).ToList()
            };
        }

        public bool Validate()
        {
            string? error = GetValidationError();
            if (!string.IsNullOrWhiteSpace(error))
            {
                SetStatus(error, "#F9E3E3", "#8C3535");
                return false;
            }

            SetStatus("Configuração validada. A rotina está pronta para exportar os sólidos selecionados.", "#DDF1F2", "#145B64");
            return true;
        }

        public void SelectAllVisible()
        {
            foreach (CorridorExportItem item in CorridorsView.Cast<CorridorExportItem>())
            {
                item.IsSelected = true;
            }
        }

        public void ClearVisibleSelection()
        {
            foreach (CorridorExportItem item in CorridorsView.Cast<CorridorExportItem>())
            {
                item.IsSelected = false;
            }
        }

        public void InvertVisibleSelection()
        {
            foreach (CorridorExportItem item in CorridorsView.Cast<CorridorExportItem>())
            {
                item.IsSelected = !item.IsSelected;
            }
        }

        public bool FilterCorridor(object obj)
        {
            if (obj is not CorridorExportItem item)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(CorridorFilterText))
            {
                return true;
            }

            return item.Name.Contains(CorridorFilterText.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public void CorridorOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CorridorExportItem.IsSelected))
            {
                OnValidationStateChanged();
            }
        }

        public void SuggestReportPath()
        {
            string newSuggestion = AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES.ExportacaoSolidosCorredoresService.BuildDefaultCsvPath(DestinationPath);
            if (string.IsNullOrWhiteSpace(_reportPath) || string.Equals(_reportPath, _lastSuggestedReportPath, StringComparison.OrdinalIgnoreCase))
            {
                _reportPath = newSuggestion;
                OnPropertyChanged(nameof(ReportPath));
            }

            _lastSuggestedReportPath = newSuggestion;
        }

        public void OnValidationStateChanged()
        {
            OnPropertyChanged(nameof(SelectedCorridorsCount));
            OnPropertyChanged(nameof(SelectedCodeEstimate));
            OnPropertyChanged(nameof(SelectedShapeCorridorsCount));
            OnPropertyChanged(nameof(SelectedLinkCorridorsCount));
            OnPropertyChanged(nameof(CanExport));
            RefreshStatusFromValidation();
        }

        public void RefreshStatusFromValidation()
        {
            string? error = GetValidationError();
            if (!string.IsNullOrWhiteSpace(error))
            {
                SetStatus(error, "#F9E3E3", "#8C3535");
                return;
            }

            SetStatus("Configuração validada. A rotina está pronta para exportar os sólidos selecionados.", "#DDF1F2", "#145B64");
        }

        public bool ComputeCanExport()
        {
            return string.IsNullOrWhiteSpace(GetValidationError());
        }

        public string? GetValidationError()
        {
            if (PropertySetStatuses.Any(p => p.IsRequired && !p.IsAvailable))
            {
                string missing = string.Join(", ", PropertySetStatuses.Where(p => p.IsRequired && !p.IsAvailable).Select(p => p.Name));
                return $"Os Property Sets essenciais não foram encontrados no desenho ativo: {missing}.";
            }

            if (SelectedCorridorsCount == 0)
            {
                return "Selecione ao menos um corredor para continuar.";
            }

            if (!ExportShapes && !ExportLinks)
            {
                return "Marque ao menos um tipo de geometria: shapes, links ou ambos.";
            }

            if (string.IsNullOrWhiteSpace(DestinationPath))
            {
                return "Informe o caminho do DWG de destino.";
            }

            if (!string.IsNullOrWhiteSpace(_activeDrawingPath) &&
                string.Equals(Path.GetFullPath(DestinationPath), Path.GetFullPath(_activeDrawingPath), StringComparison.OrdinalIgnoreCase))
            {
                return "O DWG de destino deve ser diferente do desenho ativo.";
            }

            if (GenerateReport && string.IsNullOrWhiteSpace(ReportPath))
            {
                return "Informe o caminho do relatório CSV ou desative a opção de relatório.";
            }

            return null;
        }

        public int GetVisibleCount()
        {
            int count = 0;
            foreach (object _ in CorridorsView)
            {
                count++;
            }

            return count;
        }

        public void SetStatus(string message, string background, string foreground)
        {
            StatusMessage = message;
            StatusBackground = BrushFrom(background);
            StatusForeground = BrushFrom(foreground);
            OnPropertyChanged(nameof(HasStatusMessage));
        }

        public static SolidColorBrush BrushFrom(string color)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        }
    }

    public static class ExportacaoSolidosCorredoresDesignData
    {
        public static ExportacaoSolidosCorredoresDialogViewModel Create()
        {
            ExportacaoSolidosCorredoresDialogData data = new ExportacaoSolidosCorredoresDialogData(
                new List<CorridorExportItem>
                {
                    new CorridorExportItem(ObjectId.Null, "CORR_EIXO_PRINCIPAL", 18, 7, "Local"),
                    new CorridorExportItem(ObjectId.Null, "CORR_ACESSO_NORTE", 9, 4, "Local"),
                    new CorridorExportItem(ObjectId.Null, "CORR_RAMAL_01", 6, 2, "Local")
                },
                new List<PropertySetStatusInfo>
                {
                    // Essenciais: PSets nativos do Civil 3D (anexados automaticamente
                    // pelo corridor.ExportSolids()).
                    new PropertySetStatusInfo("Corridor Identity", true, true),
                    new PropertySetStatusInfo("Corridor Model Information", true, true),
                    new PropertySetStatusInfo("Corridor Property Data – User Defined", true, true),
                    new PropertySetStatusInfo("Corridor Shape Information", true, true),
                    // Informativos: PSets do template LOIN novo (não bloqueiam a
                    // exportação; quando ausentes, o LoinCivil3DApplier os cria/atualiza).
                    new PropertySetStatusInfo("Pset_A - Dados de Projeto", false, true),
                    new PropertySetStatusInfo("Pset_B - Informacoes dos Elementos", false, true),
                    new PropertySetStatusInfo("Pset_C - Propriedades Fisicas", false, true),
                    new PropertySetStatusInfo("Pset_D - Layer IFC e Classificacao", false, true),
                    new PropertySetStatusInfo("Pset_Requisitos por Elemento", false, true),
                },
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModeloOrigem.dwg"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModeloOrigem_SOLIDOS_CORREDORES.dwg"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModeloOrigem_SOLIDOS_CORREDORES_RelatorioPSET.csv"),
                string.Empty);

            return new ExportacaoSolidosCorredoresDialogViewModel(data);
        }
    }
}



