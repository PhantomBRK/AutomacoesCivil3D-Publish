using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AutomacoesCivil3D
{
    internal sealed class SurfaceMaterialPatternTemplate
    {
        public string DisciplineKey { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string MeasurementRuleKey { get; set; } = string.Empty;
        public string TopAlias { get; set; } = string.Empty;
        public string BottomAlias { get; set; } = string.Empty;
        public string AreaAlias { get; set; } = string.Empty;
        public string VolumeAlias { get; set; } = string.Empty;
    }

    internal sealed class SurfaceMaterialPatternTemplateFile
    {
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<SurfaceMaterialPatternTemplate> Templates { get; set; } = new List<SurfaceMaterialPatternTemplate>();
    }

    internal static class SurfaceMaterialPatternCatalog
    {
        private static readonly ReadOnlyCollection<SurfaceMaterialPatternTemplate> BuiltInTemplates =
            new ReadOnlyCollection<SurfaceMaterialPatternTemplate>(new List<SurfaceMaterialPatternTemplate>
            {
                CreateTemplate("PAV", "PAVIMENTO_ASFALTICO_CBUQ_CAP", SurfaceMeasurementRuleKeys.NetAbsolute, topAlias: "PAVE", bottomAlias: "PAVE1"),
                CreateTemplate("PAV", "BASE_BRITA_GRADUADA", SurfaceMeasurementRuleKeys.NetAbsolute, topAlias: "BASE", bottomAlias: "SUBBASE"),
                CreateTemplate("PAV", "SUB_BASE_COLCHAO_DRENANTE", SurfaceMeasurementRuleKeys.NetAbsolute, topAlias: "SUBBASE", bottomAlias: "SUBLEITO"),
                CreateTemplate("PAV", "REFORCO_DO_SUB_LEITO", SurfaceMeasurementRuleKeys.NetAbsolute, topAlias: "SUBLEITO", bottomAlias: "DATUM"),
                CreateTemplate("PAV", "REGULARIZACAO_E_COMPACTACAO_DE_SUBLEITO", SurfaceMeasurementRuleKeys.AreaProjected, areaAlias: "SUBLEITO"),
                CreateTemplate("PAV", "IMPRIMACAO_DE_BASE", SurfaceMeasurementRuleKeys.AreaProjected, areaAlias: "BASE"),
                CreateTemplate("PAV", "PINTURA_DE_LIGACAO", SurfaceMeasurementRuleKeys.AreaProjected, areaAlias: "PAVE1"),
                CreateTemplate("PAV", "PAVIMENTO_ASFALTICO_TSD", SurfaceMeasurementRuleKeys.AreaProjected, areaAlias: "PAVE"),
                CreateTemplate("PAV", "PAVIMENTO_ASFALTICO_TSS", SurfaceMeasurementRuleKeys.AreaProjected, areaAlias: "PAVE"),
                CreateTemplate("PAV", "PAVIMENTO_ASFALTICO_TST", SurfaceMeasurementRuleKeys.AreaProjected, areaAlias: "PAVE"),
                CreateTemplate("PAV", "PAVIMENTO_BLOCOS_INTERTRAVADOS", SurfaceMeasurementRuleKeys.AreaProjected, areaAlias: "PAVE")
            });

        public static IReadOnlyList<SurfaceMaterialPatternTemplate> GetTemplates()
        {
            Dictionary<string, SurfaceMaterialPatternTemplate> combined =
                BuiltInTemplates.ToDictionary(GetTemplateKey, Clone, StringComparer.OrdinalIgnoreCase);

            foreach (SurfaceMaterialPatternTemplate template in SurfaceMaterialPatternStorage.LoadTemplates())
            {
                if (string.IsNullOrWhiteSpace(template.DisciplineKey) || string.IsNullOrWhiteSpace(template.MaterialName))
                {
                    continue;
                }

                combined[GetTemplateKey(template)] = Clone(template);
            }

            return combined.Values
                .OrderBy(template => template.DisciplineKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(template => template.MaterialName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static SurfaceMaterialPatternTemplate CreateTemplate(
            string disciplineKey,
            string materialToken,
            string measurementRuleKey,
            string topAlias = "",
            string bottomAlias = "",
            string areaAlias = "",
            string volumeAlias = "")
        {
            string materialName = SurfaceMaterialCatalog.TryMatchExact(disciplineKey, materialToken)?.Name ?? materialToken;
            return new SurfaceMaterialPatternTemplate
            {
                DisciplineKey = disciplineKey,
                MaterialName = materialName,
                MeasurementRuleKey = measurementRuleKey,
                TopAlias = topAlias,
                BottomAlias = bottomAlias,
                AreaAlias = areaAlias,
                VolumeAlias = volumeAlias
            };
        }

        private static string GetTemplateKey(SurfaceMaterialPatternTemplate template)
        {
            return $"{template.DisciplineKey}|{template.MaterialName}";
        }

        private static SurfaceMaterialPatternTemplate Clone(SurfaceMaterialPatternTemplate template)
        {
            return new SurfaceMaterialPatternTemplate
            {
                DisciplineKey = template.DisciplineKey,
                MaterialName = template.MaterialName,
                MeasurementRuleKey = template.MeasurementRuleKey,
                TopAlias = template.TopAlias,
                BottomAlias = template.BottomAlias,
                AreaAlias = template.AreaAlias,
                VolumeAlias = template.VolumeAlias
            };
        }
    }

    internal static class SurfaceMaterialPatternStorage
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static string GetFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AutomacoesCivil3D", "QtoSuperficies", "SurfacePatterns.json");
        }

        public static IReadOnlyList<SurfaceMaterialPatternTemplate> LoadTemplates()
        {
            try
            {
                string path = GetFilePath();
                if (!File.Exists(path))
                {
                    return Array.Empty<SurfaceMaterialPatternTemplate>();
                }

                string json = File.ReadAllText(path);
                SurfaceMaterialPatternTemplateFile? file =
                    JsonSerializer.Deserialize<SurfaceMaterialPatternTemplateFile>(json, JsonOptions);

                return file?.Templates ?? new List<SurfaceMaterialPatternTemplate>();
            }
            catch
            {
                return Array.Empty<SurfaceMaterialPatternTemplate>();
            }
        }

        public static void SaveTemplates(IEnumerable<SurfaceMaterialPatternTemplate> templates)
        {
            string path = GetFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

            SurfaceMaterialPatternTemplateFile file = new SurfaceMaterialPatternTemplateFile
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Templates = templates
                    .Where(template => !string.IsNullOrWhiteSpace(template.DisciplineKey) && !string.IsNullOrWhiteSpace(template.MaterialName))
                    .OrderBy(template => template.DisciplineKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(template => template.MaterialName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            string json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(path, json);
        }
    }

    internal static class SurfaceMaterialPatternEngine
    {
        private sealed class SurfacePatternOption
        {
            public SurfacePatternOption(SurfaceQuantitySurfaceOption option)
            {
                Option = option;
                NormalizedName = SurfaceMaterialText.NormalizeKey(option.SurfaceName);
                Alias = ExtractAlias(NormalizedName);
                Prefix = ExtractPrefix(NormalizedName);
                DisplaySegment = BuildDisplaySegment(option.SurfaceName);
            }

            public SurfaceQuantitySurfaceOption Option { get; }
            public string NormalizedName { get; }
            public string Alias { get; }
            public string Prefix { get; }
            public string DisplaySegment { get; }
        }

        public static List<SurfaceMaterialMappingSeed> BuildSeeds(
            IReadOnlyList<SurfaceQuantitySurfaceOption> surfaceOptions,
            IEnumerable<SurfaceMaterialPatternTemplate> templates)
        {
            SurfacePatternIndex index = new SurfacePatternIndex(surfaceOptions);
            Dictionary<string, SurfaceMaterialMappingSeed> seeds = new Dictionary<string, SurfaceMaterialMappingSeed>(StringComparer.OrdinalIgnoreCase);

            foreach (SurfaceMaterialPatternTemplate template in templates ?? Array.Empty<SurfaceMaterialPatternTemplate>())
            {
                foreach (SurfaceMaterialMappingSeed seed in index.BuildSeeds(template))
                {
                    string key = $"{seed.DisciplineKey}|{seed.MaterialName}|{seed.Segment}";
                    seeds[key] = seed;
                }
            }

            return seeds.Values
                .OrderBy(seed => seed.DisciplineKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(seed => seed.Segment, StringComparer.OrdinalIgnoreCase)
                .ThenBy(seed => seed.MaterialName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<SurfaceMaterialPatternTemplate> BuildTemplatesFromRequest(
            SurfaceMaterialQuantitiesRequest request,
            IReadOnlyDictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup)
        {
            Dictionary<string, SurfaceMaterialPatternTemplate> templates =
                SurfaceMaterialPatternStorage.LoadTemplates()
                    .ToDictionary(template => $"{template.DisciplineKey}|{template.MaterialName}", StringComparer.OrdinalIgnoreCase);

            foreach (SurfaceMaterialQuantitiesEntryRequest entry in request.Entries ?? Array.Empty<SurfaceMaterialQuantitiesEntryRequest>())
            {
                SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(entry.DisciplineKey, entry.MaterialName);
                if (definition == null)
                {
                    continue;
                }

                string key = $"{entry.DisciplineKey}|{entry.MaterialName}";
                templates[key] = new SurfaceMaterialPatternTemplate
                {
                    DisciplineKey = entry.DisciplineKey,
                    MaterialName = entry.MaterialName,
                    MeasurementRuleKey = SurfaceMeasurementRuleCatalog.ResolveEffectiveKey(
                        entry.DisciplineKey,
                        entry.MaterialName,
                        entry.MeasurementRuleKey),
                    TopAlias = ResolveAlias(surfaceLookup, entry.TopSurfaceKey),
                    BottomAlias = ResolveAlias(surfaceLookup, entry.BottomSurfaceKey),
                    AreaAlias = ResolveAlias(surfaceLookup, entry.AreaSurfaceKey),
                    VolumeAlias = ResolveAlias(surfaceLookup, entry.VolumeSurfaceKey)
                };
            }

            return templates.Values.ToList();
        }

        private static string ResolveAlias(
            IReadOnlyDictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup,
            string selectionKey)
        {
            if (string.IsNullOrWhiteSpace(selectionKey) ||
                !surfaceLookup.TryGetValue(selectionKey, out SurfaceQuantitySurfaceOption? option))
            {
                return string.Empty;
            }

            return ExtractAlias(SurfaceMaterialText.NormalizeKey(option.SurfaceName));
        }

        private static string ExtractAlias(string normalizedSurfaceName)
        {
            if (string.IsNullOrWhiteSpace(normalizedSurfaceName))
            {
                return string.Empty;
            }

            string[] tokens = normalizedSurfaceName
                .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);

            return tokens.Length == 0 ? string.Empty : tokens[^1];
        }

        private static string ExtractPrefix(string normalizedSurfaceName)
        {
            if (string.IsNullOrWhiteSpace(normalizedSurfaceName))
            {
                return string.Empty;
            }

            int separatorIndex = normalizedSurfaceName.LastIndexOf('_');
            return separatorIndex > 0
                ? normalizedSurfaceName.Substring(0, separatorIndex)
                : string.Empty;
        }

        private static string BuildDisplaySegment(string surfaceName)
        {
            if (string.IsNullOrWhiteSpace(surfaceName))
            {
                return string.Empty;
            }

            int separatorIndex = Math.Max(surfaceName.LastIndexOf('-'), surfaceName.LastIndexOf('_'));
            return separatorIndex > 0
                ? surfaceName.Substring(0, separatorIndex).Trim(' ', '-', '_')
                : surfaceName.Trim();
        }

        private sealed class SurfacePatternIndex
        {
            private readonly Dictionary<string, List<SurfacePatternOption>> _optionsByAlias;

            public SurfacePatternIndex(IReadOnlyList<SurfaceQuantitySurfaceOption> surfaceOptions)
            {
                _optionsByAlias = surfaceOptions
                    .Where(option => !string.IsNullOrWhiteSpace(option.SelectionKey))
                    .Select(option => new SurfacePatternOption(option))
                    .GroupBy(option => option.Alias, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        grouping => grouping.Key,
                        grouping => grouping
                            .OrderBy(option => option.Option.SurfaceName, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        StringComparer.OrdinalIgnoreCase);
            }

            public IEnumerable<SurfaceMaterialMappingSeed> BuildSeeds(SurfaceMaterialPatternTemplate template)
            {
                SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(template.DisciplineKey, template.MaterialName);
                if (definition == null)
                {
                    yield break;
                }

                foreach (string prefix in GetCandidatePrefixes(template, definition))
                {
                    SurfacePatternOption? top = Resolve(prefix, template.TopAlias, requireTin: true, requireVolume: false);
                    SurfacePatternOption? bottom = Resolve(prefix, template.BottomAlias, requireTin: true, requireVolume: false);
                    SurfacePatternOption? area = Resolve(prefix, template.AreaAlias, requireTin: true, requireVolume: false);
                    SurfacePatternOption? volume = Resolve(prefix, template.VolumeAlias, requireTin: false, requireVolume: true);

                    if (!IsUsable(definition, top, bottom, area, volume))
                    {
                        continue;
                    }

                    yield return new SurfaceMaterialMappingSeed
                    {
                        AutoMapped = true,
                        IsSelected = true,
                        DisciplineKey = template.DisciplineKey,
                        MaterialName = template.MaterialName,
                        MeasurementRuleKey = string.IsNullOrWhiteSpace(template.MeasurementRuleKey)
                            ? SurfaceMaterialCatalog.GetDefaultMeasurementRuleKey(template.DisciplineKey, template.MaterialName)
                            : template.MeasurementRuleKey,
                        Segment = ResolveSegment(prefix, top, bottom, area, volume),
                        TopSurfaceKey = top?.Option.SelectionKey ?? string.Empty,
                        BottomSurfaceKey = bottom?.Option.SelectionKey ?? string.Empty,
                        AreaSurfaceKey = area?.Option.SelectionKey ?? string.Empty,
                        VolumeSurfaceKey = volume?.Option.SelectionKey ?? string.Empty
                    };
                }
            }

            private IEnumerable<string> GetCandidatePrefixes(
                SurfaceMaterialPatternTemplate template,
                SurfaceMaterialDefinition definition)
            {
                if (definition.UsesAreaMeasurement)
                {
                    string areaAlias = !string.IsNullOrWhiteSpace(template.AreaAlias) ? template.AreaAlias : template.TopAlias;
                    return GetPrefixes(areaAlias);
                }

                if (!string.IsNullOrWhiteSpace(template.VolumeAlias))
                {
                    return GetPrefixes(template.VolumeAlias);
                }

                List<string> topPrefixes = GetPrefixes(template.TopAlias).ToList();
                List<string> bottomPrefixes = GetPrefixes(template.BottomAlias).ToList();

                if (topPrefixes.Count == 0)
                {
                    return bottomPrefixes;
                }

                if (bottomPrefixes.Count == 0)
                {
                    return topPrefixes;
                }

                return topPrefixes.Intersect(bottomPrefixes, StringComparer.OrdinalIgnoreCase);
            }

            private IEnumerable<string> GetPrefixes(string alias)
            {
                string normalizedAlias = SurfaceMaterialText.NormalizeKey(alias);
                if (string.IsNullOrWhiteSpace(normalizedAlias) ||
                    !_optionsByAlias.TryGetValue(normalizedAlias, out List<SurfacePatternOption>? options))
                {
                    return Array.Empty<string>();
                }

                return options
                    .Select(option => option.Prefix)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            private SurfacePatternOption? Resolve(string prefix, string alias, bool requireTin, bool requireVolume)
            {
                string normalizedAlias = SurfaceMaterialText.NormalizeKey(alias);
                if (string.IsNullOrWhiteSpace(normalizedAlias) ||
                    !_optionsByAlias.TryGetValue(normalizedAlias, out List<SurfacePatternOption>? options))
                {
                    return null;
                }

                return options.FirstOrDefault(option =>
                    string.Equals(option.Prefix, prefix, StringComparison.OrdinalIgnoreCase) &&
                    (!requireTin || option.Option.IsTinSurface) &&
                    (!requireVolume || option.Option.IsVolumeSurface));
            }

            private static bool IsUsable(
                SurfaceMaterialDefinition definition,
                SurfacePatternOption? top,
                SurfacePatternOption? bottom,
                SurfacePatternOption? area,
                SurfacePatternOption? volume)
            {
                if (definition.UsesAreaMeasurement)
                {
                    return area != null || top != null;
                }

                if (volume != null)
                {
                    return true;
                }

                return top != null && bottom != null;
            }

            private static string ResolveSegment(
                string prefix,
                SurfacePatternOption? top,
                SurfacePatternOption? bottom,
                SurfacePatternOption? area,
                SurfacePatternOption? volume)
            {
                SurfacePatternOption? sample = top ?? bottom ?? area ?? volume;
                if (sample != null && !string.IsNullOrWhiteSpace(sample.DisplaySegment))
                {
                    return sample.DisplaySegment;
                }

                return prefix.Replace('_', '-');
            }
        }
    }
}
