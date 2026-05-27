using System;
using System.Globalization; // Para ToString("F3", CultureInfo.InvariantCulture)
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Aec.PropertyData.DatabaseServices; // Para PropertySetDefinition, PropertyDataServices, DictionaryPropertySetDefinitions
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DataType = Autodesk.Aec.PropertyData.DataType;

namespace AutomacoesCivil3D
{
    public class PsetsPipeNetworksApplier
    {
        // Nomes dos Property Sets (conforme Rotina Sinalização.txt)
        private const string PROPSET_NAME_A = "A - Dados do Projeto";
        private const string PROPSET_NAME_B = "B - Informações dos Objetos e Elementos";
        private const string PROPSET_NAME_C = "C - Propriedades Fisicas dos Objetos e Elementos";
        private const string PROPSET_NAME_D = "D - Propriedades Geográficas";
        private const string PROPSET_NAME_E = "E - Requisitos Específicos de Projeto";

        // Nomes dos campos nos Psets (conforme suas correções e Rotina Sinalização.txt)
        // PSET B
        private const string FIELD_B_CODIGO_OBJETO = "Código_do_Objeto";
        private const string FIELD_B_ESTAQ_INICIAL = "EstaqueamentoInicial";
        private const string FIELD_B_ESTAQ_FINAL = "EstaqueamentoFinal";
        //private const string FIELD_B_QUANTIDADE_TOTAL = "Quantidade Total";
        //private const string FIELD_B_EIXO = "Eixo";

        // PSET C
        private const string FIELD_C_ALTURA = "Altura";
        private const string FIELD_C_COMPRIMENTO = "Comprimento";
        private const string FIELD_C_COTA_DE_FUNDO = "Cota_de_Fundo";
        private const string FIELD_C_COTA_DE_TOPO = "Cota_de_Topo";
        private const string FIELD_C_DIAMETRO = "Diâmetro";
        private const string FIELD_C_INCLINACAO = "Inclinação";
        private const string FIELD_C_LARGURA = "Largura";
        //private const string FIELD_C_AREA = "Área";

        // PSET D
        private const string FIELD_D_COORD_X = "Coordenada_Eixo_X";
        private const string FIELD_D_COORD_Y = "Coordenada_Eixo_Y";
        private const string FIELD_D_COORD_Z = "Coordenada_Eixo_Z";

        // PSET E
        private const string FIELD_E_MATERIAL = "Material";

        private const double EPS = 1e-8; // Tolerância para comparações de ponto flutuante

        [CommandMethod("SOLIDOS_DRENAGEM")] // Renomeei o comando para indicar que processa sólidos
        public static void AplicarPsetsRedesTubulacaoSolidos()
        {
            // Obtendo referências para os objetos do documento AutoCAD e Civil 3D
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;


            // Dicionário para armazenar os IDs das definições de Property Set, evitando buscas repetidas
            DictionaryPropertySetDefinitions psetDictionary = new DictionaryPropertySetDefinitions(db);

            try
            {
                using (DocumentLock docLock = civilDoc.LockDocument()) // Garante que o documento esteja bloqueado para modificação
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction()) // Inicia uma transação
                    {

                        // 6) Preparar ModelSpace
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        // 1. Obtém os ObjectIds das definições de Property Set
                        ObjectId propSetIdA = GetPsdIdSafe(psetDictionary, PROPSET_NAME_A, tr, docEditor);
                        ObjectId propSetIdB = GetPsdIdSafe(psetDictionary, PROPSET_NAME_B, tr, docEditor);
                        ObjectId propSetIdC = GetPsdIdSafe(psetDictionary, PROPSET_NAME_C, tr, docEditor);
                        ObjectId propSetIdD = GetPsdIdSafe(psetDictionary, PROPSET_NAME_D, tr, docEditor);
                        ObjectId propSetIdE = GetPsdIdSafe(psetDictionary, PROPSET_NAME_E, tr, docEditor);

                        // Coleta todas as redes de tubos do desenho
                        ObjectIdCollection networkIds = civilDb.GetPipeNetworkIds();
                        if (networkIds == null || networkIds.Count == 0)
                        {
                            docEditor.WriteMessage("\nNenhuma Pipe Network encontrada no desenho.");
                            tr.Commit();
                            return;
                        }

                        docEditor.WriteMessage($"\nIniciando extração de sólidos e aplicação de Psets em {networkIds.Count} Pipe Networks...");

                        // Itera sobre cada Pipe Network encontrada
                        foreach (ObjectId networkId in networkIds)
                        {
                            Network network = (Network)tr.GetObject(networkId, OpenMode.ForRead); // Abre a rede para leitura
                            docEditor.WriteMessage($"\nProcessando Pipe Network: {network.Name}");

                            // --------------------------------------------------------------------------------
                            // Processar Tubos (Pipes)
                            // --------------------------------------------------------------------------------
                            ObjectIdCollection pipeIds = network.GetPipeIds();
                            foreach (ObjectId pipeId in pipeIds)
                            {
                                Pipe pipe = (Pipe)tr.GetObject(pipeId, OpenMode.ForRead); // Abre o Pipe para leitura
                                docEditor.WriteMessage($"  Extraindo sólido do tubo: {pipe.Handle}");
                                Solid3d solidPipe = pipe.Solid3dBody; // Abre o sólido para escrita

                                // Cria o sólido 3D do tubo e adiciona ao Model Space
                                ObjectId solidPipeId = solidPipe.Id;
                                // Inserir o sólido no ModelSpace
                                var solidOuterId = ms.AppendEntity(solidPipe);
                                tr.AddNewlyCreatedDBObject(solidPipe, true);
                                


                                // Aplica os Psets no NOVO SÓLIDO, usando os dados do Pipe original
                                AplicarPsetsEmSolidDePipe(solidPipe, pipe, propSetIdA, propSetIdB, propSetIdC, propSetIdD, propSetIdE, tr, docEditor, network.Name);

                                // injeta PSET IFC:: se existir no DWG
                                string psetIfc = "IfcObject Properties";
                                DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);
                                PropertySetDefinition novo = new PropertySetDefinition();

                                if (!dict.Has(psetIfc, tr))
                                {

                                    novo.SetToStandard(db);
                                    novo.SubSetDatabaseDefaults(db);
                                    novo.AppliesToAll = true;
                                    novo.AlternateName = psetIfc;
                                    novo.Description = "IfcObject Properties";

                                    dict.AddNewRecord("IfcObject Properties", novo);
                                    tr.AddNewlyCreatedDBObject(novo, true);

                                    PropertyDefinition pdNew = new PropertyDefinition();
                                    pdNew.SetToStandard(db);
                                    pdNew.SubSetDatabaseDefaults(db);

                                    pdNew.Name = "IFC::IfcExportAs";
                                    pdNew.Description = "IFC::IfcExportAs";
                                    pdNew.DataType = DataType.Text;
                                    pdNew.DefaultData = " - ";
                                    if (!novo.Definitions.Contains(pdNew))
                                    {
                                        novo.Definitions.Add(pdNew);
                                    }

                                    PropertyDefinition pdNew1 = new PropertyDefinition();
                                    pdNew1.SetToStandard(db);
                                    pdNew1.SubSetDatabaseDefaults(db);

                                    pdNew1.Name = "IFC::IfcPredefinedType";
                                    pdNew1.Description = "IFC::IfcPredefinedType";
                                    pdNew1.DataType = DataType.Text;
                                    pdNew1.DefaultData = " - ";
                                    if (!novo.Definitions.Contains(pdNew1))
                                    {
                                        novo.Definitions.Add(pdNew1);
                                    }


                                    PropertyDataServices.AddPropertySet(solidPipe, novo.ObjectId);

                                }else
                                {
                                    PropertyDefinition pdNew = new PropertyDefinition();
                                    pdNew.SetToStandard(db);
                                    pdNew.SubSetDatabaseDefaults(db);

                                    pdNew.Name = "IFC::IfcExportAs";
                                    pdNew.Description = "IFC::IfcExportAs";
                                    pdNew.DataType = DataType.Text;
                                    pdNew.DefaultData = " - ";
                                    if (!novo.Definitions.Contains(pdNew))
                                    {
                                        novo.Definitions.Add(pdNew);
                                    }

                                    PropertyDefinition pdNew1 = new PropertyDefinition();
                                    pdNew1.SetToStandard(db);
                                    pdNew1.SubSetDatabaseDefaults(db);

                                    pdNew1.Name = "IFC::PredefinedType";
                                    pdNew1.Description = "IFC::PredefinedType";
                                    pdNew1.DataType = DataType.Text;
                                    pdNew1.DefaultData = " - ";
                                    if (!novo.Definitions.Contains(pdNew1))
                                    {
                                        novo.Definitions.Add(pdNew1);
                                    }


                                    PropertyDataServices.AddPropertySet(solidPipe, novo.ObjectId);
                                }

                                AutomacoesCivil3D.IfcConfigurator.ApplyToEntity(solidPipe, "TUBO", db, tr);



                            }

                            // --------------------------------------------------------------------------------
                            // Processar Estruturas (Structures)
                            // --------------------------------------------------------------------------------
                            ObjectIdCollection structureIds = network.GetStructureIds();
                            foreach (ObjectId structId in structureIds)
                            {
                                Structure estrutura = (Structure)tr.GetObject(structId, OpenMode.ForRead); // Abre a Estrutura para leitura
                                docEditor.WriteMessage($"  Extraindo sólido da estrutura: {estrutura.Handle}");

                                // Cria o sólido 3D da estrutura e adiciona ao Model Space
                                /*ObjectId solidStructId = estrutura.CreateSolid(tr, true);
                                if (solidStructId.IsNull)
                                {
                                    docEditor.WriteMessage($"\n[AVISO] Não foi possível criar sólido para a estrutura {estrutura.Handle}.");
                                    continue;
                                }*/
                                Solid3d solidStruct = estrutura.Solid3dBody; // Abre o sólido para escrita
                                var solidOuterId = ms.AppendEntity(solidStruct);
                                tr.AddNewlyCreatedDBObject(solidStruct, true);

                                // Aplica os Psets no NOVO SÓLIDO, usando os dados da Estrutura original
                                AplicarPsetsEmSolidDeEstrutura(solidStruct, estrutura, propSetIdA, propSetIdB, propSetIdC, propSetIdD, propSetIdE, tr, docEditor, network.Name);
                                AutomacoesCivil3D.IfcConfigurator.ApplyToEntity(solidStruct, "PV", db, tr);

                            }
                        }

                        tr.Commit(); // Confirma todas as alterações no banco de dados
                        docEditor.WriteMessage("\nProcessamento de Pipe Networks concluído com sucesso. Sólidos criados e Psets aplicados.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO FATAL] Falha durante a aplicação de Psets nas Pipe Networks: {ex.Message}");
                docEditor.WriteMessage($"\nDetalhes do erro (StackTrace): {ex.StackTrace}");
                // A transação será automaticamente abortada se uma exceção não for capturada antes do fim do 'using'
            }
        }

        /// <summary>
        /// Obtém o ObjectId de uma PropertySetDefinition pelo nome, com tratamento de erro.
        /// </summary>
        private static ObjectId GetPsdIdSafe(DictionaryPropertySetDefinitions dictionary, string psName, Transaction tr, Editor docEditor)
        {
            if (!dictionary.Has(psName, tr))
            {
                docEditor.WriteMessage($"\n[AVISO] Definição de Property Set {psName} não encontrada. Campos relacionados não serão preenchidos.");
                return ObjectId.Null;
            }
            return dictionary.GetAt(psName);
        }

        /// <summary>
        /// Aplica Psets em um Solid3d criado a partir de um Pipe.
        /// </summary>
        private static void AplicarPsetsEmSolidDePipe(
            Solid3d solid, // O NOVO sólido 3D
            Pipe originalPipe, // O Pipe original para extração de dados
            ObjectId propSetIdA,
            ObjectId propSetIdB,
            ObjectId propSetIdC,
            ObjectId propSetIdD,
            ObjectId propSetIdE,
            Transaction tr,
            Editor docEditor,
            string networkName)
        {
            // Extrair dados do Pipe original
            Point3d startPt = originalPipe.StartPoint;
            Point3d endPt = originalPipe.EndPoint;

            double comprimento3D = startPt.DistanceTo(endPt);
            double diametroInterno = originalPipe.InnerDiameterOrWidth;
            double inclinacao = 0.0;
            if (comprimento3D > EPS)
            {
                inclinacao = (endPt.Z - startPt.Z) / comprimento3D; // Inclinação como razão
            }
            double cotaTopoTubo = startPt.Z;   // Cota de início como Cota_de_Topo
            double cotaFundoTubo = endPt.Z;    // Cota de fim como Cota_de_Fundo

            // Coordenadas para Pset D (pode ser o ponto de início ou centro do sólido)
            Point3d solidCoords = originalPipe.StartPoint; // Ou originalPipe.Position;


            // Anexa os Property Sets ao SÓLIDO (se já não estiverem anexados)
            if (!propSetIdA.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdA);
            if (!propSetIdB.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdB);
            if (!propSetIdC.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdC);
            if (!propSetIdD.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdD);
            if (!propSetIdE.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdE);

            // Preencher PSET B: Informações dos Objetos e Elementos
            if (!propSetIdB.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdB);
                if (!psetId.IsNull)
                {
                    PropertySet psetB = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetB, FIELD_B_CODIGO_OBJETO, originalPipe.Handle.ToString(), docEditor); // Usar handle do pipe original como código
                    SetPsetField(psetB, FIELD_B_ESTAQ_INICIAL, "-", docEditor);
                    SetPsetField(psetB, FIELD_B_ESTAQ_FINAL, "-", docEditor);
                    //SetPsetField(psetB, FIELD_B_QUANTIDADE_TOTAL, comprimento3D.ToString("F3", CultureInfo.InvariantCulture), docEditor);
                    //SetPsetField(psetB, FIELD_B_EIXO, networkName, docEditor);
                }
            }

            // Preencher PSET C: Propriedades Fisicas dos Objetos e Elementos
            if (!propSetIdC.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdC);
                if (!psetId.IsNull)
                {
                    PropertySet psetC = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetC, FIELD_C_COMPRIMENTO, comprimento3D.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetC, FIELD_C_DIAMETRO, diametroInterno.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetC, FIELD_C_INCLINACAO, inclinacao.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetC, FIELD_C_COTA_DE_TOPO, cotaTopoTubo.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetC, FIELD_C_COTA_DE_FUNDO, cotaFundoTubo.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                }
            }

            // Preencher PSET D: Propriedades Geográficas
            if (!propSetIdD.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdD);
                if (!psetId.IsNull)
                {
                    PropertySet psetD = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetD, FIELD_D_COORD_X, solidCoords.X.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetD, FIELD_D_COORD_Y, solidCoords.Y.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetD, FIELD_D_COORD_Z, solidCoords.Z.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                }
            }

            // Preencher PSET E: Requisitos Específicos de Projeto (exemplo de Material)
            if (!propSetIdE.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdE);
                if (!psetId.IsNull)
                {
                    PropertySet psetE = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetE, FIELD_E_MATERIAL, originalPipe.DisplayName, docEditor);
                }
            }


            // Pipe
            string codePipe = "TUBO " + originalPipe.PartType.ToString();
            AutomacoesCivil3D.IfcConfigurator.ApplyToEntity(solid, codePipe, Manager.DocData, tr); // pset.Database = mesmo db
        }

        /// <summary>
        /// Aplica Psets em um Solid3d criado a partir de uma Structure.
        /// </summary>
        private static void AplicarPsetsEmSolidDeEstrutura(
            Solid3d solid, // O NOVO sólido 3D
            Structure originalStructure, // A Structure original para extração de dados
            ObjectId propSetIdA,
            ObjectId propSetIdB,
            ObjectId propSetIdC,
            ObjectId propSetIdD,
            ObjectId propSetIdE,
            Transaction tr,
            Editor docEditor,
            string networkName)
        {
            // Extrair dados da Estrutura original
            double altura = originalStructure.Height;
            double diametroOuLargura = originalStructure.DiameterOrWidth;
            double comprimentoEstrutura = 0.0;
            try { comprimentoEstrutura = originalStructure.InnerLength; }
            catch (System.Exception) { /* InnerLength pode não ser aplicável/disponível */ }
            if (comprimentoEstrutura == 0.0)
            {
                try { comprimentoEstrutura = originalStructure.Length; } catch (System.Exception) { }
            }

            

            double cotaTopoEstrutura = originalStructure.RimElevation;
            double cotaFundoEstrutura = originalStructure.SumpElevation;

            Point3d solidCoords = originalStructure.Location; // Ou originalStructure.Position;

            // Anexa os Property Sets ao SÓLIDO
            if (!propSetIdA.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdA);
            if (!propSetIdB.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdB);
            if (!propSetIdC.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdC);
            if (!propSetIdD.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdD);
            if (!propSetIdE.IsNull) PropertyDataServices.AddPropertySet(solid, propSetIdE);

            // Preencher PSET B: Informações dos Objetos e Elementos
            if (!propSetIdB.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdB);
                if (!psetId.IsNull)
                {
                    PropertySet psetB = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetB, FIELD_B_CODIGO_OBJETO, originalStructure.Handle.ToString(), docEditor);
                    SetPsetField(psetB, FIELD_B_ESTAQ_INICIAL, "-", docEditor);
                    SetPsetField(psetB, FIELD_B_ESTAQ_FINAL, "-", docEditor);
                    //SetPsetField(psetB, FIELD_B_QUANTIDADE_TOTAL, "1", docEditor);
                    //SetPsetField(psetB, FIELD_B_EIXO, networkName, docEditor);
                }
            }

            // Preencher PSET C: Propriedades Fisicas dos Objetos e Elementos
            if (!propSetIdC.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdC);
                if (!psetId.IsNull)
                {
                    PropertySet psetC = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetC, FIELD_C_ALTURA, altura.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetC, FIELD_C_COTA_DE_TOPO, cotaTopoEstrutura.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetC, FIELD_C_COTA_DE_FUNDO, cotaFundoEstrutura.ToString("F2", CultureInfo.InvariantCulture), docEditor);

                    // Dimensões específicas para Diâmetro, Largura e Comprimento de Estrutura
                    if (originalStructure.BoundingShape != null) // CORREÇÃO AQUI: usar BoundingShape
                    {
                        BoundingShapeType partType = originalStructure.BoundingShape; // CORREÇÃO AQUI: usar BoundingShapeType
                        if (partType == BoundingShapeType.Cylinder) // CORREÇÃO AQUI: usar BoundingShapeType.Cylinder
                        {
                            SetPsetField(psetC, FIELD_C_DIAMETRO, diametroOuLargura.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                        }
                        else if (partType == BoundingShapeType.Box) // CORREÇÃO AQUI: usar BoundingShapeType.Box
                        {
                            SetPsetField(psetC, FIELD_C_LARGURA, diametroOuLargura.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                            SetPsetField(psetC, FIELD_C_COMPRIMENTO, comprimentoEstrutura.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                        }
                        else
                        {
                            SetPsetField(psetC, FIELD_C_DIAMETRO, diametroOuLargura.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                        }
                    }
                }
            }

            // Preencher PSET D: Propriedades Geográficas
            if (!propSetIdD.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdD);
                if (!psetId.IsNull)
                {
                    PropertySet psetD = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetD, FIELD_D_COORD_X, solidCoords.X.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetD, FIELD_D_COORD_Y, solidCoords.Y.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                    SetPsetField(psetD, FIELD_D_COORD_Z, solidCoords.Z.ToString("F2", CultureInfo.InvariantCulture), docEditor);
                }
            }

            // Preencher PSET E: Requisitos Específicos de Projeto
            if (!propSetIdE.IsNull)
            {
                ObjectId psetId = PropertyDataServices.GetPropertySet(solid, propSetIdE);
                if (!psetId.IsNull)
                {
                    PropertySet psetE = (PropertySet)tr.GetObject(psetId, OpenMode.ForWrite);
                    SetPsetField(psetE, FIELD_E_MATERIAL, originalStructure.DisplayName, docEditor); // CORREÇÃO AQUI: usar DisplayName
                }
            }

            // Structure
            string codeStruct = originalStructure.PartType.ToString();
            AutomacoesCivil3D.IfcConfigurator.ApplyToEntity(solid, codeStruct, Manager.DocData, tr);
        }

        /// <summary>
        /// Helper para definir um campo em um PropertySet.
        /// Verifica se o campo existe antes de tentar definir o valor.
        /// </summary>
        private static void SetPsetField(PropertySet pset, string fieldName, object value, Editor docEditor)
        {
            try
            {
                int index = pset.PropertyNameToId(fieldName);
                if (index != -1)
                {
                    pset.SetAt(index, value);
                }
                else
                {
                    docEditor.WriteMessage($"\n[AVISO] Campo '{fieldName}' não encontrado no Property Set '{pset.Name}'.");
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO CAMPO] Falha ao definir campo '{fieldName}' no Pset '{pset.Name}': {ex.Message}");
            }
        }
    }
}