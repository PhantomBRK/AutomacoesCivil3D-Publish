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
    public class SolidosVazaoCombateIncendioSOL
    {
        public const string LongCommandName = "SOL_VAZAO_INCENDIO";
        public const string ShortCommandName = "SVAZINC";

        public const string ParamContributionArea = "ContributionArea";
        public const string ParamCTop = "CTop";
        public const string ParamName = "Name";
        public const string ParamCatchments = "Catchments";

        // Nomes candidatos para gravar a Contribuição Pontual (CTop está aninhado em HCalcIni).
        // Tentamos do mais simples ao mais específico até um aceitar.
        public static readonly string[] CTopWriteCandidates = new[]
        {
            "CTop",
            "HCalcFin.CTop",
            "Inicio.CTop",
            "HidraulicaInicio.CTop",
            "Hidraulica.CTop",
            "Calculation.CTop",
            "PointHydraulic.CTop",
        };

        public const string RebuildCommand = "SFORCEREBUILD";

        // Q [L/s] = Area [m²] × Coef [L/(min·m²)] / 60.
        // Coef tipicamente expresso em densidade de aplicação L/(min·m²) (sprinkler/hidrante);
        // a divisão por 60 converte para L/s.
        // CTop é armazenado pelo SOLIDOS em m³/s (painel converte ×1000 para mostrar L/s),
        // então dividimos o Q calculado por LitersPerCubicMeter antes de gravar.
        public static readonly double M2PerHa = 10000.0;
        public static readonly double SecondsPerMinute = 60.0;
        public static readonly double LitersPerCubicMeter = 1000.0;

        public static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");

        public static double _lastCoef = 0.0;

        [CommandMethod(LongCommandName)]
        public void ExecuteLong() => Execute();

        [CommandMethod(ShortCommandName)]
        public void ExecuteShort() => Execute();

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;

            // Estado persistente do projeto: arquivo JSON ao lado do DWG.
            // Sem DWG salvo (Drawing1 sem nome ainda), o estado é desabilitado e o undo não funcionará.
            string estadoPath = SolidosVazaoIncendioEstado.ResolverCaminhoEstado(doc.Name);
            SolidosVazaoIncendioEstado estado = null;
            if (estadoPath != null)
            {
                estado = SolidosVazaoIncendioEstado.Carregar(estadoPath);
                estado.DwgPath = doc.Name;
            }
            else
            {
                ed.WriteMessage(
                    "\n[AVISO] DWG não está salvo em disco — o histórico para 'undo' não será registrado." +
                    "\n        Salve o DWG antes de aplicar se quiser poder reverter depois.\n");
            }

            ed.WriteMessage(
                "\n[SOLIDOS] Vazão de Combate a Incêndio" +
                "\n  Q [L/s] = Area [m²] × Coef [L/(min·m²)] / 60" +
                "\n  Selecione dispositivos pontuais (PV / hidrante) um a um." +
                "\n  Enter para encerrar.\n");

            int aplicados = 0;
            int ignorados = 0;
            int cancelados = 0;

            while (true)
            {
                PromptEntityOptions peo = new PromptEntityOptions(
                    $"\n[{aplicados} OK] Selecione dispositivo SOLIDOS, ou Enter para terminar: ")
                {
                    AllowNone = true
                };
                peo.SetRejectMessage("\nSelecione um dispositivo do SOLIDOS.");
                peo.AddAllowedClass(typeof(Entity), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.None) break;
                if (per.Status == PromptStatus.Cancel) break;
                if (per.Status != PromptStatus.OK) { ignorados++; continue; }

                ObjectId id = per.ObjectId;
                ProcessResult r = ProcessOne(ed, id, estado);
                switch (r)
                {
                    case ProcessResult.Applied: aplicados++; break;
                    case ProcessResult.Skipped: ignorados++; break;
                    case ProcessResult.Canceled: cancelados++; break;
                }
            }

            // Persiste o estado se houve alterações.
            if (estado != null && aplicados > 0 && estadoPath != null)
            {
                try
                {
                    estado.Salvar(estadoPath);
                    ed.WriteMessage($"\n[SOLIDOS] Histórico salvo em: {estadoPath}");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage(
                        $"\n[AVISO] Não foi possível salvar histórico ({ex.Message})." +
                        "\n        O undo pode não funcionar para esta rodada.");
                }
            }

            ed.WriteMessage(
                "\n[SOLIDOS] Resumo:" +
                $"\n  Aplicados: {aplicados}" +
                $"\n  Ignorados: {ignorados}" +
                $"\n  Cancelados pelo usuário: {cancelados}\n");

            if (aplicados > 0)
            {
                PromptKeywordOptions pko = new PromptKeywordOptions(
                    $"\nRodar {RebuildCommand} agora para recalcular a rede? [Sim/Não] <Sim>: ");
                pko.Keywords.Add("Sim");
                pko.Keywords.Add("Não");
                pko.Keywords.Default = "Sim";
                pko.AllowNone = true;

                PromptResult pr = ed.GetKeywords(pko);
                string ans = (pr.Status == PromptStatus.OK) ? pr.StringResult : "Sim";
                if (string.Equals(ans, "Sim", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        doc.SendStringToExecute(RebuildCommand + " ", true, false, false);
                        ed.WriteMessage($"\n[SOLIDOS] {RebuildCommand} disparado.\n");
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[SOLIDOS] Falha ao disparar {RebuildCommand}: {ex.Message}\n");
                    }
                }
            }
        }

        public enum ProcessResult { Applied, Skipped, Canceled }

        public static ProcessResult ProcessOne(Editor ed, ObjectId id)
        {
            return ProcessOne(ed, id, null);
        }

        public static ProcessResult ProcessOne(Editor ed, ObjectId id, SolidosVazaoIncendioEstado estado)
        {
            // Nome do dispositivo (puramente informativo).
            string nome = TryGetParam<string>(id, ParamName) ?? "(sem nome)";

            // Área de contribuição já agregada pelo SOLIDOS (somatório das bacias conectadas).
            // Unidade: m² do desenho.
            object rawArea = TryGetRawParam(id, ParamContributionArea);
            if (rawArea == null)
            {
                ed.WriteMessage($"\n  [{nome}] Não é dispositivo SOLIDOS ou não tem a propriedade '{ParamContributionArea}'. Ignorado.\n");
                return ProcessResult.Skipped;
            }

            double area = ConvertToDouble(rawArea);

            if (area <= 0.0)
            {
                ed.WriteMessage(
                    $"\n  [{nome}] ContributionArea = 0 (sem bacia conectada)." +
                    "\n  Você pode informar a área manualmente.");

                PromptDoubleOptions pdoArea = new PromptDoubleOptions("\n  Área manual [m²] (Enter cancela): ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    AllowNone = true
                };
                PromptDoubleResult pdrArea = ed.GetDouble(pdoArea);
                if (pdrArea.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n  Cancelado.\n");
                    return ProcessResult.Canceled;
                }
                area = pdrArea.Value;
            }

            double areaHa = area / M2PerHa;
            ed.WriteMessage(
                $"\n  [{nome}] Área: " +
                $"{area.ToString("N3", PtBrCulture)} m²  " +
                $"({areaHa.ToString("N6", PtBrCulture)} ha)");

            // Coeficiente: pede a cada dispositivo, default = último usado.
            double coef;
            while (true)
            {
                string defStr = _lastCoef.ToString("R", CultureInfo.InvariantCulture);
                PromptDoubleOptions pdoCoef = new PromptDoubleOptions(
                    $"\n  Coeficiente [L/(min·m²)] <{defStr}>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    AllowNone = true,
                    DefaultValue = _lastCoef > 0 ? _lastCoef : 0.0
                };
                if (_lastCoef <= 0)
                {
                    // Sem default válido — força digitar.
                    pdoCoef.AllowNone = false;
                }

                PromptDoubleResult pdrCoef = ed.GetDouble(pdoCoef);
                if (pdrCoef.Status == PromptStatus.None && _lastCoef > 0)
                {
                    coef = _lastCoef;
                }
                else if (pdrCoef.Status == PromptStatus.OK)
                {
                    coef = pdrCoef.Value;
                }
                else
                {
                    ed.WriteMessage("\n  Cancelado.\n");
                    return ProcessResult.Canceled;
                }

                if (coef > 0) break;
                ed.WriteMessage("\n  Coeficiente precisa ser > 0.");
            }

            double qCalculado = area * coef / 60;
            ed.WriteMessage(
                $"\n  Q = {area.ToString("N3", PtBrCulture)} m² × {coef.ToString("R", CultureInfo.InvariantCulture)} L/(min·m²) / 60" +
                $" = {qCalculado.ToString("N4", PtBrCulture)} L/s");

            // CTop atual — leitura via HCalcIni.CTop (caminho confirmado em teste).
            // Valor retornado pelo SOLIDOS está em m³/s; convertemos para L/s pra comparar com Q.
            object rawCTop = TryGetRawParam(id, "HCalcIni.CTop");
            double cTopAtual_m3s = ConvertToDouble(rawCTop);
            double cTopAtual = cTopAtual_m3s * LitersPerCubicMeter;

            double cTopNovo;
            if (cTopAtual > 0)
            {
                ed.WriteMessage($"\n  CTop atual = {cTopAtual.ToString("N4", PtBrCulture)} L/s");
                PromptKeywordOptions pko = new PromptKeywordOptions(
                    "\n  [Substituir/Somar/Manter/Cancelar] <Substituir>: ");
                pko.Keywords.Add("Substituir");
                pko.Keywords.Add("Somar");
                pko.Keywords.Add("Manter");
                pko.Keywords.Add("Cancelar");
                pko.Keywords.Default = "Substituir";
                pko.AllowNone = true;

                PromptResult pr = ed.GetKeywords(pko);
                string ans = (pr.Status == PromptStatus.OK) ? pr.StringResult : "Substituir";

                switch (ans)
                {
                    case "Substituir": cTopNovo = qCalculado; break;
                    case "Somar":      cTopNovo = cTopAtual + qCalculado; break;
                    case "Manter":
                        _lastCoef = coef;
                        ed.WriteMessage("\n  Mantido o CTop atual.\n");
                        return ProcessResult.Skipped;
                    case "Cancelar":
                    default:
                        ed.WriteMessage("\n  Cancelado.\n");
                        return ProcessResult.Canceled;
                }
            }
            else
            {
                cTopNovo = qCalculado;
            }

            // cTopNovo está em L/s (unidade que o usuário pensa).
            // SOLIDOS armazena em m³/s, então convertemos antes de gravar.
            double cTopNovo_m3s = cTopNovo / LitersPerCubicMeter;

            if (!TrySetDoubleParam(id, "HCalcIni.CTop", cTopNovo_m3s, out string setError))
            {
                ed.WriteMessage(
                    $"\n  [{nome}] Falha ao gravar HCalcIni.CTop: {setError}\n");
                DumpDeviceProperties(ed, id);
                return ProcessResult.Skipped;
            }
            string usedName = "HCalcIni.CTop";

            // Captura a lista de bacias ANTES de desconectar — necessário para o undo
            // poder reconectar tudo na ordem inversa.
            List<ObjectId> baciasAntes = ListarCatchments(id);

            // Desconecta todas as bacias do dispositivo: a Contribuição Pontual
            // substitui a contribuição baseada em área, então não faz sentido manter
            // as bacias ligadas (elas continuariam somando vazão).
            int desconectadas = DisconnectAllCatchments(ed, id, nome);

            // Registra no estado persistente (se passado) — chamado APÓS sucesso
            // de gravação e desconexão para garantir que o JSON reflete realidade.
            if (estado != null)
            {
                SolidosVazaoIncendioEstado.OperacaoRegistrada op = new SolidosVazaoIncendioEstado.OperacaoRegistrada
                {
                    DeviceHandle = SolidosVazaoIncendioEstado.ObjectIdToHandle(id),
                    DeviceName = nome,
                    CTopAnteriorM3s = cTopAtual_m3s,
                    CTopAplicadoM3s = cTopNovo_m3s,
                    CoefLMinM2 = coef,
                    AreaUsadaM2 = area,
                    BaciasDesconectadas = new List<string>(
                        baciasAntes.ConvertAll(SolidosVazaoIncendioEstado.ObjectIdToHandle))
                };
                estado.RegistrarOuAtualizar(op);
            }

            _lastCoef = coef;
            ed.WriteMessage(
                $"\n  ✓ {nome}: {usedName} = {cTopNovo.ToString("N4", PtBrCulture)} L/s" +
                $" (gravado como {cTopNovo_m3s.ToString("G6", PtBrCulture)} m³/s)");
            if (desconectadas > 0)
            {
                ed.WriteMessage($"\n    Bacias desconectadas: {desconectadas}");
            }
            ed.WriteMessage("\n");
            return ProcessResult.Applied;
        }

        // Lista as bacias conectadas a um dispositivo (Catchments → List<ObjectId>).
        public static List<ObjectId> ListarCatchments(ObjectId deviceId)
        {
            List<ObjectId> bacias = new List<ObjectId>();
            object raw = TryGetRawParam(deviceId, ParamCatchments);
            if (raw is System.Collections.IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item is ObjectId oid && !oid.IsNull) bacias.Add(oid);
                }
            }
            return bacias;
        }

        // Le a lista Catchments do dispositivo e desconecta cada bacia.
        // Retorna a quantidade efetivamente desconectada.
        public static int DisconnectAllCatchments(Editor ed, ObjectId deviceId, string deviceName)
        {
            object raw = TryGetRawParam(deviceId, ParamCatchments);
            if (raw == null) return 0;

            // Catchments e List<ObjectId>, mas tratamos como IEnumerable pra ser seguro.
            List<ObjectId> bacias = new List<ObjectId>();
            if (raw is System.Collections.IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item is ObjectId oid && !oid.IsNull) bacias.Add(oid);
                }
            }

            if (bacias.Count == 0) return 0;

            int ok = 0;
            foreach (ObjectId baciaId in bacias)
            {
                try
                {
                    // Bacia e upstream do dispositivo (descarrega NELE).
                    string ret = SolidosAPI.DisConnectNodes(baciaId, deviceId);
                    if (string.IsNullOrEmpty(ret) ||
                        ret.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ok++;
                    }
                    else
                    {
                        ed.WriteMessage(
                            $"\n    [Aviso] DisConnect bacia handle {baciaId.Handle} retornou: {ret}");
                    }
                }
                catch (System.Exception ex)
                {
                    System.Exception root = ex;
                    while (root.InnerException != null) root = root.InnerException;
                    ed.WriteMessage(
                        $"\n    [Erro] Falha ao desconectar bacia handle {baciaId.Handle}: {root.Message}");
                }
            }
            return ok;
        }

        // Diagnostico: lista propriedades do dispositivo e marca quais sao graváveis.
        public static void DumpDeviceProperties(Editor ed, ObjectId id)
        {
            try
            {
                List<string> props = SolidosAPI.ListProperties(id);
                if (props == null || props.Count == 0)
                {
                    ed.WriteMessage("\n  (ListProperties retornou vazio)\n");
                    return;
                }

                ed.WriteMessage($"\n  Propriedades do dispositivo ({props.Count}):");
                foreach (string p in props)
                {
                    bool isRO = TryIsReadOnly(id, p);
                    string flag = isRO ? "[RO]" : "[RW]";
                    ed.WriteMessage($"\n    {flag} {p}");
                }
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  (Falha ao listar propriedades: {ex.Message})\n");
            }
        }

        public static bool TryIsReadOnly(ObjectId id, string propName)
        {
            try
            {
                Dictionary<string, object> info = SolidosAPI.GetPropertyInfo(id, propName);
                if (info != null && info.TryGetValue("ReadOnly", out object ro))
                {
                    if (ro is bool b) return b;
                    if (ro != null && bool.TryParse(ro.ToString(), out bool parsed)) return parsed;
                }
            }
            catch { }
            return false;
        }

        // ---- Helpers de acesso ao SOLIDOS (mesmo padrão de AjusteConexoesJusante) ----

        public static object TryGetRawParam(ObjectId nodeId, string propName)
        {
            try
            {
                Type propertyType = null;
                return SolidosAPI.GetNodeParam(nodeId, propName, null, ref propertyType);
            }
            catch
            {
                return null;
            }
        }

        public static T TryGetParam<T>(ObjectId nodeId, string propName)
        {
            object value = TryGetRawParam(nodeId, propName);
            if (value == null) return default;
            if (value is T tval) return tval;
            try
            {
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch
            {
                return default;
            }
        }

        public static bool TrySetDoubleParam(ObjectId nodeId, string propName, double value, out string error)
        {
            error = null;
            try
            {
                Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [propName] = value
                };
                SolidosAPI.SetNodeParams(nodeId, dic);
                return true;
            }
            catch (System.Exception ex)
            {
                System.Exception root = ex;
                while (root.InnerException != null) root = root.InnerException;
                error = $"{root.GetType().Name}: {root.Message}";
                return false;
            }
        }


        public static double ConvertToDouble(object raw)
        {
            if (raw == null) return 0.0;
            if (raw is double d) return d;
            if (raw is float f) return f;
            if (raw is int i) return i;
            if (raw is long l) return l;
            try { return Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
            catch { return 0.0; }
        }
    }
}
