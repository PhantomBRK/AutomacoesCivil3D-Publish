using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    public class IfcDrainagePsetSeeder
    {
        public readonly struct DrainagePsetCatalogSummary
        {
            public DrainagePsetCatalogSummary(int definitionCount, int createdPsets, int updatedPsets, int addedFields)
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

        private const string StandardSourceLabel = "buildingSMART IFC4x3";
        private const string CustomSourceLabel = "Petrobras drenagem industrial";
        private static readonly AecDataType StringDataType = ResolveDataType("Text", "String");
        private static readonly AecDataType RealDataType = StringDataType;
        private static readonly AecDataType IntegerDataType = StringDataType;
        private static readonly AecDataType BooleanDataType = StringDataType;
        private static readonly object SessionCacheLock = new object();
        private static readonly HashSet<int> PreparedDatabases = new HashSet<int>();
        private static readonly Lazy<List<PsetDefinitionSpec>> CachedDefinitions =
            new Lazy<List<PsetDefinitionSpec>>(() => MergeDefinitions(BuildCuratedDrainageDefinitions()));

        [CommandMethod("IFC_CRIAR_PSETS_DRENAGEM")]
        public void CriarPsetsDrenagem()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                DrainagePsetCatalogSummary summary = EnsureDrainagePsets(db);
                docEditor.WriteMessage(
                    $"\n[PSET] Catalogo de drenagem aplicado. Psets previstos: {summary.DefinitionCount} | Criados: {summary.CreatedPsets} | Atualizados: {summary.UpdatedPsets} | Campos adicionados: {summary.AddedFields}"
                );
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\n[AutoCAD] Erro ao criar PSETs IFC de drenagem: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[.NET] Erro ao criar PSETs IFC de drenagem: {FormatExceptionChain(ex)}");
            }
        }

        public static DrainagePsetCatalogSummary EnsureDrainagePsets(Database db)
        {
            int databaseKey = RuntimeHelpers.GetHashCode(db);
            lock (SessionCacheLock)
            {
                if (PreparedDatabases.Contains(databaseKey))
                {
                    return new DrainagePsetCatalogSummary(CachedDefinitions.Value.Count, 0, 0, 0);
                }
            }

            using Transaction tr = db.TransactionManager.StartTransaction();
            DrainagePsetCatalogSummary summary = EnsureDrainagePsets(db, tr);
            tr.Commit();

            lock (SessionCacheLock)
            {
                PreparedDatabases.Add(databaseKey);
            }

            return summary;
        }

        private static DrainagePsetCatalogSummary EnsureDrainagePsets(Database db, Transaction tr)
        {
            List<PsetDefinitionSpec> allDefinitions = CachedDefinitions.Value;
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

            return new DrainagePsetCatalogSummary(allDefinitions.Count, createdPsets, updatedPsets, addedFields);
        }

        private static List<PsetDefinitionSpec> BuildCuratedDrainageDefinitions()
        {
            return new List<PsetDefinitionSpec>
            {
                CreateStandardPset(
                    "Pset_DistributionSystemCommon",
                    "Pset padrao para sistema de distribuicao aplicavel a rede de drenagem.",
                    Field("Reference", StringDataType, "Identificador de referencia do sistema.")
                ),
                CreateStandardPset(
                    "Pset_DistributionPortCommon",
                    "Pset padrao para portas e conexoes de drenagem.",
                    Field("PortNumber", IntegerDataType, "Indice logico da porta no elemento."),
                    Field("ColourCode", StringDataType, "Cor de identificacao da conexao.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementCommon",
                    "Pset padrao comum para caixas, camaras e pocos de visita.",
                    Field("Reference", StringDataType, "Referencia do elemento no projeto."),
                    Field("Status", StringDataType, "Status do elemento.")
                ),
                CreateStandardPset(
                    "Qto_DistributionChamberElementBaseQuantities",
                    "Conjunto padrao de quantitativos para caixas e camaras de drenagem.",
                    Field("GrossSurfaceArea", RealDataType, "Area superficial bruta do elemento."),
                    Field("NetSurfaceArea", RealDataType, "Area superficial liquida do elemento."),
                    Field("GrossVolume", RealDataType, "Volume bruto do elemento."),
                    Field("NetVolume", RealDataType, "Volume liquido do elemento."),
                    Field("Depth", RealDataType, "Profundidade total do elemento.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementTypeInspectionChamber",
                    "Pset padrao para camara de inspecao.",
                    Field("ChamberLengthOrRadius", RealDataType, "Comprimento ou raio da camara."),
                    Field("ChamberWidth", RealDataType, "Largura da camara."),
                    Field("InspectionChamberInvertLevel", RealDataType, "Cota de fundo da camara."),
                    Field("SoffitLevel", RealDataType, "Cota superior interna."),
                    Field("WallMaterial", StringDataType, "Material da parede."),
                    Field("WallThickness", RealDataType, "Espessura da parede."),
                    Field("BaseMaterial", StringDataType, "Material da base."),
                    Field("BaseThickness", RealDataType, "Espessura da base."),
                    Field("WithBackdrop", BooleanDataType, "Indica se possui backdrop."),
                    Field("AccessCoverMaterial", StringDataType, "Material da tampa."),
                    Field("AccessLengthOrRadius", RealDataType, "Comprimento ou raio da tampa."),
                    Field("AccessWidth", RealDataType, "Largura da tampa."),
                    Field("AccessCoverLoadRating", StringDataType, "Classe de carga da tampa.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementTypeInspectionPit",
                    "Pset padrao para poco de inspecao.",
                    Field("Length", RealDataType, "Comprimento do poco."),
                    Field("Width", RealDataType, "Largura do poco."),
                    Field("Depth", RealDataType, "Profundidade do poco.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementTypeManhole",
                    "Pset padrao para manhole e poco de visita.",
                    Field("InvertLevel", RealDataType, "Cota de fundo."),
                    Field("SoffitLevel", RealDataType, "Cota superior interna."),
                    Field("WallMaterial", StringDataType, "Material da parede."),
                    Field("WallThickness", RealDataType, "Espessura da parede."),
                    Field("BaseMaterial", StringDataType, "Material da base."),
                    Field("BaseThickness", RealDataType, "Espessura da base."),
                    Field("IsShallow", BooleanDataType, "Indica se o poco e raso."),
                    Field("HasSteps", BooleanDataType, "Indica se possui degraus."),
                    Field("WithBackdrop", BooleanDataType, "Indica se possui backdrop."),
                    Field("AccessCoverMaterial", StringDataType, "Material da tampa."),
                    Field("AccessLengthOrRadius", RealDataType, "Comprimento ou raio da tampa."),
                    Field("AccessWidth", RealDataType, "Largura da tampa."),
                    Field("AccessCoverLoadRating", StringDataType, "Classe de carga da tampa."),
                    Field("IsAccessibleOnFoot", BooleanDataType, "Indica acesso a pe."),
                    Field("IsLocked", BooleanDataType, "Indica se possui travamento."),
                    Field("NumberOfCableEntries", IntegerDataType, "Quantidade de entradas de cabos."),
                    Field("NumberOfManholeCovers", IntegerDataType, "Quantidade de tampas."),
                    Field("TypeOfShaft", StringDataType, "Tipo ou finalidade do shaft.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementTypeMeterChamber",
                    "Pset padrao para camara de medicao.",
                    Field("ChamberLengthOrRadius", RealDataType, "Comprimento ou raio da camara."),
                    Field("ChamberWidth", RealDataType, "Largura da camara."),
                    Field("WallMaterial", StringDataType, "Material da parede."),
                    Field("WallThickness", RealDataType, "Espessura da parede."),
                    Field("BaseMaterial", StringDataType, "Material da base."),
                    Field("BaseThickness", RealDataType, "Espessura da base."),
                    Field("AccessCoverMaterial", StringDataType, "Material da tampa.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementTypeSump",
                    "Pset padrao para sump ou caixa de acumulacao.",
                    Field("Length", RealDataType, "Comprimento."),
                    Field("Width", RealDataType, "Largura."),
                    Field("SumpInvertLevel", RealDataType, "Cota do ponto mais baixo.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementTypeTrench",
                    "Pset padrao para trench de drenagem.",
                    Field("Width", RealDataType, "Largura."),
                    Field("Depth", RealDataType, "Profundidade."),
                    Field("InvertLevel", RealDataType, "Cota de fundo.")
                ),
                CreateStandardPset(
                    "Pset_DistributionChamberElementTypeValveChamber",
                    "Pset padrao para camara de valvulas.",
                    Field("ChamberLengthOrRadius", RealDataType, "Comprimento ou raio da camara."),
                    Field("ChamberWidth", RealDataType, "Largura da camara."),
                    Field("WallMaterial", StringDataType, "Material da parede."),
                    Field("WallThickness", RealDataType, "Espessura da parede."),
                    Field("BaseMaterial", StringDataType, "Material da base."),
                    Field("BaseThickness", RealDataType, "Espessura da base."),
                    Field("AccessCoverMaterial", StringDataType, "Material da tampa.")
                ),
                CreateStandardPset(
                    "Pset_PipeFittingOccurrence",
                    "Pset padrao de ocorrencia para conexoes de tubulacao.",
                    Field("InteriorRoughnessCoefficient", RealDataType, "Rugosidade interna."),
                    Field("Colour", StringDataType, "Cor do fitting.")
                ),
                CreateStandardPset(
                    "Pset_PipeFittingTypeCommon",
                    "Pset padrao comum para tipos de conexao de tubulacao.",
                    Field("Reference", StringDataType, "Referencia do tipo."),
                    Field("Status", StringDataType, "Status do elemento."),
                    Field("PressureClass", RealDataType, "Classe de pressao."),
                    Field("PressureRange", RealDataType, "Faixa de pressao admissivel."),
                    Field("TemperatureRange", RealDataType, "Faixa de temperatura admissivel."),
                    Field("FittingLossFactor", RealDataType, "Fator de perda de carga do fitting.")
                ),
                CreateStandardPset(
                    "Qto_PipeFittingBaseQuantities",
                    "Conjunto padrao de quantitativos para conexoes de tubulacao.",
                    Field("Length", RealDataType, "Comprimento do fitting ao longo do fluxo."),
                    Field("GrossCrossSectionArea", RealDataType, "Area bruta da secao transversal."),
                    Field("NetCrossSectionArea", RealDataType, "Area liquida da secao transversal."),
                    Field("OuterSurfaceArea", RealDataType, "Area superficial externa do fitting."),
                    Field("GrossWeight", RealDataType, "Peso bruto do fitting."),
                    Field("NetWeight", RealDataType, "Peso liquido do fitting.")
                ),
                CreateStandardPset(
                    "Pset_FittingBend",
                    "Pset padrao para curvas e joelhos.",
                    Field("BendAngle", RealDataType, "Mudanca angular do fluxo."),
                    Field("BendRadius", RealDataType, "Raio de curvatura.")
                ),
                CreateStandardPset(
                    "Pset_FittingJunction",
                    "Pset padrao para tees e cruzetas.",
                    Field("JunctionType", StringDataType, "Tipo de juncao."),
                    Field("JunctionLeftAngle", RealDataType, "Angulo da derivacao esquerda."),
                    Field("JunctionLeftRadius", RealDataType, "Raio da derivacao esquerda."),
                    Field("JunctionRightAngle", RealDataType, "Angulo da derivacao direita."),
                    Field("JunctionRightRadius", RealDataType, "Raio da derivacao direita.")
                ),
                CreateStandardPset(
                    "Pset_FittingTransition",
                    "Pset padrao para reducoes e transicoes.",
                    Field("NominalLength", RealDataType, "Comprimento nominal."),
                    Field("EccentricityInY", RealDataType, "Excentricidade no eixo Y."),
                    Field("EccentricityInZ", RealDataType, "Excentricidade no eixo Z.")
                ),
                CreateStandardPset(
                    "Pset_PipeSegmentOccurrence",
                    "Pset padrao de ocorrencia para tubos de drenagem.",
                    Field("InteriorRoughnessCoefficient", RealDataType, "Rugosidade interna."),
                    Field("Colour", StringDataType, "Cor do tubo."),
                    Field("Gradient", RealDataType, "Declividade do tubo."),
                    Field("InvertElevation", RealDataType, "Cota de fundo do tubo.")
                ),
                CreateStandardPset(
                    "Pset_PipeSegmentTypeCommon",
                    "Pset padrao comum para tipos de tubos e galerias.",
                    Field("Reference", StringDataType, "Referencia do tipo."),
                    Field("Status", StringDataType, "Status do elemento."),
                    Field("WorkingPressure", RealDataType, "Pressao de trabalho."),
                    Field("PressureRange", RealDataType, "Faixa de pressao admissivel."),
                    Field("TemperatureRange", RealDataType, "Faixa de temperatura admissivel."),
                    Field("NominalDiameter", RealDataType, "Diametro nominal."),
                    Field("InnerDiameter", RealDataType, "Diametro interno."),
                    Field("OuterDiameter", RealDataType, "Diametro externo."),
                    Field("Length", RealDataType, "Comprimento do segmento.")
                ),
                CreateStandardPset(
                    "Qto_PipeSegmentBaseQuantities",
                    "Conjunto padrao de quantitativos para tubos, bueiros e canaletas.",
                    Field("Length", RealDataType, "Comprimento do segmento."),
                    Field("GrossCrossSectionArea", RealDataType, "Area bruta da secao transversal."),
                    Field("NetCrossSectionArea", RealDataType, "Area liquida da secao transversal."),
                    Field("OuterSurfaceArea", RealDataType, "Area superficial externa do segmento."),
                    Field("GrossWeight", RealDataType, "Peso bruto do segmento."),
                    Field("NetWeight", RealDataType, "Peso liquido do segmento."),
                    Field("FootPrintArea", RealDataType, "Area de implantacao em planta.")
                ),
                CreateStandardPset(
                    "Pset_PipeSegmentTypeCulvert",
                    "Pset padrao para culvert ou galeria.",
                    Field("InternalWidth", RealDataType, "Largura interna."),
                    Field("ClearDepth", RealDataType, "Altura livre.")
                ),
                CreateStandardPset(
                    "Pset_PipeSegmentTypeGutter",
                    "Pset padrao para canaleta.",
                    Field("Slope", RealDataType, "Inclinacao angular."),
                    Field("FlowRating", RealDataType, "Capacidade de vazao."),
                    Field("Complementaryfunction", StringDataType, "Funcao complementar."),
                    Field("OrthometricHeight", RealDataType, "Altura ortometrica."),
                    Field("IsCovered", BooleanDataType, "Indica se possui grelha ou tampa."),
                    Field("IsMonitored", BooleanDataType, "Indica se e monitorada.")
                ),
                CreateCustomPset(
                    "Pset_DrenagemPetrobras",
                    "Complemento interno para drenagem industrial.",
                    Field("AreaOperacional", StringDataType, "Area operacional da instalacao."),
                    Field("Sistema", StringDataType, "Sistema principal da rede."),
                    Field("Subsistema", StringDataType, "Subsistema ou ramal."),
                    Field("CodigoObjeto", StringDataType, "Codigo interno do ativo ou objeto."),
                    Field("Catalogo", StringDataType, "Catalogo ou familia de origem no SOLIDOS."),
                    Field("Tag", StringDataType, "Tag operacional do elemento."),
                    Field("FamilyNameSolidos", StringDataType, "Nome da familia retornado pelo SOLIDOS."),
                    Field("ModeloSolidos", StringDataType, "Nome do modelo/familia no SOLIDOS."),
                    Field("FuncaoDrenagem", StringDataType, "Funcao hidraulica do elemento."),
                    Field("TipoEfluente", StringDataType, "Tipo de fluido ou efluente drenado."),
                    Field("ClasseMaterial", StringDataType, "Classe de material especificada."),
                    Field("DiametroNominalProjeto", RealDataType, "Diametro nominal de projeto."),
                    Field("DeclividadeProjeto", RealDataType, "Declividade de projeto."),
                    Field("ComprimentoProjeto", RealDataType, "Comprimento de projeto."),
                    Field("LarguraProjeto", RealDataType, "Largura de projeto."),
                    Field("BaseProjeto", RealDataType, "Base interna de projeto."),
                    Field("CotaFundoMontante", RealDataType, "Cota de fundo a montante."),
                    Field("CotaFundoJusante", RealDataType, "Cota de fundo a jusante."),
                    Field("CotaTampa", RealDataType, "Cota da tampa ou grelha."),
                    Field("ProfundidadeUtil", RealDataType, "Profundidade util."),
                    Field("VazaoProjeto", RealDataType, "Vazao de projeto."),
                    Field("ClasseCargaTampa", StringDataType, "Classe de carga da tampa."),
                    Field("CoefManning", RealDataType, "Coeficiente de Manning do SOLIDOS."),
                    Field("CoefHazenWilliams", RealDataType, "Coeficiente de Hazen-Williams do SOLIDOS."),
                    Field("CoefDarcyWeisbach", RealDataType, "Coeficiente de Darcy-Weisbach do SOLIDOS."),
                    Field("CoverHeight", RealDataType, "Cobrimento informado no SOLIDOS."),
                    Field("AlturaTampa", RealDataType, "Altura da tampa."),
                    Field("AlturaGrelha", RealDataType, "Altura da grelha."),
                    Field("AlturaPiso", RealDataType, "Altura do piso."),
                    Field("EspessuraParede", RealDataType, "Espessura da parede da caixa."),
                    Field("EspessuraLaje", RealDataType, "Espessura da laje da caixa."),
                    Field("ComprimentoGrelha", RealDataType, "Comprimento da grelha."),
                    Field("LarguraGrelha", RealDataType, "Largura da grelha."),
                    Field("TipoTampaSolidos", StringDataType, "Tipo de tampa informado no SOLIDOS."),
                    Field("TipoGrelhaSolidos", StringDataType, "Tipo de grelha informado no SOLIDOS."),
                    Field("QuantidadeTampas", IntegerDataType, "Quantidade de tampas."),
                    Field("QuantidadeAco", RealDataType, "Quantidade de aco."),
                    Field("VolumeEstrutura", RealDataType, "Volume da estrutura."),
                    Field("VolumeExterno", RealDataType, "Volume externo para escavacao."),
                    Field("VolumeConcretoArmado", RealDataType, "Volume de concreto armado."),
                    Field("VolumeConcretoMagro", RealDataType, "Volume de concreto magro."),
                    Field("ElevacaoMaximaConexao", RealDataType, "Elevacao maxima das conexoes."),
                    Field("ElevacaoMinimaConexao", RealDataType, "Elevacao minima das conexoes."),
                    Field("MaiorTuboConectado", StringDataType, "Maior tubo conectado."),
                    Field("FolgaTopo", RealDataType, "Folga entre conexoes e tampa."),
                    Field("FolgaTampa", RealDataType, "Folga da tampa."),
                    Field("Deflexao", RealDataType, "Deflexao da conexao."),
                    Field("CriticidadeOperacional", StringDataType, "Criticidade operacional."),
                    Field("AcessoInspecao", BooleanDataType, "Indica acesso seguro para inspecao."),
                    Field("Observacoes", StringDataType, "Observacoes complementares.")
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
