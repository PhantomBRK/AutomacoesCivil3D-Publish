using System;
using System.Collections.Generic;

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

namespace AutomacoesCivil3D.AUTOMAÇÕES_PLUG_IN_SOLIDOS
{
    public class SolidosAjustarSaidaCaixa
    {
       
        [CommandMethod("COMMIT")]
        public void AjustarSaidaParaMenorEntrada()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            try
            {
                

                SolidosAPI.DocCommit();

                
            }
            catch (SOLIDOS.SolidosException solEx)
            {
                docEditor.WriteMessage($"\n[SOLIDOS] {solEx.Message}");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\n[ERRO] {ex.Message}");
            }
        }

       
    }
}
