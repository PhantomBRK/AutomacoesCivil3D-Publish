using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AutomacoesCivil3D.Rotinas_Petrobras;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace AutomacoesCivil3D
{
    internal sealed class SurfaceMaterialQuantitiesResult
    {
        public int ProcessedEntries { get; set; }
        public int PavMaterialCount { get; set; }
        public int TrpMaterialCount { get; set; }
        public string PavCsvPath { get; set; } = string.Empty;
        public string TrpCsvPath { get; set; } = string.Empty;
        public List<string> Warnings { get; } = new List<string>();

        public string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Quantitativo por superficies concluido.");
            sb.AppendLine($"Linhas medidas: {ProcessedEntries}");

            if (!string.IsNullOrWhiteSpace(PavCsvPath))
            {
                sb.AppendLine($"Materiais PAV: {PavMaterialCount}");
                sb.AppendLine($"CSV PAV: {PavCsvPath}");
            }

            if (!string.IsNullOrWhiteSpace(TrpCsvPath))
            {
                sb.AppendLine($"Materiais TRP: {TrpMaterialCount}");
                sb.AppendLine($"CSV TRP: {TrpCsvPath}");
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Avisos:");
                foreach (string warning in Warnings.Take(5))
                {
                    sb.AppendLine("- " + warning);
                }
            }

            return sb.ToString().TrimEnd();
        }
    }

    internal sealed class SurfaceMaterialQuantitiesService
    {
        private const string SuggestedPattern = "QTO_<DISCIPLINA>_<MATERIAL>__<TRECHO>__<PAPEL>";
        private const string SuggestedExample = "QTO_PAV_BASE_BRITA_GRADUADA__AL01__TOP";

        private enum SurfaceNameRole
        {
            None,
            Top,
            Bottom,
            Area,
            Volume
        }

        private sealed class SurfaceBuildItem
        {
            public ObjectId SurfaceId { get; set; } = ObjectId.Null;
            public string SurfaceName { get; set; } = string.Empty;
            public string KindLabel { get; set; } = string.Empty;
            public bool IsTinSurface { get; set; }
            public bool IsVolumeSurface { get; set; }
        }

        public SurfaceMaterialQuantitiesDialogData BuildDialogData()
        {
            Document doc = Manager.DocCad;
            CivilDocument civilDoc = Manager.DocCivil;
            Database db = Manager.DocData;

            List<SurfaceQuantitySurfaceOption> surfaceOptions;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                surfaceOptions = BuildSurfaceOptions(civilDoc, tr);
                tr.Commit();
            }

            string activeDrawingPath = GetActiveDrawingPath(doc);
            string suggestedPavCsv = BuildDefaultCsvPath(activeDrawingPath, "PAV");
            string suggestedTrpCsv = BuildDefaultCsvPath(activeDrawingPath, "TRP");
            string? blockingIssue = surfaceOptions.Count == 0
                ? "Nenhuma superficie Civil 3D foi encontrada no desenho ativo."
                : null;
            List<SurfaceMaterialMappingSeed> initialMappings = BuildInitialMappings(surfaceOptions);

            return new SurfaceMaterialQuantitiesDialogData(
                surfaceOptions,
                initialMappings,
                activeDrawingPath,
                suggestedPavCsv,
                suggestedTrpCsv,
                SuggestedPattern,
                SuggestedExample,
                blockingIssue);
        }

        public SurfaceMaterialQuantitiesResult Execute(SurfaceMaterialQuantitiesRequest request)
        {
            ValidateRequest(request);

            Document doc = Manager.DocCad;
            CivilDocument civilDoc = Manager.DocCivil;
            Database db = Manager.DocData;

            SurfaceMaterialQuantitiesResult result = new SurfaceMaterialQuantitiesResult();
            Dictionary<string, MaterialSummary> quantities = new Dictionary<string, MaterialSummary>(StringComparer.OrdinalIgnoreCase);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Dictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup = BuildSurfaceOptions(civilDoc, tr)
                    .ToDictionary(option => option.SelectionKey, StringComparer.OrdinalIgnoreCase);

                Dictionary<string, double> areaCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, double> volumeCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (SurfaceMaterialQuantitiesEntryRequest entry in request.Entries)
                {
                    SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryGetByDisplayName(entry.DisciplineKey, entry.MaterialName);
                    if (definition == null)
                    {
                        result.Warnings.Add($"Material nao reconhecido: {entry.MaterialName}.");
                        continue;
                    }

                    double area = definition.UsesAreaMeasurement
                        ? MeasureArea(entry, surfaceLookup, tr, areaCache)
                        : 0.0;

                    double volume = definition.UsesAreaMeasurement
                        ? 0.0
                        : MeasureVolume(entry, definition, surfaceLookup, tr, volumeCache);

                    string quantityKey = $"{entry.DisciplineKey}|{definition.Name}";
                    if (!quantities.TryGetValue(quantityKey, out MaterialSummary? summary))
                    {
                        summary = new MaterialSummary
                        {
                            Codigo = string.Empty,
                            Name = definition.Name
                        };
                        quantities.Add(quantityKey, summary);
                    }

                    summary.TotalArea += area;
                    summary.TotalVolume += volume;
                    result.ProcessedEntries++;
                }

                try
                {
                    SurfaceMaterialPatternStorage.SaveTemplates(
                        SurfaceMaterialPatternEngine.BuildTemplatesFromRequest(request, surfaceLookup));
                }
                catch
                {
                }

                tr.Commit();
            }

            if (request.GeneratePavCsv)
            {
                List<MaterialSummary> pavData = quantities
                    .Where(pair => pair.Key.StartsWith("PAV|", StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Value)
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (pavData.Count > 0)
                {
                    CsvExporter.ExportToCsvPav(pavData, request.PavCsvPath);
                    result.PavMaterialCount = pavData.Count;
                    result.PavCsvPath = Path.GetFullPath(request.PavCsvPath);
                    OpenIfExists(result.PavCsvPath);
                }
            }

            if (request.GenerateTrpCsv)
            {
                List<MaterialSummary> trpData = quantities
                    .Where(pair => pair.Key.StartsWith("TRP|", StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Value)
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (trpData.Count > 0)
                {
                    CsvExporter.ExportToCsvTrp(trpData, request.TrpCsvPath);
                    result.TrpMaterialCount = trpData.Count;
                    result.TrpCsvPath = Path.GetFullPath(request.TrpCsvPath);
                    OpenIfExists(result.TrpCsvPath);
                }
            }

            if (result.ProcessedEntries == 0)
            {
                result.Warnings.Add("Nenhuma linha valida foi medida.");
            }

            return result;
        }

        private static List<SurfaceMaterialMappingSeed> BuildAutoMappings(IReadOnlyList<SurfaceQuantitySurfaceOption> surfaceOptions)
        {
            Dictionary<string, SurfaceMaterialMappingSeed> mappings = new Dictionary<string, SurfaceMaterialMappingSeed>(StringComparer.OrdinalIgnoreCase);

            foreach (SurfaceQuantitySurfaceOption option in surfaceOptions)
            {
                if (!TryParseSuggestedName(option.SurfaceName, out string disciplineKey, out string materialName, out string segment, out SurfaceNameRole role))
                {
                    continue;
                }

                string safeSegment = string.IsNullOrWhiteSpace(segment) ? string.Empty : segment;
                string key = $"{disciplineKey}|{materialName}|{safeSegment}";
                if (!mappings.TryGetValue(key, out SurfaceMaterialMappingSeed? seed))
                {
                    seed = new SurfaceMaterialMappingSeed
                    {
                        AutoMapped = true,
                        IsSelected = true,
                        DisciplineKey = disciplineKey,
                        MeasurementRuleKey = SurfaceMaterialCatalog.GetDefaultMeasurementRuleKey(disciplineKey, materialName),
                        MaterialName = materialName,
                        Segment = safeSegment
                    };

                    mappings.Add(key, seed);
                }

                switch (role)
                {
                    case SurfaceNameRole.Top:
                        seed.TopSurfaceKey = option.SelectionKey;
                        break;

                    case SurfaceNameRole.Bottom:
                        seed.BottomSurfaceKey = option.SelectionKey;
                        break;

                    case SurfaceNameRole.Area:
                        seed.AreaSurfaceKey = option.SelectionKey;
                        break;

                    case SurfaceNameRole.Volume:
                        seed.VolumeSurfaceKey = option.SelectionKey;
                        break;
                }
            }

            return mappings.Values
                .OrderBy(item => item.DisciplineKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Segment, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.MaterialName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<SurfaceMaterialMappingSeed> BuildInitialMappings(IReadOnlyList<SurfaceQuantitySurfaceOption> surfaceOptions)
        {
            Dictionary<string, SurfaceMaterialMappingSeed> mappings = new Dictionary<string, SurfaceMaterialMappingSeed>(StringComparer.OrdinalIgnoreCase);

            MergeMappings(
                mappings,
                SurfaceMaterialPatternEngine.BuildSeeds(surfaceOptions, SurfaceMaterialPatternCatalog.GetTemplates()));

            MergeMappings(mappings, BuildAutoMappings(surfaceOptions));

            return mappings.Values
                .OrderBy(item => item.DisciplineKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Segment, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.MaterialName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void MergeMappings(
            Dictionary<string, SurfaceMaterialMappingSeed> target,
            IEnumerable<SurfaceMaterialMappingSeed> incomingSeeds)
        {
            foreach (SurfaceMaterialMappingSeed incoming in incomingSeeds ?? Array.Empty<SurfaceMaterialMappingSeed>())
            {
                string key = $"{incoming.DisciplineKey}|{incoming.MaterialName}|{incoming.Segment}";
                if (!target.TryGetValue(key, out SurfaceMaterialMappingSeed? existing))
                {
                    target[key] = CloneSeed(incoming);
                    continue;
                }

                existing.IsSelected |= incoming.IsSelected;
                existing.AutoMapped |= incoming.AutoMapped;

                if (!string.IsNullOrWhiteSpace(incoming.MeasurementRuleKey))
                {
                    existing.MeasurementRuleKey = incoming.MeasurementRuleKey;
                }

                if (!string.IsNullOrWhiteSpace(incoming.TopSurfaceKey))
                {
                    existing.TopSurfaceKey = incoming.TopSurfaceKey;
                }

                if (!string.IsNullOrWhiteSpace(incoming.BottomSurfaceKey))
                {
                    existing.BottomSurfaceKey = incoming.BottomSurfaceKey;
                }

                if (!string.IsNullOrWhiteSpace(incoming.AreaSurfaceKey))
                {
                    existing.AreaSurfaceKey = incoming.AreaSurfaceKey;
                }

                if (!string.IsNullOrWhiteSpace(incoming.VolumeSurfaceKey))
                {
                    existing.VolumeSurfaceKey = incoming.VolumeSurfaceKey;
                }
            }
        }

        private static SurfaceMaterialMappingSeed CloneSeed(SurfaceMaterialMappingSeed seed)
        {
            return new SurfaceMaterialMappingSeed
            {
                IsSelected = seed.IsSelected,
                AutoMapped = seed.AutoMapped,
                Segment = seed.Segment,
                DisciplineKey = seed.DisciplineKey,
                MaterialName = seed.MaterialName,
                MeasurementRuleKey = seed.MeasurementRuleKey,
                TopSurfaceKey = seed.TopSurfaceKey,
                BottomSurfaceKey = seed.BottomSurfaceKey,
                AreaSurfaceKey = seed.AreaSurfaceKey,
                VolumeSurfaceKey = seed.VolumeSurfaceKey
            };
        }

        private static List<SurfaceQuantitySurfaceOption> BuildSurfaceOptions(CivilDocument civilDoc, Transaction tr)
        {
            List<SurfaceBuildItem> items = new List<SurfaceBuildItem>();

            foreach (ObjectId surfaceId in civilDoc.GetSurfaceIds())
            {
                Surface? surface = tr.GetObject(surfaceId, OpenMode.ForRead, false) as Surface;
                if (surface == null)
                {
                    continue;
                }

                bool isVolumeSurface = SafeIsVolumeSurface(surface);
                bool isTinSurface = surface is TinSurface && !isVolumeSurface;

                items.Add(new SurfaceBuildItem
                {
                    SurfaceId = surfaceId,
                    SurfaceName = surface.Name,
                    KindLabel = isVolumeSurface ? "TIN Volume" : isTinSurface ? "TIN" : surface.GetType().Name,
                    IsTinSurface = isTinSurface,
                    IsVolumeSurface = isVolumeSurface
                });
            }

            List<SurfaceBuildItem> ordered = items
                .OrderBy(item => item.SurfaceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SurfaceId.Handle.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<SurfaceQuantitySurfaceOption> result = new List<SurfaceQuantitySurfaceOption>(ordered.Count);

            foreach (IGrouping<string, SurfaceBuildItem> group in ordered.GroupBy(item => item.SurfaceName, StringComparer.OrdinalIgnoreCase))
            {
                int index = 0;
                foreach (SurfaceBuildItem item in group)
                {
                    index++;
                    string selectionKey = group.Count() == 1
                        ? item.SurfaceName
                        : $"{item.SurfaceName} [{index}]";

                    result.Add(new SurfaceQuantitySurfaceOption(
                        item.SurfaceId,
                        selectionKey,
                        item.SurfaceName,
                        item.KindLabel,
                        item.IsTinSurface,
                        item.IsVolumeSurface));
                }
            }

            return result;
        }

        private static bool SafeIsVolumeSurface(Surface surface)
        {
            try
            {
                return surface.IsVolumeSurface;
            }
            catch
            {
                return false;
            }
        }

        private static string GetActiveDrawingPath(Document doc)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(doc.Name) && Path.IsPathRooted(doc.Name))
                {
                    return Path.GetFullPath(doc.Name);
                }
            }
            catch
            {
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Desenho_Ativo.dwg");
        }

        private static string BuildDefaultCsvPath(string activeDrawingPath, string suffix)
        {
            string directory = Path.GetDirectoryName(activeDrawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fileName = Path.GetFileNameWithoutExtension(activeDrawingPath);
            return Path.Combine(directory, $"{fileName}_QTO_SUPERFICIES_{suffix}.csv");
        }

        private static bool TryParseSuggestedName(string surfaceName, out string disciplineKey, out string materialName, out string segment, out SurfaceNameRole role)
        {
            disciplineKey = string.Empty;
            materialName = string.Empty;
            segment = string.Empty;
            role = SurfaceNameRole.None;

            string canonical = CanonicalizeSurfaceName(surfaceName);
            if (!canonical.StartsWith("QTO_", StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = canonical.Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length < 2)
            {
                return false;
            }

            role = ParseRole(parts[^1]);
            if (role == SurfaceNameRole.None)
            {
                return false;
            }

            string header = parts[0];
            if (header.Length <= 4)
            {
                return false;
            }

            string payload = header.Substring(4);
            int separatorIndex = payload.IndexOf('_');
            if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
            {
                return false;
            }

            disciplineKey = payload.Substring(0, separatorIndex);
            string materialToken = payload.Substring(separatorIndex + 1);
            SurfaceMaterialDefinition? definition = SurfaceMaterialCatalog.TryMatchExact(disciplineKey, materialToken);
            if (definition == null)
            {
                return false;
            }

            materialName = definition.Name;
            segment = parts.Length > 2
                ? string.Join("__", parts.Skip(1).Take(parts.Length - 2))
                : string.Empty;

            return true;
        }

        private static string CanonicalizeSurfaceName(string surfaceName)
        {
            string raw = surfaceName ?? string.Empty;
            string withoutAccents = raw.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(withoutAccents.Length);

            foreach (char character in withoutAccents)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    sb.Append(char.ToUpperInvariant(character));
                }
                else if (character == '_')
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append('_');
                }
            }

            string value = sb.ToString().Normalize(NormalizationForm.FormC);
            while (value.Contains("___", StringComparison.Ordinal))
            {
                value = value.Replace("___", "__", StringComparison.Ordinal);
            }

            return value.Trim('_');
        }

        private static SurfaceNameRole ParseRole(string value)
        {
            return value switch
            {
                "TOP" => SurfaceNameRole.Top,
                "BOT" => SurfaceNameRole.Bottom,
                "AREA" => SurfaceNameRole.Area,
                "VOL" => SurfaceNameRole.Volume,
                _ => SurfaceNameRole.None
            };
        }

        private static double MeasureArea(
            SurfaceMaterialQuantitiesEntryRequest entry,
            IReadOnlyDictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup,
            Transaction tr,
            Dictionary<string, double> areaCache)
        {
            string selectionKey = !string.IsNullOrWhiteSpace(entry.AreaSurfaceKey)
                ? entry.AreaSurfaceKey
                : entry.TopSurfaceKey;

            if (areaCache.TryGetValue(selectionKey, out double cachedValue))
            {
                return cachedValue;
            }

            TinSurface surface = GetTinSurface(surfaceLookup, selectionKey, tr);
            double area = ComputeProjectedArea(surface);
            areaCache[selectionKey] = area;
            return area;
        }

        private static double MeasureVolume(
            SurfaceMaterialQuantitiesEntryRequest entry,
            SurfaceMaterialDefinition definition,
            IReadOnlyDictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup,
            Transaction tr,
            Dictionary<string, double> volumeCache)
        {
            string effectiveRuleKey = SurfaceMeasurementRuleCatalog.ResolveEffectiveKey(
                entry.DisciplineKey,
                definition.Name,
                entry.MeasurementRuleKey);

            if (!string.IsNullOrWhiteSpace(entry.VolumeSurfaceKey))
            {
                string directKey = $"VOL|{effectiveRuleKey}|{entry.VolumeSurfaceKey}";
                if (volumeCache.TryGetValue(directKey, out double cachedVolume))
                {
                    return cachedVolume;
                }

                TinVolumeSurface volumeSurface = GetVolumeSurface(surfaceLookup, entry.VolumeSurfaceKey, tr);
                var volumeProperties = volumeSurface.GetVolumeProperties();
                double directVolume = ResolveVolume(
                    volumeProperties.AdjustedCutVolume,
                    volumeProperties.AdjustedFillVolume,
                    volumeProperties.AdjustedNetVolume,
                    effectiveRuleKey);
                volumeCache[directKey] = directVolume;
                return directVolume;
            }

            string pairKey = $"PAIR|{effectiveRuleKey}|{entry.TopSurfaceKey}|{entry.BottomSurfaceKey}";
            if (volumeCache.TryGetValue(pairKey, out double cachedPairVolume))
            {
                return cachedPairVolume;
            }

            SurfaceQuantitySurfaceOption topOption = GetSurfaceOption(surfaceLookup, entry.TopSurfaceKey);
            SurfaceQuantitySurfaceOption bottomOption = GetSurfaceOption(surfaceLookup, entry.BottomSurfaceKey);
            TinSurface _ = GetTinSurface(surfaceLookup, entry.TopSurfaceKey, tr);
            TinSurface __ = GetTinSurface(surfaceLookup, entry.BottomSurfaceKey, tr);

            string tempName = $"QTO_TMP_{Guid.NewGuid():N}".Substring(0, 20);
            ObjectId tempId = TinVolumeSurface.Create(tempName, topOption.SurfaceId, bottomOption.SurfaceId);
            TinVolumeSurface tempVolumeSurface = (TinVolumeSurface)tr.GetObject(tempId, OpenMode.ForWrite);
            var tempVolumeProperties = tempVolumeSurface.GetVolumeProperties();
            double pairVolume = ResolveVolume(
                tempVolumeProperties.AdjustedCutVolume,
                tempVolumeProperties.AdjustedFillVolume,
                tempVolumeProperties.AdjustedNetVolume,
                effectiveRuleKey);
            tempVolumeSurface.Erase();

            volumeCache[pairKey] = pairVolume;
            return pairVolume;
        }

        private static double ResolveVolume(double cutVolume, double fillVolume, double netVolume, string effectiveRuleKey)
        {
            return effectiveRuleKey switch
            {
                SurfaceMeasurementRuleKeys.Cut => cutVolume,
                SurfaceMeasurementRuleKeys.Fill => fillVolume,
                SurfaceMeasurementRuleKeys.NetSigned => netVolume,
                _ => Math.Abs(netVolume)
            };
        }

        private static double ComputeProjectedArea(TinSurface surface)
        {
            double totalArea = 0.0;

            foreach (TinSurfaceTriangle triangle in surface.Triangles)
            {
                totalArea += ComputeTriangleProjectedArea(
                    triangle.Vertex1.Location,
                    triangle.Vertex2.Location,
                    triangle.Vertex3.Location);
            }

            return totalArea;
        }

        private static double ComputeTriangleProjectedArea(Point3d a, Point3d b, Point3d c)
        {
            double cross = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
            return Math.Abs(cross) * 0.5;
        }

        private static SurfaceQuantitySurfaceOption GetSurfaceOption(
            IReadOnlyDictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup,
            string selectionKey)
        {
            if (string.IsNullOrWhiteSpace(selectionKey) || !surfaceLookup.TryGetValue(selectionKey, out SurfaceQuantitySurfaceOption? option))
            {
                throw new InvalidOperationException($"Superficie nao encontrada: {selectionKey}.");
            }

            return option;
        }

        private static TinSurface GetTinSurface(
            IReadOnlyDictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup,
            string selectionKey,
            Transaction tr)
        {
            SurfaceQuantitySurfaceOption option = GetSurfaceOption(surfaceLookup, selectionKey);
            TinSurface? surface = tr.GetObject(option.SurfaceId, OpenMode.ForRead, false) as TinSurface;
            if (surface == null || !option.IsTinSurface)
            {
                throw new InvalidOperationException($"A superficie '{option.SurfaceName}' precisa ser uma TIN Surface.");
            }

            return surface;
        }

        private static TinVolumeSurface GetVolumeSurface(
            IReadOnlyDictionary<string, SurfaceQuantitySurfaceOption> surfaceLookup,
            string selectionKey,
            Transaction tr)
        {
            SurfaceQuantitySurfaceOption option = GetSurfaceOption(surfaceLookup, selectionKey);
            TinVolumeSurface? surface = tr.GetObject(option.SurfaceId, OpenMode.ForRead, false) as TinVolumeSurface;
            if (surface == null || !option.IsVolumeSurface)
            {
                throw new InvalidOperationException($"A superficie '{option.SurfaceName}' precisa ser uma TinVolumeSurface.");
            }

            return surface;
        }

        private static void OpenIfExists(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        private static void ValidateRequest(SurfaceMaterialQuantitiesRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.GeneratePavCsv && !request.GenerateTrpCsv)
            {
                throw new InvalidOperationException("Ative ao menos um CSV de saida.");
            }

            if (request.Entries == null || request.Entries.Count == 0)
            {
                throw new InvalidOperationException("Nenhuma linha de medicao foi selecionada.");
            }

            if (request.GeneratePavCsv && string.IsNullOrWhiteSpace(request.PavCsvPath))
            {
                throw new InvalidOperationException("O caminho do CSV de pavimentacao nao foi informado.");
            }

            if (request.GenerateTrpCsv && string.IsNullOrWhiteSpace(request.TrpCsvPath))
            {
                throw new InvalidOperationException("O caminho do CSV de terraplenagem nao foi informado.");
            }
        }
    }
}
