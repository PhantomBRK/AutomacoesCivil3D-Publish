using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
        public class OsnapPresets
        {
            // Bitcodes (OSMODE)
            private const int OSMODE_ENDPOINT = 1;
            private const int OSMODE_MIDPOINT = 2;
            private const int OSMODE_CENTER = 4;
            private const int OSMODE_NODE = 8;
            private const int OSMODE_PERPENDICULAR = 128;
            private const int OSMODE_NEAREST = 512;
            private const int OSMODE_APPARENT_INTERSECTION = 2048;
            private const int OSMODE_EXTENSION = 4096;

        // "Connector" não aparece na tabela padrão do OSMODE; em algumas instalações ele vem como bit extra.
        // Chute mais comum é 32768. Se no seu não bater, veja a observação abaixo.
        private const int OSMODE_CONNECTOR = 0;           

            // Imagem 1: Node + Connector
            private const int SNAP_IMAGEM_1 = OSMODE_CONNECTOR;

            // Imagem 2: Endpoint + Midpoint + Center + Extension + Perpendicular + Nearest + Apparent Intersection
            private const int SNAP_IMAGEM_2 =
                OSMODE_ENDPOINT +
                OSMODE_MIDPOINT +
                OSMODE_CENTER +
                OSMODE_EXTENSION +
                OSMODE_PERPENDICULAR +
                OSMODE_NEAREST +
                OSMODE_APPARENT_INTERSECTION; // = 6791

            [CommandMethod("OS1")]
            public void AtivarSnapsImagem1()
            {
                Editor docEditor = Manager.DocEditor;

                try
                {
                    Application.SetSystemVariable("OSMODE", SNAP_IMAGEM_1);
                    docEditor.WriteMessage("\nOSNAP set: Node + Connector.");
                }
                catch (Exception ex)
                {
                    docEditor.WriteMessage("\nErro ao ajustar OSMODE: " + ex.Message);
                }
            }

            [CommandMethod("OS2")]
            public void AtivarSnapsImagem2()
            {
                Editor docEditor = Manager.DocEditor;

                try
                {
                    Application.SetSystemVariable("OSMODE", SNAP_IMAGEM_2);
                    docEditor.WriteMessage("\nOSNAP set: Endpoint, Midpoint, Center, Extension, Perp, Nearest");
                }
                catch (Exception ex)
                {
                    docEditor.WriteMessage("\nErro ao ajustar OSMODE: " + ex.Message);
                }
            }
        } 
}
