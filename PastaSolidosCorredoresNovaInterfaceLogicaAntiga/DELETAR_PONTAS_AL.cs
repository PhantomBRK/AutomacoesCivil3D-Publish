using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using DocumentFormat.OpenXml.Office2010.Excel;
using System;
using System.Collections.Generic;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using AutomacoesCivil3D;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    public class AlignmentTrimCommands
    {
        [CommandMethod("DELETAR_PONTAS_AL")]
        public void DeleteAlignmentEntitiesOutsideStations()
        {
            try
            {
                Document civilDoc = Manager.DocCad;
                CivilDocument civilDb = Manager.DocCivil;
                Editor docEditor = Manager.DocEditor;
                Database db = civilDoc.Database;

                // Selecionar o alinhamento
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione um Alignment:");
                peo.AllowNone = false;
                peo.SetRejectMessage("\nObjeto inválido. Selecione um Alignment.\n");
                peo.AddAllowedClass(typeof(Alignment), exactMatch: true);
                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                using (Transaction TransCad = db.TransactionManager.StartTransaction())
                {
                    Alignment aln = (Alignment)TransCad.GetObject(per.ObjectId, OpenMode.ForWrite);
                    if (aln == null)
                    {
                        docEditor.WriteMessage("\nFalha ao abrir o Alignment para escrita.");
                        return;
                    }

                    // Ler intervalo de estacas (em unidades de desenho, double)
                    double staStart = PromptForStation(docEditor, $"Estaca inicial (valor numérico). Ex.: {aln.StartingStation:F2}", aln.StartingStation);
                    if (double.IsNaN(staStart)) return;

                    double staEnd = PromptForStation(docEditor, $"Estaca final (valor numérico). Ex.: {aln.EndingStation:F2}", aln.EndingStation);
                    if (double.IsNaN(staEnd)) return;

                    if (staEnd < staStart)
                    {
                        // Normalizar: inverter se usuário passou invertido
                        double tmp = staStart;
                        staStart = staEnd;
                        staEnd = tmp;
                    }

                    // Tolerância para comparação de duplos
                    double eps = 1e-6;

                    /*AlignmentEntityCollection ents = aln.Entities;
                    if (ents == null || ents.Count == 0)
                    {
                        docEditor.WriteMessage("\nAlignment não possui subentidades.");
                        TransCad.Commit();
                        return;
                    }*/

                    // 1) Coletar IDs das subentidades a remover (fora do intervalo)
                    List<int> idsParaRemover = new List<int>();

                
                        AlignmentEntityCollection ent = aln.Entities ;
                    int contador = 0;   
                    


                    
                    AlignmentEntity eSta0 = ent.EntityAtStation(staStart);
                    AlignmentEntity eSta1 = ent.EntityAtStation(staEnd);

                        int estacaInicial = eSta0.EntityId;
                        int estacaFinal = eSta1.EntityId;
                    int entidadeInicial = ent.FirstEntity;
                    int entidadeFinal = ent.LastEntity;
                
                 

                        // Garantir ordem das estacas da subentidade
                        if (estacaFinal < estacaInicial)
                        {
                            int t = estacaInicial;
                            estacaInicial = estacaFinal;
                            estacaFinal = t;
                        }

                    for (double i = aln.StartingStation; i < staStart; i+=10)
                    {
                        if (ent.EntityAtStation(i) == null) 
                                continue;

                        
                        ent.Remove(ent.EntityAtStation(i));
                    }

                    for (double i = staEnd; i < aln.EndingStation; i+=10)
                    {
                            if (ent.EntityAtStation(i) == null)
                                continue;
                            ent.Remove(ent.EntityAtStation(i));
                    }


                    /*foreach (AlignmentEntity id in ent)
                    {
                        if(id.EntityId < estacaInicial)
                        {
                            idsParaRemover.Add(id.EntityId);
                            docEditor.WriteMessage($"\n Index {id.EntityId} entidade removida");
                            contador++;

                       

                        }


                        


                    }
                    */


                    // 2) Remover subentidades fora do intervalo
                    /*int removidos = 0;
                    foreach (int id in idsParaRemover)
                    {
                        // Dependendo da sua assembly, a assinatura pode ser:
                        // ents.Remove(id);        // por EntityId (mais comum)
                        // ou ents.RemoveAt(index); // por índice
                        // Aqui uso por EntityId:
                        if(ent.EntityAtId(id) == null) continue;

                        ent.Remove(ent.EntityAtId(id));
                       
                        removidos++;
                    }
                    
                    docEditor.WriteMessage($"\nSubentidades removidas: {removidos}. Mantidas (dentro ou cruzando o intervalo): {ent.Count}.");

                    */                                                                                                                       


                    TransCad.Commit();
                }
            }
            catch (Exception ex)
            {
                Editor docEditor = Manager.DocEditor;
                docEditor.WriteMessage($"\nErro (AutoCAD.Runtime): {ex.Message}");
            }
            catch (System.Exception ex)
            {
                Editor docEditor = Manager.DocEditor;
                docEditor.WriteMessage($"\nErro: {ex.Message}");
            }
        }

        private double PromptForStation(Editor ed, string msg, double defaultValue)
        {
            PromptDoubleOptions pdo = new PromptDoubleOptions($"\n{msg}");
            pdo.AllowNone = true;
            pdo.DefaultValue = defaultValue;
            PromptDoubleResult pdr = ed.GetDouble(pdo);
            if (pdr.Status == PromptStatus.OK) return pdr.Value;
            if (pdr.Status == PromptStatus.None) return defaultValue;
            return double.NaN;
        }
    }
}

