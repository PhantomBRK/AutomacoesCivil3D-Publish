using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices; // Ainda é necessário para CivilDocument, se usado em Manager
using Autodesk.Civil.DatabaseServices;    // Ainda é necessário para PropertySet, etc.
using System;
using System.Collections.Generic; // Para List
using System.Linq; // Não é mais estritamente necessário, mas pode manter para outros usos
using Application = Autodesk.AutoCAD.ApplicationServices.Application; // Alias para Autodesk.AutoCAD.ApplicationServices.Application
using Exception = Autodesk.AutoCAD.Runtime.Exception; // Alias para Autodesk.AutoCAD.Runtime.Exception

namespace AutomacoesCivil3D
{
    public class ComandosBlocosFaixas
    {
        /// <summary>
        /// Comando AutoCAD para coletar dados de blocos (faixas de sinalização),
        /// ler seus atributos e aplicar Property Sets (Psets).
        /// Não lida com alinhamentos para determinação de estacas.
        /// </summary>
        [CommandMethod("ColetarDadosBlocosFaixasParaPsets")]
        public static void ColetarDadosBlocosFaixasParaPsets()
        {
            // Obtendo referências para os objetos do documento AutoCAD e Civil 3D
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil; // Mantido, pois Manager.DocCivil pode ser usado em outros lugares
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;

            // Definindo o nome do bloco alvo
            // >>> AJUSTE ESTE NOME DE BLOCO CONFORME SEU PROJETO <<<
            string targetBlockName = "LBO_217"; // Nome do bloco da imagem
            // Definindo o nome do layer alvo onde os blocos serão procurados
            string targetLayerName = "sinC_FAIXAS_BIM";

            // Lista para armazenar os IDs dos blocos processados
            List<ObjectId> processedBlockIds = new List<ObjectId>();

            // Inicia uma transação para acessar e manipular objetos no banco de dados.
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!layerTable.Has(targetLayerName))
                    {
                        docEditor.WriteMessage($"\nErro: O layer '{targetLayerName}' não foi encontrado no desenho. Verifique o nome do layer.");
                        tr.Abort(); // Aborta a transação, pois o layer essencial não existe.
                        return;
                    }

                    // 2. Configuração do Filtro de Seleção
                    // Cria um filtro para selecionar apenas BlockReferences (tipo DXF "INSERT")
                    // que estão no layer especificado. Isso otimiza a seleção em desenhos grandes.
                    TypedValue[] filter = new TypedValue[]
                    {
                        new TypedValue((int)DxfCode.Start, "INSERT"),       // Filtra por entidades do tipo BlockReference
                        new TypedValue((int)DxfCode.LayerName, targetLayerName) // Filtra por layer
                    };
                    SelectionFilter selFilter = new SelectionFilter(filter);

                    // 2. Execução da Seleção
                    PromptSelectionResult psr = docEditor.SelectAll(selFilter); // Seleciona todos os blocos do tipo

                    // Verifica se a seleção foi bem-sucedida e se há objetos selecionados.
                    if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                    {
                        docEditor.WriteMessage($"\nNenhum bloco '{targetBlockName}' encontrado no desenho.");
                        tr.Abort(); // Aborta a transação, pois não há nada para processar.
                        return;
                    }

                    docEditor.WriteMessage($"\nIniciando processamento de blocos '{targetBlockName}'...");

                    // 3. Itera sobre cada bloco selecionado
                    foreach (SelectedObject selObj in psr.Value)
                    {
                        ObjectId blockId = selObj.ObjectId;
                        BlockReference blockRef = (BlockReference)tr.GetObject(blockId, OpenMode.ForWrite); // Abrir para escrita para atualizar atributos
                        processedBlockIds.Add(blockId); // Adiciona o ID do bloco à lista de processados.
                        

                        docEditor.WriteMessage($"\nProcessando bloco ID: {blockRef.ObjectId}");

                        // Obtém o ponto de inserção do bloco
                        Point3d blockInsertionPoint = blockRef.Position;

                        // Obtém os valores dos atributos diretamente do bloco
                        string comprimentoStr = GetBlockAttributeValue(blockRef, "Comprimento", tr);
                        string estInicialStr = GetBlockAttributeValue(blockRef, "EstInicial", tr);
                        string estFinalStr = GetBlockAttributeValue(blockRef, "EstFinal", tr);
                        string eixoStr = GetBlockAttributeValue(blockRef, "Eixo", tr);
                        string areaStr = GetBlockAttributeValue(blockRef, "Área", tr); // Para uso em PSET C
                        string larguraStr = GetBlockAttributeValue(blockRef, "Largura", tr); // Para uso em PSET C
                        string corStr = GetBlockAttributeValue(blockRef, "Cor", tr);         // Para uso em PSET E

                        // Converte comprimento para double para cálculos, se necessário
                        double comprimento = 0.0;
                        if (!double.TryParse(comprimentoStr, out comprimento))
                        {
                            docEditor.WriteMessage($"\nAviso: O atributo 'Comprimento' do bloco {blockRef.ObjectId} não é um número válido ou está vazio. Usando 0.0.");
                            comprimento = 0.0;
                        }

                        // 4. Aplica e Preenche os Property Sets no Bloco
                        AplicarPsetsEmBlocoFaixa(
                            blockRef,
                            db,
                            tr,
                            docEditor,
                            estInicialStr,      // Passa a string lida do atributo
                            estFinalStr,        // Passa a string lida do atributo
                            areaStr,
                            eixoStr,            // Passa a string lida do atributo
                            blockInsertionPoint,
                            comprimento,        // Valor numérico do comprimento
                            larguraStr,         // Valor da largura
                            corStr              // Valor da cor
                        );
                    }

                    // 5. Commit da Transação
                    // Confirma todas as operações realizadas dentro da transação.
                    tr.Commit();
                    docEditor.WriteMessage($"\nProcessamento concluído. Total de blocos processados: {processedBlockIds.Count}");
                }
                catch (Exception ex)
                {
                    // Captura qualquer exceção durante o processo e exibe uma mensagem de erro
                    docEditor.WriteMessage($"\nErro durante a execução do comando 'ColetarDadosBlocosFaixasParaPsets': {ex.Message}");
                    docEditor.WriteMessage($"\nDetalhes do erro (StackTrace): {ex.StackTrace}");
                    tr.Abort(); // Em caso de erro, desfaz todas as operações para manter a integridade do desenho.
                }
            }
        }

        /// <summary>
        /// Helper para obter o valor de um atributo de um BlockReference.
        /// </summary>
        /// <param name="blockRef">O BlockReference.</param>
        /// <param name="tag">A tag do atributo.</param>
        /// <param name="tr">A transação ativa.</param>
        /// <returns>O valor do atributo como string, ou string.Empty se não encontrado.</returns>
        private static string GetBlockAttributeValue(BlockReference blockRef, string tag, Transaction tr)
        {
            foreach (ObjectId id in blockRef.AttributeCollection)
            {
                AttributeReference attRef = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
                if (attRef != null && attRef.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))
                {
                    return attRef.TextString;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Aplica e preenche os Property Sets para um BlockReference (faixa).
        /// </summary>
        /// <param name="blockRef">O BlockReference (faixa) para o qual os Psets serão aplicados.</param>
        /// <param name="db">O banco de dados do desenho.</param>
        /// <param name="tr">A transação ativa.</param>
        /// <param name="docEditor">O editor do documento para mensagens.</param>
        /// <param name="estacaInicialStr">Estaca inicial lida do atributo do bloco.</param>
        /// <param name="estacaFinalStr">Estaca final lida do atributo do bloco.</param>
        /// <param name="eixoStr">Nome do eixo/alinhamento lido do atributo do bloco.</param>
        /// <param name="insertionPoint">Ponto de inserção do bloco.</param>
        /// <param name="comprimento">Comprimento numérico do bloco.</param>
        /// <param name="larguraStr">Largura lida do atributo do bloco.</param>
        /// <param name="corStr">Cor lida do atributo do bloco.</param>
        private static void AplicarPsetsEmBlocoFaixa(
            BlockReference blockRef,
            Database db,
            Transaction tr,
            Editor docEditor,
            string estacaInicialStr,
            string estacaFinalStr,
            string areaStr,
            string eixoStr, 
            Point3d insertionPoint,
            double comprimento, // Já é double
            string larguraStr,
            string corStr)
        {
            try
            {
                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);

                // Nomes dos Property Sets. Utilize os nomes exatos conforme definidos em seu template.
                string propSetNameA = "A - Dados do Projeto";
                string propSetNameB = "B - Informações dos Objetos e Elementos";
                string propSetNameC = "C - Propriedades Fisicas dos Objetos e Elementos";
                string propSetNameD = "D - Propriedades Geográficas";
                string propSetNameE = "E - Requisitos Específicos de Projeto";

                // Obtem os IDs das definições de Property Set.
                // É crucial que essas definições já existam no desenho.
                ObjectId propSetIdA = dictionary.GetAt(propSetNameA);
                ObjectId propSetIdB = dictionary.GetAt(propSetNameB);
                ObjectId propSetIdC = dictionary.GetAt(propSetNameC);
                ObjectId propSetIdD = dictionary.GetAt(propSetNameD);
                ObjectId propSetIdE = dictionary.GetAt(propSetNameE);

                // Adiciona os Property Sets ao bloco. Se já estiverem adicionados, esta chamada não fará nada.
                PropertyDataServices.AddPropertySet(blockRef, propSetIdA);
                PropertyDataServices.AddPropertySet(blockRef, propSetIdB);
                PropertyDataServices.AddPropertySet(blockRef, propSetIdC);
                PropertyDataServices.AddPropertySet(blockRef, propSetIdD);
                PropertyDataServices.AddPropertySet(blockRef, propSetIdE);

                // --- Preenchimento dos Property Sets ---

                // PSET B: Informações dos Objetos e Elementos
                if (dictionary.Has(propSetNameB, tr))
                {
                    PropertySet psetB = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(blockRef, propSetIdB), OpenMode.ForWrite);

                    //int indexCodigoObjeto = psetB.PropertyNameToId("Código_do_Objeto");
                    int indexEstacaInicial = psetB.PropertyNameToId("EstaqueamentoInicial");
                    int indexEstacaFinal = psetB.PropertyNameToId("EstaqueamentoFinal");
                   
                    //int indexEixo = psetB.PropertyNameToId("Eixo"); // Assumindo que você tem um campo "Eixo" no PSET B

                    //psetB.SetAt(indexCodigoObjeto, blockRef.Name); // Ou outro identificador
                    psetB.SetAt(indexEstacaInicial, estacaInicialStr);
                    psetB.SetAt(indexEstacaFinal, estacaFinalStr);
                    
                   
                }

                // PSET C: Propriedades Físicas dos Objetos e Elementos
                if (dictionary.Has(propSetNameC, tr))
                {
                    PropertySet psetC = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(blockRef, propSetIdC), OpenMode.ForWrite);

                    int indexArea = psetC.PropertyNameToId("Área");
                    int indexLargura = psetC.PropertyNameToId("Largura");
                    int indexQuantidadeTotal = psetC.PropertyNameToId("Comprimento");
                    psetC.SetAt(indexQuantidadeTotal, comprimento.ToString("F3")); // Usa o comprimento numérico

                    double largura = 0.0;
                    if (!double.TryParse(larguraStr, out largura))
                    {
                        docEditor.WriteMessage($"\nAviso: Atributo 'Largura' do bloco {blockRef.ObjectId} não é um número válido ou está vazio. Usando 0.0 para cálculo de área.");
                        largura = 0.0; // Fallback para valor padrão se atributo não for numérico ou não existir
                    }

                    // Calcula área (comprimento do atributo * largura)
                    double areaCalculada = comprimento * largura;

                    psetC.SetAt(indexArea, areaStr);
                    psetC.SetAt(indexLargura, larguraStr); // Define a largura no Pset
                }

                // PSET D: Propriedades Geográficas
                if (dictionary.Has(propSetNameD, tr))
                {
                    PropertySet psetD = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(blockRef, propSetIdD), OpenMode.ForWrite);

                    int indexCoordX = psetD.PropertyNameToId("Coordenada_Eixo_X");
                    int indexCoordY = psetD.PropertyNameToId("Coordenada_Eixo_Y");
                    int indexCoordZ = psetD.PropertyNameToId("Coordenada_Eixo_Z");

                    // Usando as coordenadas de inserção do bloco
                    psetD.SetAt(indexCoordX, insertionPoint.X.ToString("F3"));
                    psetD.SetAt(indexCoordY, insertionPoint.Y.ToString("F3"));
                    psetD.SetAt(indexCoordZ, insertionPoint.Z.ToString("F3"));
                }

                // PSET E: Requisitos Específicos de Projeto (ajuste conforme a necessidade das suas faixas)
                if (dictionary.Has(propSetNameE, tr))
                {
                    PropertySet psetE = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(blockRef, propSetIdE), OpenMode.ForWrite);

                    int indexMaterial = psetE.PropertyNameToId("Material");

                    string materialFaixa = "Desconhecido";
                    if (!string.IsNullOrEmpty(corStr)) // Se o atributo Cor não estiver vazio
                    {
                        if (corStr.Equals("branca", StringComparison.OrdinalIgnoreCase)) materialFaixa = "Tinta Branca";
                        else if (corStr.Equals("amarela", StringComparison.OrdinalIgnoreCase)) materialFaixa = "Tinta Amarela";
                        else if (corStr.Equals("azul", StringComparison.OrdinalIgnoreCase)) materialFaixa = "Tinta Azul";
                        else materialFaixa = corStr; // Se for outra cor, usa o próprio valor do atributo
                    }

                    psetE.SetAt(indexMaterial, materialFaixa);
                }

                docEditor.WriteMessage($"\nProperty Sets aplicados e preenchidos para Bloco ID: {blockRef.ObjectId}");
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\nErro ao aplicar/preencher Property Sets para Bloco ID: {blockRef.ObjectId}: {ex.Message}");
                docEditor.WriteMessage($"\nDetalhes do erro (StackTrace): {ex.StackTrace}");
                // Não aborte a transação aqui, a transação principal fará isso se necessário.
            }
        }
    }
}