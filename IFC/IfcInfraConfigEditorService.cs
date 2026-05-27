using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AutomacoesCivil3D
{
    internal abstract class ObservableObject : INotifyPropertyChanged
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

    internal sealed class IfcExportGeneralSettings : ObservableObject
    {
        private string _defaultOutputFolder = @".\ExportIFC4x3";
        private string _ifcVersion = "IFC4X3_ADD2";
        private bool _generateLogFile = true;
        private bool _defaultIfcZip;
        private bool _automaticSaveDocument = true;
        private bool _abortIfOutOfDate = true;
        private bool _exportPropertiesFromCivilEntityProperties = true;
        private bool _exportMaterials = true;
        private bool _visibleLayersOnly;
        private bool _exportNonEpsgProjectedCoordinateSystem = true;
        private bool _basePointMinExtents = true;
        private bool _projectBasePointOnRootPlacement;
        private bool _generateOwnerHistory = true;
        private string _facetDistanceToleranceMetric = "0.002";
        private string _geometryDeviationTolerance = "0.0005";
        private string _basePointWarningKilometers = "16";
        private string _basePointErrorKilometers = "500";
        private string _propertyListDelimiters = ";";

        public string DefaultOutputFolder
        {
            get => _defaultOutputFolder;
            set => SetProperty(ref _defaultOutputFolder, value);
        }

        public string IfcVersion
        {
            get => _ifcVersion;
            set => SetProperty(ref _ifcVersion, value);
        }

        public bool GenerateLogFile
        {
            get => _generateLogFile;
            set => SetProperty(ref _generateLogFile, value);
        }

        public bool DefaultIfcZip
        {
            get => _defaultIfcZip;
            set => SetProperty(ref _defaultIfcZip, value);
        }

        public bool AutomaticSaveDocument
        {
            get => _automaticSaveDocument;
            set => SetProperty(ref _automaticSaveDocument, value);
        }

        public bool AbortIfOutOfDate
        {
            get => _abortIfOutOfDate;
            set => SetProperty(ref _abortIfOutOfDate, value);
        }

        public bool ExportPropertiesFromCivilEntityProperties
        {
            get => _exportPropertiesFromCivilEntityProperties;
            set => SetProperty(ref _exportPropertiesFromCivilEntityProperties, value);
        }

        public bool ExportMaterials
        {
            get => _exportMaterials;
            set => SetProperty(ref _exportMaterials, value);
        }

        public bool VisibleLayersOnly
        {
            get => _visibleLayersOnly;
            set => SetProperty(ref _visibleLayersOnly, value);
        }

        public bool ExportNonEpsgProjectedCoordinateSystem
        {
            get => _exportNonEpsgProjectedCoordinateSystem;
            set => SetProperty(ref _exportNonEpsgProjectedCoordinateSystem, value);
        }

        public bool BasePointMinExtents
        {
            get => _basePointMinExtents;
            set => SetProperty(ref _basePointMinExtents, value);
        }

        public bool ProjectBasePointOnRootPlacement
        {
            get => _projectBasePointOnRootPlacement;
            set => SetProperty(ref _projectBasePointOnRootPlacement, value);
        }

        public bool GenerateOwnerHistory
        {
            get => _generateOwnerHistory;
            set => SetProperty(ref _generateOwnerHistory, value);
        }

        public string FacetDistanceToleranceMetric
        {
            get => _facetDistanceToleranceMetric;
            set => SetProperty(ref _facetDistanceToleranceMetric, value);
        }

        public string GeometryDeviationTolerance
        {
            get => _geometryDeviationTolerance;
            set => SetProperty(ref _geometryDeviationTolerance, value);
        }

        public string BasePointWarningKilometers
        {
            get => _basePointWarningKilometers;
            set => SetProperty(ref _basePointWarningKilometers, value);
        }

        public string BasePointErrorKilometers
        {
            get => _basePointErrorKilometers;
            set => SetProperty(ref _basePointErrorKilometers, value);
        }

        public string PropertyListDelimiters
        {
            get => _propertyListDelimiters;
            set => SetProperty(ref _propertyListDelimiters, value);
        }
    }

    internal sealed class IfcExportContentSettings : ObservableObject
    {
        private bool _exportAlignments = true;
        private bool _exportCorridors = true;
        private bool _exportCorridorLinks = true;
        private bool _exportCorridorFeatureLines;
        private bool _exportCorridorShapesAsFallBackGeometry = true;
        private bool _exportCorridorShapesAsSweptGeometry = true;
        private bool _exportFeatureLines = true;
        private bool _exportSurfaces = true;
        private bool _exportSolids = true;
        private bool _exportBodies = true;
        private bool _exportAutoCadSurfaces = true;
        private bool _exportSubDMesh = true;
        private bool _exportCogoPoints;
        private bool _exportBlockReferences;
        private bool _exportPoints;
        private bool _exportPolylines;

        public bool ExportAlignments
        {
            get => _exportAlignments;
            set => SetProperty(ref _exportAlignments, value);
        }

        public bool ExportCorridors
        {
            get => _exportCorridors;
            set => SetProperty(ref _exportCorridors, value);
        }

        public bool ExportCorridorLinks
        {
            get => _exportCorridorLinks;
            set => SetProperty(ref _exportCorridorLinks, value);
        }

        public bool ExportCorridorFeatureLines
        {
            get => _exportCorridorFeatureLines;
            set => SetProperty(ref _exportCorridorFeatureLines, value);
        }

        public bool ExportCorridorShapesAsFallBackGeometry
        {
            get => _exportCorridorShapesAsFallBackGeometry;
            set => SetProperty(ref _exportCorridorShapesAsFallBackGeometry, value);
        }

        public bool ExportCorridorShapesAsSweptGeometry
        {
            get => _exportCorridorShapesAsSweptGeometry;
            set => SetProperty(ref _exportCorridorShapesAsSweptGeometry, value);
        }

        public bool ExportFeatureLines
        {
            get => _exportFeatureLines;
            set => SetProperty(ref _exportFeatureLines, value);
        }

        public bool ExportSurfaces
        {
            get => _exportSurfaces;
            set => SetProperty(ref _exportSurfaces, value);
        }

        public bool ExportSolids
        {
            get => _exportSolids;
            set => SetProperty(ref _exportSolids, value);
        }

        public bool ExportBodies
        {
            get => _exportBodies;
            set => SetProperty(ref _exportBodies, value);
        }

        public bool ExportAutoCadSurfaces
        {
            get => _exportAutoCadSurfaces;
            set => SetProperty(ref _exportAutoCadSurfaces, value);
        }

        public bool ExportSubDMesh
        {
            get => _exportSubDMesh;
            set => SetProperty(ref _exportSubDMesh, value);
        }

        public bool ExportCogoPoints
        {
            get => _exportCogoPoints;
            set => SetProperty(ref _exportCogoPoints, value);
        }

        public bool ExportBlockReferences
        {
            get => _exportBlockReferences;
            set => SetProperty(ref _exportBlockReferences, value);
        }

        public bool ExportPoints
        {
            get => _exportPoints;
            set => SetProperty(ref _exportPoints, value);
        }

        public bool ExportPolylines
        {
            get => _exportPolylines;
            set => SetProperty(ref _exportPolylines, value);
        }
    }

    internal sealed class IfcProjectSettings : ObservableObject
    {
        private string _name = string.Empty;
        private string _longName = string.Empty;
        private string _description = string.Empty;
        private string _objectType = string.Empty;
        private string _phase = string.Empty;
        private string _globalId = string.Empty;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string LongName
        {
            get => _longName;
            set => SetProperty(ref _longName, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string ObjectType
        {
            get => _objectType;
            set => SetProperty(ref _objectType, value);
        }

        public string Phase
        {
            get => _phase;
            set => SetProperty(ref _phase, value);
        }

        public string GlobalId
        {
            get => _globalId;
            set => SetProperty(ref _globalId, value);
        }
    }

    internal sealed class IfcFacilitySettings : ObservableObject
    {
        private string _ifcExportAs = string.Empty;
        private string _name = string.Empty;
        private string _longName = string.Empty;
        private string _description = string.Empty;
        private string _objectType = string.Empty;
        private string _globalId = string.Empty;

        public string IfcExportAs
        {
            get => _ifcExportAs;
            set => SetProperty(ref _ifcExportAs, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string LongName
        {
            get => _longName;
            set => SetProperty(ref _longName, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string ObjectType
        {
            get => _objectType;
            set => SetProperty(ref _objectType, value);
        }

        public string GlobalId
        {
            get => _globalId;
            set => SetProperty(ref _globalId, value);
        }
    }

    internal sealed class IfcFacilityAddressSettings : ObservableObject
    {
        private string _town = string.Empty;
        private string _region = string.Empty;
        private string _country = string.Empty;
        private string _postalCode = string.Empty;
        private string _addressLines = string.Empty;
        private string _telephoneNumbers = string.Empty;
        private string _electronicMailAddresses = string.Empty;
        private string _wwwHomePageUrl = string.Empty;

        public string Town
        {
            get => _town;
            set => SetProperty(ref _town, value);
        }

        public string Region
        {
            get => _region;
            set => SetProperty(ref _region, value);
        }

        public string Country
        {
            get => _country;
            set => SetProperty(ref _country, value);
        }

        public string PostalCode
        {
            get => _postalCode;
            set => SetProperty(ref _postalCode, value);
        }

        public string AddressLines
        {
            get => _addressLines;
            set => SetProperty(ref _addressLines, value);
        }

        public string TelephoneNumbers
        {
            get => _telephoneNumbers;
            set => SetProperty(ref _telephoneNumbers, value);
        }

        public string ElectronicMailAddresses
        {
            get => _electronicMailAddresses;
            set => SetProperty(ref _electronicMailAddresses, value);
        }

        public string WwwHomePageUrl
        {
            get => _wwwHomePageUrl;
            set => SetProperty(ref _wwwHomePageUrl, value);
        }
    }

    internal sealed class IfcAuthorSettings : ObservableObject
    {
        private string _organizationName = string.Empty;
        private string _organizationIdentification = string.Empty;
        private string _organizationEmail = string.Empty;
        private string _givenName = string.Empty;
        private string _familyName = string.Empty;
        private string _identification = string.Empty;
        private string _authorization = string.Empty;

        public string OrganizationName
        {
            get => _organizationName;
            set => SetProperty(ref _organizationName, value);
        }

        public string OrganizationIdentification
        {
            get => _organizationIdentification;
            set => SetProperty(ref _organizationIdentification, value);
        }

        public string OrganizationEmail
        {
            get => _organizationEmail;
            set => SetProperty(ref _organizationEmail, value);
        }

        public string GivenName
        {
            get => _givenName;
            set => SetProperty(ref _givenName, value);
        }

        public string FamilyName
        {
            get => _familyName;
            set => SetProperty(ref _familyName, value);
        }

        public string Identification
        {
            get => _identification;
            set => SetProperty(ref _identification, value);
        }

        public string Authorization
        {
            get => _authorization;
            set => SetProperty(ref _authorization, value);
        }
    }

    internal sealed class IfcTemplateSettings : ObservableObject
    {
        private string _propertyTemplatePaths = string.Empty;
        private string _propertyManagementPaths = string.Empty;

        public string PropertyTemplatePaths

        {
            get => _propertyTemplatePaths;
            set => SetProperty(ref _propertyTemplatePaths, value);
        }

        public string PropertyManagementPaths
        {
            get => _propertyManagementPaths;
            set => SetProperty(ref _propertyManagementPaths, value);
        }
    }

    internal sealed class IfcProgressSettings : ObservableObject
    {
        private string _reportProgressMode = "Pane";
        private bool _verbose = true;

        public string ReportProgressMode
        {
            get => _reportProgressMode;
            set => SetProperty(ref _reportProgressMode, value);
        }

        public bool Verbose
        {
            get => _verbose;
            set => SetProperty(ref _verbose, value);
        }
    }

    internal sealed class IfcInfraConfigEditorContext : ObservableObject
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        private static readonly MediaBrush InfoBackgroundBrush = CreateBrush("#E8F4FB");
        private static readonly MediaBrush InfoForegroundBrush = CreateBrush("#11435D");
        private static readonly MediaBrush WarningBackgroundBrush = CreateBrush("#FFF4D8");
        private static readonly MediaBrush WarningForegroundBrush = CreateBrush("#735420");
        private static readonly MediaBrush ErrorBackgroundBrush = CreateBrush("#FDE7E7");
        private static readonly MediaBrush ErrorForegroundBrush = CreateBrush("#8E2B2B");

        private readonly string? _sampleFilePath;
        private readonly string? _drawingFilePath;
        private readonly string? _drawingFolder;
        private JsonObject _workingRoot = new JsonObject();
        private string _configFilePath = string.Empty;
        private string _sourceDescription = string.Empty;
        private string _statusMessage = string.Empty;
        private MediaBrush _statusBackground = InfoBackgroundBrush;
        private MediaBrush _statusForeground = InfoForegroundBrush;
        private bool _runExportAfterSave;

        public IfcInfraConfigEditorContext()
        {
            _sampleFilePath = ResolveSampleConfigPath();
            _drawingFilePath = ResolveDrawingPath();
            _drawingFolder = TryGetDirectory(_drawingFilePath);
            DrawingName = string.IsNullOrWhiteSpace(_drawingFilePath)
                ? "Sem desenho salvo"
                : Path.GetFileName(_drawingFilePath);
            SampleLocation = _sampleFilePath ?? "Amostra Autodesk nao encontrada";

            IfcVersionOptions = new ObservableCollection<string>();
            FacilityTypeOptions = new ObservableCollection<string>(new[]
            {
                string.Empty,
                "IfcSite",
                "IfcRoad",
                "IfcRailway",
                "IfcBridge",
                "IfcMarineFacility"
            });
            ProgressModeOptions = new ObservableCollection<string>(new[]
            {
                "Pane",
                "CommandLine",
                "None"
            });

            General = new IfcExportGeneralSettings();
            Content = new IfcExportContentSettings();
            Project = new IfcProjectSettings();
            Facility = new IfcFacilitySettings();
            FacilityAddress = new IfcFacilityAddressSettings();
            Author = new IfcAuthorSettings();
            Templates = new IfcTemplateSettings();
            Progress = new IfcProgressSettings();
        }

        public string DrawingName { get; }

        public string? DrawingFolder => _drawingFolder;

        public string SampleLocation { get; }

        public ObservableCollection<string> IfcVersionOptions { get; }

        public ObservableCollection<string> FacilityTypeOptions { get; }

        public ObservableCollection<string> ProgressModeOptions { get; }

        public IfcExportGeneralSettings General { get; }

        public IfcExportContentSettings Content { get; }

        public IfcProjectSettings Project { get; }

        public IfcFacilitySettings Facility { get; }

        public IfcFacilityAddressSettings FacilityAddress { get; }

        public IfcAuthorSettings Author { get; }

        public IfcTemplateSettings Templates { get; }

        public IfcProgressSettings Progress { get; }

        public string ConfigFilePath
        {
            get => _configFilePath;
            set => SetProperty(ref _configFilePath, value);
        }

        public string SourceDescription
        {
            get => _sourceDescription;
            private set => SetProperty(ref _sourceDescription, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public MediaBrush StatusBackground
        {
            get => _statusBackground;
            private set => SetProperty(ref _statusBackground, value);
        }

        public MediaBrush StatusForeground
        {
            get => _statusForeground;
            private set => SetProperty(ref _statusForeground, value);
        }

        public bool RunExportAfterSave
        {
            get => _runExportAfterSave;
            set => SetProperty(ref _runExportAfterSave, value);
        }

        public string RecommendedChecklist =>
            "Preset recomendado para infraestrutura:\n" +
            "IFC4X3_ADD2, log ativo, abortar se houver corredor/superficie desatualizado,\n" +
            "salvar GlobalIds, propriedades do Civil 3D, materiais, superficies, corredores,\n" +
            "solidos e georreferenciamento.";

        public string MetadataChecklist =>
            "Preencha pelo menos:\n" +
            "Projeto > Nome\n" +
            "Facility/Site > Nome e classe IFC\n" +
            "Autor > Organizacao e e-mail\n" +
            "Qualidade > pasta de saida e tolerancia metrica.";

        public void Initialize()
        {
            string preferredPath = ResolvePreferredConfigPath();
            LoadFromPathOrSample(preferredPath);
        }

        public void LoadFromPathOrSample(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                LoadFromFile(path);
                return;
            }

            LoadFromSample(path);
        }

        public void LoadFromFile(string path)
        {
            string rawJson = File.ReadAllText(path);
            JsonNode? parsed = JsonNode.Parse(rawJson, documentOptions: DocumentOptions);
            JsonObject? root = parsed as JsonObject;
            if (root == null)
            {
                throw new InvalidOperationException("O arquivo nao possui um objeto JSON na raiz.");
            }

            _workingRoot = root.DeepClone()!.AsObject();
            ConfigFilePath = path;
            SourceDescription = $"Carregado do arquivo do projeto: {path}";
            PopulateStateFromRoot(_workingRoot);
            SetStatus($"Configuracao IFC carregada de {path}.", StatusKind.Info);
        }

        public void LoadFromSample(string? targetPath = null)
        {
            JsonObject root = CreateEmptyRoot();

            if (!string.IsNullOrWhiteSpace(_sampleFilePath) && File.Exists(_sampleFilePath))
            {
                string rawJson = File.ReadAllText(_sampleFilePath);
                JsonNode? parsed = JsonNode.Parse(rawJson, documentOptions: DocumentOptions);
                if (parsed is JsonObject sampleRoot)
                {
                    root = sampleRoot.DeepClone()!.AsObject();
                }
            }

            _workingRoot = root;
            ConfigFilePath = string.IsNullOrWhiteSpace(targetPath) ? ResolvePreferredConfigPath() : targetPath;
            SourceDescription = string.IsNullOrWhiteSpace(_sampleFilePath)
                ? "Amostra Autodesk nao encontrada. Configuracao inicial criada pelo plugin."
                : $"Baseado na amostra Autodesk: {_sampleFilePath}";

            PopulateStateFromRoot(_workingRoot);
            ApplyRecommendedPreset(fillOnlyEmptyText: true);
            SetStatus("Configuracao inicial carregada a partir da amostra da Autodesk.", StatusKind.Info);
        }

        public void ReloadCurrentSource()
        {
            LoadFromPathOrSample(ConfigFilePath);
        }

        public void ApplyRecommendedPreset(bool fillOnlyEmptyText = false)
        {
            General.GenerateLogFile = true;
            General.DefaultIfcZip = false;
            General.AutomaticSaveDocument = true;
            General.AbortIfOutOfDate = true;
            General.ExportPropertiesFromCivilEntityProperties = true;
            General.ExportMaterials = true;
            General.VisibleLayersOnly = false;
            General.ExportNonEpsgProjectedCoordinateSystem = true;
            General.BasePointMinExtents = true;
            General.ProjectBasePointOnRootPlacement = false;
            General.GenerateOwnerHistory = true;
            General.PropertyListDelimiters = string.IsNullOrWhiteSpace(General.PropertyListDelimiters) ? ";" : General.PropertyListDelimiters;
            General.FacetDistanceToleranceMetric = SetTextDefault(General.FacetDistanceToleranceMetric, "0.002", fillOnlyEmptyText);
            General.GeometryDeviationTolerance = SetTextDefault(General.GeometryDeviationTolerance, "0.0005", fillOnlyEmptyText);
            General.BasePointWarningKilometers = SetTextDefault(General.BasePointWarningKilometers, "16", fillOnlyEmptyText);
            General.BasePointErrorKilometers = SetTextDefault(General.BasePointErrorKilometers, "500", fillOnlyEmptyText);
            General.DefaultOutputFolder = SetTextDefault(General.DefaultOutputFolder, @".\ExportIFC4x3", fillOnlyEmptyText);
            General.IfcVersion = string.IsNullOrWhiteSpace(General.IfcVersion) ? PreferredIfcVersion() : General.IfcVersion;

            Content.ExportAlignments = true;
            Content.ExportCorridors = true;
            Content.ExportCorridorLinks = true;
            Content.ExportCorridorFeatureLines = false;
            Content.ExportCorridorShapesAsFallBackGeometry = true;
            Content.ExportCorridorShapesAsSweptGeometry = true;
            Content.ExportFeatureLines = true;
            Content.ExportSurfaces = true;
            Content.ExportSolids = true;
            Content.ExportBodies = true;
            Content.ExportAutoCadSurfaces = true;
            Content.ExportSubDMesh = true;
            Content.ExportCogoPoints = false;
            Content.ExportBlockReferences = false;
            Content.ExportPoints = false;
            Content.ExportPolylines = false;

            if (string.IsNullOrWhiteSpace(Project.Name))
            {
                Project.Name = DeriveProjectName();
            }

            if (string.IsNullOrWhiteSpace(Project.LongName))
            {
                Project.LongName = Project.Name;
            }

            if (string.IsNullOrWhiteSpace(Facility.IfcExportAs))
            {
                Facility.IfcExportAs = "IfcRoad";
            }

            if (string.IsNullOrWhiteSpace(Facility.Name))
            {
                Facility.Name = Project.Name;
            }

            Progress.ReportProgressMode = string.IsNullOrWhiteSpace(Progress.ReportProgressMode) ? "Pane" : Progress.ReportProgressMode;
            Progress.Verbose = true;

            SetStatus("Preset recomendado para infraestrutura aplicado.", StatusKind.Info);
        }

        public bool TrySave()
        {
            try
            {
                if (!Validate(out string validationMessage))
                {
                    SetStatus(validationMessage, StatusKind.Warning);
                    return false;
                }

                string configPath = ConfigFilePath.Trim();
                string? directory = Path.GetDirectoryName(configPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    SetStatus("Nao foi possivel identificar a pasta do arquivo de configuracao.", StatusKind.Error);
                    return false;
                }

                Directory.CreateDirectory(directory);

                JsonObject saveRoot = _workingRoot.DeepClone()!.AsObject();
                ApplyStateToRoot(saveRoot);
                string jsonContent = saveRoot.ToJsonString(SerializerOptions);

                if (File.Exists(configPath))
                {
                    string currentContent = File.ReadAllText(configPath);
                    if (!string.Equals(currentContent, jsonContent, StringComparison.Ordinal))
                    {
                        File.WriteAllText(configPath + ".bak", currentContent);
                    }
                }

                File.WriteAllText(configPath, jsonContent);
                _workingRoot = saveRoot;
                SourceDescription = $"Ultimo salvamento: {configPath}";
                SetStatus($"Configuracao IFC salva em {configPath}.", StatusKind.Info);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"Falha ao salvar a configuracao IFC: {ex.Message}", StatusKind.Error);
                return false;
            }
        }

        private bool Validate(out string message)
        {
            if (string.IsNullOrWhiteSpace(ConfigFilePath))
            {
                message = "Informe o caminho do arquivo IfcInfraConfiguration.json.";
                return false;
            }

            if (!ConfigFilePath.Trim().EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                message = "O arquivo de configuracao precisa terminar com .json.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(General.IfcVersion))
            {
                message = "Selecione a versao IFC desejada.";
                return false;
            }

            if (!TryParseFlexibleDouble(General.FacetDistanceToleranceMetric, out _))
            {
                message = "A tolerancia metrica deve ser numerica.";
                return false;
            }

            if (!TryParseFlexibleDouble(General.GeometryDeviationTolerance, out _))
            {
                message = "A tolerancia de importacao/checagem geometrica deve ser numerica.";
                return false;
            }

            if (!TryParseFlexibleDouble(General.BasePointWarningKilometers, out _))
            {
                message = "O alerta de base point em km deve ser numerico.";
                return false;
            }

            if (!TryParseFlexibleDouble(General.BasePointErrorKilometers, out _))
            {
                message = "O limite de erro de base point em km deve ser numerico.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private void PopulateStateFromRoot(JsonObject root)
        {
            UpdateOptions(root);

            General.GenerateLogFile = GetBoolean(root, true, "GenerateLogFile");
            General.DefaultOutputFolder = GetString(root, @".\ExportIFC4x3", "Export", "DefaultOutputFolder");
            General.IfcVersion = GetString(root, PreferredIfcVersion(), "Export", "IfcVersion");
            General.DefaultIfcZip = GetBoolean(root, false, "Export", "DefaultIfcZip");
            General.AutomaticSaveDocument = GetBoolean(root, false, "Export", "AutomaticSaveDocument");
            General.AbortIfOutOfDate = GetBoolean(root, false, "Export", "AbortIfOutOfDate");
            General.ExportPropertiesFromCivilEntityProperties = GetBoolean(root, true, "Export", "ExportPropertiesFromCivilEntityProperties");
            General.ExportMaterials = GetBoolean(root, true, "Export", "ExportMaterials");
            General.VisibleLayersOnly = GetBoolean(root, false, "Export", "VisibleLayersOnly");
            General.ExportNonEpsgProjectedCoordinateSystem = GetBoolean(root, true, "Export", "ExportNonEPSGProjectedCoordinateSystem");
            General.BasePointMinExtents = GetBoolean(root, true, "Export", "BasePointMinExtents");
            General.ProjectBasePointOnRootPlacement = GetBoolean(root, false, "Export", "ProjectBasePointOnRootPlacement");
            General.GenerateOwnerHistory = GetBoolean(root, false, "Export", "GenerateOwnerHistory");
            General.FacetDistanceToleranceMetric = GetNumberAsString(root, "0.002", "Export", "FacetDistanceToleranceMetric");
            General.GeometryDeviationTolerance = GetNumberAsString(root, "0.0005", "Import", "GeometryDeviationTolerance");
            General.BasePointWarningKilometers = GetNumberAsString(root, "16", "Export", "BasePointWarningKilometers");
            General.BasePointErrorKilometers = GetNumberAsString(root, "500", "Export", "BasePointErrorKilometers");
            General.PropertyListDelimiters = GetString(root, ";", "Export", "PropertyListDelimiters");

            Content.ExportAlignments = GetBoolean(root, true, "Export", "ExportAlignments");
            Content.ExportCorridors = GetBoolean(root, true, "Export", "ExportCorridors");
            Content.ExportCorridorLinks = GetBoolean(root, true, "Export", "ExportCorridorLinks");
            Content.ExportCorridorFeatureLines = GetBoolean(root, false, "Export", "ExportCorridorFeatureLines");
            Content.ExportCorridorShapesAsFallBackGeometry = GetBoolean(root, true, "Export", "ExportCorridorShapesAsFallBackGeometry");
            Content.ExportCorridorShapesAsSweptGeometry = GetBoolean(root, true, "Export", "ExportCorridorShapesAsSweptGeometry");
            Content.ExportFeatureLines = GetBoolean(root, true, "Export", "ExportFeatureLines");
            Content.ExportSurfaces = GetBoolean(root, true, "Export", "ExportSurfaces");
            Content.ExportSolids = GetBoolean(root, true, "Export", "ExportSolids");
            Content.ExportBodies = GetBoolean(root, true, "Export", "ExportBodies");
            Content.ExportAutoCadSurfaces = GetBoolean(root, true, "Export", "ExportAutoCADSurfaces");
            Content.ExportSubDMesh = GetBoolean(root, true, "Export", "ExportSubDMesh");
            Content.ExportCogoPoints = GetBoolean(root, true, "Export", "ExportCogoPoints");
            Content.ExportBlockReferences = GetBoolean(root, true, "Export", "ExportBlockReferences");
            Content.ExportPoints = GetBoolean(root, true, "Export", "ExportPoints");
            Content.ExportPolylines = GetBoolean(root, true, "Export", "ExportPolylines");

            Project.Name = GetString(root, string.Empty, "Export", "ProjectAttributes", "Name");
            Project.LongName = GetString(root, string.Empty, "Export", "ProjectAttributes", "LongName");
            Project.Description = GetString(root, string.Empty, "Export", "ProjectAttributes", "Description");
            Project.ObjectType = GetString(root, string.Empty, "Export", "ProjectAttributes", "ObjectType");
            Project.Phase = GetString(root, string.Empty, "Export", "ProjectAttributes", "Phase");
            Project.GlobalId = GetString(root, string.Empty, "Export", "ProjectAttributes", "GlobalId");

            Facility.IfcExportAs = GetString(root, string.Empty, "Export", "FacilityAttributes", "IfcExportAs");
            Facility.Name = GetString(root, string.Empty, "Export", "FacilityAttributes", "Name");
            Facility.LongName = GetString(root, string.Empty, "Export", "FacilityAttributes", "LongName");
            Facility.Description = GetString(root, string.Empty, "Export", "FacilityAttributes", "Description");
            Facility.ObjectType = GetString(root, string.Empty, "Export", "FacilityAttributes", "ObjectType");
            Facility.GlobalId = GetString(root, string.Empty, "Export", "FacilityAttributes", "GlobalId");

            FacilityAddress.Town = GetString(root, string.Empty, "Export", "FacilityAttributes", "FacilityAddress", "Town");
            FacilityAddress.Region = GetString(root, string.Empty, "Export", "FacilityAttributes", "FacilityAddress", "Region");
            FacilityAddress.Country = GetString(root, string.Empty, "Export", "FacilityAttributes", "FacilityAddress", "Country");
            FacilityAddress.PostalCode = GetString(root, string.Empty, "Export", "FacilityAttributes", "FacilityAddress", "PostalCode");
            FacilityAddress.AddressLines = GetStringArray(root, "Export", "FacilityAttributes", "FacilityAddress", "AddressLines");
            FacilityAddress.TelephoneNumbers = GetStringArray(root, "Export", "FacilityAttributes", "FacilityAddress", "TelephoneNumbers");
            FacilityAddress.ElectronicMailAddresses = GetStringArray(root, "Export", "FacilityAttributes", "FacilityAddress", "ElectronicMailAddresses");
            FacilityAddress.WwwHomePageUrl = GetString(root, string.Empty, "Export", "FacilityAttributes", "FacilityAddress", "WWWHomePageURL");

            Author.Identification = GetString(root, string.Empty, "Export", "AuthorAttributes", "Identification");
            Author.FamilyName = GetString(root, string.Empty, "Export", "AuthorAttributes", "FamilyName");
            Author.GivenName = GetString(root, string.Empty, "Export", "AuthorAttributes", "GivenName");
            Author.OrganizationIdentification = GetString(root, string.Empty, "Export", "AuthorAttributes", "OrganizationIdentification");
            Author.OrganizationName = GetString(root, string.Empty, "Export", "AuthorAttributes", "OrganizationName");
            Author.OrganizationEmail = GetString(root, string.Empty, "Export", "AuthorAttributes", "OrganizationEmail");
            Author.Authorization = GetString(root, string.Empty, "Export", "AuthorAttributes", "Authorization");

            Templates.PropertyTemplatePaths = GetStringArray(root, "Export", "PropertyTemplatePaths");
            Templates.PropertyManagementPaths = GetStringArray(root, "Export", "PropertyManagementPaths");

            Progress.ReportProgressMode = GetString(root, "Pane", "ProgressReporting", "ReportProgressMode");
            Progress.Verbose = GetBoolean(root, true, "ProgressReporting", "Verbose");
        }

        private void ApplyStateToRoot(JsonObject root)
        {
            SetValue(root, General.GenerateLogFile, "GenerateLogFile");
            SetValue(root, General.DefaultOutputFolder.Trim(), "Export", "DefaultOutputFolder");
            SetValue(root, General.IfcVersion.Trim(), "Export", "IfcVersion");
            SetValue(root, General.DefaultIfcZip, "Export", "DefaultIfcZip");
            SetValue(root, General.AutomaticSaveDocument, "Export", "AutomaticSaveDocument");
            SetValue(root, General.AbortIfOutOfDate, "Export", "AbortIfOutOfDate");
            SetValue(root, General.ExportPropertiesFromCivilEntityProperties, "Export", "ExportPropertiesFromCivilEntityProperties");
            SetValue(root, General.ExportMaterials, "Export", "ExportMaterials");
            SetValue(root, General.VisibleLayersOnly, "Export", "VisibleLayersOnly");
            SetValue(root, General.ExportNonEpsgProjectedCoordinateSystem, "Export", "ExportNonEPSGProjectedCoordinateSystem");
            SetValue(root, General.BasePointMinExtents, "Export", "BasePointMinExtents");
            SetValue(root, General.ProjectBasePointOnRootPlacement, "Export", "ProjectBasePointOnRootPlacement");
            SetValue(root, General.GenerateOwnerHistory, "Export", "GenerateOwnerHistory");
            SetValue(root, ParseFlexibleDouble(General.FacetDistanceToleranceMetric), "Export", "FacetDistanceToleranceMetric");
            SetValue(root, ParseFlexibleDouble(General.GeometryDeviationTolerance), "Import", "GeometryDeviationTolerance");
            SetValue(root, ParseFlexibleDouble(General.BasePointWarningKilometers), "Export", "BasePointWarningKilometers");
            SetValue(root, ParseFlexibleDouble(General.BasePointErrorKilometers), "Export", "BasePointErrorKilometers");
            SetValue(root, General.PropertyListDelimiters, "Export", "PropertyListDelimiters");

            SetValue(root, Content.ExportAlignments, "Export", "ExportAlignments");
            SetValue(root, Content.ExportCorridors, "Export", "ExportCorridors");
            SetValue(root, Content.ExportCorridorLinks, "Export", "ExportCorridorLinks");
            SetValue(root, Content.ExportCorridorFeatureLines, "Export", "ExportCorridorFeatureLines");
            SetValue(root, Content.ExportCorridorShapesAsFallBackGeometry, "Export", "ExportCorridorShapesAsFallBackGeometry");
            SetValue(root, Content.ExportCorridorShapesAsSweptGeometry, "Export", "ExportCorridorShapesAsSweptGeometry");
            SetValue(root, Content.ExportFeatureLines, "Export", "ExportFeatureLines");
            SetValue(root, Content.ExportSurfaces, "Export", "ExportSurfaces");
            SetValue(root, Content.ExportSolids, "Export", "ExportSolids");
            SetValue(root, Content.ExportBodies, "Export", "ExportBodies");
            SetValue(root, Content.ExportAutoCadSurfaces, "Export", "ExportAutoCADSurfaces");
            SetValue(root, Content.ExportSubDMesh, "Export", "ExportSubDMesh");
            SetValue(root, Content.ExportCogoPoints, "Export", "ExportCogoPoints");
            SetValue(root, Content.ExportBlockReferences, "Export", "ExportBlockReferences");
            SetValue(root, Content.ExportPoints, "Export", "ExportPoints");
            SetValue(root, Content.ExportPolylines, "Export", "ExportPolylines");

            SetValue(root, Project.Name.Trim(), "Export", "ProjectAttributes", "Name");
            SetValue(root, Project.LongName.Trim(), "Export", "ProjectAttributes", "LongName");
            SetValue(root, Project.Description.Trim(), "Export", "ProjectAttributes", "Description");
            SetValue(root, Project.ObjectType.Trim(), "Export", "ProjectAttributes", "ObjectType");
            SetValue(root, Project.Phase.Trim(), "Export", "ProjectAttributes", "Phase");
            SetValue(root, Project.GlobalId.Trim(), "Export", "ProjectAttributes", "GlobalId");

            SetValue(root, Facility.IfcExportAs.Trim(), "Export", "FacilityAttributes", "IfcExportAs");
            SetValue(root, Facility.Name.Trim(), "Export", "FacilityAttributes", "Name");
            SetValue(root, Facility.LongName.Trim(), "Export", "FacilityAttributes", "LongName");
            SetValue(root, Facility.Description.Trim(), "Export", "FacilityAttributes", "Description");
            SetValue(root, Facility.ObjectType.Trim(), "Export", "FacilityAttributes", "ObjectType");
            SetValue(root, Facility.GlobalId.Trim(), "Export", "FacilityAttributes", "GlobalId");

            SetValue(root, FacilityAddress.Town.Trim(), "Export", "FacilityAttributes", "FacilityAddress", "Town");
            SetValue(root, FacilityAddress.Region.Trim(), "Export", "FacilityAttributes", "FacilityAddress", "Region");
            SetValue(root, FacilityAddress.Country.Trim(), "Export", "FacilityAttributes", "FacilityAddress", "Country");
            SetValue(root, FacilityAddress.PostalCode.Trim(), "Export", "FacilityAttributes", "FacilityAddress", "PostalCode");
            SetStringArray(root, FacilityAddress.AddressLines, "Export", "FacilityAttributes", "FacilityAddress", "AddressLines");
            SetStringArray(root, FacilityAddress.TelephoneNumbers, "Export", "FacilityAttributes", "FacilityAddress", "TelephoneNumbers");
            SetStringArray(root, FacilityAddress.ElectronicMailAddresses, "Export", "FacilityAttributes", "FacilityAddress", "ElectronicMailAddresses");
            SetValue(root, FacilityAddress.WwwHomePageUrl.Trim(), "Export", "FacilityAttributes", "FacilityAddress", "WWWHomePageURL");

            SetValue(root, Author.Identification.Trim(), "Export", "AuthorAttributes", "Identification");
            SetValue(root, Author.FamilyName.Trim(), "Export", "AuthorAttributes", "FamilyName");
            SetValue(root, Author.GivenName.Trim(), "Export", "AuthorAttributes", "GivenName");
            SetValue(root, Author.OrganizationIdentification.Trim(), "Export", "AuthorAttributes", "OrganizationIdentification");
            SetValue(root, Author.OrganizationName.Trim(), "Export", "AuthorAttributes", "OrganizationName");
            SetValue(root, Author.OrganizationEmail.Trim(), "Export", "AuthorAttributes", "OrganizationEmail");
            SetValue(root, Author.Authorization.Trim(), "Export", "AuthorAttributes", "Authorization");

            SetStringArray(root, Templates.PropertyTemplatePaths, "Export", "PropertyTemplatePaths");
            SetStringArray(root, Templates.PropertyManagementPaths, "Export", "PropertyManagementPaths");

            SetValue(root, Progress.ReportProgressMode.Trim(), "ProgressReporting", "ReportProgressMode");
            SetValue(root, Progress.Verbose, "ProgressReporting", "Verbose");
        }

        private void UpdateOptions(JsonObject root)
        {
            List<string> versions = GetStringList(root, "Export", "IFCVerionsCurrentlySupported");
            if (versions.Count == 0)
            {
                versions = new List<string> { "IFC4X3_ADD2", "IFC4", "DRAFT_IFC4X4" };
            }

            ReplaceCollection(IfcVersionOptions, versions);
        }

        private static JsonObject CreateEmptyRoot()
        {
            return new JsonObject
            {
                ["GenerateLogFile"] = true,
                ["Export"] = new JsonObject
                {
                    ["IFCVerionsCurrentlySupported"] = new JsonArray("IFC4", "IFC4X3_ADD2", "DRAFT_IFC4X4")
                },
                ["Import"] = new JsonObject(),
                ["ProgressReporting"] = new JsonObject()
            };
        }

        private string ResolvePreferredConfigPath()
        {
            if (!string.IsNullOrWhiteSpace(_drawingFolder))
            {
                return Path.Combine(_drawingFolder, "IfcInfraConfiguration.json");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "IfcInfraConfiguration.json");
        }

        private string PreferredIfcVersion()
        {
            if (IfcVersionOptions.Contains("IFC4X3_ADD2"))
            {
                return "IFC4X3_ADD2";
            }

            return IfcVersionOptions.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "IFC4X3_ADD2";
        }

        private string DeriveProjectName()
        {
            if (!string.IsNullOrWhiteSpace(_drawingFilePath))
            {
                return Path.GetFileNameWithoutExtension(_drawingFilePath);
            }

            return "Projeto IFC";
        }

        private void SetStatus(string message, StatusKind kind)
        {
            StatusMessage = message;

            switch (kind)
            {
                case StatusKind.Warning:
                    StatusBackground = WarningBackgroundBrush;
                    StatusForeground = WarningForegroundBrush;
                    break;
                case StatusKind.Error:
                    StatusBackground = ErrorBackgroundBrush;
                    StatusForeground = ErrorForegroundBrush;
                    break;
                default:
                    StatusBackground = InfoBackgroundBrush;
                    StatusForeground = InfoForegroundBrush;
                    break;
            }
        }

        private static string? ResolveSampleConfigPath()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "ApplicationPlugins",
                "Civil3D_IFC-2026.bundle",
                "Contents",
                "Samples",
                "IfcInfraConfiguration.json");

            return File.Exists(path) ? path : null;
        }

        private static string? ResolveDrawingPath()
        {
            try
            {
                return Manager.DocCad?.Name;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetDirectory(string? filePath)
        {
            try
            {
                return string.IsNullOrWhiteSpace(filePath) ? null : Path.GetDirectoryName(filePath);
            }
            catch
            {
                return null;
            }
        }

        private static void ReplaceCollection(ObservableCollection<string> target, IEnumerable<string> values)
        {
            target.Clear();
            foreach (string value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                target.Add(value);
            }
        }

        private static JsonNode? GetNode(JsonObject root, params string[] path)
        {
            JsonNode? current = root;
            foreach (string segment in path)
            {
                current = current?[segment];
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private static string GetString(JsonObject root, string fallback, params string[] path)
        {
            JsonNode? node = GetNode(root, path);
            if (node == null)
            {
                return fallback;
            }

            try
            {
                return node.GetValue<string>() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool GetBoolean(JsonObject root, bool fallback, params string[] path)
        {
            JsonNode? node = GetNode(root, path);
            if (node == null)
            {
                return fallback;
            }

            try
            {
                return node.GetValue<bool>();
            }
            catch
            {
                return fallback;
            }
        }

        private static string GetNumberAsString(JsonObject root, string fallback, params string[] path)
        {
            JsonNode? node = GetNode(root, path);
            if (node == null)
            {
                return fallback;
            }

            try
            {
                return node.ToJsonString().Trim('"');
            }
            catch
            {
                return fallback;
            }
        }

        private static string GetStringArray(JsonObject root, params string[] path)
        {
            JsonNode? node = GetNode(root, path);
            if (node is not JsonArray array)
            {
                return string.Empty;
            }

            return string.Join("; ", array
                .Select(item => item == null ? string.Empty : item.ToJsonString().Trim('"'))
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static List<string> GetStringList(JsonObject root, params string[] path)
        {
            JsonNode? node = GetNode(root, path);
            if (node is not JsonArray array)
            {
                return new List<string>();
            }

            return array
                .Select(item => item == null ? string.Empty : item.ToJsonString().Trim('"'))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        private static void SetValue(JsonObject root, bool value, params string[] path)
        {
            JsonObject container = EnsureContainer(root, path);
            container[path[^1]] = value;
        }

        private static void SetValue(JsonObject root, double value, params string[] path)
        {
            JsonObject container = EnsureContainer(root, path);
            container[path[^1]] = value;
        }

        private static void SetValue(JsonObject root, string value, params string[] path)
        {
            JsonObject container = EnsureContainer(root, path);
            container[path[^1]] = value;
        }

        private static void SetStringArray(JsonObject root, string source, params string[] path)
        {
            JsonArray array = new JsonArray();
            foreach (string item in SplitList(source))
            {
                array.Add(item);
            }

            JsonObject container = EnsureContainer(root, path);
            container[path[^1]] = array;
        }

        private static JsonObject EnsureContainer(JsonObject root, IReadOnlyList<string> path)
        {
            JsonObject current = root;
            for (int index = 0; index < path.Count - 1; index++)
            {
                string segment = path[index];
                if (current[segment] is not JsonObject child)
                {
                    child = new JsonObject();
                    current[segment] = child;
                }

                current = child;
            }

            return current;
        }

        private static IReadOnlyList<string> SplitList(string source)
        {
            return (source ?? string.Empty)
                .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        private static bool TryParseFlexibleDouble(string text, out double value)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.GetCultureInfo("pt-BR"), out value);
        }

        private static double ParseFlexibleDouble(string text)
        {
            if (TryParseFlexibleDouble(text, out double value))
            {
                return value;
            }

            return 0.0;
        }

        private static string SetTextDefault(string currentValue, string defaultValue, bool fillOnlyEmptyText)
        {
            if (!fillOnlyEmptyText)
            {
                return defaultValue;
            }

            return string.IsNullOrWhiteSpace(currentValue) ? defaultValue : currentValue;
        }

        private static MediaBrush CreateBrush(string hex)
        {
            SolidColorBrush brush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(hex)!);
            brush.Freeze();
            return brush;
        }

        private enum StatusKind
        {
            Info,
            Warning,
            Error
        }
    }
}
