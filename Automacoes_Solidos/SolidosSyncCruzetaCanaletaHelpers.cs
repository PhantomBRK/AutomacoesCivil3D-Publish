using Autodesk.AutoCAD.DatabaseServices;

namespace AutomacoesCivil3D
{
    public static class SolidosSyncCruzetaCanaletaHelpers
    {

        public static int ConnectorToPortIndex(string connectorId)
        {
            if (string.IsNullOrWhiteSpace(connectorId)) { return 0; }

            string s = connectorId.Trim();
            if (s.Equals("ConnectorP1", StringComparison.OrdinalIgnoreCase)) { return 1; }
            if (s.Equals("ConnectorP2", StringComparison.OrdinalIgnoreCase)) { return 2; }
            if (s.Equals("ConnectorP3", StringComparison.OrdinalIgnoreCase)) { return 3; }
            if (s.Equals("ConnectorP4", StringComparison.OrdinalIgnoreCase)) { return 4; }

            return 0;
        }

        public static bool IsCruzeta(SolidosInterop sol, ObjectId id)
        {
            // Cruzeta “de canaleta”: parâmetros AlturaInicialP1..P4 (você disse que vai deixar isso nela)
            if (!sol.HasProperty(id, "AlturaInicialP1")) { return false; }
            if (!sol.HasProperty(id, "AlturaInicialP2")) { return false; }
            if (!sol.HasProperty(id, "AlturaInicialP3")) { return false; }
            if (!sol.HasProperty(id, "AlturaInicialP4")) { return false; }
            return true;
        }

        public static bool TryBuildCanaletaInfo(SolidosInterop sol, ObjectId id, out CanaletaInfo info)
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
    }
}