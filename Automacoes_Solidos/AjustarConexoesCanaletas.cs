using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D
{
    public class SolidosSyncCruzetaCanaleta
    {
        [CommandMethod("SOL_SYNC_CRUZETA_CANALETAS")]
        public void SyncCruzetaPortsAndDownstreamCanaletas()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database database = civilDoc.Database;

            SolidosInterop sol = SolidosInterop.TryCreate(docEditor, database);
            if (!sol.IsReady)
            {
                docEditor.WriteMessage(
                    "\n[SOLIDOS] Não encontrei wrappers SolidosGetNodeParam/SolidosSetNodeParams/SolidosGetPropertyType/SolidosCommit.\n" +
                    "Carregue o plug-in do SOLIDOS e rode de novo.\n");
                return;
            }

            int cruzetasFound = 0;
            int canaletasFound = 0;

            int cruzetasUpdatedPorts = 0;
            int canaletasDownUpdated = 0;

            int skippedNoInlets = 0;
            int skippedNoOutPartProp = 0;

            using (DocumentLock documentLock = civilDoc.LockDocument())
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                    ObjectId modelSpaceId = blockTable[BlockTableRecord.ModelSpace];
                    BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(modelSpaceId, OpenMode.ForRead);

                    List<ObjectId> allEntIds = new List<ObjectId>();
                    foreach (ObjectId id in modelSpace)
                    {
                        allEntIds.Add(id);
                    }

                    // 1) Coleta cruzetas
                    HashSet<ObjectId> cruzetas = new HashSet<ObjectId>();
                    foreach (ObjectId id in allEntIds)
                    {
                        if (IsCruzeta(sol, id))
                        {
                            cruzetas.Add(id);
                        }
                    }
                    cruzetasFound = cruzetas.Count;

                    // 2) Coleta canaletas
                    List<CanaletaInfo> canaletas = new List<CanaletaInfo>();
                    foreach (ObjectId id in allEntIds)
                    {
                        CanaletaInfo info;
                        if (TryBuildCanaletaInfo(sol, id, out info))
                        {
                            canaletas.Add(info);
                        }
                    }
                    canaletasFound = canaletas.Count;

                    // 3) Indexa canaletas por cruzeta (entradas e saídas)
                    Dictionary<ObjectId, List<CanaletaInfo>> inletsByCruzeta = new Dictionary<ObjectId, List<CanaletaInfo>>();
                    Dictionary<ObjectId, List<CanaletaInfo>> outletsByCruzeta = new Dictionary<ObjectId, List<CanaletaInfo>>();

                    int outPartReadable = 0;

                    foreach (CanaletaInfo c in canaletas)
                    {
                        // Saída da cruzeta (jusante): canaleta cujo InPart == cruzeta
                        if (c.InPart != ObjectId.Null && cruzetas.Contains(c.InPart))
                        {
                            if (!outletsByCruzeta.ContainsKey(c.InPart))
                            {
                                outletsByCruzeta[c.InPart] = new List<CanaletaInfo>();
                            }
                            outletsByCruzeta[c.InPart].Add(c);
                        }

                        // Entrada na cruzeta (montante): canaleta cujo OutPart == cruzeta
                        if (c.HasOutPart && c.OutPart != ObjectId.Null)
                        {
                            outPartReadable++;
                            if (cruzetas.Contains(c.OutPart))
                            {
                                if (!inletsByCruzeta.ContainsKey(c.OutPart))
                                {
                                    inletsByCruzeta[c.OutPart] = new List<CanaletaInfo>();
                                }
                                inletsByCruzeta[c.OutPart].Add(c);
                            }
                        }
                    }

                    if (outPartReadable == 0)
                    {
                        skippedNoOutPartProp = cruzetasFound; // aviso: não vai conseguir achar entradas com segurança
                    }

                    // 4) Processa cada cruzeta
                    foreach (ObjectId cruzetaId in cruzetas)
                    {
                        List<CanaletaInfo> inlets;
                        if (!inletsByCruzeta.TryGetValue(cruzetaId, out inlets) || inlets.Count == 0)
                        {
                            skippedNoInlets++;
                            continue;
                        }

                        // 4.1) Atualiza AlturaInicialP# (porta por porta) a partir das entradas
                        Dictionary<int, double> portAltura = new Dictionary<int, double>(); // P1..P4 => max(AlturaFim)
                        List<double> alturasEntrada = new List<double>();
                        List<double> declividadesEntrada = new List<double>();

                        foreach (CanaletaInfo inlet in inlets)
                        {
                            int portIndex = ConnectorToPortIndex(inlet.EndConnectorId);
                            if (portIndex < 1 || portIndex > 4)
                            {
                                continue;
                            }

                            alturasEntrada.Add(inlet.AlturaFim);
                            declividadesEntrada.Add(inlet.Declividade);

                            if (!portAltura.ContainsKey(portIndex))
                            {
                                portAltura[portIndex] = inlet.AlturaFim;
                            }
                            else
                            {
                                portAltura[portIndex] = Math.Max(portAltura[portIndex], inlet.AlturaFim);
                            }
                        }

                        if (portAltura.Count > 0)
                        {
                            Dictionary<string, object> setPorts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                            foreach (KeyValuePair<int, double> kv in portAltura)
                            {
                                string propName = "AlturaInicialP" + kv.Key.ToString(CultureInfo.InvariantCulture);
                                if (sol.HasProperty(cruzetaId, propName))
                                {
                                    setPorts[propName] = kv.Value;
                                }
                            }

                            if (setPorts.Count > 0)
                            {
                                bool okPorts = sol.TrySetParams(cruzetaId, setPorts);
                                if (okPorts)
                                {
                                    cruzetasUpdatedPorts++;
                                }
                            }
                        }

                        // 4.2) Calcula SAÍDA global (critério que você pediu)
                        // - AlturaSaida = maior Altura de entrada (equivalente ao menor fundo)
                        // - DeclividadeSaida = maior declividade que entra
                        double alturaSaida = alturasEntrada.Count > 0 ? alturasEntrada.Max() : double.NaN;
                        double declividadeSaida = declividadesEntrada.Count > 0 ? declividadesEntrada.Max() : double.NaN;

                        if (double.IsNaN(alturaSaida) || double.IsNaN(declividadeSaida))
                        {
                            continue;
                        }

                        // força declividade positiva
                        declividadeSaida = Math.Abs(declividadeSaida);

                        // 4.3) Aplica em TODAS as canaletas jusante conectadas (InPart == cruzeta)
                        List<CanaletaInfo> outlets;
                        if (outletsByCruzeta.TryGetValue(cruzetaId, out outlets) && outlets.Count > 0)
                        {
                            foreach (CanaletaInfo outlet in outlets)
                            {
                                Dictionary<string, object> pairs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                                if (sol.HasProperty(outlet.Id, "AlturaInicial"))
                                {
                                    pairs["AlturaInicial"] = alturaSaida;
                                }

                                if (sol.HasProperty(outlet.Id, "Declividade"))
                                {
                                    pairs["Declividade"] = declividadeSaida;
                                }

                                if (pairs.Count == 0)
                                {
                                    continue;
                                }

                                bool ok = sol.TrySetParams(outlet.Id, pairs);
                                if (ok)
                                {
                                    canaletasDownUpdated++;
                                }
                            }
                        }
                    }

                    transaction.Commit();
                }

                sol.Commit($"Sync cruzeta/canaletas: cruzetas={cruzetasFound}, canaletas={canaletasFound}, portsUpd={cruzetasUpdatedPorts}, downUpd={canaletasDownUpdated}");
            }

            docEditor.WriteMessage(
                "\n[SOLIDOS] SOL_SYNC_CRUZETA_CANALETAS concluído." +
                $"\n- Cruzeta(s) encontradas: {cruzetasFound}" +
                $"\n- Canaleta(s) encontradas: {canaletasFound}" +
                $"\n- Cruzetas com portas atualizadas (AlturaInicialP#): {cruzetasUpdatedPorts}" +
                $"\n- Canaletas jusante atualizadas (AlturaInicial/Declividade): {canaletasDownUpdated}" +
                $"\n- Cruzetas sem entradas detectadas: {skippedNoInlets}" +
                (skippedNoOutPartProp > 0 ? "\n- ATENÇÃO: não consegui ler OutPart/OutPartId em nenhuma canaleta (entradas podem não ser detectadas)." : "") +
                "\n");
        }

        private static bool IsCruzeta(SolidosInterop sol, ObjectId id)
        {
            // Cruzeta “de canaleta”: parâmetros AlturaInicialP1..P4 (você disse que vai deixar isso nela)
            if (!sol.HasProperty(id, "AlturaInicialP1")) { return false; }
            if (!sol.HasProperty(id, "AlturaInicialP2")) { return false; }
            if (!sol.HasProperty(id, "AlturaInicialP3")) { return false; }
            if (!sol.HasProperty(id, "AlturaInicialP4")) { return false; }
            return true;
        }

        private static bool TryBuildCanaletaInfo(SolidosInterop sol, ObjectId id, out CanaletaInfo info)
        {
            info = new CanaletaInfo();

            if (!sol.HasProperty(id, "AlturaFim")) { return false; }
            if (!sol.HasProperty(id, "Declividade")) { return false; }
            if (!sol.HasProperty(id, "InPart")) { return false; }
            if (!sol.HasProperty(id, "StartConnectorId")) { return false; }
            if (!sol.HasProperty(id, "EndConnectorId")) { return false; }

            ObjectId inPart;
            if (!sol.TryGetObjectId(id, "InPart", out inPart))
            {
                return false;
            }

            string startConnectorId;
            string endConnectorId;

            if (!sol.TryGetString(id, "StartConnectorId", out startConnectorId))
            {
                startConnectorId = string.Empty;
            }

            if (!sol.TryGetString(id, "EndConnectorId", out endConnectorId))
            {
                endConnectorId = string.Empty;
            }

            double alturaFim;
            if (!sol.TryGetDouble(id, "AlturaFim", out alturaFim))
            {
                return false;
            }

            double declividade;
            if (!sol.TryGetDouble(id, "Declividade", out declividade))
            {
                return false;
            }

            // OutPart é opcional, mas é o que permite detectar entradas
            bool hasOutPart = false;
            ObjectId outPart = ObjectId.Null;

            ObjectId outPartTry;
            if (sol.TryGetObjectId(id, "OutPart", out outPartTry))
            {
                hasOutPart = true;
                outPart = outPartTry;
            }
            else if (sol.TryGetObjectId(id, "OutPartId", out outPartTry))
            {
                hasOutPart = true;
                outPart = outPartTry;
            }
            else if (sol.TryGetObjectId(id, "EndPart", out outPartTry))
            {
                hasOutPart = true;
                outPart = outPartTry;
            }
            else if (sol.TryGetObjectId(id, "EndPartId", out outPartTry))
            {
                hasOutPart = true;
                outPart = outPartTry;
            }

            info.Id = id;
            info.InPart = inPart;
            info.HasOutPart = hasOutPart;
            info.OutPart = outPart;
            info.StartConnectorId = startConnectorId ?? string.Empty;
            info.EndConnectorId = endConnectorId ?? string.Empty;
            info.AlturaFim = alturaFim;
            info.Declividade = declividade;

            return true;
        }

        private static int ConnectorToPortIndex(string connectorId)
        {
            if (string.IsNullOrWhiteSpace(connectorId)) { return 0; }

            string s = connectorId.Trim();
            if (s.Equals("ConnectorP1", StringComparison.OrdinalIgnoreCase)) { return 1; }
            if (s.Equals("ConnectorP2", StringComparison.OrdinalIgnoreCase)) { return 2; }
            if (s.Equals("ConnectorP3", StringComparison.OrdinalIgnoreCase)) { return 3; }
            if (s.Equals("ConnectorP4", StringComparison.OrdinalIgnoreCase)) { return 4; }

            return 0;
        }

        private struct CanaletaInfo
        {
            public ObjectId Id;
            public ObjectId InPart;

            public bool HasOutPart;
            public ObjectId OutPart;

            public string StartConnectorId;
            public string EndConnectorId;

            public double AlturaFim;
            public double Declividade;
        }
    }

    internal sealed class SolidosInterop
    {
        private const int TypeCodeDouble = 5001;
        private const int TypeCodeString = 5005;
        private const int TypeCodeObjectId = 5006;
        private const int TypeCodeBool = 5021;

        private readonly Editor _ed;
        private readonly Database _db;

        private readonly MethodInfo _miGetNodeParam;
        private readonly MethodInfo _miSetNodeParams;
        private readonly MethodInfo _miGetPropertyType;
        private readonly MethodInfo _miCommit;

        private SolidosInterop(Editor ed, Database db, MethodInfo miGetNodeParam, MethodInfo miSetNodeParams, MethodInfo miGetPropertyType, MethodInfo miCommit)
        {
            _ed = ed;
            _db = db;

            _miGetNodeParam = miGetNodeParam;
            _miSetNodeParams = miSetNodeParams;
            _miGetPropertyType = miGetPropertyType;
            _miCommit = miCommit;
        }

        public bool IsReady
        {
            get
            {
                return _miGetNodeParam != null && _miSetNodeParams != null && _miGetPropertyType != null && _miCommit != null;
            }
        }

        public static SolidosInterop TryCreate(Editor ed, Database db)
        {
            MethodInfo miGetNodeParam = FindBest("SolidosGetNodeParam");
            MethodInfo miSetNodeParams = FindBest("SolidosSetNodeParams");
            MethodInfo miGetPropertyType = FindBest("SolidosGetPropertyType");
            MethodInfo miCommit = FindBest("SolidosCommit");

            SolidosInterop sol = new SolidosInterop(ed, db, miGetNodeParam, miSetNodeParams, miGetPropertyType, miCommit);
            return sol;

            static MethodInfo FindBest(string methodName)
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (Assembly asm in assemblies)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch { continue; }

                    foreach (Type t in types)
                    {
                        MethodInfo[] methods;
                        try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                        catch { continue; }

                        foreach (MethodInfo m in methods)
                        {
                            if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) { continue; }

                            ParameterInfo[] ps = m.GetParameters();

                            if (ps.Length == 1 && typeof(ResultBuffer).IsAssignableFrom(ps[0].ParameterType)) { return m; }

                            if (methodName == "SolidosGetNodeParam" && ps.Length == 2 &&
                                ps[0].ParameterType == typeof(ObjectId) && ps[1].ParameterType == typeof(string))
                            {
                                return m;
                            }

                            if (methodName == "SolidosCommit" && ps.Length == 1 && ps[0].ParameterType == typeof(string)) { return m; }
                        }
                    }
                }
                return null;
            }
        }

        public bool HasProperty(ObjectId id, string propName)
        {
            try
            {
                object result = InvokeGetPropertyType(id, propName);
                if (result == null) { return false; }

                string s = Convert.ToString(result, CultureInfo.InvariantCulture);
                return !string.IsNullOrWhiteSpace(s);
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetString(ObjectId id, string propName, out string value)
        {
            value = string.Empty;

            try
            {
                object raw = InvokeGetNodeParam(id, propName);
                if (raw == null) { return false; }

                value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetObjectId(ObjectId id, string propName, out ObjectId value)
        {
            value = ObjectId.Null;

            try
            {
                object raw = InvokeGetNodeParam(id, propName);
                if (raw == null) { return false; }

                if (raw is ObjectId objectId)
                {
                    value = objectId;
                    return true;
                }

                if (raw is Handle handle)
                {
                    value = _db.GetObjectId(false, handle, 0);
                    return value != ObjectId.Null;
                }

                string s = Convert.ToString(raw, CultureInfo.InvariantCulture);
                if (TryParseHandle(s, out Handle h))
                {
                    value = _db.GetObjectId(false, h, 0);
                    return value != ObjectId.Null;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetDouble(ObjectId id, string propName, out double value)
        {
            value = double.NaN;

            try
            {
                object raw = InvokeGetNodeParam(id, propName);
                if (raw == null) { return false; }

                if (raw is double d)
                {
                    value = d;
                    return true;
                }

                if (raw is float f)
                {
                    value = f;
                    return true;
                }

                if (raw is int i)
                {
                    value = i;
                    return true;
                }

                if (raw is long l)
                {
                    value = l;
                    return true;
                }

                string s = Convert.ToString(raw, CultureInfo.InvariantCulture);
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                {
                    value = parsed;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool TrySetParams(ObjectId id, Dictionary<string, object> pairs)
        {
            try
            {
                if (pairs == null || pairs.Count == 0) { return true; }

                if (_miSetNodeParams != null)
                {
                    ParameterInfo[] ps = _miSetNodeParams.GetParameters();
                    if (ps.Length == 1 && typeof(ResultBuffer).IsAssignableFrom(ps[0].ParameterType))
                    {
                        ResultBuffer rb = BuildSetParamsBuffer(id, pairs);
                        object ret = _miSetNodeParams.Invoke(null, new object[] { rb });
                        return ConvertToBool(ret);
                    }

                    if (ps.Length >= 2 && ps[0].ParameterType == typeof(ObjectId))
                    {
                        List<object> args = new List<object>();
                        args.Add(id);
                        foreach (KeyValuePair<string, object> kv in pairs)
                        {
                            args.Add(kv.Key);
                            args.Add(kv.Value);
                        }

                        object ret = _miSetNodeParams.Invoke(null, args.ToArray());
                        return ConvertToBool(ret);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public void Commit(string msg)
        {
            try
            {
                if (_miCommit == null) { return; }

                ParameterInfo[] ps = _miCommit.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    _miCommit.Invoke(null, new object[] { msg });
                    return;
                }

                if (ps.Length == 1 && typeof(ResultBuffer).IsAssignableFrom(ps[0].ParameterType))
                {
                    ResultBuffer rb = new ResultBuffer(new TypedValue(TypeCodeString, msg));
                    _miCommit.Invoke(null, new object[] { rb });
                }
            }
            catch
            {
                // não trava o comando
            }
        }

        private object InvokeGetNodeParam(ObjectId id, string propName)
        {
            if (_miGetNodeParam == null) { return null; }

            ParameterInfo[] ps = _miGetNodeParam.GetParameters();

            if (ps.Length == 2 && ps[0].ParameterType == typeof(ObjectId) && ps[1].ParameterType == typeof(string))
            {
                return _miGetNodeParam.Invoke(null, new object[] { id, propName });
            }

            if (ps.Length == 1 && typeof(ResultBuffer).IsAssignableFrom(ps[0].ParameterType))
            {
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue(TypeCodeObjectId, id),
                    new TypedValue(TypeCodeString, propName)
                );
                return _miGetNodeParam.Invoke(null, new object[] { rb });
            }

            return null;
        }

        private object InvokeGetPropertyType(ObjectId id, string propName)
        {
            if (_miGetPropertyType == null) { return null; }

            ParameterInfo[] ps = _miGetPropertyType.GetParameters();

            if (ps.Length == 1 && typeof(ResultBuffer).IsAssignableFrom(ps[0].ParameterType))
            {
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue(TypeCodeObjectId, id),
                    new TypedValue(TypeCodeString, propName)
                );
                return _miGetPropertyType.Invoke(null, new object[] { rb });
            }

            if (ps.Length == 2 && ps[0].ParameterType == typeof(ObjectId) && ps[1].ParameterType == typeof(string))
            {
                return _miGetPropertyType.Invoke(null, new object[] { id, propName });
            }

            return null;
        }

        private static ResultBuffer BuildSetParamsBuffer(ObjectId id, Dictionary<string, object> pairs)
        {
            List<TypedValue> values = new List<TypedValue>();
            values.Add(new TypedValue(TypeCodeObjectId, id));

            foreach (KeyValuePair<string, object> kv in pairs)
            {
                values.Add(new TypedValue(TypeCodeString, kv.Key));

                object v = kv.Value;
                if (v is bool b)
                {
                    values.Add(new TypedValue(TypeCodeBool, b));
                }
                else if (v is double d)
                {
                    values.Add(new TypedValue(TypeCodeDouble, d));
                }
                else if (v is float f)
                {
                    values.Add(new TypedValue(TypeCodeDouble, (double)f));
                }
                else if (v is int i)
                {
                    values.Add(new TypedValue(TypeCodeDouble, (double)i));
                }
                else if (v is long l)
                {
                    values.Add(new TypedValue(TypeCodeDouble, (double)l));
                }
                else
                {
                    values.Add(new TypedValue(TypeCodeString, Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty));
                }
            }

            ResultBuffer rb = new ResultBuffer(values.ToArray());
            return rb;
        }

        private static bool ConvertToBool(object o)
        {
            if (o == null) { return false; }
            if (o is bool b) { return b; }

            string s = Convert.ToString(o, CultureInfo.InvariantCulture);
            if (bool.TryParse(s, out bool parsed)) { return parsed; }

            return false;
        }

        private static bool TryParseHandle(string s, out Handle h)
        {
            h = new Handle();

            if (string.IsNullOrWhiteSpace(s)) { return false; }

            string text = s.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }

            if (!long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex))
            {
                return false;
            }

            h = new Handle(hex);
            return true;
        }
    }
}
