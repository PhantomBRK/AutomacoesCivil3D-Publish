using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

using AutomacoesCivil3D;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    public class AlignmentSubentitiesTools
    {
        [Autodesk.AutoCAD.Runtime.CommandMethod("DELETEENTITIES")]
        public static void DeleteAlignmentSubentitiesOutsideStationRange()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            try
            {
                // 1) Selecionar um Alignment
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione um Alignment:");
                peo.SetRejectMessage("\nEntidade inválida. Selecione um Alignment.");
                peo.AddAllowedClass(typeof(Alignment), exactMatch: true);

                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                // 2) Ler estacas inicial e final
                double staIni = ReadDouble(docEditor, "\nInforme a estaca inicial (double): ");
                if (double.IsNaN(staIni))
                {
                    docEditor.WriteMessage("\nValor inválido. Cancelado.");
                    return;
                }

                double staFim = ReadDouble(docEditor, "\nInforme a estaca final (double): ");
                if (double.IsNaN(staFim))
                {
                    docEditor.WriteMessage("\nValor inválido. Cancelado.");
                    return;
                }

                if (staFim < staIni)
                {
                    // Normalizar
                    double aux = staIni;
                    staIni = staFim;
                    staFim = aux;
                }

                using (DocumentLock docLock = civilDoc.LockDocument())
                {
                    using (Transaction TransCad = civilDoc.TransactionManager.StartTransaction())
                    {
                        Alignment align = (Alignment)TransCad.GetObject(per.ObjectId, OpenMode.ForWrite);

                        // 3) Coletar as subentidades como AlignmentEntity (NÃO use Entity genérica)
                        AlignmentEntityCollection entCol = align.Entities;

                        int count = entCol.Count;
                        if (count == 0)
                        {
                            docEditor.WriteMessage("\nO Alignment não possui subentidades.");
                            TransCad.Commit();
                            return;
                        }

                        // 4) Montar tabela com índice e faixa de estacas por subentidade
                        //    Obs.: StartStation/EndStation pertencem a AlignmentEntity
                        List<(int Index, double Sta0, double Sta1)> map = new List<(int, double, double)>(capacity: count);

                        for (int i = 0; i < count; i++)
                        {
                            AlignmentEntity aent = entCol[i]; // Garante o tipo correto
                            double eSta0 = entCol.EntityAtStation(staIni).EntityId;
                            double eSta1 = entCol.EntityAtStation(staFim).EntityId;

                            // Normalizar para Sta0 <= Sta1
                            if (eSta1 < eSta0)
                            {
                                double t = eSta0;
                                eSta0 = eSta1;
                                eSta1 = t;
                            }

                            map.Add((i, eSta0, eSta1));
                        }

                        // 5) Determinar quem apaga: inteiramente antes ou inteiramente depois do intervalo
                        List<int> toDeleteIdx = new List<int>();

                        foreach (var row in map)
                        {
                            bool entirelyBefore = row.Sta1 < staIni;
                            bool entirelyAfter = row.Sta0 > staFim;

                            if (entirelyBefore || entirelyAfter)
                                toDeleteIdx.Add(row.Index);
                        }

                        if (toDeleteIdx.Count == 0)
                        {
                            docEditor.WriteMessage("\nNão há subentidades inteiramente fora do intervalo. Nada a apagar.");
                            TransCad.Commit();
                            return;
                        }

                        // 6) Remover em ordem decrescente de índice para evitar 'shift' da coleção
                        toDeleteIdx.Sort((a, b) => b.CompareTo(a));

                        int removed = 0;

                        foreach (int idx in toDeleteIdx)
                        {
                            // Dependendo da sua build, você pode ter:
                            // a) entCol.RemoveAt(int index)
                            // b) entCol.Remove(AlignmentEntity entity)
                            // Ajuste o bloco abaixo conforme sua coleção expõe.

                            // Opção A: RemoveAt (comum)
                            entCol.RemoveAt(idx);
                            removed++;

                            // Opção B (se existente na sua build): 
                            // AlignmentEntity aentRem = entCol[idx]; // Cuidado: se for usar isso, pegue antes de remover
                            // entCol.Remove(aentRem);
                            // removed++;
                        }

                        docEditor.WriteMessage($"\nSubentidades removidas: {removed}.");

                        TransCad.Commit();
                    }
                }
            }
            catch (Exception exAcad)
            {
                docEditor.WriteMessage($"\nErro (AutoCAD): {exAcad.Message}");
            }
            catch (System.Exception ex)
            {
                Editor localEditor = Application.DocumentManager.MdiActiveDocument.Editor;
                localEditor.WriteMessage($"\nErro: {ex.Message}");
            }
        }

        private static double ReadDouble(Editor ed, string prompt)
        {
            PromptDoubleOptions pdo = new PromptDoubleOptions(prompt);
            pdo.AllowNone = false;

            PromptDoubleResult pdr = ed.GetDouble(pdo);
            if (pdr.Status != PromptStatus.OK)
                return double.NaN;

            return pdr.Value;
        }
    }
}

