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
    public class SolidosConectarRedePorAnchorBi
    {
        private const double ZTol = 1e-3;          // 1mm
        private const int GuardMaxDevices = 8000;  // anti-travamento
        private const int GuardMaxPipeEdits = 20000;

        [CommandMethod("SOL_CONECTAR_REDE_POR_ANCHOR_BI")]
        public void ConectarRedeBidirecionalMantendoDeclividade()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            ProgressMeter progressMeter = null;

            try
            {
                ObjectId anchorId = GetNodeIdBySelection(docEditor, "\nSelecione o DISPOSITIVO ÂNCORA: ");
                if (anchorId.IsNull)
                {
                    docEditor.WriteMessage("\nNada selecionado.");
                    return;
                }

                GeometryPoint anchorLoc = TryGetParam<GeometryPoint>(anchorId, "Location");
                if (anchorLoc == null)
                {
                    docEditor.WriteMessage("\nÂncora sem Location.");
                    return;
                }

                bool adjustAltura = GetYesNo(docEditor, "\nAjustar parâmetro 'Altura' quando Location.Z mudar? [Sim/Nao] <Nao>: ", false);

                Dictionary<ObjectId, double> zTarget = new Dictionary<ObjectId, double>();
                Dictionary<ObjectId, double> oldZ = new Dictionary<ObjectId, double>();

                zTarget[anchorId] = anchorLoc.Z;
                oldZ[anchorId] = anchorLoc.Z;

                Queue<ObjectId> q = new Queue<ObjectId>();
                HashSet<ObjectId> inQueue = new HashSet<ObjectId>();
                q.Enqueue(anchorId);
                inQueue.Add(anchorId);

                int processedDevices = 0;
                int editedPipes = 0;
                int editedDevices = 0;

                // -------- PROGRESSO (aproximado) --------
                // Atualiza a cada X nós processados para reduzir overhead.
                const int progressStep = 10;
                int progressLimit = Math.Max(1, GuardMaxDevices / progressStep);
                int progressTicks = 0;

                progressMeter = new ProgressMeter();
                progressMeter.SetLimit(progressLimit);
                progressMeter.Start("SOLIDOS: Conectando rede (montante + jusante)...");

                while (q.Count > 0)
                {
                    processedDevices++;
                    if (processedDevices > GuardMaxDevices)
                    {
                        docEditor.WriteMessage("\nGuardMaxDevices atingido. Parei pra não travar.");
                        break;
                    }

                    // Progress tick (aproximado)
                    int targetTicks = processedDevices / progressStep;
                    while (progressTicks < targetTicks && progressTicks < progressLimit)
                    {
                        progressMeter.MeterProgress();
                        progressTicks++;
                    }

                    ObjectId curNodeId = q.Dequeue();
                    inQueue.Remove(curNodeId);

                    GeometryPoint curLoc = TryGetParam<GeometryPoint>(curNodeId, "Location");
                    if (curLoc == null)
                    {
                        continue;
                    }

                    double curZ;
                    if (!zTarget.TryGetValue(curNodeId, out curZ))
                    {
                        curZ = curLoc.Z;
                        zTarget[curNodeId] = curZ;
                    }

                    // Garante que o nó atual esteja na cota-alvo (só Z, mantém XY)
                    bool nodeChanged = SetDeviceLocationZOnly(curNodeId, curZ, adjustAltura, oldZ);
                    if (nodeChanged)
                    {
                        editedDevices++;
                    }

                    List<ObjectId> connected = TryGetConnectedDevices(curNodeId);
                    if (connected.Count == 0)
                    {
                        continue;
                    }

                    foreach (ObjectId pipeId in connected)
                    {
                        if (editedPipes > GuardMaxPipeEdits)
                        {
                            docEditor.WriteMessage("\nGuardMaxPipeEdits atingido. Parei pra não travar.");
                            q.Clear();
                            break;
                        }

                        PipeInfo pipe = TryReadPipe(pipeId);
                        if (pipe == null)
                        {
                            continue;
                        }

                        // Só processa se o nó atual estiver em uma das pontas lógicas
                        if (pipe.InPart != curNodeId && pipe.OutPart != curNodeId)
                        {
                            continue;
                        }

                        ObjectId otherNodeId = (pipe.InPart == curNodeId) ? pipe.OutPart : pipe.InPart;
                        if (otherNodeId.IsNull)
                        {
                            continue;
                        }

                        GeometryPoint otherLoc = TryGetParam<GeometryPoint>(otherNodeId, "Location");
                        if (otherLoc == null)
                        {
                            continue;
                        }

                        bool pipeChanged = AdjustPipeAtNodeKeepSlope(pipe, curNodeId, curLoc, curZ, out double otherZ);
                        if (pipeChanged)
                        {
                            editedPipes++;
                        }

                        // Atualiza/propaga a cota do outro nó
                        bool otherHad = zTarget.TryGetValue(otherNodeId, out double otherOldTarget);
                        bool needUpdate = (!otherHad) || (Math.Abs(otherOldTarget - otherZ) > ZTol);

                        zTarget[otherNodeId] = otherZ;

                        if (needUpdate && !inQueue.Contains(otherNodeId))
                        {
                            q.Enqueue(otherNodeId);
                            inQueue.Add(otherNodeId);
                        }
                    }
                }

                SolidosAPI.DocCommit();

                docEditor.WriteMessage(
                    $"\nOK. Rede reconectada (montante + jusante) mantendo declividade. Pipes editados: {editedPipes}. Devices (Z) editados: {editedDevices}.");
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
                if (progressMeter != null)
                {
                    try
                    {
                        progressMeter.Stop();
                    }
                    catch
                    {
                        // ignora
                    }
                }
            }
        }


        // -------------------- CORE: AJUSTA UM TUBO NO LADO DO NÓ --------------------

        private static bool AdjustPipeAtNodeKeepSlope(PipeInfo pipe, ObjectId nodeId, GeometryPoint nodeLoc, double targetZ, out double otherZ)
        {
            otherZ = 0.0;

            GeometryPoint sp = pipe.StartPoint;
            GeometryPoint ep = pipe.EndPoint;

            double planLen = PlanLengthXY(sp, ep);
            if (planLen < 1e-6)
            {
                planLen = 1e-6;
            }

            double slope = (ep.Z - sp.Z) / planLen;

            EndpointSide sideAtNode = ResolveEndpointSide(pipe, nodeId, nodeLoc);

            double newStartZ = sp.Z;
            double newEndZ = ep.Z;

            if (sideAtNode == EndpointSide.StartPoint)
            {
                if (Math.Abs(sp.Z - targetZ) <= ZTol)
                {
                    otherZ = ep.Z;
                    return false;
                }

                newStartZ = targetZ;
                newEndZ = newStartZ + (slope * planLen);
                otherZ = newEndZ;
            }
            else
            {
                if (Math.Abs(ep.Z - targetZ) <= ZTol)
                {
                    otherZ = sp.Z;
                    return false;
                }

                newEndZ = targetZ;
                newStartZ = newEndZ - (slope * planLen);
                otherZ = newStartZ;
            }

            Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            dic["StartPoint"] = new GeometryPoint(sp.X, sp.Y, newStartZ);
            dic["EndPoint"] = new GeometryPoint(ep.X, ep.Y, newEndZ);

            SolidosAPI.SetNodeParams(pipe.PipeId, dic);
            return true;
        }

        // -------------------- DEVICE Z ONLY + ALTURA (OPCIONAL) --------------------

        private static bool SetDeviceLocationZOnly(ObjectId devId, double targetZ, bool adjustAltura, Dictionary<ObjectId, double> oldZ)
        {
            GeometryPoint loc = TryGetParam<GeometryPoint>(devId, "Location");
            if (loc == null)
            {
                return false;
            }

            double currentZ = loc.Z;
            if (Math.Abs(currentZ - targetZ) <= ZTol)
            {
                return false;
            }

            if (!oldZ.ContainsKey(devId))
            {
                oldZ[devId] = currentZ;
            }

            Dictionary<string, object> dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            dic["Location"] = new GeometryPoint(loc.X, loc.Y, targetZ);
            SolidosAPI.SetNodeParams(devId, dic);

            if (adjustAltura && TryGetParamDouble(devId, "Altura", out double alturaOld))
            {
                double deltaZ = targetZ - currentZ;
                double alturaNew = alturaOld - deltaZ;

                Dictionary<string, object> dicAlt = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                dicAlt["Altura"] = alturaNew;
                SolidosAPI.SetNodeParams(devId, dicAlt);
            }

            return true;
        }

        // -------------------- ENDPOINT SIDE --------------------

        private enum EndpointSide
        {
            StartPoint,
            EndPoint
        }

        // 1) InPart -> StartPoint
        // 2) OutPart -> EndPoint
        // 3) fallback: proximidade em XY com Location
        private static EndpointSide ResolveEndpointSide(PipeInfo pipe, ObjectId nodeId, GeometryPoint nodeLoc)
        {
            if (!pipe.InPart.IsNull && pipe.InPart == nodeId)
            {
                return EndpointSide.StartPoint;
            }

            if (!pipe.OutPart.IsNull && pipe.OutPart == nodeId)
            {
                return EndpointSide.EndPoint;
            }

            double d2s = Dist2XY(nodeLoc.X, nodeLoc.Y, pipe.StartPoint.X, pipe.StartPoint.Y);
            double d2e = Dist2XY(nodeLoc.X, nodeLoc.Y, pipe.EndPoint.X, pipe.EndPoint.Y);

            return (d2e < d2s) ? EndpointSide.EndPoint : EndpointSide.StartPoint;
        }

        // -------------------- SELEÇÃO --------------------

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

        private static bool GetYesNo(Editor docEditor, string message, bool defaultYes)
        {
            PromptKeywordOptions pko = new PromptKeywordOptions(message);
            pko.AllowNone = true;
            pko.Keywords.Add("Sim");
            pko.Keywords.Add("Nao");

            PromptResult pr = docEditor.GetKeywords(pko);
            if (pr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(pr.StringResult))
            {
                return defaultYes;
            }

            return pr.StringResult.Equals("Sim", StringComparison.OrdinalIgnoreCase);
        }

        // -------------------- PIPE / PARAMS --------------------

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

        private static bool TryGetParamDouble(ObjectId nodeId, string propName, out double value)
        {
            value = 0.0;

            try
            {
                Type propertyType = null;
                object raw = SolidosAPI.GetNodeParam(nodeId, propName, null, ref propertyType);
                if (raw == null)
                {
                    return false;
                }

                if (raw is double d)
                {
                    value = d;
                    return true;
                }

                if (raw is int i)
                {
                    value = i;
                    return true;
                }

                if (raw is string s)
                {
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                    {
                        value = dv;
                        return true;
                    }

                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out dv))
                    {
                        value = dv;
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // -------------------- MATH --------------------

        private static double PlanLengthXY(GeometryPoint a, GeometryPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static double Dist2XY(double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            return (dx * dx) + (dy * dy);
        }
    }
}
