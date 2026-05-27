using System;
using System.Collections.Generic;
using System.Globalization;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

using SOLIDOS;

namespace AutomacoesCivil3D
{
    public class SolidosConectarRedePorJusante
    {
        private const double ZTol = 1e-3; // 1mm
        private const int GuardMaxDevices = 5000;

        

        // Se ainda acontecer “mudou mas não atualizou visualmente”, ligue isso.
        private const bool RunSolidosAuditAfter = false;
        // Troque pelo comando de auditoria que você usa no SOLIDOS (ex.: SOL_AUDITAR, SOL_AUDIT, etc.)
        private const string SolidosAuditCommand = "SOL_AUDITAR";
        private const string ParamFamilyName = "FamilyName";
        private const string ParamLocation = "Location";
        private const string ParamCotaSaida = "cotaSaida";
        private const string ParamLadoDeriva = "LadoDeriva";
        private const string ParamSetarReferencia = "SetarReferencia";
        private const string RefPontoAlto = "PONTO ALTO";
        private const string RefPontoBaixo = "PONTO BAIXO";
        private static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly string[] FamilyParamCandidates = new string[]
        {
            ParamFamilyName,
            "Family",
            "Familia",
            "NomeFamilia",
            "PartFamilyName",
            "Family Name",
            "Family_Name"
        };
                        private static readonly string[] CotaSaidaParamCandidates = new string[]
        {
            ParamCotaSaida,
            "CotaSaida",
            "cota_saida",
            "Cota_Saida",
            "Cota Saida",
            "SaidaCota"
        };

        [CommandMethod("SOL_CONECTAR_REDE_POR_JUSANTE2")]
        public void ConectarRedePorJusanteMantendoDeclividade()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            ProgressMeter progress = null;
            Dictionary<ObjectId, bool> ladoDerivaOriginalByDevice = new Dictionary<ObjectId, bool>();
            Dictionary<ObjectId, double> initialOutletZByDevice = new Dictionary<ObjectId, double>();
            bool ladoDerivaRestored = false;

            try
            {
                ObjectId downId = GetNodeIdBySelection(docEditor, "\nSelecione o DISPOSITIVO A JUSANTE (âncora): ");
                if (downId.IsNull)
                {
                    docEditor.WriteMessage("\nNada selecionado.");
                    return;
                }

                double downZ = GetDeviceOutletZ(downId);
                ToggleLadoDerivaIfPresent(downId, ladoDerivaOriginalByDevice);
                CaptureInitialOutletZIfNeeded(downId, initialOutletZByDevice, downZ);

                Dictionary<ObjectId, double> zByDevice = new Dictionary<ObjectId, double>();
                zByDevice[downId] = downZ;

                Queue<ObjectId> queue = new Queue<ObjectId>();
                queue.Enqueue(downId);
                HashSet<ObjectId> inQueue = new HashSet<ObjectId>();
                inQueue.Add(downId);

                int adjustedPipes = 0;
                int adjustedDevices = 0;
                int processed = 0;

                progress = new ProgressMeter();
                progress.SetLimit(GuardMaxDevices);
                progress.Start("Processando rede SOLIDOS (jusante → montante)...");

                while (queue.Count > 0)
                {
                    processed++;
                    if (processed > GuardMaxDevices)
                    {
                        docEditor.WriteMessage("\nGuardMax atingido (rede grande). Parei pra não travar.");
                        break;
                    }

                    progress.MeterProgress();

                    ObjectId curDownDevice = queue.Dequeue();
                    inQueue.Remove(curDownDevice);
                    ToggleLadoDerivaIfPresent(curDownDevice, ladoDerivaOriginalByDevice);
                    CaptureInitialOutletZIfNeeded(curDownDevice, initialOutletZByDevice);

                    double curDownZ;
                    if (!zByDevice.TryGetValue(curDownDevice, out curDownZ))
                    {
                        curDownZ = GetDeviceOutletZ(curDownDevice);
                        zByDevice[curDownDevice] = curDownZ;
                    }

                    List<ObjectId> connected = TryGetConnectedDevices(curDownDevice);
                    foreach (ObjectId pipeId in connected)
                    {
                        PipeInfo pipeInfo = TryReadPipe(pipeId);
                        if (pipeInfo == null)
                        {
                            continue;
                        }

                        // Tubos que "entram" no dispositivo atual (jusante do tubo)
                        if (pipeInfo.OutPart.IsNull || pipeInfo.OutPart != curDownDevice)
                        {
                            continue;
                        }

                        ObjectId upDevice = pipeInfo.InPart;
                        if (upDevice.IsNull)
                        {
                            continue;
                        }

                        ToggleLadoDerivaIfPresent(upDevice, ladoDerivaOriginalByDevice);
                        CaptureInitialOutletZIfNeeded(upDevice, initialOutletZByDevice);

                        // Ajusta o tubo para bater no Z do dispositivo jusante, mantendo declividade
                        bool changed = AdjustPipeToDownstreamZ(pipeInfo, curDownDevice, curDownZ, out double newUpZ);
                        if (changed)
                        {
                            adjustedPipes++;
                        }

                        // Ajusta "saída" do dispositivo montante:
                        // - normal: Location.Z
                        // - RALO/FUNIL: cotaSaida
                        bool deviceChanged = SetDeviceOutletZOnly(upDevice, newUpZ);
                        if (deviceChanged)
                        {
                            adjustedDevices++;
                        }

                        bool hadTarget = zByDevice.TryGetValue(upDevice, out double oldUpZ);
                        bool targetChanged = (!hadTarget) || (Math.Abs(oldUpZ - newUpZ) > ZTol);
                        zByDevice[upDevice] = newUpZ;

                        if (targetChanged && !inQueue.Contains(upDevice))
                        {
                            queue.Enqueue(upDevice);
                            inQueue.Add(upDevice);
                        }
                    }
                }

                SolidosAPI.DocCommit();
                RestoreLadoDerivaValues(ladoDerivaOriginalByDevice);
                if (ladoDerivaOriginalByDevice.Count > 0)
                {
                    SolidosAPI.DocCommit();
                }
                if (ApplySetarReferenciaFromCotaChange(initialOutletZByDevice))
                {
                    SolidosAPI.DocCommit();
                }
                ladoDerivaRestored = true;

                ForceVisualRefresh(civilDoc, docEditor);
                TryRunSolidosAudit(civilDoc, docEditor);

                docEditor.WriteMessage(
                    $"\nOK. Ajuste por jusante concluído. Tubos ajustados: {adjustedPipes}. Dispositivos (Z saída) ajustados: {adjustedDevices}.");
            }
            catch (SOLIDOS.SolidosException solEx)
            {
                docEditor.WriteMessage($"\n[SOLIDOS] {solEx.Message}");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO] {ex.Message}");
            }
            finally
            {
                if (!ladoDerivaRestored)
                {
                    try
                    {
                        RestoreLadoDerivaValues(ladoDerivaOriginalByDevice);
                        if (ladoDerivaOriginalByDevice.Count > 0)
                        {
                            SolidosAPI.DocCommit();
                            ForceVisualRefresh(civilDoc, docEditor);
                        }
                    }
                    catch (System.Exception restoreEx)
                    {
                        docEditor.WriteMessage($"\n[ERRO] Falha ao restaurar {ParamLadoDeriva}: {restoreEx.Message}");
                    }
                }

                if (progress != null)
                {
                    progress.Stop();
                }
            }
        }

        // -------------------- CORE --------------------

        private static bool AdjustPipeToDownstreamZ(PipeInfo pipe, ObjectId downDevice, double downZ, out double upZ)
        {
            upZ = 0.0;

            GeometryPoint sp = pipe.StartPoint;
            GeometryPoint ep = pipe.EndPoint;

            GeometryPoint downLoc = TryGetParam<GeometryPoint>(downDevice, ParamLocation);
            EndpointSide sideAtDown = ResolveEndpointSide(pipe, downDevice, downLoc);
            bool downAtEnd = sideAtDown == EndpointSide.EndPoint;

            GeometryPoint downPt = downAtEnd ? ep : sp;
            GeometryPoint upPt = downAtEnd ? sp : ep;

            double planLen = PlanLengthXY(upPt, downPt);
            if (planLen < 1e-6)
            {
                planLen = 1e-6;
            }

            // declividade atual no sentido montante -> jusante (mantida)
            double slope = (downPt.Z - upPt.Z) / planLen;

            double newDownZ = downZ;
            double newUpZ = newDownZ - (slope * planLen);

            if (Math.Abs(downPt.Z - newDownZ) <= ZTol && Math.Abs(upPt.Z - newUpZ) <= ZTol)
            {
                upZ = upPt.Z;
                return false;
            }

            Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (downAtEnd)
            {
                dic["StartPoint"] = new GeometryPoint(sp.X, sp.Y, newUpZ);
                dic["EndPoint"] = new GeometryPoint(ep.X, ep.Y, newDownZ);
            }
            else
            {
                dic["StartPoint"] = new GeometryPoint(sp.X, sp.Y, newDownZ);
                dic["EndPoint"] = new GeometryPoint(ep.X, ep.Y, newUpZ);
            }

            SolidosAPI.SetNodeParams(pipe.PipeId, dic);

            upZ = newUpZ;
            return true;
        }
        // -------------------- DEVICE OUTLET Z (Location.Z ou cotaSaida) --------------------

        private static double GetDeviceOutletZ(ObjectId devId)
        {
            if (IsRaloOrFunil(devId))
            {
                if (TryGetDeviceCotaSaida(devId, out double cotaSaida))
                {
                    return cotaSaida;
                }

                GeometryPoint locFallback = TryGetParam<GeometryPoint>(devId, ParamLocation);
                return (locFallback != null) ? locFallback.Z : 0.0;
            }

            GeometryPoint loc = TryGetParam<GeometryPoint>(devId, ParamLocation);
            return (loc != null) ? loc.Z : 0.0;
        }

        private static bool SetDeviceOutletZOnly(ObjectId devId, double z)
        {
            if (IsRaloOrFunil(devId))
            {
                double cur = GetDeviceOutletZ(devId);
                if (Math.Abs(cur - z) <= ZTol)
                {
                    return false;
                }

                if (TrySetDeviceCotaSaida(devId, z))
                {
                    return true;
                }

                // Ralos e funis nao devem usar o parametro padrao Location.Z.
                return false;
            }

            return SetDeviceLocationZOnly(devId, z);
        }

        private static bool IsRaloOrFunil(ObjectId devId)
        {
            string family = TryGetFamilyName(devId);
            if (string.IsNullOrWhiteSpace(family))
            {
                return false;
            }

            string u = family.ToUpperInvariant();
            return u.Contains("RALO") || u.Contains("FUNIL");
        }

        private static string TryGetFamilyName(ObjectId devId)
        {
            return TryGetFirstStringParam(devId, FamilyParamCandidates);
        }

        private static bool SetDeviceLocationZOnly(ObjectId devId, double z)
        {
            GeometryPoint loc = TryGetParam<GeometryPoint>(devId, ParamLocation);
            if (loc == null)
            {
                return false;
            }

            if (Math.Abs(loc.Z - z) <= ZTol)
            {
                return false;
            }

            Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            // Location e o parametro padrao dos dispositivos comuns.
            dic[ParamLocation] = new GeometryPoint(loc.X, loc.Y, z);
            SolidosAPI.SetNodeParams(devId, dic);
            return true;
        }

        private static bool TryGetDeviceCotaSaida(ObjectId devId, out double z)
        {
            foreach (string param in CotaSaidaParamCandidates)
            {
                if (TryGetDoubleParam(devId, param, out z))
                {
                    return true;
                }
            }

            z = 0.0;
            return false;
        }

                        private static bool TrySetDeviceCotaSaida(ObjectId devId, double z)
        {
            foreach (string param in CotaSaidaParamCandidates)
            {
                try
                {
                    Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    dic[param] = z;
                    SolidosAPI.SetNodeParams(devId, dic);

                    // valida leitura de volta para evitar falso positivo quando o param e ignorado
                    if (TryGetDoubleParam(devId, param, out double appliedZ) && Math.Abs(appliedZ - z) <= ZTol)
                    {
                        return true;
                    }
                }
                catch
                {
                    // tenta proximo alias
                }
            }

            return false;
        }

        private static bool TryGetDoubleParam(ObjectId nodeId, string propName, out double value)
        {
            object raw = TryGetParam<object>(nodeId, propName);
            return TryConvertToDouble(raw, out value);
        }

        private static void CaptureInitialOutletZIfNeeded(ObjectId devId, IDictionary<ObjectId, double> initialOutletZByDevice, double? knownZ = null)
        {
            if (devId.IsNull || initialOutletZByDevice.ContainsKey(devId))
            {
                return;
            }

            initialOutletZByDevice[devId] = knownZ ?? GetDeviceOutletZ(devId);
        }

        private static bool ApplySetarReferenciaFromCotaChange(IEnumerable<KeyValuePair<ObjectId, double>> initialOutletZByDevice)
        {
            bool anyChanged = false;

            foreach (KeyValuePair<ObjectId, double> entry in initialOutletZByDevice)
            {
                double finalZ = GetDeviceOutletZ(entry.Key);
                string referenceValue = finalZ < (entry.Value - ZTol) ? RefPontoBaixo : RefPontoAlto;

                if (TrySetStringParam(entry.Key, ParamSetarReferencia, referenceValue))
                {
                    anyChanged = true;
                }
            }

            return anyChanged;
        }

        private static void ToggleLadoDerivaIfPresent(ObjectId devId, IDictionary<ObjectId, bool> originalValues)
        {
            if (devId.IsNull || originalValues.ContainsKey(devId))
            {
                return;
            }

            if (!TryGetBoolParam(devId, ParamLadoDeriva, out bool currentValue))
            {
                return;
            }

            originalValues[devId] = currentValue;
            TrySetBoolParam(devId, ParamLadoDeriva, !currentValue);
        }

        private static void RestoreLadoDerivaValues(IEnumerable<KeyValuePair<ObjectId, bool>> originalValues)
        {
            foreach (KeyValuePair<ObjectId, bool> entry in originalValues)
            {
                TrySetBoolParam(entry.Key, ParamLadoDeriva, entry.Value);
            }
        }

        private static bool TryGetBoolParam(ObjectId nodeId, string propName, out bool value)
        {
            object raw = TryGetParam<object>(nodeId, propName);
            return TryConvertToBool(raw, out value);
        }

        private static bool TrySetBoolParam(ObjectId nodeId, string propName, bool value)
        {
            try
            {
                Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                dic[propName] = value;
                SolidosAPI.SetNodeParams(nodeId, dic);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetStringParam(ObjectId nodeId, string propName, string value)
        {
            if (!HasNodeParam(nodeId, propName))
            {
                return false;
            }

            try
            {
                Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                dic[propName] = value;
                SolidosAPI.SetNodeParams(nodeId, dic);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasNodeParam(ObjectId nodeId, string propName)
        {
            try
            {
                Type propertyType = null;
                object value = SolidosAPI.GetNodeParam(nodeId, propName, null, ref propertyType);
                return propertyType != null || value != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertToDouble(object raw, out double value)
        {
            value = 0.0;
            if (raw == null)
            {
                return false;
            }

            if (raw is double d) { value = d; return true; }
            if (raw is float f) { value = f; return true; }
            if (raw is int i) { value = i; return true; }
            if (raw is long l) { value = l; return true; }
            if (raw is decimal m) { value = (double)m; return true; }
            if (raw is short sh) { value = sh; return true; }
            if (raw is byte b) { value = b; return true; }

            if (raw is IConvertible)
            {
                try
                {
                    value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    // segue para parse textual
                }
            }

            string txt = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return TryParseFlexibleDouble(txt, out value);
        }

        private static bool TryConvertToBool(object raw, out bool value)
        {
            value = false;
            if (raw == null)
            {
                return false;
            }

            if (raw is bool b)
            {
                value = b;
                return true;
            }

            if (raw is byte by)
            {
                value = by != 0;
                return true;
            }

            if (raw is short s)
            {
                value = s != 0;
                return true;
            }

            if (raw is int i)
            {
                value = i != 0;
                return true;
            }

            if (raw is long l)
            {
                value = l != 0;
                return true;
            }

            if (raw is float f)
            {
                value = Math.Abs(f) > double.Epsilon;
                return true;
            }

            if (raw is double d)
            {
                value = Math.Abs(d) > double.Epsilon;
                return true;
            }

            if (raw is decimal m)
            {
                value = m != 0m;
                return true;
            }

            string text = Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = text.Trim();
            if (bool.TryParse(normalized, out bool boolValue))
            {
                value = boolValue;
                return true;
            }

            switch (normalized.ToUpperInvariant())
            {
                case "1":
                case "SIM":
                case "YES":
                case "Y":
                    value = true;
                    return true;
                case "0":
                case "NAO":
                case "N":
                case "NO":
                    value = false;
                    return true;
            }

            return false;
        }

        private static bool TryParseFlexibleDouble(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string t = text.Trim();
            if (double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            if (double.TryParse(t, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            if (double.TryParse(t, NumberStyles.Any, PtBrCulture, out value)) return true;

            // fallback comum para "1.234,56" ou "1234,56"
            string normalized = t.Replace(" ", string.Empty);
            string swapped = normalized.Replace(".", string.Empty).Replace(",", ".");
            if (double.TryParse(swapped, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;

            // fallback comum para "1,234.56"
            swapped = normalized.Replace(",", string.Empty);
            if (double.TryParse(swapped, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;

            return false;
        }

        private static string TryGetFirstStringParam(ObjectId nodeId, IEnumerable<string> propNames)
        {
            foreach (string propName in propNames)
            {
                object raw = TryGetParam<object>(nodeId, propName);
                if (raw == null)
                {
                    continue;
                }

                string value = Convert.ToString(raw, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        // -------------------- PIPE READ --------------------

        private sealed class PipeInfo
        {
            public ObjectId PipeId { get; }
            public ObjectId InPart { get; }
            public ObjectId OutPart { get; }
            public GeometryPoint StartPoint { get; }
            public GeometryPoint EndPoint { get; }

            public PipeInfo(ObjectId pipeId, ObjectId inPart, ObjectId outPart, GeometryPoint startPoint, GeometryPoint endPoint)
            {
                PipeId = pipeId;
                InPart = inPart;
                OutPart = outPart;
                StartPoint = startPoint;
                EndPoint = endPoint;
            }
        }

        private static PipeInfo TryReadPipe(ObjectId pipeId)
        {
            ObjectId inPart = TryGetParam<ObjectId>(pipeId, "InPart");
            ObjectId outPart = TryGetParam<ObjectId>(pipeId, "OutPart");
            GeometryPoint sp = TryGetParam<GeometryPoint>(pipeId, "StartPoint");
            GeometryPoint ep = TryGetParam<GeometryPoint>(pipeId, "EndPoint");

            if (inPart.IsNull || outPart.IsNull || sp == null || ep == null)
            {
                return null;
            }

            PipeInfo pipe = new PipeInfo(pipeId, inPart, outPart, sp, ep);
            return pipe;
        }

        // -------------------- SELECTION --------------------

        private static ObjectId GetNodeIdBySelection(Editor docEditor, string msg)
        {
            PromptEntityOptions peo = new PromptEntityOptions(msg);
            peo.SetRejectMessage("\nSelecione um nó/objeto do SOLIDOS.");

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                return ObjectId.Null;
            }

            return per.ObjectId;
        }

        // -------------------- SOLIDOS PARAMS --------------------

        private static List<ObjectId> TryGetConnectedDevices(ObjectId nodeId)
        {
            List<ObjectId> result = new List<ObjectId>();

            object raw = TryGetParam<object>(nodeId, "ConnectedDevices");
            if (raw == null)
            {
                return result;
            }

            if (raw is List<ObjectId> list)
            {
                result.AddRange(list);
                return result;
            }

            if (raw is ObjectId[] arr)
            {
                result.AddRange(arr);
                return result;
            }

            if (raw is ObjectIdCollection col)
            {
                foreach (ObjectId oid in col)
                {
                    result.Add(oid);
                }
                return result;
            }

            if (raw is IEnumerable<ObjectId> enumerable)
            {
                foreach (ObjectId oid in enumerable)
                {
                    result.Add(oid);
                }
            }

            return result;
        }

        private static T TryGetParam<T>(ObjectId nodeId, string propName)
        {
            try
            {
                Type propertyType = null;
                object value = SolidosAPI.GetNodeParam(nodeId, propName, null, ref propertyType);

                if (value == null)
                {
                    return default;
                }

                if (value is T tval)
                {
                    return tval;
                }

                return default;
            }
            catch
            {
                return default;
            }
        }


// -------------------- REFRESH / AUDIT --------------------

private static void ForceVisualRefresh(Document civilDoc, Editor docEditor)
{
    try
    {
        civilDoc.Database.TransactionManager.QueueForGraphicsFlush();
        docEditor.Regen();
        Application.UpdateScreen();
    }
    catch
    {
        // ignore
    }
}

private static void TryRunSolidosAudit(Document civilDoc, Editor docEditor)
{
    if (!RunSolidosAuditAfter)
    {
        return;
    }

    try
    {
        // Síncrono quando possível
        docEditor.Command(SolidosAuditCommand);
    }
    catch
    {
        try
        {
            // Fallback (enfileira)
            civilDoc.SendStringToExecute(SolidosAuditCommand + " ", true, false, false);
        }
        catch
        {
            // ignore
        }
    }
}

                private enum EndpointSide
        {
            StartPoint,
            EndPoint
        }

        private static EndpointSide ResolveEndpointSide(PipeInfo pipe, ObjectId nodeId, GeometryPoint nodeLoc)
        {
            if (nodeLoc != null)
            {
                double d2s = Dist2XY(nodeLoc.X, nodeLoc.Y, pipe.StartPoint.X, pipe.StartPoint.Y);
                double d2e = Dist2XY(nodeLoc.X, nodeLoc.Y, pipe.EndPoint.X, pipe.EndPoint.Y);
                if (Math.Abs(d2s - d2e) > 1e-10)
                {
                    return (d2e < d2s) ? EndpointSide.EndPoint : EndpointSide.StartPoint;
                }
            }

            if (!pipe.InPart.IsNull && pipe.InPart == nodeId)
            {
                return EndpointSide.StartPoint;
            }

            if (!pipe.OutPart.IsNull && pipe.OutPart == nodeId)
            {
                return EndpointSide.EndPoint;
            }

            return EndpointSide.StartPoint;
        }

        private static double Dist2XY(double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            return (dx * dx) + (dy * dy);
        }

        // -------------------- MATH --------------------

        private static double PlanLengthXY(GeometryPoint a, GeometryPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }
    }
}
