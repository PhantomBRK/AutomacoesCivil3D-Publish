using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;

public sealed class ExportacaoSolidosCorredoresResult
{
	public int ProcessedCorridors { get; set; }

	public int ExportedSolids { get; set; }

	public int ExportedBodies { get; set; }

	public string DestinationPath { get; set; } = string.Empty;

	public string ReportPath { get; set; } = string.Empty;

	public List<string> Warnings { get; } = new List<string>();

	public int TotalEntities => ExportedSolids + ExportedBodies;

	public string BuildSummary()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("ExportaÃ§Ã£o concluÃ­da.");
		sb.AppendLine($"Corredores processados: {ProcessedCorridors}");
		sb.AppendLine($"SÃ³lidos exportados: {ExportedSolids}");
		sb.AppendLine($"Bodies exportados: {ExportedBodies}");
		sb.AppendLine($"DWG destino: {DestinationPath}");

		if (!string.IsNullOrWhiteSpace(ReportPath))
		{
			sb.AppendLine($"CSV: {ReportPath}");
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
