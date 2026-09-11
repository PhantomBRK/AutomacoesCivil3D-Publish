using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Comandos do Planejador de Vias — desenho interativo de vias urbanas em planta
    /// com seções-tipo, junções automáticas e quantitativos de planejamento.
    ///
    /// PLANEJADOR_VIAS ........ abre a janela (editor de seções + parâmetros)
    /// PLANVIAS_DESENHAR ...... desenho interativo do eixo com preview da via
    /// PLANVIAS_DE_POLYLINES .. gera vias a partir de polylines existentes
    /// PLANVIAS_QUANTITATIVOS . take-off aproximado das vias (resumo + CSV)
    /// </summary>
    public class PlanejadorViasCommand
    {
        private static PlanejadorViasWindow? _janela;

        [CommandMethod("PLANEJADOR_VIAS")]
        public void AbrirPlanejadorVias()
        {
            Editor editor = Manager.DocEditor;

            try
            {
                if (_janela != null && _janela.IsLoaded)
                {
                    _janela.Activate();
                    return;
                }

                _janela = new PlanejadorViasWindow();
                _janela.Closed += (_, _) => _janela = null;
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(_janela);
                editor.WriteMessage("\nPlanejador de Vias aberto: escolha a seção-tipo e clique em \"Desenhar via\".");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD ao abrir o Planejador de Vias: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro ao abrir o Planejador de Vias: {ex.Message}");
            }
        }

        [CommandMethod("PLANVIAS_DESENHAR")]
        public void DesenharVia()
        {
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                SecaoTipoVia secao = PlanejadorViasEstado.ObterSecaoOuPadrao();
                PlanejadorViasOpcoes opcoes = PlanejadorViasEstado.Opcoes;

                editor.WriteMessage(
                    $"\nDesenhando via \"{secao.Nome}\" (largura total {secao.LarguraTotal:0.00} m). " +
                    "Cruze ou toque eixos existentes do Planejador para formar junções automaticamente.");

                List<Point2d>? pontos = PlanejadorViasDesenho.ColetarPontosInterativo(editor, secao, opcoes.RaioCurvaEixo);
                if (pontos == null)
                {
                    editor.WriteMessage("\nDesenho de via cancelado.");
                    return;
                }

                List<string> avisos = new List<string>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline eixo = PlanejadorViasDesenho.CriarPolylineEixo(pontos, opcoes.RaioCurvaEixo);
                    if (eixo.NumberOfVertices < 2)
                    {
                        eixo.Dispose();
                        editor.WriteMessage("\nPontos insuficientes para gerar o eixo.");
                        return;
                    }

                    ViaGerada via = PlanejadorViasDesenho.GerarVia(tr, db, eixo, secao);
                    avisos.AddRange(via.Avisos);

                    if (opcoes.FormarJuncoes)
                    {
                        avisos.AddRange(PlanejadorViasJuncoes.ProcessarJuncoesDaVia(tr, db, via.Id, opcoes.RaioMeioFio));
                    }

                    tr.Commit();
                }

                editor.WriteMessage("\nVia criada com sucesso.");
                RelatarAvisos(editor, avisos);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD ao desenhar a via: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro ao desenhar a via: {ex.Message}");
            }
        }

        [CommandMethod("PLANVIAS_DE_POLYLINES")]
        public void ViasDePolylines()
        {
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                SecaoTipoVia secao = PlanejadorViasEstado.ObterSecaoOuPadrao();
                PlanejadorViasOpcoes opcoes = PlanejadorViasEstado.Opcoes;

                PromptSelectionOptions opcoesSelecao = new PromptSelectionOptions
                {
                    MessageForAdding = $"\nSelecione as polylines dos eixos (seção \"{secao.Nome}\"): "
                };
                SelectionFilter filtro = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                });

                PromptSelectionResult selecao = editor.GetSelection(opcoesSelecao, filtro);
                if (selecao.Status != PromptStatus.OK || selecao.Value == null || selecao.Value.Count == 0)
                {
                    editor.WriteMessage("\nNenhuma polyline selecionada.");
                    return;
                }

                int criadas = 0;
                List<string> avisos = new List<string>();

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject item in selecao.Value)
                    {
                        Polyline? origem = tr.GetObject(item.ObjectId, OpenMode.ForRead, false) as Polyline;
                        if (origem == null || origem.IsErased)
                        {
                            continue;
                        }

                        // Não regenerar entidades que já pertencem ao Planejador.
                        if (PlanejadorViasDesenho.LerXData(origem) != null)
                        {
                            avisos.Add("Polyline ignorada: já pertence a uma via do Planejador.");
                            continue;
                        }

                        Polyline eixo = (Polyline)origem.Clone();
                        ViaGerada via = PlanejadorViasDesenho.GerarVia(tr, db, eixo, secao);
                        avisos.AddRange(via.Avisos);
                        criadas++;

                        if (opcoes.FormarJuncoes)
                        {
                            avisos.AddRange(PlanejadorViasJuncoes.ProcessarJuncoesDaVia(tr, db, via.Id, opcoes.RaioMeioFio));
                        }
                    }

                    tr.Commit();
                }

                editor.WriteMessage($"\n{criadas} via(s) gerada(s) a partir de polylines. As polylines originais foram mantidas.");
                RelatarAvisos(editor, avisos);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD ao gerar vias de polylines: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro ao gerar vias de polylines: {ex.Message}");
            }
        }

        [CommandMethod("PLANVIAS_QUANTITATIVOS")]
        public void Quantitativos()
        {
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                List<QuantitativoVia> quantitativos;
                List<string> avisos;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    quantitativos = PlanejadorViasQuantitativos.Calcular(tr, db, out avisos);
                    tr.Commit();
                }

                string resumo = PlanejadorViasQuantitativos.MontarResumo(quantitativos);
                editor.WriteMessage("\n" + resumo.Replace("\r\n", "\n"));
                RelatarAvisos(editor, avisos);
                AcadApp.ShowAlertDialog(resumo);

                if (quantitativos.Count == 0)
                {
                    return;
                }

                SaveFileDialog dialogo = new SaveFileDialog
                {
                    Title = "Salvar quantitativos das vias (CSV)",
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = "Quantitativos_PlanejadorVias.csv",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dialogo.ShowDialog() == true)
                {
                    string csv = PlanejadorViasQuantitativos.MontarCsv(quantitativos);
                    File.WriteAllText(dialogo.FileName, csv, new UTF8Encoding(true));
                    editor.WriteMessage($"\nCSV salvo em: {dialogo.FileName}");
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD nos quantitativos: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro nos quantitativos: {ex.Message}");
            }
        }

        private static void RelatarAvisos(Editor editor, List<string> avisos)
        {
            foreach (string aviso in avisos.Distinct().Take(12))
            {
                editor.WriteMessage($"\n  Aviso: {aviso}");
            }

            if (avisos.Count > 12)
            {
                editor.WriteMessage($"\n  (+{avisos.Count - 12} avisos omitidos)");
            }
        }
    }
}
