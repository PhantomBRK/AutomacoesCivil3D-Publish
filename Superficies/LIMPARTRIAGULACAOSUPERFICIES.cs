using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
    public class SuperficieLimpeza
    {
        [Autodesk.AutoCAD.Runtime.CommandMethod("LIMPA_SUP_CURVAS")]
        public void LimparSuperficieCurvas()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            try
            {
                // 1) Selecionar a superfície TIN
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione a Superfície TIN:");
                peo.SetRejectMessage("\nEntidade inválida. Selecione uma superfície TIN.");
                peo.AllowNone = false;
                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                // 2) Selecionar uma ou mais polilinhas fechadas para a borda
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelecione uma ou mais Polilinhas fechadas para a borda (Enter para finalizar):";
                pso.AllowDuplicates = false;

                // Filtro para LWPOLYLINE (polilinha 2D). Se quiser incluir POLYLINE 2D, ajuste o filtro e validação.
                TypedValue[] tv = new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                };
                SelectionFilter filter = new SelectionFilter(tv);
                PromptSelectionResult psr = docEditor.GetSelection(pso, filter);

                if (psr.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nNenhuma polilinha selecionada. Operação cancelada.");
                    return;
                }

                // 3) Perguntar mid-ordinate distance
                PromptDoubleOptions pdo = new PromptDoubleOptions("\nInforme o mid-ordinate distance para aproximar arcos (ex.: 0.20):");
                pdo.DefaultValue = 0.20;
                pdo.AllowNegative = false;
                pdo.AllowZero = false;
                PromptDoubleResult pdr = docEditor.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }
                double midOrd = pdr.Value;

                Database acadDb = civilDoc.Database;

                using (Transaction TransCad = acadDb.TransactionManager.StartTransaction())
                {
                    // Abrir a entidade selecionada e validar se é TinSurface
                    Entity ent = (Entity)TransCad.GetObject(per.ObjectId, OpenMode.ForRead);
                    TinSurface tin = ent as TinSurface;
                    if (tin == null)
                    {
                        // Em alguns casos, a superfície aparece como CivilSurface base. Tentar cast por ObjectId.
                        TinSurface tin2 = (TinSurface)TransCad.GetObject(per.ObjectId, OpenMode.ForWrite) as TinSurface;
                        if (tin2 == null)
                        {
                            docEditor.WriteMessage("\nA entidade selecionada não é uma TinSurface.");
                            return;
                        }
                        tin = tin2;
                    }
                    else
                    {
                        // Reabrir para escrita se necessário
                        if (ent.IsReadEnabled)
                        {
                            ent.UpgradeOpen();
                        }
                    }

                    // Validar polilinhas fechadas e coletar seus ObjectIds
                    ObjectIdCollection boundaryIds = new ObjectIdCollection();
                    SelectionSet sel = psr.Value;

                    foreach (SelectedObject so in sel)
                    {
                        if (so == null || so.ObjectId == ObjectId.Null) continue;

                        Polyline pl = (Polyline)TransCad.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (pl == null)
                        {
                            docEditor.WriteMessage($"\nEntidade {so.ObjectId} não é uma LWPOLYLINE válida.");
                            continue;
                        }

                        if (!pl.Closed)
                        {
                            docEditor.WriteMessage($"\nA polilinha {so.ObjectId} não está fechada. Ignorada.");
                            continue;
                        }

                        boundaryIds.Add(so.ObjectId);
                    }

                    if (boundaryIds.Count == 0)
                    {
                        docEditor.WriteMessage("\nNenhuma polilinha fechada válida selecionada. Operação cancelada.");
                        return;
                    }

                    // 4) Adicionar as bordas como Outer à TinSurface
                    // IMPORTANTE: Assinatura típica (pode variar por versão):
                    // tin.BoundariesDefinition.AddBoundaries(ObjectIdCollection boundaryIds, double midOrdinateDist, SurfaceBoundaryType boundaryType, bool nonDestructive);
                    //
                    // Se a sua versão tiver assinatura diferente, me avise que ajusto.
                    tin.BoundariesDefinition.AddBoundaries(
                        boundaryIds,
                        midOrd,
                        SurfaceBoundaryType.Outer,
                        true // Non-destructive (mantém dados originais). Para corte direto, normalmente funciona bem com Outer.
                    );

                    // 5) Reconstruir a superfície para aplicar a borda
                    tin.Rebuild();

                    TransCad.Commit();
                }

                docEditor.WriteMessage("\nLimpeza concluída: borda adicionada e superfície reconstruída.");
            }
            catch (Exception ex)
            {
                // Exceções de AutoCAD/Civil 3D
                docEditor.WriteMessage($"\nErro da API: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                // Exceções gerais do .NET
                docEditor.WriteMessage($"\nErro: {ex.Message}");
            }
        }
    }
}