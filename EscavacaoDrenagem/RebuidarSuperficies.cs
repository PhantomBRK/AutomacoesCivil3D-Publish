using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Civil.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using System.Collections.Generic;
using Site = Autodesk.Civil.DatabaseServices.Site;
using Autodesk.Civil;
using System;
using System.Security.Cryptography.Pkcs;
using System.Windows.Forms;
using Autodesk.Aec.Modeler;
using Autodesk.AutoCAD.BoundaryRepresentation;
using System.Windows.Forms.Design;
using Microsoft.Office.Interop.Excel;
using Autodesk.AutoCAD.GraphicsSystem;
using System.Security.Policy;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Autodesk.Aec.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace AutomacoesCivil3D
{
    public class RebuidarSuperficies
    {
        [CommandMethod("RebuidSurface")]
        public void RebuidaSuperficies(Transaction tr)
        {
            Document docCad = Manager.DocCad;
            CivilDocument docCivil = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;


            using (tr = docData.TransactionManager.StartTransaction())
            {
                try
                {

                    foreach (ObjectId surfaceId in docCivil.GetSurfaceIds())
                    {
                        Surface superficie = (Surface)tr.GetObject(surfaceId,OpenMode.ForRead);

                        if (superficie.IsVolumeSurface)
                        {
                            TinVolumeSurface superficieVolume = (TinVolumeSurface)superficie;                       
                            superficieVolume.Rebuild();

                        }
                       

                    }


                }
                catch (Exception ex)
                {


                }


            }
        }
    }
}
