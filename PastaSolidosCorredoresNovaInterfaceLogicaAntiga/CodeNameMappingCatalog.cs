using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;

public sealed class CodeNameMappingCatalog
{
	public const string UnmappedCategory = "NAO_MAPEADO";

	private static readonly Dictionary<string, string> BuiltInDirectMappings = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["BASE"] = "BASE",
		["BASE_DE_BRITA_GRADUADA"] = "BASE",
		["IMPRIMACAO"] = "BASE",
		["IMPRIMACAO_DE_BASE"] = "BASE",
		["SUBBASE"] = "SUB_BASE",
		["SUB_BASE"] = "SUB_BASE",
		["SUB_BASE_COLCHAO_DRENANTE"] = "SUB_BASE",
		["PAVE"] = "PAVIMENTO",
		["PAVE1"] = "PAVIMENTO",
		["PAVE2"] = "PAVIMENTO",
		["CBUQ"] = "PAVIMENTO",
		["PINTURA_DE_LIGACAO"] = "PAVIMENTO",
		["PINTURA_IMPERMIABILIZANTE_1"] = "PAVIMENTO",
		["PINTURA_IMPERMIABILIZANTE_2"] = "PAVIMENTO",
		["CFT"] = "CFT",
		["REGULARIZACAO_E_COMPACTACAO_DE_SUBLEITO"] = "CFT",
		["PASSEIO"] = "PASSEIO",
		["DAYLIGHT_FILL"] = "TALUDE_ATERRO",
		["DAYLIGHT_CUT"] = "TALUDE_CORTE",
		["DAYLIGHT"] = "OFFSET_TALUDE",
		["SLOPE_LINK"] = "OFFSET_TALUDE",
		["TOP"] = "TOP",
		["DATUM"] = "TOP",
		["FLANGE"] = "TOP",
		["RIP_RAP"] = "TOP"
	};

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	private readonly Dictionary<string, CodeNameCatalogEntry> _entriesByKey;

	public string SourcePath { get; }

	public CodeNameCatalogFile Data { get; }

	public bool WasUpdated { get; }

	public int CorridorCount => Data.Corridors.Count;

	public int EntryCount => Data.CodeNames.Count;

	public int UnmappedCount => Data.CodeNames.Count((CodeNameCatalogEntry entry) => IsUnmappedCategory(GetEffectiveCategory(entry)));

	private CodeNameMappingCatalog(string sourcePath, CodeNameCatalogFile data, bool wasUpdated)
	{
		SourcePath = sourcePath;
		Data = data;
		WasUpdated = wasUpdated;
		_entriesByKey = data.CodeNames.Where((CodeNameCatalogEntry entry) => !string.IsNullOrWhiteSpace(entry.CodeName)).GroupBy<CodeNameCatalogEntry, string>((CodeNameCatalogEntry entry) => NormalizeKey(entry.CodeName), StringComparer.Ordinal).ToDictionary<IGrouping<string, CodeNameCatalogEntry>, string, CodeNameCatalogEntry>((IGrouping<string, CodeNameCatalogEntry> group) => group.Key, (IGrouping<string, CodeNameCatalogEntry> group) => group.First(), StringComparer.Ordinal);
	}

	public string ResolveCategory(string rawCodeName)
	{
		if (!TryGetMappedCategory(rawCodeName, out string category))
		{
			return rawCodeName;
		}
		return category;
	}

	public bool TryGetMappedCategory(string rawCodeName, out string category)
	{
		category = string.Empty;
		if (string.IsNullOrWhiteSpace(rawCodeName))
		{
			return false;
		}
		if (TryResolveBuiltInCategory(rawCodeName, out category))
		{
			return true;
		}
		string key = NormalizeKey(rawCodeName);
		if (!_entriesByKey.TryGetValue(key, out CodeNameCatalogEntry value))
		{
			return false;
		}
		if (!value.Enabled)
		{
			return false;
		}
		category = GetEffectiveCategory(value);
		if (IsUnmappedCategory(category))
		{
			category = string.Empty;
			return false;
		}
		return true;
	}

	public static bool TryResolveBuiltInCategory(string codeName, out string category)
	{
		category = string.Empty;
		string text = NormalizeKey(codeName);
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		return BuiltInDirectMappings.TryGetValue(text, out category);
	}

	public string[] GetUnmappedCodeNames(int maxCount = 0)
	{
		IEnumerable<string> source = (from entry in Data.CodeNames
			where IsUnmappedCategory(GetEffectiveCategory(entry))
			select entry.CodeName into name
			where !string.IsNullOrWhiteSpace(name)
			select name).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string name) => name, StringComparer.OrdinalIgnoreCase);
		if (maxCount > 0)
		{
			source = source.Take(maxCount);
		}
		return source.ToArray();
	}

	public static string BuildDefaultPath(string activeDrawingPath)
	{
		string? path = Path.GetDirectoryName(activeDrawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(activeDrawingPath);
		return Path.Combine(path, fileNameWithoutExtension + "_CodeNames.json");
	}

	public static CodeNameMappingCatalog Sync(string activeDrawingPath, IEnumerable<CorridorCodeInfo> corridorInfos)
	{
		string fullPath = Path.GetFullPath(BuildDefaultPath(activeDrawingPath));
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal));
		CodeNameCatalogFile codeNameCatalogFile = BuildGeneratedFile(activeDrawingPath, corridorInfos);
		CodeNameCatalogFile codeNameCatalogFile2 = codeNameCatalogFile;
		bool num = File.Exists(fullPath);
		if (num)
		{
			codeNameCatalogFile2 = Merge(JsonSerializer.Deserialize<CodeNameCatalogFile>(File.ReadAllText(fullPath, Encoding.UTF8), JsonOptions) ?? throw new InvalidOperationException("Falha ao ler o catálogo JSON de CodeNames."), codeNameCatalogFile);
		}
		string text = JsonSerializer.Serialize(codeNameCatalogFile2, JsonOptions);
		bool flag = !num;
		if (!flag)
		{
			flag = !string.Equals(File.ReadAllText(fullPath, Encoding.UTF8), text, StringComparison.Ordinal);
		}
		if (flag)
		{
			File.WriteAllText(fullPath, text, Encoding.UTF8);
		}
		return new CodeNameMappingCatalog(fullPath, codeNameCatalogFile2, flag);
	}

	public static string SuggestCategory(string codeName)
	{
		if (TryResolveBuiltInCategory(codeName, out string category))
		{
			return category;
		}
		if (MatchesPattern(codeName, "ACOSTAMENTO_PAVIMENTO"))
		{
			return "ACOSTAMENTO_PAVIMENTO";
		}
		if (MatchesPattern(codeName, "PAVIMENTO", "PAVIMENTO1", "PAVIMENTO2"))
		{
			return "PAVIMENTO";
		}
		if (MatchesPattern(codeName, "PASSEIO"))
		{
			return "PASSEIO";
		}
		if (MatchesPattern(codeName, "SUB_BASE", "SUBBASE"))
		{
			return "SUB_BASE";
		}
		if (MatchesPattern(codeName, "BASE"))
		{
			return "BASE";
		}
		if (MatchesPattern(codeName, "GUIA"))
		{
			return "GUIA";
		}
		if (MatchesPattern(codeName, "CFT"))
		{
			return "CFT";
		}
		if (MatchesPattern(codeName, "OFFSET_TALUDE"))
		{
			return "OFFSET_TALUDE";
		}
		if (MatchesPattern(codeName, "TALUDE_ATERRO"))
		{
			return "TALUDE_ATERRO";
		}
		if (MatchesPattern(codeName, "TALUDE_CORTE"))
		{
			return "TALUDE_CORTE";
		}
		if (MatchesPattern(codeName, "BARREIRA"))
		{
			return "BARREIRA";
		}
		if (MatchesPattern(codeName, "PONTE"))
		{
			return "PONTE";
		}
		if (MatchesPattern(codeName, "TOP"))
		{
			return "TOP";
		}
		return "NAO_MAPEADO";
	}

	public static string NormalizeKey(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = value.Normalize(NormalizationForm.FormD);
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		bool flag = false;
		string text2 = text;
		foreach (char c in text2)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				if (char.IsLetterOrDigit(c))
				{
					stringBuilder.Append(char.ToUpperInvariant(c));
					flag = false;
				}
				else if (!flag)
				{
					stringBuilder.Append('_');
					flag = true;
				}
			}
		}
		return stringBuilder.ToString().Trim('_');
	}

	private static CodeNameCatalogFile BuildGeneratedFile(string activeDrawingPath, IEnumerable<CorridorCodeInfo> corridorInfos)
	{
		List<CodeNameCatalogCorridor> list = (from info in corridorInfos.Where((CorridorCodeInfo info) => info != null && !string.IsNullOrWhiteSpace(info.CorridorName)).OrderBy<CorridorCodeInfo, string>((CorridorCodeInfo info) => info.CorridorName, StringComparer.OrdinalIgnoreCase).ToList()
			select new CodeNameCatalogCorridor
			{
				Name = info.CorridorName,
				ShapeCodes = info.ShapeCodes.Where((string code) => !string.IsNullOrWhiteSpace(code)).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string code) => code, StringComparer.OrdinalIgnoreCase)
					.ToList(),
				LinkCodes = info.LinkCodes.Where((string code) => !string.IsNullOrWhiteSpace(code)).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string code) => code, StringComparer.OrdinalIgnoreCase)
					.ToList()
			}).ToList();
		List<(string, string, string)> list2 = new List<(string, string, string)>();
		foreach (CodeNameCatalogCorridor corridor in list)
		{
			list2.AddRange(corridor.ShapeCodes.Select((string code) => (code: code, Name: corridor.Name, "Shape")));
			list2.AddRange(corridor.LinkCodes.Select((string code) => (code: code, Name: corridor.Name, "Link")));
		}
		List<CodeNameCatalogEntry> codeNames = list2.GroupBy<(string, string, string), string>(((string CodeName, string CorridorName, string SourceType) entry) => NormalizeKey(entry.CodeName), StringComparer.Ordinal).Select(delegate(IGrouping<string, (string CodeName, string CorridorName, string SourceType)> group)
		{
			string codeName = group.Select(((string CodeName, string CorridorName, string SourceType) item) => item.CodeName).First((string name) => !string.IsNullOrWhiteSpace(name));
			string text = SuggestCategory(codeName);
			return new CodeNameCatalogEntry
			{
				CodeName = codeName,
				Category = text,
				SuggestedCategory = text,
				Enabled = true,
				CorridorNames = (from item in @group
					select item.CorridorName into name
					where !string.IsNullOrWhiteSpace(name)
					select name).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string name) => name, StringComparer.OrdinalIgnoreCase).ToList(),
				SourceTypes = (from item in @group
					select item.SourceType into type
					where !string.IsNullOrWhiteSpace(type)
					select type).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string type) => type, StringComparer.OrdinalIgnoreCase).ToList()
			};
		}).OrderBy<CodeNameCatalogEntry, string>((CodeNameCatalogEntry entry) => entry.CodeName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		return new CodeNameCatalogFile
		{
			DrawingPath = activeDrawingPath,
			ExportedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
			Corridors = list,
			CodeNames = codeNames
		};
	}

	private static CodeNameCatalogFile Merge(CodeNameCatalogFile existing, CodeNameCatalogFile generated)
	{
		Dictionary<string, CodeNameCatalogEntry> dictionary = existing.CodeNames.Where((CodeNameCatalogEntry entry) => !string.IsNullOrWhiteSpace(entry.CodeName)).GroupBy<CodeNameCatalogEntry, string>((CodeNameCatalogEntry entry) => NormalizeKey(entry.CodeName), StringComparer.Ordinal).ToDictionary<IGrouping<string, CodeNameCatalogEntry>, string, CodeNameCatalogEntry>((IGrouping<string, CodeNameCatalogEntry> group) => group.Key, (IGrouping<string, CodeNameCatalogEntry> group) => group.First(), StringComparer.Ordinal);
		foreach (CodeNameCatalogEntry codeName in generated.CodeNames)
		{
			string key = NormalizeKey(codeName.CodeName);
			if (dictionary.TryGetValue(key, out var value))
			{
				codeName.Enabled = value.Enabled;
				if (!string.IsNullOrWhiteSpace(value.Category))
				{
					codeName.Category = value.Category.Trim();
				}
			}
		}
		generated.ExportedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
		return generated;
	}

	private static bool MatchesPattern(string codeName, params string[] patterns)
	{
		string text = NormalizeKey(codeName);
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		for (int i = 0; i < patterns.Length; i++)
		{
			string value = NormalizeKey(patterns[i]);
			if (!string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static string GetEffectiveCategory(CodeNameCatalogEntry entry)
	{
		if (!string.IsNullOrWhiteSpace(entry.Category))
		{
			return entry.Category.Trim();
		}
		return entry.SuggestedCategory ?? string.Empty;
	}

	private static bool IsUnmappedCategory(string? category)
	{
		if (!string.IsNullOrWhiteSpace(category))
		{
			return string.Equals(NormalizeKey(category), "NAO_MAPEADO", StringComparison.Ordinal);
		}
		return true;
	}
}
