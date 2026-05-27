using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

using AutomacoesCivil3D;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    public class CorridorBoundaryBatch
    {
        // Fila estática para controlar o processamento em lote
        private static Queue<ObjectId> _corridorQueue = new Queue<ObjectId>();
        private static bool _isRunning = false;

        [CommandMethod("AC3D_ExtractBoundaryFromCorridors_ALL")]
        public static void AC3D_ExtractBoundaryFromCorridors_ALL()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;

            if (_isRunning)
            {
                docEditor.WriteMessage("\nJá existe um processamento em andamento. Use AC3D_ExtractBoundaryFromCorridors_CANCEL para cancelar.");
                return;
            }

            try
            {
                // Monta a fila com todos os corredores do desenho
                _corridorQueue.Clear();

                // Observação: Corridors é uma coleção de ObjectIds dos corredores
                foreach (ObjectId corridorId in civilDb.CorridorCollection)
                {
                    _corridorQueue.Enqueue(corridorId);
                }

                if (_corridorQueue.Count == 0)
                {
                    docEditor.WriteMessage("\nNenhum corredor encontrado no desenho.");
                    return;
                }

                // Assina eventos e inicia o primeiro
                civilDoc.CommandEnded += OnCommandEnded;
                civilDoc.CommandCancelled += OnCommandCancelled;
                civilDoc.CommandFailed += OnCommandFailed;

                _isRunning = true;
                docEditor.WriteMessage($"\nIniciando extração de boundaries em lote. Corredores na fila: {_corridorQueue.Count}");
                StartNext(civilDoc, docEditor);
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage("\nFalha ao iniciar processamento: " + ex.Message);
                Cleanup(civilDoc, docEditor);
            }
        }

        [CommandMethod("AC3D_ExtractBoundaryFromCorridors_CANCEL")]
        public static void AC3D_ExtractBoundaryFromCorridors_CANCEL()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Cleanup(civilDoc, docEditor);
            docEditor.WriteMessage("\nProcessamento cancelado pelo usuário.");
        }

        // Inicia o processamento do próximo corredor da fila
        private static void StartNext(Document civilDoc, Editor docEditor)
        {
            if (_corridorQueue.Count == 0)
            {
                docEditor.WriteMessage("\nProcessamento concluído para todos os corredores.");
                Cleanup(civilDoc, docEditor);
                return;
            }

            ObjectId corridorId = _corridorQueue.Peek(); // não remove ainda (só remove quando o comando termina com sucesso)

            try
            {
                // Tenta pré-selecionar o corredor
                ObjectId[] pick = new ObjectId[] { corridorId };
                docEditor.SetImpliedSelection(pick);

                // Dispara o comando nativo (2026)
                // Dica: o underscore força o nome em inglês
                // Observação: as opções do comando serão pedidas no prompt (você confirma/enter nas defaults)
                Application.DocumentManager.MdiActiveDocument.SendStringToExecute("_AeccCreateBoundaryFromCorridor ", true, false, false);

                // A continuação ocorre nos handlers de eventos (CommandEnded/Failed/Cancelled)
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage("\nFalha ao enviar comando para o corredor atual: " + ex.Message);
                // Pula este corredor e tenta o próximo
                _corridorQueue.Dequeue();
                StartNext(civilDoc, docEditor);
            }
        }

        // Evento chamado quando um comando termina
        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            try
            {
                // O GlobalCommandName vem em caps; conferimos se é o nosso comando alvo
                // Em geral, para comandos Aecc, o GlobalCommandName aparece sem underscore.
                if (string.Equals(e.GlobalCommandName, "AECCCREATEBOUNDARYFROMCORRIDOR", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove o corredor atual (processado) e vai para o próximo
                    if (_corridorQueue.Count > 0)
                    {
                        _corridorQueue.Dequeue();
                    }

                    // Limpa pré-seleção para evitar efeitos colaterais
                    docEditor.SetImpliedSelection(new ObjectId[0]);

                    // Inicia o próximo
                    StartNext(civilDoc, docEditor);
                }
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage("\nErro no pós-comando: " + ex.Message);
                Cleanup(civilDoc, docEditor);
            }
        }

        private static void OnCommandCancelled(object sender, CommandEventArgs e)
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            if (string.Equals(e.GlobalCommandName, "AECCCREATEBOUNDARYFROMCORRIDOR", StringComparison.OrdinalIgnoreCase))
            {
                docEditor.WriteMessage("\nComando cancelado pelo usuário. Encerrando processamento em lote.");
                Cleanup(civilDoc, docEditor);
            }
        }

        private static void OnCommandFailed(object sender, CommandEventArgs e)
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            if (string.Equals(e.GlobalCommandName, "AECCCREATEBOUNDARYFROMCORRIDOR", StringComparison.OrdinalIgnoreCase))
            {
                docEditor.WriteMessage("\nO comando falhou para este corredor. Tentando o próximo...");
                // Remove o corredor atual e avança
                if (_corridorQueue.Count > 0)
                {
                    _corridorQueue.Dequeue();
                }
                // Limpa pré-seleção
                docEditor.SetImpliedSelection(new ObjectId[0]);
                // Inicia o próximo
                StartNext(civilDoc, docEditor);
            }
        }

        private static void Cleanup(Document civilDoc, Editor docEditor)
        {
            try
            {
                civilDoc.CommandEnded -= OnCommandEnded;
                civilDoc.CommandCancelled -= OnCommandCancelled;
                civilDoc.CommandFailed -= OnCommandFailed;
            }
            catch { /* ignore */ }

            _corridorQueue.Clear();
            _isRunning = false;

            try
            {
                // Garante que nenhuma seleção fique pendurada
                docEditor.SetImpliedSelection(new ObjectId[0]);
            }
            catch { /* ignore */ }
        }
    }
}

