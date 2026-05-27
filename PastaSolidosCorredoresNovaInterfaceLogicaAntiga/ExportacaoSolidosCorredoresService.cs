using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AutomacoesCivil3D.loin;
using AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = System.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;

public sealed class ExportacaoSolidosCorredoresService
{
	public sealed class PropertySetBinding
	{
		public string Name { get; }

		public ObjectId DefinitionId { get; }

		public PropertySetDefinition Definition { get; }

		public PropertySetBinding(string name, ObjectId definitionId, PropertySetDefinition definition)
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			Name = name;
			DefinitionId = definitionId;
			Definition = definition;
		}
	}

	public sealed class ReportRow
	{
		public string Corridor { get; set; } = string.Empty;

		public string EntityType { get; set; } = string.Empty;

		public string Handle { get; set; } = string.Empty;

		public string Layer { get; set; } = string.Empty;

		public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	private static readonly JsonSerializerOptions PsetSnapshotJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	// Apenas os PSets NATIVOS do Civil 3D continuam sendo "essenciais" porque o
	// corridor.ExportSolids() os anexa automaticamente em cada sólido — se não
	// existirem é sinal de DWG inconsistente. Os PSets customizados antigos
	// (Pset_A/B/C/D - "...dos Objetos e Elementos" e Pset_COORDENAÇÃO) foram
	// removidos da lista após a refatoração do pipeline LOIN: o cálculo agora
	// vem direto via PropertySets.ColetarParametrosPorGuidGenerico, sem precisar
	// dos legacy estarem anexados.
	private static readonly string[] LegacyPropertySets = new string[4] {
		"Corridor Identity",
		"Corridor Model Information",
		"Corridor Property Data – User Defined",
		"Corridor Shape Information"
	};

	private static readonly Dictionary<string, string[]> PropertySetAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["Pset_B - Informações dos Objetos e Elementos"] = new string[2] { "Pset_B - Informações dos Objetos e Elementos", "Pset_B - Informacoes dos Objetos e Elementos" },
		["Pset_C - Propriedades Físicas dos Objetos e Elementos"] = new string[2] { "Pset_C - Propriedades Físicas dos Objetos e Elementos", "Pset_C - Propriedades Fisicas dos Objetos e Elementos" },
		["Pset_D - Propriedades Geográficas"] = new string[2] { "Pset_D - Propriedades Geográficas", "Pset_D - Propriedades Geograficas" },
		["Pset_COORDENAÇÃO"] = new string[2] { "Pset_COORDENAÇÃO", "Pset_COORDENACAO" },
		["Corridor Property Data – User Defined"] = new string[3] { "Corridor Property Data – User Defined", "Corridor Property Data - User Defined", "Corridor Property Data — User Defined" }
	};

	public ExportacaoSolidosCorredoresDialogData BuildDialogData()
	{
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Expected O, but got Unknown
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		Document docCad = Manager.DocCad;
		CivilDocument docCivil = Manager.DocCivil;
		Database docData = Manager.DocData;
		List<CorridorExportItem> list = new List<CorridorExportItem>();
		List<PropertySetStatusInfo> list2 = new List<PropertySetStatusInfo>();
		string blockingIssue = null;
		Transaction tr = docData.TransactionManager.StartTransaction();
		try
		{
			foreach (ObjectId item in docCivil.CorridorCollection)
			{
				ObjectId current = item;
				if (!current.IsValid || current.IsNull)
				{
					continue;
				}
				DBObject obj = tr.GetObject(current, (OpenMode)0, false);
				Corridor corridor = (Corridor)(object)((obj is Corridor) ? obj : null);
				if (!((DisposableWrapper)(object)corridor == (DisposableWrapper)null) && !corridor.IsReferenceObject)
				{
					int shapeCodeCount = SafeGetCodes(() => corridor.GetShapeCodes()).Length;
					int linkCodeCount = SafeGetCodes(() => corridor.GetLinkCodes()).Length;
					list.Add(new CorridorExportItem(current, corridor.Name, shapeCodeCount, linkCodeCount, "Local"));
				}
			}
			list = list.OrderBy<CorridorExportItem, string>((CorridorExportItem c) => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
			DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(docData);
			// Essenciais: PSets nativos do Civil 3D (4 itens em LegacyPropertySets agora).
			list2.AddRange(LegacyPropertySets.Select((string name) => new PropertySetStatusInfo(name, isRequired: true, TryGetPsetDefinitionId(dictionary, tr, name) != ObjectId.Null)));
			// Informativos: PSets do template LOIN novo — não bloqueiam a exportação
			// (quando ausentes, são criados pelo LoinCivil3DApplier.EnsureResources).
			string[] loinPsets = new[]
			{
				"Pset_A - Dados de Projeto",
				"Pset_B - Informacoes dos Elementos",
				"Pset_C - Propriedades Fisicas",
				"Pset_D - Layer IFC e Classificacao",
				"Pset_Requisitos por Elemento"
			};
			list2.AddRange(loinPsets.Select((string name) => new PropertySetStatusInfo(name, isRequired: false, TryGetPsetDefinitionId(dictionary, tr, name) != ObjectId.Null)));
			tr.Commit();
		}
		finally
		{
			if (tr != null)
			{
				((IDisposable)tr).Dispose();
			}
		}
		if (list.Count == 0)
		{
			blockingIssue = "Nenhum corredor local foi encontrado no desenho ativo.";
		}
		string activeDrawingPath = GetActiveDrawingPath(docCad);
		string text = BuildDefaultDestinationPath(activeDrawingPath);
		string suggestedReportPath = BuildDefaultCsvPath(text);
		return new ExportacaoSolidosCorredoresDialogData(list, list2, activeDrawingPath, text, suggestedReportPath, blockingIssue);
	}

	public ExportacaoSolidosCorredoresResult Execute(ExportacaoSolidosCorredoresRequest request)
	{
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Expected O, but got Unknown
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Expected O, but got Unknown
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0214: Unknown result type (might be due to invalid IL or missing references)
		//IL_0219: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		//IL_023b: Expected O, but got Unknown
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_025d: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0345: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0379: Unknown result type (might be due to invalid IL or missing references)
		ValidateRequest(request);
		Editor docEditor = Manager.DocEditor;
		Database docData = Manager.DocData;
		CodeNameMappingCatalog codeNameMappingCatalog = null;
		string text = null;
		try
		{
			codeNameMappingCatalog = SynchronizeCodeNameCatalog();
		}
		catch (Exception ex)
		{
			text = "Não foi possível sincronizar o catálogo JSON de CodeNames. A exportação seguiu com a lógica interna atual. Detalhe: " + ex.Message;
		}
		ExportacaoSolidosCorredoresResult exportacaoSolidosCorredoresResult = new ExportacaoSolidosCorredoresResult
		{
			DestinationPath = Path.GetFullPath(request.DestinationPath),
			ReportPath = (request.GenerateReport ? Path.GetFullPath(request.ReportPath) : string.Empty)
		};
		string directoryName = Path.GetDirectoryName(exportacaoSolidosCorredoresResult.DestinationPath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		ObjectIdCollection val = new ObjectIdCollection();
		List<ReportRow> rows = new List<ReportRow>();
		if (!string.IsNullOrWhiteSpace(text))
		{
			exportacaoSolidosCorredoresResult.Warnings.Add(text);
		}
		if (codeNameMappingCatalog != null && codeNameMappingCatalog.UnmappedCount > 0)
		{
			string text2 = string.Join(", ", codeNameMappingCatalog.GetUnmappedCodeNames(5));
			exportacaoSolidosCorredoresResult.Warnings.Add($"Catálogo JSON de CodeNames sincronizado em '{codeNameMappingCatalog.SourcePath}'. Existem {codeNameMappingCatalog.UnmappedCount} códigos sem categoria definida" + (string.IsNullOrWhiteSpace(text2) ? "." : (": " + text2 + ".")));
		}
		Transaction val2 = docData.TransactionManager.StartTransaction();
		try
		{
			List<PropertySetBinding> propertySets = ResolvePropertySets(new DictionaryPropertySetDefinitions(docData), val2);
			PropertySets propertySets2 = new PropertySets(codeNameMappingCatalog);
			foreach (ObjectId corridorId in request.CorridorIds)
			{
				DBObject obj = val2.GetObject(corridorId, (OpenMode)0, false);
				Corridor val3 = (Corridor)(object)((obj is Corridor) ? obj : null);
				if ((DisposableWrapper)(object)val3 == (DisposableWrapper)null || val3.IsReferenceObject)
				{
					continue;
				}
				exportacaoSolidosCorredoresResult.ProcessedCorridors++;
				string[] array = BuildIncludedCodes(val3, request);
				if (array.Length == 0)
				{
					exportacaoSolidosCorredoresResult.Warnings.Add("O corredor '" + val3.Name + "' não possui códigos compatíveis com as opções selecionadas.");
					continue;
				}
				ExportCorridorSolidsParams val4 = new ExportCorridorSolidsParams
				{
					IncludedCodes = array,
					ExportLinks = request.ExportLinks,
					ExportShapes = request.ExportShapes
				};
				foreach (ObjectId item in val3.ExportSolids(val4, docData))
				{
					ObjectId val5 = item;
					if (!val5.IsValid || val5.IsNull || (DisposableWrapper)(object)val5.ObjectClass == (DisposableWrapper)null)
					{
						continue;
					}
					if (val5.ObjectClass.Name == "AcDb3dSolid")
					{
						DBObject obj2 = val2.GetObject(val5, (OpenMode)1, false);
						Solid3d val6 = (Solid3d)(object)((obj2 is Solid3d) ? obj2 : null);
						if (!((DisposableWrapper)(object)val6 == (DisposableWrapper)null))
						{
							ApplyPropertySets((Entity)(object)val6, propertySets);
							propertySets2.PSetSolid(val6, docData, val2);
							val.Add(((DBObject)val6).ObjectId);
							exportacaoSolidosCorredoresResult.ExportedSolids++;
							if (request.GenerateReport)
							{
								TryAddReportRow(rows, val2, (Entity)(object)val6, val3.Name, "3DSOLID", propertySets, exportacaoSolidosCorredoresResult.Warnings);
							}
						}
					}
					else
					{
						if (!(val5.ObjectClass.Name == "AcDbBody"))
						{
							continue;
						}
						DBObject obj3 = val2.GetObject(val5, (OpenMode)1, false);
						Body val7 = (Body)(object)((obj3 is Body) ? obj3 : null);
						if (!((DisposableWrapper)(object)val7 == (DisposableWrapper)null))
						{
							ApplyPropertySets((Entity)(object)val7, propertySets);
							propertySets2.PSetBody(val7, docData, val2);
							val.Add(((DBObject)val7).ObjectId);
							exportacaoSolidosCorredoresResult.ExportedBodies++;
							if (request.GenerateReport)
							{
								TryAddReportRow(rows, val2, (Entity)(object)val7, val3.Name, "BODY", propertySets, exportacaoSolidosCorredoresResult.Warnings);
							}
						}
					}
				}
			}
			val2.Commit();
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
		if (val.Count == 0)
		{
			exportacaoSolidosCorredoresResult.Warnings.Add("Nenhum sólido ou body foi gerado com a seleção atual.");
			return exportacaoSolidosCorredoresResult;
		}
		if (request.GenerateReport)
		{
			ExportReportToCsv(exportacaoSolidosCorredoresResult.ReportPath, rows);
		}
		new Civil3DObjectCopier2().CopyObjectsBetweenDrawings(val, exportacaoSolidosCorredoresResult.DestinationPath, null, docData);
		if (request.RemoveSourceSolidsAfterCopy)
		{
			Transaction val8 = docData.TransactionManager.StartTransaction();
			try
			{
				new ExclusaoObjetos().ApagarSolid3d(val, val8);
				val8.Commit();
			}
			finally
			{
				((IDisposable)val8)?.Dispose();
			}
		}
		docEditor.WriteMessage($"\nExportação finalizada. {exportacaoSolidosCorredoresResult.TotalEntities} entidades enviadas para '{exportacaoSolidosCorredoresResult.DestinationPath}'.");
		return exportacaoSolidosCorredoresResult;
	}

	public CodeNameMappingCatalog SynchronizeCodeNameCatalog()
	{
		Document docCad = Manager.DocCad;
		Transaction val = Manager.DocData.TransactionManager.StartTransaction();
		List<CorridorCodeInfo> corridorInfos;
		try
		{
			corridorInfos = CollectCorridorCodeInfos(val);
			val.Commit();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		return CodeNameMappingCatalog.Sync(GetActiveDrawingPath(docCad), corridorInfos);
	}

	public static string BuildDefaultCsvPath(string destinationPath)
	{
		string path = (string.IsNullOrWhiteSpace(destinationPath) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Exportacao_Solidos.dwg") : destinationPath);
		string path2 = Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string text = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		return Path.Combine(path2, fileNameWithoutExtension + "_RelatorioPSET_" + text + ".csv");
	}

	public static string[] BuildIncludedCodes(Corridor corridor, ExportacaoSolidosCorredoresRequest request)
	{
		IEnumerable<string> enumerable = Array.Empty<string>();
		if (request.ExportShapes)
		{
			enumerable = enumerable.Concat(SafeGetCodes(() => corridor.GetShapeCodes()));
		}
		if (request.ExportLinks)
		{
			enumerable = enumerable.Concat(SafeGetCodes(() => corridor.GetLinkCodes()));
		}
		return enumerable.Where((string c) => !string.IsNullOrWhiteSpace(c)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public static void ApplyPropertySets(Entity entity, IEnumerable<PropertySetBinding> propertySets)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		List<string> list = new List<string>();
		foreach (PropertySetBinding propertySet in propertySets)
		{
			if (!EnsurePropertySet(entity, propertySet.DefinitionId))
			{
				list.Add(propertySet.Name);
			}
		}
		if (list.Count > 0)
		{
			throw new InvalidOperationException($"Falha ao anexar os Property Sets {string.Join(", ", list)} na entidade {((object)entity).GetType().Name} ({((DBObject)entity).Handle}).");
		}
	}

	public static List<CorridorCodeInfo> CollectCorridorCodeInfos(Transaction tr)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		CivilDocument docCivil = Manager.DocCivil;
		List<CorridorCodeInfo> list = new List<CorridorCodeInfo>();
		foreach (ObjectId item in docCivil.CorridorCollection)
		{
			ObjectId current = item;
			if (!current.IsValid || current.IsNull)
			{
				continue;
			}
			DBObject obj = tr.GetObject(current, (OpenMode)0, false);
			Corridor corridor = (Corridor)(object)((obj is Corridor) ? obj : null);
			if (!((DisposableWrapper)(object)corridor == (DisposableWrapper)null) && !corridor.IsReferenceObject)
			{
				list.Add(new CorridorCodeInfo
				{
					CorridorName = corridor.Name,
					ShapeCodes = (from code in SafeGetCodes(() => corridor.GetShapeCodes())
						where !string.IsNullOrWhiteSpace(code)
						select code).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string code) => code, StringComparer.OrdinalIgnoreCase).ToList(),
					LinkCodes = (from code in SafeGetCodes(() => corridor.GetLinkCodes())
						where !string.IsNullOrWhiteSpace(code)
						select code).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string code) => code, StringComparer.OrdinalIgnoreCase).ToList()
				});
			}
		}
		return list.OrderBy<CorridorCodeInfo, string>((CorridorCodeInfo info) => info.CorridorName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static List<PropertySetBinding> ResolvePropertySets(DictionaryPropertySetDefinitions dictionary, Transaction tr)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		List<PropertySetBinding> resolved = new List<PropertySetBinding>();
		string[] legacyPropertySets = LegacyPropertySets;
		foreach (string name in legacyPropertySets)
		{
			ObjectId val = TryGetPsetDefinitionId(dictionary, tr, name);
			if (!(val == ObjectId.Null))
			{
				DBObject obj = tr.GetObject(val, (OpenMode)0, false);
				PropertySetDefinition val2 = (PropertySetDefinition)(object)((obj is PropertySetDefinition) ? obj : null);
				if ((DisposableWrapper)(object)val2 != (DisposableWrapper)null)
				{
					resolved.Add(new PropertySetBinding(name, val, val2));
				}
			}
		}
		string[] array = LegacyPropertySets.Where((string b) => resolved.All((PropertySetBinding r) => !string.Equals(r.Name, b, StringComparison.OrdinalIgnoreCase))).ToArray();
		if (array.Length != 0)
		{
			throw new InvalidOperationException("Os Property Sets essenciais não foram encontrados: " + string.Join(", ", array) + ".");
		}
		return resolved;
	}

	public static void TryAddReportRow(ICollection<ReportRow> rows, Transaction tr, Entity entity, string corridorName, string entityType, IEnumerable<PropertySetBinding> propertySets, ICollection<string> warnings)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			rows.Add(BuildReportRow(tr, entity, corridorName, entityType, propertySets, warnings));
		}
		catch (Exception ex)
		{
			AddReportWarning(warnings, $"Falha ao gerar uma linha do relatório para a entidade {entityType} ({((DBObject)entity).Handle}). Detalhe: {ex.Message}");
		}
	}

	public static ReportRow BuildReportRow(Transaction tr, Entity entity, string corridorName, string entityType, IEnumerable<PropertySetBinding> propertySets, ICollection<string> warnings)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		ReportRow reportRow = new ReportRow
		{
			Corridor = corridorName,
			EntityType = entityType,
			Handle = ((object)((DBObject)entity).Handle/*cast due to .constrained prefix*/).ToString(),
			Layer = entity.Layer
		};
		foreach (PropertySetBinding propertySet in propertySets)
		{
			PropertySet val = TryOpenReportPropertySet(tr, entity, propertySet, warnings);
			if ((DisposableWrapper)(object)val == (DisposableWrapper)null)
			{
				continue;
			}
			foreach (string reportPropertyName in GetReportPropertyNames(propertySet, warnings))
			{
				if (!string.IsNullOrWhiteSpace(reportPropertyName))
				{
					string key = propertySet.Name + "." + reportPropertyName;
					if (!reportRow.Values.ContainsKey(key))
					{
						reportRow.Values[key] = TryReadPropertyValue(val, entity, reportPropertyName);
					}
				}
			}
		}
		return reportRow;
	}

	public static PropertySet? TryOpenReportPropertySet(Transaction tr, Entity entity, PropertySetBinding binding, ICollection<string> warnings)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			ObjectId propertySet = PropertyDataServices.GetPropertySet((DBObject)(object)entity, binding.DefinitionId);
			if (propertySet == ObjectId.Null)
			{
				return null;
			}
			DBObject obj = tr.GetObject(propertySet, (OpenMode)0, false);
			return (PropertySet?)(object)((obj is PropertySet) ? obj : null);
		}
		catch (Exception ex)
		{
			AddReportWarning(warnings, "Falha ao acessar o Property Set '" + binding.Name + "' durante a geração do relatório. Detalhe: " + ex.Message);
			return null;
		}
	}

	public static List<string> GetReportPropertyNames(PropertySetBinding binding, ICollection<string> warnings)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		List<string> list = new List<string>();
		try
		{
			foreach (PropertyDefinition definition2 in binding.Definition.Definitions)
			{
				PropertyDefinition definition = definition2;
				if (!((DisposableWrapper)(object)definition == (DisposableWrapper)null) && !string.IsNullOrWhiteSpace(definition.Name) && !list.Any((string name) => string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase)))
				{
					list.Add(definition.Name);
				}
			}
		}
		catch (Exception ex)
		{
			AddReportWarning(warnings, "Falha ao listar as propriedades do Property Set '" + binding.Name + "' durante a geração do relatório. Detalhe: " + ex.Message);
		}
		return list;
	}

	public static string TryReadPropertyValue(PropertySet propertySet, Entity host, string propertyName)
	{
		try
		{
			int num = propertySet.PropertyNameToId(propertyName);
			object at;
			try
			{
				at = propertySet.GetAt(num, (DBObject)(object)host);
			}
			catch
			{
				at = propertySet.GetAt(num);
			}
			return Convert.ToString(at, CultureInfo.InvariantCulture) ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	public static void AddReportWarning(ICollection<string> warnings, string message)
	{
		if (!warnings.Any((string existing) => string.Equals(existing, message, StringComparison.OrdinalIgnoreCase)))
		{
			warnings.Add(message);
		}
	}

	public static void ExportReportToCsv(string path, List<ReportRow> rows)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal));
		List<string> list = new List<string> { "Corridor", "EntityType", "Handle", "Layer" };
		list.AddRange(rows.SelectMany((ReportRow r) => r.Values.Keys).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string c) => c, StringComparer.OrdinalIgnoreCase));
		StringBuilder stringBuilder = new StringBuilder(1024);
		stringBuilder.AppendLine(string.Join(";", list.Select(EscapeCsv)));
		foreach (ReportRow row in rows)
		{
			List<string> list2 = new List<string>(list.Count);
			foreach (string item in list)
			{
				List<string> list3 = list2;
				list3.Add(item switch
				{
					"Corridor" => EscapeCsv(row.Corridor), 
					"EntityType" => EscapeCsv(row.EntityType), 
					"Handle" => EscapeCsv(row.Handle), 
					"Layer" => EscapeCsv(row.Layer), 
					_ => EscapeCsv(row.Values.TryGetValue(item, out string value) ? value : string.Empty), 
				});
			}
			stringBuilder.AppendLine(string.Join(";", list2));
		}
		File.WriteAllText(path, stringBuilder.ToString(), Encoding.UTF8);
	}

	public static string EscapeCsv(string? value)
	{
		string text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
		if (text.Contains(';') || text.Contains('"'))
		{
			text = "\"" + text.Replace("\"", "\"\"") + "\"";
		}
		return text;
	}

	public static bool EnsurePropertySet(Entity entity, ObjectId propertySetDefinitionId)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			ObjectId propertySet = PropertyDataServices.GetPropertySet((DBObject)(object)entity, propertySetDefinitionId);
			if (propertySet != ObjectId.Null && propertySet.IsValid)
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			PropertyDataServices.AddPropertySet((DBObject)(object)entity, propertySetDefinitionId);
		}
		catch
		{
		}
		try
		{
			ObjectId propertySet2 = PropertyDataServices.GetPropertySet((DBObject)(object)entity, propertySetDefinitionId);
			return propertySet2 != ObjectId.Null && propertySet2.IsValid;
		}
		catch
		{
			return false;
		}
	}

	public static void EnsureDestinationPropertySetDefinitions(string destinationPath, IEnumerable<string> propertySetNames)
	{
		string[] array = propertySetNames.Where((string name) => !string.IsNullOrWhiteSpace(name)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length == 0)
		{
			return;
		}
		PsetSnapshotResult psetSnapshotResult = PsetSnapshotService.Execute(Manager.DocCad);
		string text = Path.Combine(Path.GetTempPath(), $"SOLIDOS_CORREDORES_PSETS_{Guid.NewGuid():N}.json");
		try
		{
			PsetSnapshotFile psetSnapshotFile = JsonSerializer.Deserialize<PsetSnapshotFile>(File.ReadAllText(psetSnapshotResult.OutputPath, Encoding.UTF8), PsetSnapshotJsonOptions);
			if (psetSnapshotFile == null)
			{
				throw new InvalidOperationException("Falha ao preparar o snapshot dos Property Sets para exportaÃ§Ã£o.");
			}
			HashSet<string> requestedDefinitionNames = BuildRequestedPropertySetNames(array);
			psetSnapshotFile.Definicoes = psetSnapshotFile.Definicoes.Where((PsetDefinitionSnapshot definition) => requestedDefinitionNames.Contains(definition.Nome)).ToList();
			if (psetSnapshotFile.Definicoes.Count != 0)
			{
				File.WriteAllText(text, JsonSerializer.Serialize(psetSnapshotFile, PsetSnapshotJsonOptions), Encoding.UTF8);
				PsetSnapshotImportService.Execute(EnsureDocumentOpen(destinationPath), text);
			}
		}
		finally
		{
			TryDeleteFile(text);
			TryDeleteFile(psetSnapshotResult.OutputPath);
		}
	}

	public static HashSet<string> BuildRequestedPropertySetNames(IEnumerable<string> propertySetNames)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string propertySetName in propertySetNames)
		{
			foreach (string propertySetCandidateName in GetPropertySetCandidateNames(propertySetName))
			{
				hashSet.Add(propertySetCandidateName);
			}
		}
		return hashSet;
	}

	public static IEnumerable<string> GetPropertySetCandidateNames(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return Array.Empty<string>();
		}
		if (PropertySetAliases.TryGetValue(name, out string[] value))
		{
			return (from candidate in value.Concat(new string[1] { name })
				where !string.IsNullOrWhiteSpace(candidate)
				select candidate).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		}
		return new string[1] { name };
	}

	public static Document EnsureDocumentOpen(string path)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		string fullPath = Path.GetFullPath(path);
		if (!File.Exists(fullPath))
		{
			Database val = new Database(true, true);
			try
			{
				val.SaveAs(fullPath, (DwgVersion)33);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		if (TryGetOpenDocument(fullPath, out Document document))
		{
			return document;
		}
		return DocumentCollectionExtension.Open(Application.DocumentManager, fullPath, false);
	}

	public static bool TryGetOpenDocument(string fullPath, out Document? document)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		document = null;
		foreach (Document item in Application.DocumentManager)
		{
			Document val = item;
			try
			{
				if (string.Equals(Path.GetFullPath(val.Name), fullPath, StringComparison.OrdinalIgnoreCase))
				{
					document = val;
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	public static void TryDeleteFile(string? path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	public static ObjectId TryGetPsetDefinitionId(DictionaryPropertySetDefinitions dictionary, Transaction tr, string name)
	{
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		string[] value;
		foreach (string item in (IEnumerable<string>)(PropertySetAliases.TryGetValue(name, out value) ? value : new string[1] { name }))
		{
			try
			{
				if (dictionary.Has(item, tr))
				{
					return dictionary.GetAt(item);
				}
			}
			catch
			{
			}
			try
			{
				if ((DisposableWrapper)(object)dictionary != (DisposableWrapper)null && dictionary.Has(item, tr))
				{
					return dictionary.GetAt(item);
				}
			}
			catch
			{
			}
		}
		return ObjectId.Null;
	}

	public static string[] SafeGetCodes(Func<string[]> getter)
	{
		try
		{
			return getter() ?? Array.Empty<string>();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	public static string GetActiveDrawingPath(Document doc)
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
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Desenho_Ativo.dwg");
	}

	public static string BuildDefaultDestinationPath(string activeDrawingPath)
	{
		string? path = Path.GetDirectoryName(activeDrawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(activeDrawingPath);
		return Path.Combine(path, fileNameWithoutExtension + "_SOLIDOS_CORREDORES.dwg");
	}

	public static void ValidateRequest(ExportacaoSolidosCorredoresRequest request)
	{
		if (request == null)
		{
			throw new ArgumentNullException("request");
		}
		if (request.CorridorIds == null || request.CorridorIds.Count == 0)
		{
			throw new InvalidOperationException("Nenhum corredor foi selecionado para exportação.");
		}
		if (!request.ExportShapes && !request.ExportLinks)
		{
			throw new InvalidOperationException("Selecione ao menos um tipo de geometria para exportação.");
		}
		if (string.IsNullOrWhiteSpace(request.DestinationPath))
		{
			throw new InvalidOperationException("O caminho do DWG de destino não foi informado.");
		}
		if (request.GenerateReport && string.IsNullOrWhiteSpace(request.ReportPath))
		{
			throw new InvalidOperationException("O caminho do relatório CSV não foi informado.");
		}
	}
}
