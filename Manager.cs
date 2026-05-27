using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace AutomacoesCivil3D  
{
    public static class Manager
    {

        public static Document DocCad
        {
            get
            {
                return Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;

            }
        }
        public static Editor DocEditor
        {
            get
            {
                return Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;

            }

        }

        public static Database DocData
        {
            get
            {
                return Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Database;
            }
        }

        public static CivilDocument DocCivil
        {
            get
            {
                return CivilApplication.ActiveDocument;

            }
        }

       

    }


}