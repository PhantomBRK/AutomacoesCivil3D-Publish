using System.Collections.Generic;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;

public sealed class CodeNameCatalogFile
{
	public int SchemaVersion { get; set; } = 1;

	public string DrawingPath { get; set; } = string.Empty;

	public string ExportedAtUtc { get; set; } = string.Empty;

	public List<CodeNameCatalogCorridor> Corridors { get; set; } = new List<CodeNameCatalogCorridor>();

	public List<CodeNameCatalogEntry> CodeNames { get; set; } = new List<CodeNameCatalogEntry>();
}
