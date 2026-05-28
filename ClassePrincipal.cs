/*using Autodesk.Aec.Modeler;
using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System.Text.Json;
using System.Windows.Forms.Design;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Autodesk.AutoCAD.GraphicsSystem;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using System.Collections.Generic;
using CriaProfiles;

namespace AutomacoesCivil3D
{
    public class ClassePrincipal
    {

        [CommandMethod("CriaProfile")]
        public static void CriaProfile()
        {
            //Chama o Formulario de Estilos
            SlEstilos Janela = new SlEstilos();
            //Abre o Formulario
            Janela.ShowDialog();

            //Carrega os documentos
            Document Cad = Manager.DocCad;
            CivilDocument doc = Manager.DocCivil;
            Editor ed = Manager.DocEditor;
            Database db = Manager.DocData;

            //Verifica se o usuario clicou em OK e executa o código
            if (Janela.OK)
            {
                //Seleciona as polylines para criar alinhamentos
                PromptSelectionOptions opt = new PromptSelectionOptions();
                opt.AllowDuplicates = false;
                opt.AllowSubSelections = false; // Impede a seleção de sub-objetos dentro da polyline
                opt.SingleOnly = false; // Permite selecionar múltiplas polylines
                PromptSelectionResult res = ed.GetSelection(opt);

                if (res.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nNenhuma seleção válida. Operação cancelada.");
                    return;
                }

                // Solicita o ponto de inserção dos Profile Views
                PromptPointResult pointResult = ed.GetPoint("\nEspecifique o ponto de inserção dos Profile Views:");
                if (pointResult.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nOperação cancelada pelo usuário.");
                    return;
                }
                Point3d basePoint = pointResult.Value;

                // Definir a distância vertical entre os Profile Views
                double verticalOffset = -250.0; // Ajuste conforme necessário. Este valor define o espaçamento vertical entre os Profile Views.

                //Utiliza uma transação para realizar múltiplas operações
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        List<ObjectId> alignmentIds = new List<ObjectId>();

                        foreach (SelectedObject obj in res.Value)
                        {
                            if (obj.ObjectId.GetObject(OpenMode.ForRead) is Polyline pline)
                            {
                                // Ignora polylines fechadas
                                if (pline.Closed)
                                {
                                    ed.WriteMessage($"\nA Polyline com ObjectId {obj.ObjectId.Handle} está fechada e não pode ser usada para criar um alinhamento.");
                                    continue;
                                }

                                // Cria e configura os PolylineOptions para cada polyline selecionada
                                PolylineOptions plops = new PolylineOptions();
                                plops.AddCurvesBetweenTangents = true;
                                plops.EraseExistingEntities = false; // Decidido manter as polylines originais
                                plops.PlineId = obj.ObjectId;

                                // Define os estilos e cria o alinhamento e profile view
                                string EstiloAlinhamento = Janela.EstiloAlSelecionado;
                                string EstiloLabels = Janela.LabelSelecionado;
                                string Nome = Janela.NomeAlTrp(alignmentIds.Count); // Nome único para cada alinhamento
                                alignmentIds.Add(obj.ObjectId);

                                CriarLayers criarLayers = new CriarLayers();
                                criarLayers.CreateLayer("TRP - ALINHAMENTO");

                                // Criação do Alinhamento
                                ObjectId Alinhamento = Alignment.Create(doc, plops, Nome, "PROJETADO", "TRP - ALINHAMENTO", EstiloAlinhamento, EstiloLabels);

                                if (Alinhamento == ObjectId.Null)
                                {
                                    throw new Exception();
                                }

                                // Calcula o ponto de inserção para cada profile view
                                Point3d pvPoint = new Point3d(basePoint.X, basePoint.Y + (alignmentIds.Count - 1) * verticalOffset, basePoint.Z);

                                // Definição da superficie
                                string Superficie = Janela.SuperficieSelecionada;

                                // Criação do Profile
                                ObjectId profileId = Profile.CreateFromSurface(Nome, doc, Nome, Superficie, "TRP - ALINHAMENTO", "PETRO-PERFIL_TERRENO_NATURAL_01", "VAZIO");

                                // Definição dos estilos para o Profile View
                                ObjectId ProfileBand = Janela.BandSelecionado;
                                ObjectId ProfileStyle = Janela.PVSelecionado;

                                // Criação do Profile View
                                ObjectId ProfileViewId = ProfileView.Create(Alinhamento, pvPoint, Nome, ProfileBand, ProfileStyle);

                                if (ProfileViewId == ObjectId.Null)
                                {
                                    throw new Exception();
                                }

                                ed.WriteMessage($"\nAlinhamento e Profile View criados para a Polyline {obj.ObjectId.Handle}");
                            }
                        }

                        ed.WriteMessage($"\nTotal de Alinhamentos Criados: {alignmentIds.Count}");

                        tr.Commit();
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage($"\nErro ao manipular objetos: {ex.Message}");
                        tr.Abort(); // Desfaz todas as alterações dentro da transação em caso de erro
                    }
                }
            }
        }
    }
}*/
