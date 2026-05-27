using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D

{
    public static class LayerCreator
    {
        [CommandMethod("CREATE_AWP_LAYERS")]
        public static void CreateAwPLayers()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;
            

            string[] layerNames = new string[]
            {
                "AWP - ARRASAMENTO DAS ESTACAS",
                "AWP - ARRASAMENTO DAS ESTACAS DO TABULEIRO 1",
                "AWP - ARRASAMENTO DAS ESTACAS DO TABULEIRO 2",
                "AWP - ARRASAMENTO DAS ESTACAS DO TABULEIRO 3",
                "AWP - BARREIRAS NEW JERSEY",
                "AWP - CONSTRUCAO DO MURO - TRECHO 1 E 2",
                "AWP - CONSTRUCAO DO MURO - TRECHO 3 (PI - PN PROVISÓRIA)",
                "AWP - CONTENCAO EM TERRAMESH",
                "AWP - CONTENCAO PI EM SOLO GRAMPEADO",
                "AWP - CONTENCAO TEMPORARIA EM TRILHO METALICO DA LINHA 1",
                "AWP - CONTENCAO TEMPORARIA EM TRILHO METALICO DA LINHA 4",
                "AWP - DEMOLICAO GERAL (CIVIL)",
                "AWP - DESLIGAMENTO E REMOCAO DA PN AUTOMATICA",
                "AWP - DRENAGEM",
                "AWP - DRENAGEM PROFUNDA",
                "AWP - DRENAGENS",
                "AWP - EDIFICACOES (CONTAINER E BANHEIROS)",
                "AWP - EXECUCAO DA PLATAFORMA 1 (GUINDASTE)",
                "AWP - EXECUCAO DA PLATAFORMA 2 (GUINDASTE E PATIO DE PRE MOLDADOS)",
                "AWP - EXECUCAO DA PN NA LINHA 4",
                "AWP - EXECUCAO DAS ESTACAS RAIZ DO TABULEIRO 1",
                "AWP - EXECUCAO DAS ESTACAS RAIZ DO TABULEIRO 2",
                "AWP - EXECUCAO DAS ESTACAS RAIZ DO TABULEIRO 3",
                "AWP - EXECUCAO DAS ESTACAS RAIZ DO TABULEIRO 4",
                "AWP - EXECUCAO DO TABULEIRO 3",
                "AWP - INFRA CIVIL (PAVIMENTACAO E BASE DE CONCRETO)",
                "AWP - INFRA TELECOM (CABOS DE CONTROLE E FIBRA ÓTICA)",
                "AWP - INSTALACAO DAS CANCELAS SEMIAUTOMÁTICAS",
                "AWP - INSTALACAO DO TABULEIRO 1",
                "AWP - INSTALACAO DO TABULEIRO 2",
                "AWP - LANCAMENTO DE CABOS E LIGACAO (ILUMINACAO DEFINITIVA)",
                "AWP - LIGACAO DEFINITIVA DOS CABOS DE CONTROLE E FIBRA ÓTICA",
                "AWP - LIGACAO GERAL DO SISTEMA (CFTV E CANCELAS)",
                "AWP - LIMPEZA E REGULARIZACAO DO TERRENO",
                "AWP - OBRAS CIVIS DA PI",
                "AWP - PAISAGISMO",
                "AWP - PAVIMENTACAO",
                "AWP - PAVIMENTACAO (PRIMÁRIO)",
                "AWP - PAVIMENTACAO E DRENAGEM SUPERFICIAL",
                "AWP - PROLONGAMENTO DO BUEIRO",
                "AWP - REALOCACAO DE INFRA ENTERRADA (CABOS DE CONTROLE E FIBRA ÓTICA)",
                "AWP - REALOCACAO LTR - ILUMINACAO DO PÁTIO - CFTV",
                "AWP - REBAIXAMENTO DO TERRENO",
                "AWP - REBAIXAMENTO DO TERRENO DO TABULEIRO 1",
                "AWP - REBAIXAMENTO DO TERRENO DO TABULEIRO 2",
                "AWP - REBAIXAMENTO DO TERRENO DO TABULEIRO 3",
                "AWP - RECONSTITUICAO DA LINHA 1",
                "AWP - RECONSTITUICAO DA LINHA 2",
                "AWP - RECONSTITUICAO DA LINHA 3",
                "AWP - RETIRADA DA PN PROVISÓRIA",
                "AWP - SERVICOS COMPLEMENTARES (ACABAMENTO E SINALIZACAO)",
                "AWP - SUPERESTRUTURA CIVIL DA QUARTA LINHA",
                "AWP - SUPERESTRUTURA CIVIL DO TABULEIRO 1",
                "AWP - SUPERESTRUTURA CIVIL DO TABULEIRO 2",
                "AWP - SUPERESTRUTURA CIVIL DO TABULEIRO 3",
                "AWP - SUPERESTRUTURA FERROVIÁRIA DA QUARTA LINHA",
                "AWP - SUPRESSAO VEGETAL E LIMPEZA",
                "AWP - SUPRESSAO VEGETAL - LIMPEZA E REGULARIZACAO DO TERRENO",
                "AWP - TABULEIRO 4",
                "AWP - TERRAPLENAGEM",
                "AWP - TERRAPLENAGEM DA PI",
                "AWP - TERRAPLENAGEM DO ACESSO"
            };
            string[] layerNamesFiltrados = new string[layerNames.Length];

            int createdCount = 0;
            int skippedCount = 0;
            List<string> adjustedNames = new List<string>();

            try
            {
                using (Transaction trans = db.TransactionManager.StartTransaction())
                {
                    LayerTable layerTable = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForRead);


                    foreach (string layerName in layerNames)
                    {// Verifica se a layer já existe
                        if (layerTable.Has(layerName))
                        {
                            // Se a layer já existir, exibe uma mensagem de erro e encerra a execução do comando
                            docEditor.WriteMessage($"\nLayer '{layerName}' já existe.");
                            
                        }
                        else
                        {

                            adjustedNames.Add(layerName);


                        }
                    }
                    



                    foreach (string original in adjustedNames)
                    {
                        
                        

                        layerTable.UpgradeOpen();

                        LayerTableRecord ltr = new LayerTableRecord();

                        

                        ltr.Name = original;

                        ObjectId layerId = layerTable.Add(ltr);
                        trans.AddNewlyCreatedDBObject(ltr, true);
                        // Define propriedades da layer
                        ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 3); // Cor Vermelha (índice 1 na paleta ACI)
                        ltr.LineWeight = LineWeight.ByLineWeightDefault; // Peso da linha padrão
                        ltr.LinetypeObjectId = db.ContinuousLinetype; // Tipo de linha contínua (padrão)
                        ltr.IsPlottable = true; // Define se a layer é plotável


                        createdCount++;
                        layerTable.DowngradeOpen();
                        docEditor.WriteMessage($"\nLayer criado: {ltr.Name}.");

                    }

                    trans.Commit();
                }

                docEditor.WriteMessage($"\nLayers criados: {createdCount}. Layers já existentes: {skippedCount}.");

                if (adjustedNames.Count > 0)
                {
                    docEditor.WriteMessage("\nAlguns nomes continham caracteres inválidos e foram ajustados:");
                    foreach (string info in adjustedNames)
                    {
                        docEditor.WriteMessage("\n - " + info);
                    }
                }
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage("\nErro (AutoCAD.Runtime): " + ex.Message);
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage("\nErro (System): " + ex.Message);
            }
        }

        // Substitui caracteres inválidos de nomes de layer por '-'
        // AutoCAD não permite: <>/":;?*|=
        private static string SanitizeLayerName(string name)
        {
            string sanitized = name;

            char[] invalid = new char[] {'/',};
            for (int i = 0; i < invalid.Length; i++)
            {
                if (sanitized.IndexOf(invalid[i]) >= 0)
                {
                    sanitized = sanitized.Replace(invalid[i], '-');
                }
            }

            // Trim de espaços excessivos nas pontas
            sanitized = sanitized.Trim();
            return sanitized;
        }
    }
}