using System;
using System.Collections.Generic;
using System.Globalization;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    // Desfaz as aplicações de SOL_VAZAO_INCENDIO registradas no JSON ao lado do DWG:
    //  - Restaura o CTop anterior (em m³/s) em cada dispositivo
    //  - Reconecta as bacias que tinham sido desconectadas
    //  - Remove o JSON ao final (ou esvazia se houve falhas parciais)
    public class SolidosVazaoIncendioDesfazerSOL
    {
        public const string LongCommandName = "SOL_VAZAO_INCENDIO_DESFAZER";
        public const string ShortCommandName = "SVAZINC_DESFAZER";

        [CommandMethod(LongCommandName)]
        public void ExecuteLong() => Execute();

        [CommandMethod(ShortCommandName)]
        public void ExecuteShort() => Execute();

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            string estadoPath = SolidosVazaoIncendioEstado.ResolverCaminhoEstado(doc.Name);
            if (estadoPath == null)
            {
                ed.WriteMessage("\n[SOLIDOS] DWG não está salvo em disco — não há histórico para desfazer.\n");
                return;
            }

            SolidosVazaoIncendioEstado estado = SolidosVazaoIncendioEstado.Carregar(estadoPath);
            if (estado.Operacoes == null || estado.Operacoes.Count == 0)
            {
                ed.WriteMessage($"\n[SOLIDOS] Nenhuma operação registrada em: {estadoPath}\n");
                return;
            }

            // Resumo + confirmação.
            ed.WriteMessage(
                $"\n[SOLIDOS] Desfazer Vazão de Combate a Incêndio" +
                $"\n  Arquivo: {estadoPath}" +
                $"\n  Operações registradas: {estado.Operacoes.Count}");

            foreach (var op in estado.Operacoes)
            {
                double aplicadoLs = op.CTopAplicadoM3s * SolidosVazaoCombateIncendioSOL.LitersPerCubicMeter;
                double anteriorLs = op.CTopAnteriorM3s * SolidosVazaoCombateIncendioSOL.LitersPerCubicMeter;
                ed.WriteMessage(
                    $"\n    - {op.DeviceName ?? "(?)"} (h={op.DeviceHandle}): " +
                    $"CTop {aplicadoLs.ToString("N4", SolidosVazaoCombateIncendioSOL.PtBrCulture)} L/s " +
                    $"-> {anteriorLs.ToString("N4", SolidosVazaoCombateIncendioSOL.PtBrCulture)} L/s; " +
                    $"reconectar {op.BaciasDesconectadas?.Count ?? 0} bacia(s)");
            }

            PromptKeywordOptions pko = new PromptKeywordOptions(
                "\n  Reverter todas? [Sim/Não] <Sim>: ");
            pko.Keywords.Add("Sim");
            pko.Keywords.Add("Não");
            pko.Keywords.Default = "Sim";
            pko.AllowNone = true;
            PromptResult pr = ed.GetKeywords(pko);
            string ans = (pr.Status == PromptStatus.OK) ? pr.StringResult : "Sim";
            if (!string.Equals(ans, "Sim", StringComparison.OrdinalIgnoreCase))
            {
                ed.WriteMessage("\n  Cancelado.\n");
                return;
            }

            int devicesRevertidos = 0;
            int devicesComFalha = 0;
            int bacias_reconectadas = 0;
            int bacias_falha = 0;
            List<string> handlesRevertidos = new List<string>();

            foreach (var op in estado.Operacoes)
            {
                bool ok = ReverterUma(ed, db, op, ref bacias_reconectadas, ref bacias_falha);
                if (ok)
                {
                    devicesRevertidos++;
                    handlesRevertidos.Add(op.DeviceHandle);
                }
                else
                {
                    devicesComFalha++;
                }
            }

            // Remove do estado os que foram revertidos com sucesso.
            foreach (string h in handlesRevertidos)
            {
                estado.RemoverPorHandle(h);
            }

            // Salva (ou apaga) o estado.
            try
            {
                if (estado.Operacoes.Count == 0)
                {
                    System.IO.File.Delete(estadoPath);
                }
                else
                {
                    estado.Salvar(estadoPath);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[AVISO] Falha ao atualizar arquivo de histórico: {ex.Message}");
            }

            ed.WriteMessage(
                "\n[SOLIDOS] Resumo do desfazer:" +
                $"\n  Dispositivos revertidos: {devicesRevertidos}" +
                $"\n  Dispositivos com falha: {devicesComFalha}" +
                $"\n  Bacias reconectadas: {bacias_reconectadas}" +
                $"\n  Bacias com falha de reconexão: {bacias_falha}");
            if (estado.Operacoes.Count > 0)
            {
                ed.WriteMessage(
                    $"\n  Operações restantes no histórico: {estado.Operacoes.Count}" +
                    "\n  (provavelmente porque o dispositivo/bacia foi apagado do DWG)");
            }
            ed.WriteMessage("\n");

            // Pergunta rebuild.
            if (devicesRevertidos > 0)
            {
                PromptKeywordOptions pkoReb = new PromptKeywordOptions(
                    $"\nRodar {SolidosVazaoCombateIncendioSOL.RebuildCommand} agora para recalcular a rede? [Sim/Não] <Sim>: ");
                pkoReb.Keywords.Add("Sim");
                pkoReb.Keywords.Add("Não");
                pkoReb.Keywords.Default = "Sim";
                pkoReb.AllowNone = true;
                PromptResult prReb = ed.GetKeywords(pkoReb);
                string ansReb = (prReb.Status == PromptStatus.OK) ? prReb.StringResult : "Sim";
                if (string.Equals(ansReb, "Sim", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        doc.SendStringToExecute(SolidosVazaoCombateIncendioSOL.RebuildCommand + " ", true, false, false);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[SOLIDOS] Falha ao disparar rebuild: {ex.Message}\n");
                    }
                }
            }
        }

        // Reverte uma operação: restaura CTop anterior + reconecta bacias.
        // Retorna true se TUDO funcionou; false se houve alguma falha (e nesse caso
        // o registro permanece no histórico para tentativa futura ou inspeção manual).
        private static bool ReverterUma(
            Editor ed, Database db,
            SolidosVazaoIncendioEstado.OperacaoRegistrada op,
            ref int baciasOk, ref int baciasFalha)
        {
            if (!SolidosVazaoIncendioEstado.TryHandleToObjectId(db, op.DeviceHandle, out ObjectId deviceId))
            {
                ed.WriteMessage(
                    $"\n  [{op.DeviceName ?? op.DeviceHandle}] dispositivo não encontrado no DWG (handle {op.DeviceHandle}); pulado.");
                return false;
            }

            // Restaura CTop.
            bool ctopOk = SolidosVazaoCombateIncendioSOL.TrySetDoubleParam(
                deviceId, "HCalcIni.CTop", op.CTopAnteriorM3s, out string err);
            if (!ctopOk)
            {
                ed.WriteMessage(
                    $"\n  [{op.DeviceName ?? op.DeviceHandle}] falha ao restaurar CTop: {err}");
                return false;
            }

            // Reconecta bacias.
            bool todasOk = true;
            if (op.BaciasDesconectadas != null)
            {
                foreach (string baciaHandle in op.BaciasDesconectadas)
                {
                    if (!SolidosVazaoIncendioEstado.TryHandleToObjectId(db, baciaHandle, out ObjectId baciaId))
                    {
                        ed.WriteMessage(
                            $"\n  [{op.DeviceName ?? op.DeviceHandle}] bacia handle {baciaHandle} não existe mais no DWG; ignorada.");
                        baciasFalha++;
                        todasOk = false;
                        continue;
                    }

                    try
                    {
                        // Mesma semântica do disconnect: bacia é upstream, dispositivo é downstream.
                        string ret = SolidosAPI.ConnectNodes(baciaId, deviceId);
                        if (string.IsNullOrEmpty(ret) ||
                            ret.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            baciasOk++;
                        }
                        else
                        {
                            ed.WriteMessage(
                                $"\n  [{op.DeviceName ?? op.DeviceHandle}] ConnectNodes retornou: {ret}");
                            baciasFalha++;
                            todasOk = false;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Exception root = ex;
                        while (root.InnerException != null) root = root.InnerException;
                        ed.WriteMessage(
                            $"\n  [{op.DeviceName ?? op.DeviceHandle}] falha ao reconectar bacia {baciaHandle}: {root.Message}");
                        baciasFalha++;
                        todasOk = false;
                    }
                }
            }

            double anteriorLs = op.CTopAnteriorM3s * SolidosVazaoCombateIncendioSOL.LitersPerCubicMeter;
            ed.WriteMessage(
                $"\n  ✓ {op.DeviceName ?? op.DeviceHandle}: CTop restaurado para " +
                $"{anteriorLs.ToString("N4", SolidosVazaoCombateIncendioSOL.PtBrCulture)} L/s");

            return todasOk;
        }
    }
}
