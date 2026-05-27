using System.Collections.Generic;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;

public sealed class CodeNameCatalogEntry
{
	public string CodeName { get; set; } = string.Empty;

	public string Category { get; set; } = string.Empty;

	public string SuggestedCategory { get; set; } = string.Empty;

	public bool Enabled { get; set; } = true;

	public List<string> CorridorNames { get; set; } = new List<string>();

	public List<string> SourceTypes { get; set; } = new List<string>();
}
