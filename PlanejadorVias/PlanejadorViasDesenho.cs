using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Informações gravadas em XData nas entidades criadas pelo Planejador de Vias.
    /// </summary>
    public sealed class InfoXDataVia
    {
        public string Papel { get; set; } = string.Empty;
        public Guid ViaId { get; set; }
        public string Secao { get; set; } = string.Empty;
        public double Offset { get; set; }
        public double WEsq { get; set; }
        public double WDir { get; set; }
        public double LarguraTotal { get; set; }
    }

    /// <summary>
    /// Resultado da geração de uma via em planta.
    /// </summary>
    public sealed class ViaGerada
    {
        public Guid Id { get; set; }
        public ObjectId EixoId { get; set; }
        public List<ObjectId> Limites { get; } = new List<ObjectId>();
        public List<string> Avisos { get; } = new List<string>();
    }

    /// <summary>
    /// Geração do desenho em planta de uma via (eixo com concordâncias + linhas de
    /// limite por offset, camadas, XData e grupo). Coração do Planejador de Vias.
    /// </summary>
    public static class PlanejadorViasDesenho
    {
        public const string RegApp = "AUTOMACOES_PLANVIAS";
        public const string PapelEixo = "EIXO";
        public const string PapelLimite = "LIMITE";
        public const string PapelJuncao = "JUNCAO";
        public const string PrefixoCamada = "PLANVIAS_";

        private const double ToleranciaPonto = 1e-6;

        // ------------------------------------------------------------------
        // Camadas e RegApp
        // ------------------------------------------------------------------

        public static void RegistrarRegApp(Transaction tr, Database db)
        {
            RegAppTable tabela = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (tabela.Has(RegApp))
            {
                return;
            }

            tabela.UpgradeOpen();
            RegAppTableRecord registro = new RegAppTableRecord { Name = RegApp };
            tabela.Add(registro);
            tr.AddNewlyCreatedDBObject(registro, true);
        }

        public static ObjectId GarantirCamada(Transaction tr, Database db, string nome, short corAci, bool tracoCentro = false)
        {
            LayerTable tabela = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (tabela.Has(nome))
            {
                return tabela[nome];
            }

            tabela.UpgradeOpen();
            LayerTableRecord camada = new LayerTableRecord
            {
                Name = nome,
                Color = Color.FromColorIndex(ColorMethod.ByAci, corAci)
            };

            if (tracoCentro)
            {
                ObjectId linetypeId = TentarObterLinetypeCenter(tr, db);
                if (!linetypeId.IsNull)
                {
                    camada.LinetypeObjectId = linetypeId;
                }
            }

            ObjectId id = tabela.Add(camada);
            tr.AddNewlyCreatedDBObject(camada, true);
            return id;
        }

        private static ObjectId TentarObterLinetypeCenter(Transaction tr, Database db)
        {
            try
            {
                LinetypeTable tabela = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (tabela.Has("CENTER"))
                {
                    return tabela["CENTER"];
                }

                try
                {
                    db.LoadLineTypeFile("CENTER", "acad.lin");
                }
                catch
                {
                    try
                    {
                        db.LoadLineTypeFile("CENTER", "acadiso.lin");
                    }
                    catch
                    {
                        // Sem arquivo de linetype disponível: segue com contínua.
                    }
                }

                tabela = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (tabela.Has("CENTER"))
                {
                    return tabela["CENTER"];
                }
            }
            catch
            {
            }

            return ObjectId.Null;
        }

        public static string CamadaDoEixo()
        {
            return PrefixoCamada + "EIXO";
        }

        public static string CamadaDoLimite(TipoElementoVia tipo)
        {
            return PrefixoCamada + TipoElementoViaInfo.SufixoCamada(tipo);
        }

        // ------------------------------------------------------------------
        // Construção do eixo com concordâncias
        // ------------------------------------------------------------------

        public static Polyline CriarPolylineEixo(IList<Point2d> pontosBrutos, double raioConcordancia)
        {
            List<Point2d> pontos = new List<Point2d>();
            foreach (Point2d ponto in pontosBrutos)
            {
                if (pontos.Count == 0 || pontos[pontos.Count - 1].GetDistanceTo(ponto) > 0.01)
                {
                    pontos.Add(ponto);
                }
            }

            Polyline eixo = new Polyline();
            if (pontos.Count == 0)
            {
                return eixo;
            }

            int indice = 0;
            eixo.AddVertexAt(indice++, pontos[0], 0.0, 0.0, 0.0);

            for (int i = 1; i < pontos.Count - 1; i++)
            {
                ConcordanciaVertice conc = raioConcordancia > 1e-6
                    ? PlanejadorViasGeometria.ConcordarVertice(pontos[i - 1], pontos[i], pontos[i + 1], raioConcordancia)
                    : new ConcordanciaVertice { Possivel = false };

                if (conc.Possivel)
                {
                    eixo.AddVertexAt(indice++, conc.TangenteEntrada, conc.Bulge, 0.0, 0.0);
                    eixo.AddVertexAt(indice++, conc.TangenteSaida, 0.0, 0.0, 0.0);
                }
                else
                {
                    eixo.AddVertexAt(indice++, pontos[i], 0.0, 0.0, 0.0);
                }
            }

            if (pontos.Count > 1)
            {
                eixo.AddVertexAt(indice, pontos[pontos.Count - 1], 0.0, 0.0, 0.0);
            }

            return eixo;
        }

        // ------------------------------------------------------------------
        // Offsets assinados (positivo = esquerda do sentido de caminhamento)
        // ------------------------------------------------------------------

        /// <summary>
        /// Descobre empiricamente se GetOffsetCurves(+d) desloca para a esquerda (+1) ou
        /// direita (-1) — a convenção da API depende da orientação da curva.
        /// </summary>
        public static double DescobrirSinalOffsetEsquerda(Polyline eixo)
        {
            try
            {
                double d = 0.05;
                Vector3d tangente = eixo.GetFirstDerivative(eixo.StartParam);
                if (tangente.Length < ToleranciaPonto)
                {
                    return 1.0;
                }

                tangente = tangente.GetNormal();
                Vector3d esquerda = new Vector3d(-tangente.Y, tangente.X, 0.0);
                Point3d esperadoEsquerda = eixo.StartPoint + esquerda * d;
                Point3d esperadoDireita = eixo.StartPoint - esquerda * d;

                DBObjectCollection colecao = eixo.GetOffsetCurves(d);
                try
                {
                    if (colecao.Count == 0)
                    {
                        return 1.0;
                    }

                    Curve? curva = colecao[0] as Curve;
                    if (curva == null)
                    {
                        return 1.0;
                    }

                    Point3d inicio = curva.StartPoint;
                    return inicio.DistanceTo(esperadoEsquerda) <= inicio.DistanceTo(esperadoDireita) ? 1.0 : -1.0;
                }
                finally
                {
                    foreach (DBObject obj in colecao)
                    {
                        obj.Dispose();
                    }
                }
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>
        /// Curvas paralelas ao eixo no offset assinado informado (esquerda positiva).
        /// As curvas retornadas NÃO estão no banco (caller decide adicionar/descartar).
        /// </summary>
        public static List<Curve> ObterOffsetAssinado(Polyline eixo, double offsetAssinado, double sinalEsquerda)
        {
            List<Curve> resultado = new List<Curve>();
            double magnitude = Math.Abs(offsetAssinado);
            if (magnitude < ToleranciaPonto)
            {
                return resultado;
            }

            double valorApi = (offsetAssinado > 0 ? sinalEsquerda : -sinalEsquerda) * magnitude;
            DBObjectCollection colecao = eixo.GetOffsetCurves(valorApi);
            foreach (DBObject obj in colecao)
            {
                if (obj is Curve curva)
                {
                    resultado.Add(curva);
                }
                else
                {
                    obj.Dispose();
                }
            }

            return resultado;
        }

        // ------------------------------------------------------------------
        // XData
        // ------------------------------------------------------------------

        public static void AplicarXDataEixo(Entity entidade, Guid viaId, SecaoTipoVia secao)
        {
            entidade.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, PapelEixo),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, viaId.ToString("N")),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, secao.Nome ?? string.Empty),
                new TypedValue((int)DxfCode.ExtendedDataReal, secao.MeiaLarguraPavimento(true)),
                new TypedValue((int)DxfCode.ExtendedDataReal, secao.MeiaLarguraPavimento(false)),
                new TypedValue((int)DxfCode.ExtendedDataReal, secao.LarguraTotal));
        }

        public static void AplicarXDataLimite(Entity entidade, Guid viaId, double offsetAssinado)
        {
            entidade.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, PapelLimite),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, viaId.ToString("N")),
                new TypedValue((int)DxfCode.ExtendedDataReal, offsetAssinado));
        }

        public static void AplicarXDataJuncao(Entity entidade, Guid viaIdA, Guid viaIdB)
        {
            entidade.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, PapelJuncao),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, viaIdA.ToString("N")),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, viaIdB.ToString("N")));
        }

        public static InfoXDataVia? LerXData(Entity entidade)
        {
            ResultBuffer? rb = entidade.GetXDataForApplication(RegApp);
            if (rb == null)
            {
                return null;
            }

            try
            {
                List<string> textos = new List<string>();
                List<double> reais = new List<double>();
                foreach (TypedValue valor in rb)
                {
                    if (valor.TypeCode == (int)DxfCode.ExtendedDataAsciiString && valor.Value is string s)
                    {
                        textos.Add(s);
                    }
                    else if (valor.TypeCode == (int)DxfCode.ExtendedDataReal && valor.Value is double d)
                    {
                        reais.Add(d);
                    }
                }

                if (textos.Count < 2)
                {
                    return null;
                }

                InfoXDataVia info = new InfoXDataVia { Papel = textos[0] };
                Guid viaId;
                if (Guid.TryParseExact(textos[1], "N", out viaId))
                {
                    info.ViaId = viaId;
                }

                if (info.Papel == PapelEixo)
                {
                    info.Secao = textos.Count > 2 ? textos[2] : string.Empty;
                    info.WEsq = reais.Count > 0 ? reais[0] : 0.0;
                    info.WDir = reais.Count > 1 ? reais[1] : 0.0;
                    info.LarguraTotal = reais.Count > 2 ? reais[2] : 0.0;
                }
                else if (info.Papel == PapelLimite)
                {
                    info.Offset = reais.Count > 0 ? reais[0] : 0.0;
                }

                return info;
            }
            finally
            {
                rb.Dispose();
            }
        }

        /// <summary>
        /// Copia camada, cor, linetype e XData para pedaços gerados por recorte.
        /// </summary>
        public static void CopiarAparenciaEXData(Entity origem, Entity destino)
        {
            destino.LayerId = origem.LayerId;
            destino.Color = origem.Color;
            destino.LinetypeId = origem.LinetypeId;
            destino.LinetypeScale = origem.LinetypeScale;

            ResultBuffer? rb = origem.GetXDataForApplication(RegApp);
            if (rb != null)
            {
                using (rb)
                {
                    destino.XData = rb;
                }
            }
        }

        // ------------------------------------------------------------------
        // Geração da via completa
        // ------------------------------------------------------------------

        /// <summary>
        /// Adiciona ao Model Space o eixo informado (assumindo posse da entidade) e as
        /// linhas de limite da seção-tipo, com camadas, XData e grupo.
        /// </summary>
        public static ViaGerada GerarVia(Transaction tr, Database db, Polyline eixo, SecaoTipoVia secao)
        {
            RegistrarRegApp(tr, db);

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            ViaGerada via = new ViaGerada { Id = Guid.NewGuid() };

            ObjectId camadaEixoId = GarantirCamada(tr, db, CamadaDoEixo(), 3, tracoCentro: true);
            eixo.SetDatabaseDefaults(db);
            eixo.LayerId = camadaEixoId;
            via.EixoId = modelSpace.AppendEntity(eixo);
            tr.AddNewlyCreatedDBObject(eixo, true);
            AplicarXDataEixo(eixo, via.Id, secao);

            double sinalEsquerda = DescobrirSinalOffsetEsquerda(eixo);

            List<ObjectId> idsDoGrupo = new List<ObjectId> { via.EixoId };
            HashSet<long> offsetsJaCriados = new HashSet<long>();

            foreach (LimiteSecaoVia limite in secao.ObterLimites())
            {
                // Divisas entre duas faixas de rolamento não são desenhadas em planta.
                if (limite.ElementoInterno == TipoElementoVia.Faixa && limite.ElementoExterno == TipoElementoVia.Faixa)
                {
                    continue;
                }

                // Evita duplicar limites coincidentes (elementos de largura zero).
                long chave = (long)Math.Round(limite.Offset * 1e6);
                if (!offsetsJaCriados.Add(chave))
                {
                    continue;
                }

                if (Math.Abs(limite.Offset) < ToleranciaPonto)
                {
                    continue; // coincide com o eixo
                }

                TipoElementoVia tipoCamada = limite.TipoParaCamada;
                ObjectId camadaId = GarantirCamada(tr, db, CamadaDoLimite(tipoCamada), TipoElementoViaInfo.CorAci(tipoCamada));

                List<Curve> curvas = ObterOffsetAssinado(eixo, limite.Offset, sinalEsquerda);
                if (curvas.Count == 0)
                {
                    via.Avisos.Add($"Não foi possível gerar o offset {limite.Offset:0.00} m (geometria muito fechada).");
                    continue;
                }

                foreach (Curve curva in curvas)
                {
                    curva.SetDatabaseDefaults(db);
                    curva.LayerId = camadaId;
                    ObjectId idCurva = modelSpace.AppendEntity(curva);
                    tr.AddNewlyCreatedDBObject(curva, true);
                    AplicarXDataLimite(curva, via.Id, limite.Offset);
                    via.Limites.Add(idCurva);
                    idsDoGrupo.Add(idCurva);
                }
            }

            CriarGrupo(tr, db, via.Id, idsDoGrupo);
            return via;
        }

        private static void CriarGrupo(Transaction tr, Database db, Guid viaId, List<ObjectId> ids)
        {
            try
            {
                DBDictionary dicionario = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                string nome = "PLANVIA_" + viaId.ToString("N").Substring(0, 8).ToUpperInvariant();
                if (dicionario.Contains(nome))
                {
                    nome += "_" + (DateTime.Now.Ticks % 10000);
                }

                Group grupo = new Group("Via do Planejador de Vias", true);
                dicionario.SetAt(nome, grupo);
                tr.AddNewlyCreatedDBObject(grupo, true);

                ObjectIdCollection colecao = new ObjectIdCollection();
                foreach (ObjectId id in ids)
                {
                    colecao.Add(id);
                }

                grupo.Append(colecao);
            }
            catch
            {
                // Grupo é conveniência de seleção; falha aqui não invalida a via.
            }
        }

        // ------------------------------------------------------------------
        // Coleta interativa de pontos com preview ao vivo (jig)
        // ------------------------------------------------------------------

        /// <summary>
        /// Coleta os pontos do eixo com preview da via (bordas de pavimento e bordas
        /// externas) acompanhando o cursor. Retorna null se o usuário cancelar.
        /// </summary>
        public static List<Point2d>? ColetarPontosInterativo(Editor editor, SecaoTipoVia secao, double raioConcordancia)
        {
            PromptPointOptions opcoesInicio = new PromptPointOptions("\nPonto inicial da via: ");
            opcoesInicio.AllowNone = false;
            PromptPointResult resultadoInicio = editor.GetPoint(opcoesInicio);
            if (resultadoInicio.Status != PromptStatus.OK)
            {
                return null;
            }

            List<Point2d> pontos = new List<Point2d>
            {
                new Point2d(resultadoInicio.Value.X, resultadoInicio.Value.Y)
            };

            while (true)
            {
                ViaJig jig = new ViaJig(pontos, secao, raioConcordancia);
                PromptResult resultado = editor.Drag(jig);

                if (resultado.Status == PromptStatus.OK)
                {
                    Point2d novo = new Point2d(jig.PontoAtual.X, jig.PontoAtual.Y);
                    if (pontos[pontos.Count - 1].GetDistanceTo(novo) < 0.01)
                    {
                        editor.WriteMessage("\nPonto coincidente ignorado.");
                        continue;
                    }

                    pontos.Add(novo);
                    continue;
                }

                if (resultado.Status == PromptStatus.Keyword)
                {
                    if (resultado.StringResult == "Desfazer")
                    {
                        if (pontos.Count > 1)
                        {
                            pontos.RemoveAt(pontos.Count - 1);
                            editor.WriteMessage("\nÚltimo ponto removido.");
                        }
                        else
                        {
                            editor.WriteMessage("\nNada a desfazer.");
                        }

                        continue;
                    }

                    if (resultado.StringResult == "Concluir")
                    {
                        break;
                    }

                    continue;
                }

                if (resultado.Status == PromptStatus.None)
                {
                    break; // Enter conclui
                }

                return null; // Esc cancela
            }

            if (pontos.Count < 2)
            {
                editor.WriteMessage("\nSão necessários ao menos 2 pontos para desenhar a via.");
                return null;
            }

            return pontos;
        }
    }

    /// <summary>
    /// Jig que mostra o eixo e as bordas da via acompanhando o cursor
    /// (preview equivalente ao "Draw road" do planejador de referência).
    /// </summary>
    public class ViaJig : DrawJig
    {
        private readonly List<Point2d> _pontos;
        private readonly double _raioConcordancia;
        private readonly List<(double Offset, short Cor)> _offsetsPreview;
        private Point3d _atual;

        public Point3d PontoAtual
        {
            get { return _atual; }
        }

        public ViaJig(List<Point2d> pontos, SecaoTipoVia secao, double raioConcordancia)
        {
            _pontos = pontos;
            _raioConcordancia = raioConcordancia;
            _atual = pontos.Count > 0
                ? new Point3d(pontos[pontos.Count - 1].X, pontos[pontos.Count - 1].Y, 0.0)
                : Point3d.Origin;

            _offsetsPreview = new List<(double, short)>();
            double metade = secao.LarguraTotal / 2.0;
            double wEsq = secao.MeiaLarguraPavimento(true);
            double wDir = secao.MeiaLarguraPavimento(false);

            if (wEsq > 1e-6)
            {
                _offsetsPreview.Add((wEsq, TipoElementoViaInfo.CorAci(TipoElementoVia.MeioFio)));
            }

            if (wDir > 1e-6)
            {
                _offsetsPreview.Add((-wDir, TipoElementoViaInfo.CorAci(TipoElementoVia.MeioFio)));
            }

            if (metade > 1e-6)
            {
                short corCalcada = TipoElementoViaInfo.CorAci(TipoElementoVia.Calcada);
                if (Math.Abs(metade - wEsq) > 1e-6)
                {
                    _offsetsPreview.Add((metade, corCalcada));
                }

                if (Math.Abs(metade - wDir) > 1e-6)
                {
                    _offsetsPreview.Add((-metade, corCalcada));
                }
            }
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opcoes = new JigPromptPointOptions("\nPróximo ponto da via ou [Desfazer/Concluir] <Concluir>: ");
            opcoes.UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted;
            opcoes.Keywords.Add("Desfazer");
            opcoes.Keywords.Add("Concluir");

            PromptPointResult resultado = prompts.AcquirePoint(opcoes);
            if (resultado.Status != PromptStatus.OK)
            {
                return SamplerStatus.Cancel;
            }

            if (_atual.DistanceTo(resultado.Value) < 1e-9)
            {
                return SamplerStatus.NoChange;
            }

            _atual = resultado.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw desenho)
        {
            Polyline? eixo = null;
            try
            {
                List<Point2d> pontos = new List<Point2d>(_pontos)
                {
                    new Point2d(_atual.X, _atual.Y)
                };

                if (pontos.Count < 2)
                {
                    return true;
                }

                eixo = PlanejadorViasDesenho.CriarPolylineEixo(pontos, _raioConcordancia);
                if (eixo.NumberOfVertices < 2)
                {
                    return true;
                }

                eixo.ColorIndex = 3;
                desenho.Geometry.Draw(eixo);

                double sinalEsquerda = PlanejadorViasDesenho.DescobrirSinalOffsetEsquerda(eixo);
                foreach ((double offset, short cor) in _offsetsPreview)
                {
                    try
                    {
                        List<Curve> curvas = PlanejadorViasDesenho.ObterOffsetAssinado(eixo, offset, sinalEsquerda);
                        foreach (Curve curva in curvas)
                        {
                            try
                            {
                                curva.ColorIndex = cor;
                                desenho.Geometry.Draw(curva);
                            }
                            finally
                            {
                                curva.Dispose();
                            }
                        }
                    }
                    catch
                    {
                        // Offset impossível na posição atual do cursor: ignora no preview.
                    }
                }
            }
            catch
            {
                // Preview nunca deve derrubar o comando.
            }
            finally
            {
                if (eixo != null)
                {
                    eixo.Dispose();
                }
            }

            return true;
        }
    }
}
