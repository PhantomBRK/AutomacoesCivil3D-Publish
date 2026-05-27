using System.Collections.Generic;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;

public sealed class CorridorCodeInfo
{
	public string CorridorName { get; set; } = string.Empty;

	public List<string> ShapeCodes { get; set; } = new List<string>();

	public List<string> LinkCodes { get; set; } = new List<string>();
}
