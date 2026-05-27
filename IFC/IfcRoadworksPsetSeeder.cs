using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Aec.DatabaseServices;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AecDataType = Autodesk.Aec.PropertyData.DataType;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    public class IfcRoadworksPsetSeeder
    {
        public readonly struct RoadworksPsetCatalogSummary
        {
            public RoadworksPsetCatalogSummary(int definitionCount, int createdPsets, int updatedPsets, int addedFields)
            {
                DefinitionCount = definitionCount;
                CreatedPsets = createdPsets;
                UpdatedPsets = updatedPsets;
                AddedFields = addedFields;
            }

            public int DefinitionCount { get; }
            public int CreatedPsets { get; }
            public int UpdatedPsets { get; }
            public int AddedFields { get; }
        }

        private const string StandardSourceLabel = "buildingSMART IFC4x3 road domain";
        private const string CustomSourceLabel = "Pavimentacao e terraplenagem rodoviaria";
        private static readonly AecDataType StringDataType = ResolveDataType("Text", "String");
        private static readonly AecDataType RealDataType = StringDataType;
        private static readonly AecDataType IntegerDataType = StringDataType;
        private static readonly AecDataType BooleanDataType = StringDataType;

        [CommandMethod("IFC_CRIAR_PSETS_RODOVIARIOS")]
        public void CriarPsetsRodoviarios()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                RoadworksPsetCatalogSummary summary = EnsureRoadworksPsets(db);

                docEditor.WriteMessage(
                    $"\n[PSET] Catalogo rodoviario aplicado. Psets previstos: {summary.DefinitionCount} | Criados: {summary.CreatedPsets} | Atualizados: {summary.UpdatedPsets} | Campos adicionados: {summary.AddedFields}"
                );
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\n[AutoCAD] Erro ao criar PSETs IFC rodoviarios: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[.NET] Erro ao criar PSETs IFC rodoviarios: {FormatExceptionChain(ex)}");
            }
        }

        public static RoadworksPsetCatalogSummary EnsureRoadworksPsets(Database db)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            RoadworksPsetCatalogSummary summary = EnsureRoadworksPsets(db, tr);
            tr.Commit();
            return summary;
        }

        internal static RoadworksPsetCatalogSummary EnsureRoadworksPsets(Database db, Transaction tr)
        {
            IfcPsetFactory.EnsureDefaultPsets(db, tr, Manager.DocEditor);

            List<PsetDefinitionSpec> allDefinitions = MergeDefinitions(BuildCuratedRoadworksDefinitions());
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);
            int createdPsets = 0;
            int updatedPsets = 0;
            int addedFields = 0;

            foreach (PsetDefinitionSpec definition in allDefinitions.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                EnsureOrUpdatePropertySetDefinition(
                    db,
                    tr,
                    dict,
                    definition,
                    ref createdPsets,
                    ref updatedPsets,
                    ref addedFields
                );
            }

            return new RoadworksPsetCatalogSummary(allDefinitions.Count, createdPsets, updatedPsets, addedFields);
        }

        private static List<PsetDefinitionSpec> BuildCuratedRoadworksDefinitions()
        {
            return new List<PsetDefinitionSpec>
            {
                CreateStandardPset(
                    "Pset_CivilElementCommon",
                    "Pset padrao comum para elementos civis lineares e areais.",
                    Field("Reference", StringDataType, "Referencia do elemento no projeto."),
                    Field("Status", StringDataType, "Status do elemento.")
                ),
                CreateStandardPset(
                    "Pset_CourseCommon",
                    "Pset padrao para camadas de pavimento e outras courses lineares.",
                    Field("NominalLength", RealDataType, "Comprimento nominal da camada."),
                    Field("NominalThickness", RealDataType, "Espessura nominal da camada."),
                    Field("NominalWidth", RealDataType, "Largura nominal da camada.")
                ),
                CreateStandardPset(
                    "Qto_CourseBaseQuantities",
                    "Conjunto padrao de quantitativos para elementos IfcCourse.",
                    Field("Length", RealDataType, "Comprimento do elemento."),
                    Field("Width", RealDataType, "Largura do elemento."),
                    Field("Thickness", RealDataType, "Espessura geometrica do elemento."),
                    Field("Volume", RealDataType, "Volume do elemento."),
                    Field("GrossVolume", RealDataType, "Volume bruto do elemento."),
                    Field("Weight", RealDataType, "Peso total do elemento.")
                ),
                CreateStandardPset(
                    "Pset_CourseApplicationConditions",
                    "Pset padrao para condicoes de aplicacao da camada.",
                    Field("ApplicationTemperature", RealDataType, "Temperatura prevista de aplicacao."),
                    Field("WeatherConditions", StringDataType, "Condicoes climaticas de aplicacao.")
                ),
                CreateStandardPset(
                    "Pset_BoundedCourseCommon",
                    "Pset padrao para camadas delimitadas por espalhamento.",
                    Field("SpreadingRate", RealDataType, "Taxa de espalhamento por area.")
                ),
                CreateStandardPset(
                    "Pset_PavementCommon",
                    "Pset padrao para estruturas de pavimento.",
                    Field("Reference", StringDataType, "Referencia do pavimento."),
                    Field("Status", StringDataType, "Status do pavimento."),
                    Field("NominalThicknessEnd", RealDataType, "Espessura nominal no final do trecho."),
                    Field("StructuralSlope", RealDataType, "Inclinacao estrutural prevista."),
                    Field("StructuralSlopeType", StringDataType, "Tipo de inclinacao estrutural."),
                    Field("NominalWidth", RealDataType, "Largura nominal do pavimento."),
                    Field("NominalLength", RealDataType, "Comprimento nominal do pavimento."),
                    Field("NominalThickness", RealDataType, "Espessura nominal do pavimento.")
                ),
                CreateStandardPset(
                    "Qto_PavementBaseQuantities",
                    "Conjunto padrao de quantitativos para elementos IfcPavement.",
                    Field("Length", RealDataType, "Comprimento do pavimento."),
                    Field("Width", RealDataType, "Largura do pavimento."),
                    Field("Depth", RealDataType, "Profundidade ou espessura do pavimento."),
                    Field("GrossArea", RealDataType, "Area bruta do pavimento."),
                    Field("NetArea", RealDataType, "Area liquida do pavimento."),
                    Field("GrossVolume", RealDataType, "Volume bruto do pavimento."),
                    Field("NetVolume", RealDataType, "Volume liquido do pavimento.")
                ),
                CreateStandardPset(
                    "Pset_PavementSurfaceCommon",
                    "Pset padrao para caracteristicas da superficie do pavimento.",
                    Field("PavementRoughness", StringDataType, "Rugosidade superficial."),
                    Field("PavementTexture", StringDataType, "Textura superficial.")
                ),
                CreateStandardPset(
                    "Pset_PavementMillingCommon",
                    "Pset padrao para fresagem de pavimento.",
                    Field("NominalDepth", RealDataType, "Profundidade nominal de fresagem."),
                    Field("NominalWidth", RealDataType, "Largura nominal de fresagem.")
                ),
                CreateStandardPset(
                    "Pset_TrenchExcavationCommon",
                    "Pset padrao para escavacoes em vala associadas a obras lineares.",
                    Field("NominalDepth", RealDataType, "Profundidade nominal da escavacao."),
                    Field("NominalWidth", RealDataType, "Largura nominal da escavacao.")
                ),
                CreateStandardPset(
                    "Pset_TransitionSectionCommon",
                    "Pset padrao para secoes de transicao em terraplenagem e plataforma.",
                    Field("NominalLength", RealDataType, "Comprimento nominal da secao de transicao.")
                ),
                CreateStandardPset(
                    "Pset_RoadDesignCriteriaCommon",
                    "Pset padrao para criterios de projeto rodoviario.",
                    Field("Crossfall", RealDataType, "Inclinacao transversal de projeto."),
                    Field("DesignSpeed", RealDataType, "Velocidade de projeto."),
                    Field("DesignTrafficVolume", RealDataType, "Volume de trafego de projeto."),
                    Field("DesignVehicleClass", StringDataType, "Classe do veiculo de projeto."),
                    Field("LaneWidth", RealDataType, "Largura de faixa de projeto."),
                    Field("NumberOfThroughLanes", IntegerDataType, "Numero de faixas de circulacao."),
                    Field("RoadDesignClass", StringDataType, "Classe funcional ou normativa da via.")
                ),
                CreateStandardPset(
                    "Pset_Superelevation",
                    "Pset padrao para parametrizacao de superelevacao.",
                    Field("Side", StringDataType, "Lado ao qual a superelevacao se aplica."),
                    Field("Superelevation", RealDataType, "Valor de superelevacao."),
                    Field("TransitionSuperelevation", RealDataType, "Superelevacao na transicao.")
                ),
                CreateStandardPset(
                    "Pset_Width",
                    "Pset padrao para largura e sua transicao.",
                    Field("Side", StringDataType, "Lado ao qual a largura se aplica."),
                    Field("TransitionWidth", RealDataType, "Largura em transicao."),
                    Field("NominalWidth", RealDataType, "Largura nominal.")
                ),
                CreateStandardPset(
                    "Pset_ReferentCommon",
                    "Pset padrao para referenciacao linear e marcos.",
                    Field("NameFormat", StringDataType, "Formato do nome do referencial.")
                ),
                CreateStandardPset(
                    "Pset_Stationing",
                    "Pset padrao para estaca e progressiva ao longo do alinhamento.",
                    Field("IncomingStation", RealDataType, "Estaca de chegada ao marco."),
                    Field("Station", RealDataType, "Estaca do elemento."),
                    Field("HasIncreasingStation", BooleanDataType, "Indica se a progressiva cresce no sentido de referencia."),
                    Field("StationInterval", StringDataType, "Intervalo textual de estacas do elemento.")
                ),
                CreateStandardPset(
                    "Pset_LinearReferencingMethod",
                    "Pset padrao para metodo de referenciacao linear.",
                    Field("LRMName", StringDataType, "Nome do metodo de referenciacao linear."),
                    Field("LRMType", StringDataType, "Tipo de metodo de referenciacao linear."),
                    Field("UserDefinedLRMType", StringDataType, "Tipo definido pelo usuario."),
                    Field("LRMUnit", StringDataType, "Unidade usada no metodo."),
                    Field("LRMConstraint", StringDataType, "Restricoes ou regras do metodo.")
                ),
                CreateCustomPset(
                    "Pset_Rodoviario",
                    "Complemento interno para objetos rodoviarios exportados em IFC.",
                    Field("Segmento", StringDataType, "Segmento contratual ou funcional."),
                    Field("Trecho", StringDataType, "Trecho rodoviario."),
                    Field("Lote", StringDataType, "Lote ou contrato."),
                    Field("Rodovia", StringDataType, "Identificacao da rodovia."),
                    Field("UF", StringDataType, "Unidade federativa."),
                    Field("Municipio", StringDataType, "Municipio principal do trecho."),
                    Field("SistemaCoordenadas", StringDataType, "Sistema de coordenadas de trabalho."),
                    Field("CodigoObjeto", StringDataType, "Codigo interno do objeto."),
                    Field("CodeName", StringDataType, "CodeName herdado do corredor quando houver."),
                    Field("SubassemblyName", StringDataType, "Subassembly de origem quando houver."),
                    Field("NomeCorredor", StringDataType, "Nome do corredor de origem."),
                    Field("RegionName", StringDataType, "Regiao do corredor ou trecho."),
                    Field("Side", StringDataType, "Lado da plataforma."),
                    Field("Situacao", StringDataType, "Situacao de implantacao."),
                    Field("Localizacao", StringDataType, "Descricao da localizacao."),
                    Field("EstacaInicial", StringDataType, "Estaca inicial."),
                    Field("EstacaFinal", StringDataType, "Estaca final."),
                    Field("IntervaloEstacas", StringDataType, "Intervalo textual de estacas do elemento."),
                    Field("LRMName", StringDataType, "Metodo de referenciacao linear adotado."),
                    Field("EstagioProjeto", StringDataType, "Etapa de projeto."),
                    Field("Observacoes", StringDataType, "Observacoes complementares.")
                ),
                CreateCustomPset(
                    "Pset_Pavimentacao",
                    "Complemento interno para pavimentacao rodoviaria.",
                    Field("Disciplina", StringDataType, "Disciplina do elemento."),
                    Field("Pista", StringDataType, "Identificacao da pista."),
                    Field("Faixa", StringDataType, "Faixa de rolamento ou acostamento."),
                    Field("Camada", StringDataType, "Nome da camada de pavimento."),
                    Field("FuncaoCamada", StringDataType, "Funcao estrutural ou funcional da camada."),
                    Field("TipoPavimento", StringDataType, "Tipo de pavimento."),
                    Field("Material", StringDataType, "Material predominante."),
                    Field("EspecificacaoMaterial", StringDataType, "Especificacao normativa do material."),
                    Field("ClasseMistura", StringDataType, "Classe da mistura asfaltica ou granular."),
                    Field("EspessuraProjeto", RealDataType, "Espessura de projeto."),
                    Field("LarguraProjeto", RealDataType, "Largura de projeto."),
                    Field("ComprimentoProjeto", RealDataType, "Comprimento de projeto."),
                    Field("AreaProjeto", RealDataType, "Area de projeto."),
                    Field("VolumeProjeto", RealDataType, "Volume de projeto."),
                    Field("CBRProjeto", RealDataType, "CBR de projeto."),
                    Field("CrossfallProjeto", RealDataType, "Crossfall de projeto."),
                    Field("SuperelevacaoProjeto", RealDataType, "Superelevacao de projeto."),
                    Field("EstacaInicial", StringDataType, "Estaca inicial formatada para exibicao."),
                    Field("EstacaFinal", StringDataType, "Estaca final formatada para exibicao."),
                    Field("IRIProjeto", RealDataType, "Meta de irregularidade longitudinal."),
                    Field("MacrotexturaProjeto", RealDataType, "Meta de macrotextura."),
                    Field("TemperaturaAplicacao", RealDataType, "Temperatura de aplicacao."),
                    Field("CondicaoClimatica", StringDataType, "Condicao climatica prevista."),
                    Field("ProfundidadeFresagem", RealDataType, "Profundidade de fresagem prevista.")
                ),
                CreateCustomPset(
                    "Pset_Terraplenagem",
                    "Complemento interno para terraplenagem rodoviaria.",
                    Field("Disciplina", StringDataType, "Disciplina do elemento."),
                    Field("NaturezaMovimentoTerra", StringDataType, "Natureza do movimento de terra."),
                    Field("SecaoTipo", StringDataType, "Secao tipo associada."),
                    Field("OrigemMaterial", StringDataType, "Origem do material."),
                    Field("DestinoMaterial", StringDataType, "Destino do material."),
                    Field("ClasseSolo", StringDataType, "Classe geotecnica do solo."),
                    Field("CategoriaMaterial", StringDataType, "Categoria do material escavado ou compactado."),
                    Field("FatorEmpolamento", RealDataType, "Fator de empolamento."),
                    Field("FatorContracao", RealDataType, "Fator de contracao."),
                    Field("GrauCompactacao", RealDataType, "Grau de compactacao requerido."),
                    Field("UmidadeCompactacao", RealDataType, "Umidade de compactacao."),
                    Field("VolumeCorte", RealDataType, "Volume de corte."),
                    Field("VolumeAterro", RealDataType, "Volume de aterro."),
                    Field("VolumeBotaFora", RealDataType, "Volume destinado a bota-fora."),
                    Field("AreaSecao", RealDataType, "Area de secao."),
                    Field("Altura", RealDataType, "Altura ou desnível."),
                    Field("Largura", RealDataType, "Largura da plataforma."),
                    Field("TaludeCorte", RealDataType, "Inclinacao do talude de corte."),
                    Field("TaludeAterro", RealDataType, "Inclinacao do talude de aterro."),
                    Field("ProfundidadeEscavacao", RealDataType, "Profundidade de escavacao."),
                    Field("TratamentoFundacao", StringDataType, "Tratamento de fundacao previsto.")
                )
            };
        }

        private static PsetDefinitionSpec CreateStandardPset(string name, string description, params PsetFieldSpec[] fields)
        {
            return new PsetDefinitionSpec(name, StandardSourceLabel, description, fields.ToList());
        }

        private static PsetDefinitionSpec CreateCustomPset(string name, string description, params PsetFieldSpec[] fields)
        {
            return new PsetDefinitionSpec(name, CustomSourceLabel, description, fields.ToList());
        }

        private static PsetFieldSpec Field(string propertyName, AecDataType dataType, string description)
        {
            return new PsetFieldSpec(propertyName, dataType, description);
        }

        private static List<PsetDefinitionSpec> MergeDefinitions(IEnumerable<PsetDefinitionSpec> definitions)
        {
            Dictionary<string, PsetDefinitionSpec> merged = new Dictionary<string, PsetDefinitionSpec>(StringComparer.OrdinalIgnoreCase);

            foreach (PsetDefinitionSpec definition in definitions.Where(d => d != null))
            {
                if (!merged.TryGetValue(definition.Name, out PsetDefinitionSpec existing))
                {
                    merged[definition.Name] = definition.Clone();
                    continue;
                }

                if (!string.Equals(existing.SourceLabel, definition.SourceLabel, StringComparison.OrdinalIgnoreCase) &&
                    !existing.SourceLabel.Contains(definition.SourceLabel, StringComparison.OrdinalIgnoreCase))
                {
                    existing.SourceLabel += " + " + definition.SourceLabel;
                }

                if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(definition.Description))
                    existing.Description = definition.Description;

                Dictionary<string, PsetFieldSpec> fieldMap = existing.Fields.ToDictionary(f => f.PropertyName, StringComparer.OrdinalIgnoreCase);
                foreach (PsetFieldSpec field in definition.Fields)
                    MergeField(fieldMap, field);

                existing.Fields = fieldMap.Values.OrderBy(f => f.PropertyName, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return merged.Values.ToList();
        }

        private static void MergeField(IDictionary<string, PsetFieldSpec> fields, PsetFieldSpec incoming)
        {
            if (!fields.TryGetValue(incoming.PropertyName, out PsetFieldSpec existing))
            {
                fields[incoming.PropertyName] = incoming;
                return;
            }

            if (existing.DataType.Equals(StringDataType) && !incoming.DataType.Equals(StringDataType))
                existing.DataType = incoming.DataType;

            if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(incoming.Description))
                existing.Description = incoming.Description;
        }

        private static void EnsureOrUpdatePropertySetDefinition(
            Database db,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            PsetDefinitionSpec definition,
            ref int createdPsets,
            ref int updatedPsets,
            ref int addedFields)
        {
            bool created = false;
            PropertySetDefinition psd = GetOrCreatePropertySetDefinition(db, tr, dict, definition, ref created);

            HashSet<string> existingNames = new HashSet<string>(
                psd.Definitions.Cast<PropertyDefinition>().Select(p => p.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase
            );

            foreach (PsetFieldSpec field in definition.Fields)
            {
                if (existingNames.Contains(field.PropertyName))
                    continue;

                PropertyDefinition prop = new PropertyDefinition();
                prop.SetToStandard(db);
                prop.SubSetDatabaseDefaults(db);
                prop.Name = field.PropertyName;
                prop.DataType = field.DataType;

                TrySetStringProperty(prop, "AlternateName", field.PropertyName);
                TrySetStringProperty(prop, "Description", field.Description);

                psd.Definitions.Add(prop);
                existingNames.Add(field.PropertyName);
                addedFields++;
            }

            if (created)
                createdPsets++;
            else
                updatedPsets++;
        }

        private static PropertySetDefinition GetOrCreatePropertySetDefinition(
            Database db,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            PsetDefinitionSpec definition,
            ref bool created)
        {
            if (dict.Has(definition.Name, tr))
            {
                ObjectId existingId = dict.GetAt(definition.Name);
                PropertySetDefinition existing = (PropertySetDefinition)tr.GetObject(existingId, OpenMode.ForWrite);
                existing.AlternateName = definition.SourceLabel;
                TrySetStringProperty(existing, "Description", definition.Description);
                return existing;
            }

            PropertySetDefinition psd = new PropertySetDefinition();
            psd.SetToStandard(db);
            psd.SubSetDatabaseDefaults(db);
            psd.AppliesToAll = true;
            psd.AlternateName = definition.SourceLabel;
            TrySetStringProperty(psd, "Description", definition.Description);
            dict.AddNewRecord(definition.Name, psd);
            tr.AddNewlyCreatedDBObject(psd, true);
            created = true;
            return psd;
        }

        private static string FormatExceptionChain(System.Exception ex)
        {
            StringBuilder sb = new StringBuilder();

            while (ex != null)
            {
                if (sb.Length > 0)
                    sb.Append(" | INNER: ");

                sb.Append(ex.GetType().Name);
                sb.Append(": ");
                sb.Append(ex.Message);
                ex = ex.InnerException;
            }

            return sb.ToString();
        }

        private static void TrySetStringProperty(object target, string propertyName, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(value))
                return;

            try
            {
                var property = target.GetType().GetProperty(propertyName);
                if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                    return;

                property.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static AecDataType ResolveDataType(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (Enum.TryParse(candidate, true, out AecDataType parsed))
                    return parsed;
            }

            return (AecDataType)Autodesk.AutoCAD.DatabaseServices.DataType.String;
        }

        private sealed class PsetDefinitionSpec
        {
            public PsetDefinitionSpec(string name, string sourceLabel, string description, List<PsetFieldSpec> fields)
            {
                Name = name;
                SourceLabel = sourceLabel;
                Description = description;
                Fields = fields ?? new List<PsetFieldSpec>();
            }

            public string Name { get; }
            public string SourceLabel { get; set; }
            public string Description { get; set; }
            public List<PsetFieldSpec> Fields { get; set; }

            public PsetDefinitionSpec Clone()
            {
                return new PsetDefinitionSpec(
                    Name,
                    SourceLabel,
                    Description,
                    Fields.Select(f => f.Clone()).ToList()
                );
            }
        }

        private sealed class PsetFieldSpec
        {
            public PsetFieldSpec(string propertyName, AecDataType dataType, string description)
            {
                PropertyName = propertyName;
                DataType = dataType;
                Description = description;
            }

            public string PropertyName { get; }
            public AecDataType DataType { get; set; }
            public string Description { get; set; }

            public PsetFieldSpec Clone()
            {
                return new PsetFieldSpec(PropertyName, DataType, Description);
            }
        }
    }
}
