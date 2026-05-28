using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace AutomacoesCivil3D
{
    // Implementação simples de BindableBase usada pelo restante do arquivo.
    // Preserva INotifyPropertyChanged e fornece SetProperty/OnPropertyChanged.
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

    internal static class SurfaceMaterialText
    {
        public static string NormalizeKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(normalized.Length);

            foreach (char character in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    sb.Append(character);
                }
                else
                {
                    sb.Append('_');
                }
            }

            string compact = sb.ToString().Normalize(NormalizationForm.FormC);
            while (compact.Contains("__", StringComparison.Ordinal))
            {
                compact = compact.Replace("__", "_", StringComparison.Ordinal);
            }

            return compact.Trim('_');
        }
    }

    internal sealed class SurfaceMaterialDefinition
    {
        public SurfaceMaterialDefinition(string disciplineKey, string name, string unitLabel, bool usesAreaMeasurement, params string[] aliases)
        {
            DisciplineKey = disciplineKey;
            Name = name;
            UnitLabel = unitLabel;
            UsesAreaMeasurement = usesAreaMeasurement;
            NormalizedName = SurfaceMaterialText.NormalizeKey(name);
            NormalizedAliases = new ReadOnlyCollection<string>(
                (aliases ?? Array.Empty<string>())
                    .Select(SurfaceMaterialText.NormalizeKey)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        public string DisciplineKey { get; }
        public string Name { get; }
        public string UnitLabel { get; }
        public bool UsesAreaMeasurement { get; }
        public string NormalizedName { get; }
        public ReadOnlyCollection<string> NormalizedAliases { get; }
    }

    public sealed class SurfaceMaterialChoiceOption
    {
        public SurfaceMaterialChoiceOption(string disciplineKey, string name, string unitLabel, bool usesAreaMeasurement)
        {
            DisciplineKey = disciplineKey;
            Name = name;
            UnitLabel = unitLabel;
            UsesAreaMeasurement = usesAreaMeasurement;
            DisplayLabel = $"{name} ({unitLabel})";
        }

        public string DisciplineKey { get; }
        public string Name { get; }
        public string UnitLabel { get; }
        public bool UsesAreaMeasurement { get; }
        public string DisplayLabel { get; }
    }

    public sealed class SurfaceDisciplineChoiceOption
    {
        public SurfaceDisciplineChoiceOption(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; }
    }

    public sealed class SurfaceQuantitySurfaceOption
    {
        public SurfaceQuantitySurfaceOption(ObjectId surfaceId, string selectionKey, string surfaceName, string kindLabel, bool isTinSurface, bool isVolumeSurface)
        {
            SurfaceId = surfaceId;
            SelectionKey = selectionKey ?? string.Empty;
            SurfaceName = surfaceName ?? string.Empty;
            KindLabel = kindLabel ?? string.Empty;
            IsTinSurface = isTinSurface;
            IsVolumeSurface = isVolumeSurface;
            DisplayLabel = string.IsNullOrWhiteSpace(SelectionKey)
                ? "<nenhuma>"
                : $"{SurfaceName} [{KindLabel}]";
            SearchKey = SurfaceMaterialText.NormalizeKey($"{SurfaceName} {KindLabel}");
        }

        public ObjectId SurfaceId { get; }
        public string SelectionKey { get; }
        public string SurfaceName { get; }
        public string KindLabel { get; }
        public bool IsTinSurface { get; }
        public bool IsVolumeSurface { get; }
        public string DisplayLabel { get; }
        public string SearchKey { get; }

        public static SurfaceQuantitySurfaceOption Empty { get; } =
            new SurfaceQuantitySurfaceOption(ObjectId.Null, string.Empty, string.Empty, "Nenhuma", false, false);
    }

    internal static class SurfaceMeasurementRuleKeys
    {
        public const string Auto = "AUTO";
        public const string AreaProjected = "AREA";
        public const string Cut = "CUT";
        public const string Fill = "FILL";
        public const string NetAbsolute = "ABSNET";
        public const string NetSigned = "NET";
    }

    public sealed class SurfaceMeasurementRuleOption
    {
        public SurfaceMeasurementRuleOption(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; }
    }

    public sealed class SurfaceMaterialMappingSeed
    {
        public bool IsSelected { get; set; } = true;
        public bool AutoMapped { get; set; }
        public string Segment { get; set; } = string.Empty;
        public string DisciplineKey { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string MeasurementRuleKey { get; set; } = string.Empty;
        public string TopSurfaceKey { get; set; } = string.Empty;
        public string BottomSurfaceKey { get; set; } = string.Empty;
        public string AreaSurfaceKey { get; set; } = string.Empty;
        public string VolumeSurfaceKey { get; set; } = string.Empty;

        public static SurfaceMaterialMappingSeed Blank()
        {
            return new SurfaceMaterialMappingSeed();
        }
    }

    public sealed class SurfaceMaterialQuantitiesDialogData
    {
        public SurfaceMaterialQuantitiesDialogData(
            IReadOnlyList<SurfaceQuantitySurfaceOption> surfaceOptions,
            IReadOnlyList<SurfaceMaterialMappingSeed> initialMappings,
            string activeDrawingPath,
            string suggestedPavCsvPath,
            string suggestedTrpCsvPath,
            string suggestedPattern,
            string suggestedExample,
            string? blockingIssue)
        {
            SurfaceOptions = surfaceOptions;
            InitialMappings = initialMappings;
            ActiveDrawingPath = activeDrawingPath;
            SuggestedPavCsvPath = suggestedPavCsvPath;
            SuggestedTrpCsvPath = suggestedTrpCsvPath;
            SuggestedPattern = suggestedPattern;
            SuggestedExample = suggestedExample;
            BlockingIssue = blockingIssue ?? string.Empty;
        }

        public IReadOnlyList<SurfaceQuantitySurfaceOption> SurfaceOptions { get; }
        public IReadOnlyList<SurfaceMaterialMappingSeed> InitialMappings { get; }
        public string ActiveDrawingPath { get; }
        public string SuggestedPavCsvPath { get; }
        public string SuggestedTrpCsvPath { get; }
        public string SuggestedPattern { get; }
        public string SuggestedExample { get; }
        public string BlockingIssue { get; }
        public bool HasBlockingIssue => !string.IsNullOrWhiteSpace(BlockingIssue);
    }

    public sealed class SurfaceMaterialQuantitiesEntryRequest
    {
        public string Segment { get; set; } = string.Empty;
        public string DisciplineKey { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string MeasurementRuleKey { get; set; } = string.Empty;
        public string TopSurfaceKey { get; set; } = string.Empty;
        public string BottomSurfaceKey { get; set; } = string.Empty;
        public string AreaSurfaceKey { get; set; } = string.Empty;
        public string VolumeSurfaceKey { get; set; } = string.Empty;
    }

    public sealed class SurfaceMaterialQuantitiesRequest
    {
        public bool GeneratePavCsv { get; set; }
        public bool GenerateTrpCsv { get; set; }
        public string PavCsvPath { get; set; } = string.Empty;
        public string TrpCsvPath { get; set; } = string.Empty;
        public IReadOnlyList<SurfaceMaterialQuantitiesEntryRequest> Entries { get; set; } = Array.Empty<SurfaceMaterialQuantitiesEntryRequest>();
    }

    internal static class SurfaceMaterialCatalog
    {
        private static readonly ReadOnlyCollection<SurfaceMaterialDefinition> Definitions =
            new ReadOnlyCollection<SurfaceMaterialDefinition>(new List<SurfaceMaterialDefinition>
            {
                new SurfaceMaterialDefinition("PAV", "ARMADURA DE AÇO - AÇO CA 60", "kg", false, "ARMADURA_ACO_CA_60", "ACO_CA_60"),
                new SurfaceMaterialDefinition("PAV", "BASE BRITA GRADUADA", "m³", false, "BASE_BRITA_GRADUADA", "BRITA_GRADUADA"),
                new SurfaceMaterialDefinition("PAV", "IMPRIMAÇÃO DE BASE", "m²", true, "IMPRIMACAO_DE_BASE", "IMPRIMACAO_BASE"),
                new SurfaceMaterialDefinition("PAV", "PAVIMENTO ASFÁLTICO - CBUQ - CAP", "m³", false, "PAVIMENTO_ASFALTICO_CBUQ_CAP", "CBUQ_CAP", "CBUQ"),
                new SurfaceMaterialDefinition("PAV", "PAVIMENTO ASFÁLTICO - TSD", "m²", true, "PAVIMENTO_ASFALTICO_TSD", "TSD"),
                new SurfaceMaterialDefinition("PAV", "PAVIMENTO ASFÁLTICO - TSS", "m²", true, "PAVIMENTO_ASFALTICO_TSS", "TSS"),
                new SurfaceMaterialDefinition("PAV", "PAVIMENTO ASFÁLTICO - TST", "m²", true, "PAVIMENTO_ASFALTICO_TST", "TST"),
                new SurfaceMaterialDefinition("PAV", "PAVIMENTO DE BLOCOS INTERTRAVADOS", "m²", true, "PAVIMENTO_BLOCOS_INTERTRAVADOS", "BLOCOS_INTERTRAVADOS"),
                new SurfaceMaterialDefinition("PAV", "PAVIMENTO DE CONCRETO ARMADO FCK = 30 MPA", "m³", false, "PAVIMENTO_CONCRETO_ARMADO_FCK_30_MPA", "CONCRETO_ARMADO_30_MPA"),
                new SurfaceMaterialDefinition("PAV", "PINTURA DE LIGAÇÃO", "m²", true, "PINTURA_DE_LIGACAO", "PINTURA_LIGACAO"),
                new SurfaceMaterialDefinition("PAV", "PLANTIO DE GRAMA TIPO ESMERALDA", "m²", true, "PLANTIO_GRAMA_ESMERALDA", "GRAMA_ESMERALDA"),
                new SurfaceMaterialDefinition("PAV", "PLANTIO DE GRAMA TIPO SÃO CARLOS/BATATAIS", "m²", true, "PLANTIO_GRAMA_SAO_CARLOS_BATATAIS", "GRAMA_SAO_CARLOS_BATATAIS"),
                new SurfaceMaterialDefinition("PAV", "REFORÇO DO SUB LEITO", "m³", false, "REFORCO_DO_SUB_LEITO", "REFORCO_SUB_LEITO"),
                new SurfaceMaterialDefinition("PAV", "REGULARIZAÇÃO E COMPACTAÇÃO DE SUBLEITO", "m²", true, "REGULARIZACAO_E_COMPACTACAO_DE_SUBLEITO", "REGULARIZACAO_COMPACTACAO_SUBLEITO"),
                new SurfaceMaterialDefinition("PAV", "SUB BASE/COLCHÃO DRENANTE", "m³", false, "SUB_BASE_COLCHAO_DRENANTE", "COLCHAO_DRENANTE"),

                new SurfaceMaterialDefinition("TRP", "LIMPEZA TERRENO (MECANIZADO)", "m²", true, "LIMPEZA_TERRENO_MECANIZADO", "LIMPEZA_TERRENO"),
                new SurfaceMaterialDefinition("TRP", "ESCAVAÇAO MATERIAL 1º  CATEGORIA", "m³", false, "ESCAVACAO_MATERIAL_1_CATEGORIA", "ESCAVACAO_MATERIAL_1CAT", "MAT_1CAT"),
                new SurfaceMaterialDefinition("TRP", "ESCAVAÇAO MATERIAL 2º  CATEGORIA", "m³", false, "ESCAVACAO_MATERIAL_2_CATEGORIA", "ESCAVACAO_MATERIAL_2CAT", "MAT_2CAT"),
                new SurfaceMaterialDefinition("TRP", "ESCAVAÇAO MATERIAL 3º  CATEGORIA", "m³", false, "ESCAVACAO_MATERIAL_3_CATEGORIA", "ESCAVACAO_MATERIAL_3CAT", "MAT_3CAT"),
                new SurfaceMaterialDefinition("TRP", "COMPACTAÇÃO DE ATERRO 95% PROCTOR NORMAL", "m³", false, "COMPACTACAO_DE_ATERRO_95_PROCTOR_NORMAL", "COMPACTACAO_95PN", "ATERRO_95PN"),
                new SurfaceMaterialDefinition("TRP", "COMPACTAÇÃO DE ATERRO 100% PROCTOR NORMAL", "m³", false, "COMPACTACAO_DE_ATERRO_100_PROCTOR_NORMAL", "COMPACTACAO_100PN", "ATERRO_100PN"),
                new SurfaceMaterialDefinition("TRP", "CARGA/DESCARGA E TRANSPORTE DE SOLO CONTAMINADO PARA BOTA-FORA", "m³", false, "CARGA_DESCARGA_E_TRANSPORTE_DE_SOLO_CONTAMINADO_PARA_BOTA_FORA", "SOLO_CONTAMINADO_BOTA_FORA"),
                new SurfaceMaterialDefinition("TRP", "CARGA/DESCARGA E TRANSPORTE DE SOLO PARA BOTA-FORA", "m³", false, "CARGA_DESCARGA_E_TRANSPORTE_DE_SOLO_PARA_BOTA_FORA", "SOLO_BOTA_FORA"),
                new SurfaceMaterialDefinition("TRP", "EMPRÉSTIMO DE MATERIAL DE JAZIDA", "m³", false, "EMPRESTIMO_DE_MATERIAL_DE_JAZIDA", "MATERIAL_JAZIDA"),
            });

        private static readonly ReadOnlyCollection<SurfaceMaterialChoiceOption> AllChoices =
            new ReadOnlyCollection<SurfaceMaterialChoiceOption>(
                Definitions
                    .OrderBy(definition => definition.DisciplineKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(definition => new SurfaceMaterialChoiceOption(
                        definition.DisciplineKey,
                        definition.Name,
                        definition.UnitLabel,
                        definition.UsesAreaMeasurement))
                    .ToList());

        private static readonly Dictionary<string, ReadOnlyCollection<SurfaceMaterialChoiceOption>> ChoicesByDiscipline =
            AllChoices
                .GroupBy(choice => choice.DisciplineKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    grouping => grouping.Key,
                    grouping => new ReadOnlyCollection<SurfaceMaterialChoiceOption>(
                        grouping.OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase).ToList()),
                    StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<SurfaceMaterialChoiceOption> GetChoiceOptions(string disciplineKey)
        {
            if (string.IsNullOrWhiteSpace(disciplineKey))
            {
                return AllChoices;
            }

            return ChoicesByDiscipline.TryGetValue(disciplineKey.Trim(), out ReadOnlyCollection<SurfaceMaterialChoiceOption>? choices)
                ? choices
                : AllChoices;
        }

        public static SurfaceMaterialDefinition? TryGetByDisplayName(string disciplineKey, string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            return Definitions.FirstOrDefault(definition =>
                string.Equals(definition.DisciplineKey, disciplineKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(definition.Name, displayName, StringComparison.OrdinalIgnoreCase));
        }

        public static SurfaceMaterialDefinition? TryMatchExact(string disciplineKey, string token)
        {
            string normalizedToken = SurfaceMaterialText.NormalizeKey(token);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                return null;
            }

            return Definitions.FirstOrDefault(definition =>
                string.Equals(definition.DisciplineKey, disciplineKey, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(definition.NormalizedName, normalizedToken, StringComparison.OrdinalIgnoreCase) ||
                 definition.NormalizedAliases.Any(alias => string.Equals(alias, normalizedToken, StringComparison.OrdinalIgnoreCase))));
        }

        public static string GetDefaultMeasurementRuleKey(string disciplineKey, string displayName)
        {
            SurfaceMaterialDefinition? definition = TryGetByDisplayName(disciplineKey, displayName);
            if (definition == null)
            {
                return SurfaceMeasurementRuleKeys.NetAbsolute;
            }

            if (definition.UsesAreaMeasurement)
            {
                return SurfaceMeasurementRuleKeys.AreaProjected;
            }

            if (string.Equals(definition.DisciplineKey, "TRP", StringComparison.OrdinalIgnoreCase))
            {
                if (definition.NormalizedName.StartsWith("ESCAVACAO_MATERIAL", StringComparison.OrdinalIgnoreCase) ||
                    definition.NormalizedName.Contains("BOTA_FORA", StringComparison.OrdinalIgnoreCase))
                {
                    return SurfaceMeasurementRuleKeys.Cut;
                }

                if (definition.NormalizedName.Contains("COMPACTACAO_DE_ATERRO", StringComparison.OrdinalIgnoreCase) ||
                    definition.NormalizedName.Contains("EMPRESTIMO_DE_MATERIAL_DE_JAZIDA", StringComparison.OrdinalIgnoreCase))
                {
                    return SurfaceMeasurementRuleKeys.Fill;
                }
            }

            return SurfaceMeasurementRuleKeys.NetAbsolute;
        }
    }

    internal static class SurfaceMeasurementRuleCatalog
    {
        private static readonly ReadOnlyCollection<SurfaceMeasurementRuleOption> AreaOptions =
            new ReadOnlyCollection<SurfaceMeasurementRuleOption>(new List<SurfaceMeasurementRuleOption>
            {
                new SurfaceMeasurementRuleOption(SurfaceMeasurementRuleKeys.Auto, "Auto material"),
                new SurfaceMeasurementRuleOption(SurfaceMeasurementRuleKeys.AreaProjected, "Area projetada")
            });

        private static readonly ReadOnlyCollection<SurfaceMeasurementRuleOption> VolumeOptions =
            new ReadOnlyCollection<SurfaceMeasurementRuleOption>(new List<SurfaceMeasurementRuleOption>
            {
                new SurfaceMeasurementRuleOption(SurfaceMeasurementRuleKeys.Auto, "Auto material"),
                new SurfaceMeasurementRuleOption(SurfaceMeasurementRuleKeys.Cut, "Corte"),
                new SurfaceMeasurementRuleOption(SurfaceMeasurementRuleKeys.Fill, "Aterro"),
                new SurfaceMeasurementRuleOption(SurfaceMeasurementRuleKeys.NetAbsolute, "Liquido abs."),
                new SurfaceMeasurementRuleOption(SurfaceMeasurementRuleKeys.NetSigned, "Liquido sinal")
            });

        private static readonly ReadOnlyCollection<SurfaceMeasurementRuleOption> UnknownOptions =
            new ReadOnlyCollection<SurfaceMeasurementRuleOption>(AreaOptions.Concat(VolumeOptions.Skip(1)).ToList());

        public static IReadOnlyList<SurfaceMeasurementRuleOption> GetOptions(string disciplineKey, string materialName)
        {
            SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(disciplineKey, materialName);
            if (definition == null)
            {
                return UnknownOptions;
            }

            return definition.UsesAreaMeasurement ? AreaOptions : VolumeOptions;
        }

        public static bool IsAllowed(string disciplineKey, string materialName, string? requestedKey)
        {
            string normalizedKey = NormalizeRequestedKey(requestedKey);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return false;
            }

            return GetOptions(disciplineKey, materialName).Any(option =>
                string.Equals(option.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeSelection(string disciplineKey, string materialName, string? requestedKey)
        {
            SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(disciplineKey, materialName);
            if (definition == null)
            {
                return string.Empty;
            }

            string normalizedKey = NormalizeRequestedKey(requestedKey);
            return IsAllowed(disciplineKey, materialName, normalizedKey)
                ? normalizedKey
                : SurfaceMeasurementRuleKeys.Auto;
        }

        public static string ResolveEffectiveKey(string disciplineKey, string materialName, string? requestedKey)
        {
            SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(disciplineKey, materialName);
            if (definition == null)
            {
                return SurfaceMeasurementRuleKeys.NetAbsolute;
            }

            string normalizedKey = NormalizeSelection(disciplineKey, materialName, requestedKey);
            if (definition.UsesAreaMeasurement)
            {
                return SurfaceMeasurementRuleKeys.AreaProjected;
            }

            return string.Equals(normalizedKey, SurfaceMeasurementRuleKeys.Auto, StringComparison.OrdinalIgnoreCase)
                ? SurfaceMaterialCatalog.GetDefaultMeasurementRuleKey(disciplineKey, materialName)
                : normalizedKey;
        }

        private static string NormalizeRequestedKey(string? requestedKey)
        {
            return (requestedKey ?? string.Empty).Trim().ToUpperInvariant();
        }
    }

    public class SurfaceMaterialQuantityRow : BindableBase
    {
        private readonly ReadOnlyCollection<SurfaceQuantitySurfaceOption> _surfaceOptions;
        private readonly Dictionary<string, SurfaceQuantitySurfaceOption> _surfaceLookup;
        private bool _isSelected;
        private string _segment;
        private string _disciplineKey;
        private string _materialName;
        private string _measurementRuleKey;
        private string _topSurfaceKey;
        private string _bottomSurfaceKey;
        private string _areaSurfaceKey;
        private string _volumeSurfaceKey;

        public SurfaceMaterialQuantityRow(SurfaceMaterialMappingSeed seed, ReadOnlyCollection<SurfaceQuantitySurfaceOption> surfaceOptions)
        {
            _surfaceOptions = surfaceOptions;
            _surfaceLookup = surfaceOptions
                .Where(option => !string.IsNullOrWhiteSpace(option.SelectionKey))
                .ToDictionary(option => option.SelectionKey, StringComparer.OrdinalIgnoreCase);

            _isSelected = seed.IsSelected;
            AutoMapped = seed.AutoMapped;
            _segment = seed.Segment ?? string.Empty;
            _disciplineKey = seed.DisciplineKey ?? string.Empty;
            _materialName = seed.MaterialName ?? string.Empty;
            _measurementRuleKey = SurfaceMeasurementRuleCatalog.NormalizeSelection(_disciplineKey, _materialName, seed.MeasurementRuleKey);
            _topSurfaceKey = seed.TopSurfaceKey ?? string.Empty;
            _bottomSurfaceKey = seed.BottomSurfaceKey ?? string.Empty;
            _areaSurfaceKey = seed.AreaSurfaceKey ?? string.Empty;
            _volumeSurfaceKey = seed.VolumeSurfaceKey ?? string.Empty;
        }

        public bool AutoMapped { get; }
        public ReadOnlyCollection<SurfaceQuantitySurfaceOption> SurfaceOptions => _surfaceOptions;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    RefreshComputedProperties();
                }
            }
        }

        public string Segment
        {
            get => _segment;
            set
            {
                if (SetProperty(ref _segment, value ?? string.Empty))
                {
                    RefreshComputedProperties();
                }
            }
        }

        public string DisciplineKey
        {
            get => _disciplineKey;
            set
            {
                string normalizedValue = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (SetProperty(ref _disciplineKey, normalizedValue))
                {
                    if (!string.IsNullOrWhiteSpace(MaterialName) &&
                        SurfaceMaterialCatalog.TryGetByDisplayName(_disciplineKey, MaterialName) == null)
                    {
                        _materialName = string.Empty;
                        OnPropertyChanged(nameof(MaterialName));
                    }

                    EnsureMeasurementRuleSelection(resetToDefault: true);
                    RefreshComputedProperties();
                }
            }
        }

        public string MaterialName
        {
            get => _materialName;
            set
            {
                if (SetProperty(ref _materialName, value ?? string.Empty))
                {
                    EnsureMeasurementRuleSelection(resetToDefault: true);
                    RefreshComputedProperties();
                }
            }
        }

        public string MeasurementRuleKey
        {
            get => _measurementRuleKey;
            set
            {
                string normalizedValue = SurfaceMeasurementRuleCatalog.NormalizeSelection(DisciplineKey, MaterialName, value);
                if (SetProperty(ref _measurementRuleKey, normalizedValue))
                {
                    RefreshComputedProperties();
                }
            }
        }

        public string TopSurfaceKey
        {
            get => _topSurfaceKey;
            set
            {
                if (SetProperty(ref _topSurfaceKey, value ?? string.Empty))
                {
                    RefreshComputedProperties();
                }
            }
        }

        public string BottomSurfaceKey
        {
            get => _bottomSurfaceKey;
            set
            {
                if (SetProperty(ref _bottomSurfaceKey, value ?? string.Empty))
                {
                    RefreshComputedProperties();
                }
            }
        }

        public string AreaSurfaceKey
        {
            get => _areaSurfaceKey;
            set
            {
                if (SetProperty(ref _areaSurfaceKey, value ?? string.Empty))
                {
                    RefreshComputedProperties();
                }
            }
        }

        public string VolumeSurfaceKey
        {
            get => _volumeSurfaceKey;
            set
            {
                if (SetProperty(ref _volumeSurfaceKey, value ?? string.Empty))
                {
                    RefreshComputedProperties();
                }
            }
        }

        public string SourceTag => AutoMapped ? "Auto" : "Manual";

        public string RowLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Segment))
                {
                    return Segment.Trim();
                }

                if (!string.IsNullOrWhiteSpace(MaterialName))
                {
                    return MaterialName.Trim();
                }

                return "Linha sem nome";
            }
        }

        public IReadOnlyList<SurfaceMaterialChoiceOption> AvailableMaterialOptions =>
            SurfaceMaterialCatalog.GetChoiceOptions(DisciplineKey);

        public IReadOnlyList<SurfaceMeasurementRuleOption> AvailableMeasurementRules =>
            SurfaceMeasurementRuleCatalog.GetOptions(DisciplineKey, MaterialName);

        public string UnitLabel =>
            SurfaceMaterialCatalog.TryGetByDisplayName(DisciplineKey, MaterialName)?.UnitLabel ?? string.Empty;

        public string SourceRequirementLabel
        {
            get
            {
                SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(DisciplineKey, MaterialName);
                if (definition == null)
                {
                    return string.Empty;
                }

                return definition.UsesAreaMeasurement
                    ? "AREA ou TOP (TIN)"
                    : "VOL ou TOP/BOT (TIN)";
            }
        }

        public string StatusText => GetValidationError() ?? "Pronto";

        public bool IsReady => string.IsNullOrWhiteSpace(GetValidationError());

        public string? GetValidationError()
        {
            if (string.IsNullOrWhiteSpace(DisciplineKey))
            {
                return "Defina a disciplina.";
            }

            SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(DisciplineKey, MaterialName);
            if (definition == null)
            {
                return "Defina um material valido.";
            }

            if (!SurfaceMeasurementRuleCatalog.IsAllowed(DisciplineKey, MaterialName, MeasurementRuleKey))
            {
                return definition.UsesAreaMeasurement
                    ? "Defina uma regra valida para medir area."
                    : "Defina uma regra valida de corte/aterro/liquido.";
            }

            if (definition.UsesAreaMeasurement)
            {
                string areaKey = !string.IsNullOrWhiteSpace(AreaSurfaceKey)
                    ? AreaSurfaceKey
                    : TopSurfaceKey;

                if (string.IsNullOrWhiteSpace(areaKey))
                {
                    return "Defina AREA ou TOP para medir em m2.";
                }

                if (!TryGetSurface(areaKey, out SurfaceQuantitySurfaceOption? option) || !option.IsTinSurface)
                {
                    return "AREA/TOP precisa ser uma TIN Surface.";
                }

                return null;
            }

            if (!string.IsNullOrWhiteSpace(VolumeSurfaceKey))
            {
                if (!TryGetSurface(VolumeSurfaceKey, out SurfaceQuantitySurfaceOption? volumeOption) || !volumeOption.IsVolumeSurface)
                {
                    return "VOL precisa ser uma TinVolumeSurface.";
                }

                return null;
            }

            if (string.IsNullOrWhiteSpace(TopSurfaceKey) || string.IsNullOrWhiteSpace(BottomSurfaceKey))
            {
                return "Defina TOP e BOT ou uma VOL surface.";
            }

            if (!TryGetSurface(TopSurfaceKey, out SurfaceQuantitySurfaceOption? topOption) || !topOption.IsTinSurface)
            {
                return "TOP precisa ser uma TIN Surface.";
            }

            if (!TryGetSurface(BottomSurfaceKey, out SurfaceQuantitySurfaceOption? bottomOption) || !bottomOption.IsTinSurface)
            {
                return "BOT precisa ser uma TIN Surface.";
            }

            return null;
        }

        public bool MatchesFilter(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
            {
                return true;
            }

            string filterKey = SurfaceMaterialText.NormalizeKey(filterText);
            if (string.IsNullOrWhiteSpace(filterKey))
            {
                return true;
            }

            string rowKey = SurfaceMaterialText.NormalizeKey(
                $"{Segment} {DisciplineKey} {MaterialName} {TopSurfaceKey} {BottomSurfaceKey} {AreaSurfaceKey} {VolumeSurfaceKey}");

            return rowKey.Contains(filterKey, StringComparison.OrdinalIgnoreCase);
        }

        public SurfaceMaterialQuantitiesEntryRequest BuildRequest()
        {
            return new SurfaceMaterialQuantitiesEntryRequest
            {
                Segment = Segment.Trim(),
                DisciplineKey = DisciplineKey.Trim(),
                MaterialName = MaterialName.Trim(),
                MeasurementRuleKey = MeasurementRuleKey.Trim(),
                TopSurfaceKey = TopSurfaceKey.Trim(),
                BottomSurfaceKey = BottomSurfaceKey.Trim(),
                AreaSurfaceKey = AreaSurfaceKey.Trim(),
                VolumeSurfaceKey = VolumeSurfaceKey.Trim()
            };
        }

        private bool TryGetSurface(string selectionKey, out SurfaceQuantitySurfaceOption? option)
        {
            option = null;
            if (string.IsNullOrWhiteSpace(selectionKey))
            {
                return false;
            }

            return _surfaceLookup.TryGetValue(selectionKey, out option);
        }

        private void RefreshComputedProperties()
        {
            OnPropertyChanged(nameof(AvailableMaterialOptions));
            OnPropertyChanged(nameof(AvailableMeasurementRules));
            OnPropertyChanged(nameof(UnitLabel));
            OnPropertyChanged(nameof(SourceRequirementLabel));
            OnPropertyChanged(nameof(SourceTag));
            OnPropertyChanged(nameof(RowLabel));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsReady));
        }

        private void EnsureMeasurementRuleSelection(bool resetToDefault = false)
        {
            string requestedKey = resetToDefault
                ? ResolveDefaultMeasurementRuleKey()
                : _measurementRuleKey;
            string normalizedValue = SurfaceMeasurementRuleCatalog.NormalizeSelection(DisciplineKey, MaterialName, requestedKey);
            if (!string.Equals(_measurementRuleKey, normalizedValue, StringComparison.OrdinalIgnoreCase))
            {
                _measurementRuleKey = normalizedValue;
                OnPropertyChanged(nameof(MeasurementRuleKey));
            }
        }

        private string ResolveDefaultMeasurementRuleKey()
        {
            return SurfaceMaterialCatalog.TryGetByDisplayName(DisciplineKey, MaterialName) == null
                ? string.Empty
                : SurfaceMaterialCatalog.GetDefaultMeasurementRuleKey(DisciplineKey, MaterialName);
        }
    }

    public class SurfaceMaterialQuantitiesDialogViewModel : BindableBase
    {
        private static readonly ReadOnlyCollection<SurfaceDisciplineChoiceOption> DefaultDisciplineOptions =
            new ReadOnlyCollection<SurfaceDisciplineChoiceOption>(new List<SurfaceDisciplineChoiceOption>
            {
                new SurfaceDisciplineChoiceOption(string.Empty, "<definir>"),
                new SurfaceDisciplineChoiceOption("PAV", "Pavimentacao"),
                new SurfaceDisciplineChoiceOption("TRP", "Terraplenagem")
            });

        private readonly ObservableCollection<SurfaceMaterialQuantityRow> _entries;
        private readonly string _activeDrawingPath;
        private readonly string _blockingIssue;
        private string _entryFilterText = string.Empty;
        private string _pavCsvPath;
        private string _trpCsvPath;
        private bool _generatePavCsv;
        private bool _generateTrpCsv;
        private string _statusMessage = string.Empty;
        private Brush _statusBackground;
        private Brush _statusForeground;

        public SurfaceMaterialQuantitiesDialogViewModel(SurfaceMaterialQuantitiesDialogData data)
        {
            SurfaceOptions = new ReadOnlyCollection<SurfaceQuantitySurfaceOption>(
                new[] { SurfaceQuantitySurfaceOption.Empty }
                    .Concat(data.SurfaceOptions)
                    .ToList());

            DisciplineOptions = DefaultDisciplineOptions;
            SuggestedPattern = data.SuggestedPattern;
            SuggestedExample = data.SuggestedExample;

            _entries = new ObservableCollection<SurfaceMaterialQuantityRow>(
                (data.InitialMappings.Count > 0 ? data.InitialMappings : new[] { SurfaceMaterialMappingSeed.Blank() })
                .Select(seed => new SurfaceMaterialQuantityRow(seed, SurfaceOptions)));

            foreach (SurfaceMaterialQuantityRow entry in _entries)
            {
                entry.PropertyChanged += EntryOnPropertyChanged;
            }

            EntriesView = CollectionViewSource.GetDefaultView(_entries);
            EntriesView.Filter = FilterEntry;

            _activeDrawingPath = data.ActiveDrawingPath;
            _blockingIssue = data.BlockingIssue;
            _pavCsvPath = data.SuggestedPavCsvPath;
            _trpCsvPath = data.SuggestedTrpCsvPath;
            _statusBackground = BrushFrom("#EEF4FA");
            _statusForeground = BrushFrom("#173042");

            _generatePavCsv = _entries.Any(entry => string.Equals(entry.DisciplineKey, "PAV", StringComparison.OrdinalIgnoreCase));
            _generateTrpCsv = _entries.Any(entry => string.Equals(entry.DisciplineKey, "TRP", StringComparison.OrdinalIgnoreCase));
            if (!_generatePavCsv && !_generateTrpCsv)
            {
                _generatePavCsv = true;
                _generateTrpCsv = true;
            }

            if (data.HasBlockingIssue)
            {
                SetStatus(data.BlockingIssue, "#F9E3E3", "#8C3535");
            }
            else
            {
                SetStatus("Revise os vinculos entre superficies e materiais antes de exportar.", "#EEF4FA", "#173042");
            }
        }

        public ICollectionView EntriesView { get; }
        public ReadOnlyCollection<SurfaceQuantitySurfaceOption> SurfaceOptions { get; }
        public ReadOnlyCollection<SurfaceDisciplineChoiceOption> DisciplineOptions { get; }
        public string SuggestedPattern { get; }
        public string SuggestedExample { get; }

        public string EntryFilterText
        {
            get => _entryFilterText;
            set
            {
                if (SetProperty(ref _entryFilterText, value ?? string.Empty))
                {
                    EntriesView.Refresh();
                    OnPropertyChanged(nameof(FilteredEntriesLabel));
                }
            }
        }

        public string PavCsvPath
        {
            get => _pavCsvPath;
            set
            {
                if (SetProperty(ref _pavCsvPath, value ?? string.Empty))
                {
                    OnValidationStateChanged();
                }
            }
        }

        public string TrpCsvPath
        {
            get => _trpCsvPath;
            set
            {
                if (SetProperty(ref _trpCsvPath, value ?? string.Empty))
                {
                    OnValidationStateChanged();
                }
            }
        }

        public bool GeneratePavCsv
        {
            get => _generatePavCsv;
            set
            {
                if (SetProperty(ref _generatePavCsv, value))
                {
                    OnValidationStateChanged();
                }
            }
        }

        public bool GenerateTrpCsv
        {
            get => _generateTrpCsv;
            set
            {
                if (SetProperty(ref _generateTrpCsv, value))
                {
                    OnValidationStateChanged();
                }
            }
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
        public int SurfaceCount => SurfaceOptions.Count(option => !string.IsNullOrWhiteSpace(option.SelectionKey));
        public int TinSurfaceCount => SurfaceOptions.Count(option => option.IsTinSurface);
        public int VolumeSurfaceCount => SurfaceOptions.Count(option => option.IsVolumeSurface);
        public int AutoMappedCount => _entries.Count(entry => entry.AutoMapped);
        public int SelectedEntriesCount => _entries.Count(entry => entry.IsSelected);
        public int SelectedPavEntriesCount => _entries.Count(entry => entry.IsSelected && string.Equals(entry.DisciplineKey, "PAV", StringComparison.OrdinalIgnoreCase));
        public int SelectedTrpEntriesCount => _entries.Count(entry => entry.IsSelected && string.Equals(entry.DisciplineKey, "TRP", StringComparison.OrdinalIgnoreCase));
        public int ReadyEntriesCount => _entries.Count(entry => entry.IsSelected && entry.IsReady);
        public string FilteredEntriesLabel => $"{GetVisibleCount()} de {_entries.Count} linhas";
        public bool CanExport => string.IsNullOrWhiteSpace(GetValidationError());

        public void SetPavCsvPath(string path)
        {
            PavCsvPath = path;
        }

        public void SetTrpCsvPath(string path)
        {
            TrpCsvPath = path;
        }

        public void AddBlankEntry()
        {
            SurfaceMaterialQuantityRow entry = new SurfaceMaterialQuantityRow(SurfaceMaterialMappingSeed.Blank(), SurfaceOptions);
            entry.PropertyChanged += EntryOnPropertyChanged;
            _entries.Add(entry);
            EntriesView.Refresh();
            OnValidationStateChanged();
        }

        public void RemoveEntries(IEnumerable<SurfaceMaterialQuantityRow> entries)
        {
            List<SurfaceMaterialQuantityRow> toRemove = entries
                .Where(entry => entry != null)
                .Distinct()
                .ToList();

            if (toRemove.Count == 0)
            {
                return;
            }

            foreach (SurfaceMaterialQuantityRow entry in toRemove)
            {
                entry.PropertyChanged -= EntryOnPropertyChanged;
                _entries.Remove(entry);
            }

            if (_entries.Count == 0)
            {
                AddBlankEntry();
            }

            EntriesView.Refresh();
            OnValidationStateChanged();
        }

        public void SelectAllVisible()
        {
            foreach (SurfaceMaterialQuantityRow entry in EntriesView.Cast<SurfaceMaterialQuantityRow>())
            {
                entry.IsSelected = true;
            }
        }

        public void ClearVisibleSelection()
        {
            foreach (SurfaceMaterialQuantityRow entry in EntriesView.Cast<SurfaceMaterialQuantityRow>())
            {
                entry.IsSelected = false;
            }
        }

        public SurfaceMaterialQuantitiesRequest BuildRequest()
        {
            return new SurfaceMaterialQuantitiesRequest
            {
                GeneratePavCsv = GeneratePavCsv,
                GenerateTrpCsv = GenerateTrpCsv,
                PavCsvPath = PavCsvPath.Trim(),
                TrpCsvPath = TrpCsvPath.Trim(),
                Entries = _entries
                    .Where(entry => entry.IsSelected && IsEnabledDiscipline(entry.DisciplineKey))
                    .Select(entry => entry.BuildRequest())
                    .ToList()
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

            SetStatus("Configuracao validada. A rotina esta pronta para medir e gerar os CSVs.", "#DDF1F2", "#145B64");
            return true;
        }
        private bool FilterEntry(object item)
        {
            return item is SurfaceMaterialQuantityRow entry && entry.MatchesFilter(EntryFilterText);
        }

        private void EntryOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            EntriesView.Refresh();
            OnValidationStateChanged();
        }

        private void OnValidationStateChanged()
        {
            OnPropertyChanged(nameof(SurfaceCount));
            OnPropertyChanged(nameof(TinSurfaceCount));
            OnPropertyChanged(nameof(VolumeSurfaceCount));
            OnPropertyChanged(nameof(AutoMappedCount));
            OnPropertyChanged(nameof(SelectedEntriesCount));
            OnPropertyChanged(nameof(SelectedPavEntriesCount));
            OnPropertyChanged(nameof(SelectedTrpEntriesCount));
            OnPropertyChanged(nameof(ReadyEntriesCount));
            OnPropertyChanged(nameof(FilteredEntriesLabel));
            OnPropertyChanged(nameof(CanExport));
        }

        private bool IsEnabledDiscipline(string disciplineKey)
        {
            return string.Equals(disciplineKey, "PAV", StringComparison.OrdinalIgnoreCase)
                ? GeneratePavCsv
                : string.Equals(disciplineKey, "TRP", StringComparison.OrdinalIgnoreCase) && GenerateTrpCsv;
        }

        private string? GetValidationError()
        {
            if (!string.IsNullOrWhiteSpace(_blockingIssue))
            {
                return _blockingIssue;
            }

            if (_entries.Count == 0)
            {
                return "Adicione ao menos uma linha de medicao.";
            }

            if (SelectedEntriesCount == 0)
            {
                return "Marque ao menos uma linha para exportacao.";
            }

            if (!GeneratePavCsv && !GenerateTrpCsv)
            {
                return "Ative ao menos um CSV de saida.";
            }

            if (GeneratePavCsv && string.IsNullOrWhiteSpace(PavCsvPath))
            {
                return "Informe o caminho do CSV de pavimentacao.";
            }

            if (GenerateTrpCsv && string.IsNullOrWhiteSpace(TrpCsvPath))
            {
                return "Informe o caminho do CSV de terraplenagem.";
            }

            if (GeneratePavCsv && !_entries.Any(entry => entry.IsSelected && string.Equals(entry.DisciplineKey, "PAV", StringComparison.OrdinalIgnoreCase)))
            {
                return "Nao ha linhas PAV selecionadas para gerar o CSV.";
            }

            if (GenerateTrpCsv && !_entries.Any(entry => entry.IsSelected && string.Equals(entry.DisciplineKey, "TRP", StringComparison.OrdinalIgnoreCase)))
            {
                return "Nao ha linhas TRP selecionadas para gerar o CSV.";
            }

            SurfaceMaterialQuantityRow? invalidEntry = _entries.FirstOrDefault(entry =>
                entry.IsSelected &&
                IsEnabledDiscipline(entry.DisciplineKey) &&
                !string.IsNullOrWhiteSpace(entry.GetValidationError()));

            if (invalidEntry != null)
            {
                return $"{invalidEntry.RowLabel}: {invalidEntry.GetValidationError()}";
            }

            if (!string.IsNullOrWhiteSpace(_activeDrawingPath))
            {
                try
                {
                    if (GeneratePavCsv &&
                        string.Equals(Path.GetFullPath(PavCsvPath), Path.GetFullPath(_activeDrawingPath), StringComparison.OrdinalIgnoreCase))
                    {
                        return "O CSV de pavimentacao nao pode sobrescrever o desenho ativo.";
                    }

                    if (GenerateTrpCsv &&
                        string.Equals(Path.GetFullPath(TrpCsvPath), Path.GetFullPath(_activeDrawingPath), StringComparison.OrdinalIgnoreCase))
                    {
                        return "O CSV de terraplenagem nao pode sobrescrever o desenho ativo.";
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private int GetVisibleCount()
        {
            int count = 0;
            foreach (object _ in EntriesView)
            {
                count++;
            }

            return count;
        }

        private void SetStatus(string message, string background, string foreground)
        {
            StatusMessage = message;
            StatusBackground = BrushFrom(background);
            StatusForeground = BrushFrom(foreground);
            OnPropertyChanged(nameof(HasStatusMessage));
        }

        private static SolidColorBrush BrushFrom(string color)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        }

        public override bool Equals(object? obj)
        {
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override string? ToString()
        {
            return base.ToString();
        }
    }

    internal static class SurfaceMaterialQuantitiesDesignData
    {
        public static SurfaceMaterialQuantitiesDialogViewModel Create()
        {
            SurfaceMaterialQuantitiesDialogData data = new SurfaceMaterialQuantitiesDialogData(
                new List<SurfaceQuantitySurfaceOption>
                {
                    new SurfaceQuantitySurfaceOption(ObjectId.Null, "QTO_PAV_BASE_BRITA_GRADUADA__AL01__TOP", "QTO_PAV_BASE_BRITA_GRADUADA__AL01__TOP", "TIN", true, false),
                    new SurfaceQuantitySurfaceOption(ObjectId.Null, "QTO_PAV_BASE_BRITA_GRADUADA__AL01__BOT", "QTO_PAV_BASE_BRITA_GRADUADA__AL01__BOT", "TIN", true, false),
                    new SurfaceQuantitySurfaceOption(ObjectId.Null, "QTO_TRP_ESCAVACAO_MATERIAL_1CAT__AL01__VOL", "QTO_TRP_ESCAVACAO_MATERIAL_1CAT__AL01__VOL", "TIN Volume", false, true),
                    new SurfaceQuantitySurfaceOption(ObjectId.Null, "QTO_PAV_PINTURA_DE_LIGACAO__AL01__AREA", "QTO_PAV_PINTURA_DE_LIGACAO__AL01__AREA", "TIN", true, false),
                },
                new List<SurfaceMaterialMappingSeed>
                {
                    new SurfaceMaterialMappingSeed
                    {
                        AutoMapped = true,
                        DisciplineKey = "PAV",
                        MaterialName = "BASE BRITA GRADUADA",
                        Segment = "AL01",
                        TopSurfaceKey = "QTO_PAV_BASE_BRITA_GRADUADA__AL01__TOP",
                        BottomSurfaceKey = "QTO_PAV_BASE_BRITA_GRADUADA__AL01__BOT",
                    },
                    new SurfaceMaterialMappingSeed
                    {
                        AutoMapped = true,
                        DisciplineKey = "TRP",
                        MaterialName = "ESCAVAÇAO MATERIAL 1º  CATEGORIA",
                        Segment = "AL01",
                        VolumeSurfaceKey = "QTO_TRP_ESCAVACAO_MATERIAL_1CAT__AL01__VOL",
                    },
                    new SurfaceMaterialMappingSeed
                    {
                        AutoMapped = true,
                        DisciplineKey = "PAV",
                        MaterialName = "PINTURA DE LIGAÇÃO",
                        Segment = "AL01",
                        AreaSurfaceKey = "QTO_PAV_PINTURA_DE_LIGACAO__AL01__AREA",
                    }
                },
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModeloAtivo.dwg"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModeloAtivo_QTO_SUPERFICIES_PAV.csv"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModeloAtivo_QTO_SUPERFICIES_TRP.csv"),
                "QTO_<DISCIPLINA>_<MATERIAL>__<TRECHO>__<PAPEL>",
                "QTO_PAV_BASE_BRITA_GRADUADA__AL01__TOP",
                string.Empty);

            return new SurfaceMaterialQuantitiesDialogViewModel(data);
        }
    }
}
