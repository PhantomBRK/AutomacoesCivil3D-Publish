using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutomacoesCivil3D
{
    /// <summary>Linha de limite registrada de uma via (entidade + offset assinado).</summary>
    public sealed class LimiteRegistrado
    {
        public ObjectId Id { get; set; }
        public double Offset { get; set; }
    }

    /// <summary>Via do Planejador presente no desenho, reconstruída a partir do XData.</summary>
    public sealed class ViaRegistrada
    {
        public Guid Id { get; set; }
        public ObjectId EixoId { get; set; }
        public double WEsq { get; set; }
        public double WDir { get; set; }
        public string Secao { get; set; } = string.Empty;
        public List<LimiteRegistrado> Limites { get; } = new List<LimiteRegistrado>();

        /// <summary>Meia-largura da caixa rodável no lado indicado (+1 esquerda, -1 direita).</summary>
        public double MeiaLargura(int lado)
        {
            return lado > 0 ? WEsq : WDir;
        }
    }

    /// <summary>
    /// Formação automática de junções entre vias do Planejador: recorta a caixa do
    /// cruzamento e insere arcos de concordância de meio-fio em cada quadrante.
    /// Suporta cruzamentos em X, junções em T e cantos em L; assume trechos
    /// aproximadamente retos no entorno imediato do cruzamento (típico de malha
    /// urbana de planejamento).
    /// </summary>
    public static class PlanejadorViasJuncoes
    {
        private const double ToleranciaOffset = 1e-4;
        private const double ComprimentoMinimoPedaco = 0.02;
        private const double AnguloMinimoGraus = 15.0;

        // ------------------------------------------------------------------
        // Registro das vias existentes (varredura do Model Space por XData)
        // ------------------------------------------------------------------

        public static Dictionary<Guid, ViaRegistrada> CarregarVias(Transaction tr, Database db)
        {
            Dictionary<Guid, ViaRegistrada> vias = new Dictionary<Guid, ViaRegistrada>();

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in modelSpace)
            {
                Entity? entidade = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (entidade == null || entidade.IsErased)
                {
                    continue;
                }

                InfoXDataVia? info = PlanejadorViasDesenho.LerXData(entidade);
                if (info == null || info.ViaId == Guid.Empty)
                {
                    continue;
                }

                ViaRegistrada? via;
                if (!vias.TryGetValue(info.ViaId, out via))
                {
                    via = new ViaRegistrada { Id = info.ViaId };
                    vias[info.ViaId] = via;
                }

                if (info.Papel == PlanejadorViasDesenho.PapelEixo)
                {
                    via.EixoId = id;
                    via.WEsq = info.WEsq;
                    via.WDir = info.WDir;
                    via.Secao = info.Secao;
                }
                else if (info.Papel == PlanejadorViasDesenho.PapelLimite)
                {
                    via.Limites.Add(new LimiteRegistrado { Id = id, Offset = info.Offset });
                }
            }

            // Vias sem eixo (apagado manualmente) não participam de junções.
            foreach (Guid chave in vias.Keys.Where(k => vias[k].EixoId.IsNull).ToList())
            {
                vias.Remove(chave);
            }

            return vias;
        }

        // ------------------------------------------------------------------
        // Processamento das junções de uma via recém-criada
        // ------------------------------------------------------------------

        public static List<string> ProcessarJuncoesDaVia(Transaction tr, Database db, Guid viaNovaId, double raioMeioFio)
        {
            List<string> avisos = new List<string>();
            if (raioMeioFio <= 1e-6)
            {
                avisos.Add("Raio de meio-fio nulo: junções não formadas.");
                return avisos;
            }

            Dictionary<Guid, ViaRegistrada> vias = CarregarVias(tr, db);
            ViaRegistrada? viaNova;
            if (!vias.TryGetValue(viaNovaId, out viaNova))
            {
                avisos.Add("Via recém-criada não encontrada no registro (XData).");
                return avisos;
            }

            foreach (ViaRegistrada viaExistente in vias.Values.Where(v => v.Id != viaNovaId).ToList())
            {
                List<Point3d> pontos = ObterInterseccoesDosEixos(tr, viaNova, viaExistente);
                foreach (Point3d ponto in pontos)
                {
                    try
                    {
                        ProcessarUmaJuncao(tr, db, viaExistente, viaNova, ponto, raioMeioFio, avisos);
                    }
                    catch (System.Exception ex)
                    {
                        avisos.Add($"Falha ao formar junção em ({ponto.X:0.00}, {ponto.Y:0.00}): {ex.Message}");
                    }
                }
            }

            return avisos;
        }

        private static List<Point3d> ObterInterseccoesDosEixos(Transaction tr, ViaRegistrada viaA, ViaRegistrada viaB)
        {
            List<Point3d> resultado = new List<Point3d>();

            Curve? eixoA = tr.GetObject(viaA.EixoId, OpenMode.ForRead, false) as Curve;
            Curve? eixoB = tr.GetObject(viaB.EixoId, OpenMode.ForRead, false) as Curve;
            if (eixoA == null || eixoB == null || eixoA.IsErased || eixoB.IsErased)
            {
                return resultado;
            }

            Point3dCollection pontos = new Point3dCollection();
            eixoA.IntersectWith(eixoB, Intersect.OnBothOperands, pontos, IntPtr.Zero, IntPtr.Zero);

            foreach (Point3d ponto in pontos)
            {
                bool repetido = resultado.Any(p => p.DistanceTo(ponto) < 0.01);
                if (!repetido)
                {
                    resultado.Add(ponto);
                }
            }

            return resultado;
        }

        // ------------------------------------------------------------------
        // Uma junção (um ponto de cruzamento entre duas vias)
        // ------------------------------------------------------------------

        private sealed class LadoDaJuncao
        {
            public bool QuadrantePositivoFormado; // quadrante (lado, +1) do outro eixo
            public bool QuadranteNegativoFormado; // quadrante (lado, -1)

            public bool Ativo
            {
                get { return QuadrantePositivoFormado || QuadranteNegativoFormado; }
            }
        }

        private sealed class ContextoVia
        {
            public ViaRegistrada Via = null!;
            public Curve Eixo = null!;
            public Vector2d Direcao;
            public Vector2d NormalEsquerda;
            public double ComprimentoAntes;   // do início do eixo até P
            public double ComprimentoDepois;  // de P até o fim do eixo
            public Dictionary<int, LadoDaJuncao> Lados = new Dictionary<int, LadoDaJuncao>
            {
                { 1, new LadoDaJuncao() },
                { -1, new LadoDaJuncao() }
            };

            // Tangentes acumuladas por anel: chave = offset assinado arredondado.
            public Dictionary<long, List<Point3d>> TangentesPorAnel = new Dictionary<long, List<Point3d>>();

            public List<LimiteRegistrado> AneisDoLado(int lado)
            {
                double w = Via.MeiaLargura(lado);
                if (w < ToleranciaOffset)
                {
                    return new List<LimiteRegistrado>();
                }

                return Via.Limites
                    .Where(l => Math.Sign(l.Offset) == lado && Math.Abs(l.Offset) >= w - ToleranciaOffset)
                    .GroupBy(l => ChaveOffset(l.Offset))
                    .Select(g => g.First())
                    .OrderBy(l => Math.Abs(l.Offset))
                    .ToList();
            }

            public List<LimiteRegistrado> InterioresDoLado(int lado)
            {
                double w = Via.MeiaLargura(lado);
                return Via.Limites
                    .Where(l => Math.Sign(l.Offset) == lado
                        && Math.Abs(l.Offset) > ToleranciaOffset
                        && Math.Abs(l.Offset) < w - ToleranciaOffset)
                    .GroupBy(l => ChaveOffset(l.Offset))
                    .Select(g => g.First())
                    .OrderBy(l => Math.Abs(l.Offset))
                    .ToList();
            }
        }

        private static long ChaveOffset(double offset)
        {
            return (long)Math.Round(offset * 1e4);
        }

        private static void ProcessarUmaJuncao(
            Transaction tr,
            Database db,
            ViaRegistrada viaA,
            ViaRegistrada viaB,
            Point3d pontoBruto,
            double raio,
            List<string> avisos)
        {
            ContextoVia? ctxA = MontarContexto(tr, viaA, pontoBruto);
            ContextoVia? ctxB = MontarContexto(tr, viaB, pontoBruto);
            if (ctxA == null || ctxB == null)
            {
                return;
            }

            double seno = Math.Abs(PlanejadorViasGeometria.Cruzado(ctxA.Direcao, ctxB.Direcao));
            if (seno < Math.Sin(AnguloMinimoGraus * Math.PI / 180.0))
            {
                avisos.Add($"Cruzamento quase paralelo em ({pontoBruto.X:0.00}, {pontoBruto.Y:0.00}) ignorado.");
                return;
            }

            Point2d p = new Point2d(pontoBruto.X, pontoBruto.Y);

            // ----------------------------------------------------------------
            // Quadrantes: para cada combinação de lados, verifica alcance físico
            // das duas vias e concorda os anéis (borda de pavimento para fora).
            // ----------------------------------------------------------------
            List<Arc> arcos = new List<Arc>();

            foreach (int ladoA in new[] { 1, -1 })
            {
                foreach (int ladoB in new[] { 1, -1 })
                {
                    if (!ViaAlcancaLado(ctxB, ctxA, ladoA, raio, seno) ||
                        !ViaAlcancaLado(ctxA, ctxB, ladoB, raio, seno))
                    {
                        continue;
                    }

                    List<LimiteRegistrado> aneisA = ctxA.AneisDoLado(ladoA);
                    List<LimiteRegistrado> aneisB = ctxB.AneisDoLado(ladoB);
                    if (aneisA.Count == 0 || aneisB.Count == 0)
                    {
                        continue;
                    }

                    // Anel 0 = borda do pavimento: define o retorno de meio-fio do quadrante.
                    FilletQuadrante filletBorda = PlanejadorViasGeometria.FilletDeQuadrante(
                        p,
                        ctxA.NormalEsquerda,
                        ctxB.NormalEsquerda,
                        ladoA * Math.Abs(aneisA[0].Offset),
                        ladoB * Math.Abs(aneisB[0].Offset),
                        raio);

                    if (!filletBorda.Possivel)
                    {
                        continue;
                    }

                    MarcarQuadrante(ctxA.Lados[ladoA], ladoB);
                    MarcarQuadrante(ctxB.Lados[ladoB], ladoA);

                    double profundidadeBaseA = Math.Abs(aneisA[0].Offset);
                    double profundidadeBaseB = Math.Abs(aneisB[0].Offset);

                    int pares = Math.Min(aneisA.Count, aneisB.Count);
                    for (int k = 0; k < pares; k++)
                    {
                        FilletQuadrante fillet;
                        if (k == 0)
                        {
                            fillet = filletBorda;
                        }
                        else
                        {
                            double profA = Math.Abs(aneisA[k].Offset) - profundidadeBaseA;
                            double profB = Math.Abs(aneisB[k].Offset) - profundidadeBaseB;

                            Point2d quina;
                            bool temQuina = PlanejadorViasGeometria.ResolverBaseObliqua(
                                p,
                                ctxA.NormalEsquerda,
                                ctxB.NormalEsquerda,
                                ladoA * Math.Abs(aneisA[k].Offset),
                                ladoB * Math.Abs(aneisB[k].Offset),
                                out quina);

                            if (temQuina && Math.Abs(profA - profB) < 1e-4 && raio - profA > 0.05)
                            {
                                // Bandas de mesma profundidade nas duas vias: arco
                                // concêntrico ao retorno de meio-fio (largura constante).
                                fillet = PlanejadorViasGeometria.FilletConcentrico(
                                    filletBorda.Centro,
                                    raio - profA,
                                    ctxA.NormalEsquerda,
                                    ladoA,
                                    ctxB.NormalEsquerda,
                                    ladoB,
                                    quina);
                            }
                            else
                            {
                                // Profundidades diferentes: fillet independente tangente
                                // às duas linhas do anel.
                                fillet = PlanejadorViasGeometria.FilletDeQuadrante(
                                    p,
                                    ctxA.NormalEsquerda,
                                    ctxB.NormalEsquerda,
                                    ladoA * Math.Abs(aneisA[k].Offset),
                                    ladoB * Math.Abs(aneisB[k].Offset),
                                    raio);
                            }
                        }

                        if (!fillet.Possivel)
                        {
                            continue;
                        }

                        Arc arco = new Arc(
                            new Point3d(fillet.Centro.X, fillet.Centro.Y, 0.0),
                            fillet.Raio,
                            fillet.AnguloInicial,
                            fillet.AnguloFinal);
                        arcos.Add(arco);

                        Point3d tangenteA = new Point3d(fillet.TangenteA.X, fillet.TangenteA.Y, 0.0);
                        Point3d tangenteB = new Point3d(fillet.TangenteB.X, fillet.TangenteB.Y, 0.0);

                        GuardarTangente(ctxA, aneisA[k].Offset, tangenteA);
                        GuardarTangente(ctxB, aneisB[k].Offset, tangenteB);
                    }

                    // Anéis excedentes (sem par na outra via): recorte reto na linha
                    // do anel mais externo da outra via.
                    RecortarAneisSemPar(p, ctxA, ladoA, aneisA, ctxB, ladoB, aneisB, pares);
                    RecortarAneisSemPar(p, ctxB, ladoB, aneisB, ctxA, ladoA, aneisA, pares);
                }
            }

            if (arcos.Count == 0)
            {
                return; // nenhum quadrante viável: não altera nada
            }

            // ----------------------------------------------------------------
            // Recortes dos anéis nas tangentes acumuladas.
            // ----------------------------------------------------------------
            RecortarAneis(tr, ctxA, pontoBruto, avisos);
            RecortarAneis(tr, ctxB, pontoBruto, avisos);

            // ----------------------------------------------------------------
            // Limites interiores (canteiros etc.): abre a caixa da junção.
            // ----------------------------------------------------------------
            RecortarInteriores(tr, p, ctxA, ctxB, pontoBruto, avisos);
            RecortarInteriores(tr, p, ctxB, ctxA, pontoBruto, avisos);

            // ----------------------------------------------------------------
            // Insere os arcos de concordância no banco.
            // ----------------------------------------------------------------
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ObjectId camadaMeioFio = PlanejadorViasDesenho.GarantirCamada(
                tr, db,
                PlanejadorViasDesenho.CamadaDoLimite(TipoElementoVia.MeioFio),
                TipoElementoViaInfo.CorAci(TipoElementoVia.MeioFio));

            foreach (Arc arco in arcos)
            {
                arco.SetDatabaseDefaults(db);
                arco.LayerId = camadaMeioFio;
                modelSpace.AppendEntity(arco);
                tr.AddNewlyCreatedDBObject(arco, true);
                PlanejadorViasDesenho.AplicarXDataJuncao(arco, viaA.Id, viaB.Id);
            }
        }

        private static void MarcarQuadrante(LadoDaJuncao lado, int ladoOutro)
        {
            if (ladoOutro > 0)
            {
                lado.QuadrantePositivoFormado = true;
            }
            else
            {
                lado.QuadranteNegativoFormado = true;
            }
        }

        private static ContextoVia? MontarContexto(Transaction tr, ViaRegistrada via, Point3d pontoBruto)
        {
            Curve? eixo = tr.GetObject(via.EixoId, OpenMode.ForRead, false) as Curve;
            if (eixo == null || eixo.IsErased)
            {
                return null;
            }

            try
            {
                Point3d sobreEixo = eixo.GetClosestPointTo(pontoBruto, false);
                double parametro = eixo.GetParameterAtPoint(sobreEixo);
                Vector3d derivada = eixo.GetFirstDerivative(parametro);
                if (derivada.Length < 1e-9)
                {
                    return null;
                }

                Vector2d direcao = Normalizar(new Vector2d(derivada.X, derivada.Y));
                double distancia = eixo.GetDistanceAtParameter(parametro);
                double comprimento = eixo.GetDistanceAtParameter(eixo.EndParam);

                return new ContextoVia
                {
                    Via = via,
                    Eixo = eixo,
                    Direcao = direcao,
                    NormalEsquerda = PlanejadorViasGeometria.NormalEsquerda(direcao),
                    ComprimentoAntes = distancia,
                    ComprimentoDepois = Math.Max(0.0, comprimento - distancia)
                };
            }
            catch
            {
                return null;
            }
        }

        private static Vector2d Normalizar(Vector2d v)
        {
            double comprimento = v.Length;
            return comprimento > 1e-12
                ? new Vector2d(v.X / comprimento, v.Y / comprimento)
                : new Vector2d(1.0, 0.0);
        }

        /// <summary>
        /// Verifica se a via "quem" se estende fisicamente para o lado <paramref name="lado"/>
        /// da via "alvo" o bastante para formar o quadrante (atravessar a caixa + raio).
        /// </summary>
        private static bool ViaAlcancaLado(ContextoVia quem, ContextoVia alvo, int lado, double raio, double seno)
        {
            double componente = quem.Direcao.DotProduct(alvo.NormalEsquerda) * lado;
            double disponivel = componente > 0 ? quem.ComprimentoDepois : quem.ComprimentoAntes;

            double penetracaoNecessaria = alvo.Via.MeiaLargura(lado) + raio + 0.30;
            double comprimentoNecessario = penetracaoNecessaria / Math.Max(seno, 0.25);

            return disponivel + 1e-6 >= comprimentoNecessario;
        }

        private static void GuardarTangente(ContextoVia ctx, double offset, Point3d tangente)
        {
            long chave = ChaveOffset(offset);
            List<Point3d>? lista;
            if (!ctx.TangentesPorAnel.TryGetValue(chave, out lista))
            {
                lista = new List<Point3d>();
                ctx.TangentesPorAnel[chave] = lista;
            }

            lista.Add(tangente);
        }

        /// <summary>
        /// Anéis sem par correspondente na outra via: guarda como "tangente" o ponto de
        /// interseção com a linha do anel mais externo da outra via (recorte reto).
        /// </summary>
        private static void RecortarAneisSemPar(
            Point2d p,
            ContextoVia ctx,
            int lado,
            List<LimiteRegistrado> aneis,
            ContextoVia ctxOutra,
            int ladoOutra,
            List<LimiteRegistrado> aneisOutra,
            int pares)
        {
            if (aneis.Count <= pares || aneisOutra.Count == 0)
            {
                return;
            }

            double offsetExternoOutra = ladoOutra * Math.Abs(aneisOutra[aneisOutra.Count - 1].Offset);
            for (int k = pares; k < aneis.Count; k++)
            {
                Point2d ponto;
                if (!PlanejadorViasGeometria.ResolverBaseObliqua(
                    p,
                    ctx.NormalEsquerda,
                    ctxOutra.NormalEsquerda,
                    lado * Math.Abs(aneis[k].Offset),
                    offsetExternoOutra,
                    out ponto))
                {
                    continue;
                }

                GuardarTangente(ctx, aneis[k].Offset, new Point3d(ponto.X, ponto.Y, 0.0));
            }
        }

        /// <summary>
        /// Aplica os recortes acumulados nos anéis de uma via: com dois pontos,
        /// remove o trecho entre eles; com um ponto, remove a ponta voltada à junção.
        /// </summary>
        private static void RecortarAneis(Transaction tr, ContextoVia ctx, Point3d pontoJuncao, List<string> avisos)
        {
            foreach (KeyValuePair<long, List<Point3d>> par in ctx.TangentesPorAnel)
            {
                List<Point3d> pontos = par.Value;
                LimiteRegistrado? anel = ctx.Via.Limites.FirstOrDefault(l => ChaveOffset(l.Offset) == par.Key);
                if (anel == null || pontos.Count == 0)
                {
                    continue;
                }

                if (pontos.Count >= 2)
                {
                    RecortarEntrePontos(tr, ctx.Via, anel.Offset, pontos[0], pontos[1], pontoJuncao, avisos);
                }
                else
                {
                    RecortarPontaDaJuncao(tr, ctx.Via, anel.Offset, pontos[0], pontoJuncao, avisos);
                }
            }
        }

        /// <summary>
        /// Abre a caixa da junção nos limites interiores (ex.: canteiro central) da via
        /// "ctx", recortando entre as bordas de pavimento da outra via.
        /// </summary>
        private static void RecortarInteriores(
            Transaction tr,
            Point2d p,
            ContextoVia ctx,
            ContextoVia ctxOutra,
            Point3d pontoJuncao,
            List<string> avisos)
        {
            foreach (int lado in new[] { 1, -1 })
            {
                if (!ctx.Lados[lado].Ativo)
                {
                    continue;
                }

                List<LimiteRegistrado> interiores = ctx.InterioresDoLado(lado);
                if (interiores.Count == 0)
                {
                    continue;
                }

                double wOutraEsq = ctxOutra.Via.MeiaLargura(1);
                double wOutraDir = ctxOutra.Via.MeiaLargura(-1);
                if (wOutraEsq < ToleranciaOffset && wOutraDir < ToleranciaOffset)
                {
                    continue;
                }

                foreach (LimiteRegistrado interior in interiores)
                {
                    Point2d ponto1;
                    Point2d ponto2;
                    bool ok1 = PlanejadorViasGeometria.ResolverBaseObliqua(
                        p, ctx.NormalEsquerda, ctxOutra.NormalEsquerda,
                        interior.Offset, wOutraEsq, out ponto1);
                    bool ok2 = PlanejadorViasGeometria.ResolverBaseObliqua(
                        p, ctx.NormalEsquerda, ctxOutra.NormalEsquerda,
                        interior.Offset, -wOutraDir, out ponto2);

                    if (!ok1 || !ok2)
                    {
                        continue;
                    }

                    RecortarEntrePontos(
                        tr,
                        ctx.Via,
                        interior.Offset,
                        new Point3d(ponto1.X, ponto1.Y, 0.0),
                        new Point3d(ponto2.X, ponto2.Y, 0.0),
                        pontoJuncao,
                        avisos);
                }
            }
        }

        // ------------------------------------------------------------------
        // Operações de recorte sobre as entidades reais
        // ------------------------------------------------------------------

        private static Curve? LocalizarEntidadeDoAnel(
            Transaction tr,
            ViaRegistrada via,
            double offset,
            Point3d referencia,
            out LimiteRegistrado? registro)
        {
            registro = null;
            Curve? melhor = null;
            double melhorDistancia = double.MaxValue;

            foreach (LimiteRegistrado candidato in via.Limites.Where(l => ChaveOffset(l.Offset) == ChaveOffset(offset)))
            {
                Curve? curva = tr.GetObject(candidato.Id, OpenMode.ForRead, false) as Curve;
                if (curva == null || curva.IsErased)
                {
                    continue;
                }

                try
                {
                    double distancia = curva.GetClosestPointTo(referencia, false).DistanceTo(referencia);
                    if (distancia < melhorDistancia)
                    {
                        melhorDistancia = distancia;
                        melhor = curva;
                        registro = candidato;
                    }
                }
                catch
                {
                }
            }

            // A referência deve estar praticamente sobre a entidade correta; em vias
            // curvas a aproximação local admite algum desvio.
            if (melhor == null || melhorDistancia > 2.5)
            {
                return null;
            }

            return melhor;
        }

        private static void RecortarEntrePontos(
            Transaction tr,
            ViaRegistrada via,
            double offset,
            Point3d ponto1,
            Point3d ponto2,
            Point3d pontoJuncao,
            List<string> avisos)
        {
            LimiteRegistrado? registro;
            Curve? curva = LocalizarEntidadeDoAnel(tr, via, offset, ponto1, out registro);
            if (curva == null || registro == null)
            {
                avisos.Add($"Limite (offset {offset:0.00}) não localizado para recorte.");
                return;
            }

            // O segundo ponto precisa cair na mesma entidade; caso contrário (limite já
            // dividido por outra junção), recorta cada ponta separadamente.
            double distancia2 = double.MaxValue;
            try
            {
                distancia2 = curva.GetClosestPointTo(ponto2, false).DistanceTo(ponto2);
            }
            catch
            {
            }

            if (distancia2 > 2.5)
            {
                // Limite já dividido ou mais curto que a caixa (caso típico do T):
                // recorta cada ponta que existir, sem exigir as duas.
                RecortarPontaDaJuncao(tr, via, offset, ponto1, pontoJuncao, avisos, avisarSeNaoEncontrar: false);
                RecortarPontaDaJuncao(tr, via, offset, ponto2, pontoJuncao, avisos, avisarSeNaoEncontrar: false);
                return;
            }

            try
            {
                Point3d q1 = curva.GetClosestPointTo(ponto1, false);
                Point3d q2 = curva.GetClosestPointTo(ponto2, false);
                double t1 = curva.GetParameterAtPoint(q1);
                double t2 = curva.GetParameterAtPoint(q2);
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                    (q1, q2) = (q2, q1);
                }

                if (Math.Abs(t2 - t1) < 1e-9)
                {
                    return;
                }

                Point3d pontoMeio = curva.GetPointAtParameter((t1 + t2) / 2.0);
                SubstituirPorPedacos(tr, via, registro, curva, new List<Point3d> { q1, q2 }, pontoMeio);
            }
            catch (System.Exception ex)
            {
                avisos.Add($"Recorte falhou no offset {offset:0.00}: {ex.Message}");
            }
        }

        private static void RecortarPontaDaJuncao(
            Transaction tr,
            ViaRegistrada via,
            double offset,
            Point3d ponto,
            Point3d pontoJuncao,
            List<string> avisos,
            bool avisarSeNaoEncontrar = true)
        {
            LimiteRegistrado? registro;
            Curve? curva = LocalizarEntidadeDoAnel(tr, via, offset, ponto, out registro);
            if (curva == null || registro == null)
            {
                if (avisarSeNaoEncontrar)
                {
                    avisos.Add($"Limite (offset {offset:0.00}) não localizado para recorte de ponta.");
                }

                return;
            }

            try
            {
                Point3d q = curva.GetClosestPointTo(ponto, false);
                double t = curva.GetParameterAtPoint(q);
                double tInicio = curva.StartParam;
                double tFim = curva.EndParam;
                if (t - tInicio < 1e-9 || tFim - t < 1e-9)
                {
                    return; // ponto na extremidade: nada a recortar
                }

                // O trecho descartado é o que está voltado para a junção.
                double distInicio = curva.StartPoint.DistanceTo(pontoJuncao);
                double distFim = curva.EndPoint.DistanceTo(pontoJuncao);
                double parametroDescartado = distInicio < distFim
                    ? (tInicio + t) / 2.0
                    : (t + tFim) / 2.0;
                Point3d pontoDescartado = curva.GetPointAtParameter(parametroDescartado);

                SubstituirPorPedacos(tr, via, registro, curva, new List<Point3d> { q }, pontoDescartado);
            }
            catch (System.Exception ex)
            {
                avisos.Add($"Recorte de ponta falhou no offset {offset:0.00}: {ex.Message}");
            }
        }

        /// <summary>
        /// Divide a curva nos pontos dados, descarta o pedaço que contém
        /// <paramref name="pontoDescartado"/> e mantém os demais (herdam camada e XData).
        /// Atualiza o registro em memória da via.
        /// </summary>
        private static void SubstituirPorPedacos(
            Transaction tr,
            ViaRegistrada via,
            LimiteRegistrado registro,
            Curve curva,
            List<Point3d> pontosDeCorte,
            Point3d pontoDescartado)
        {
            List<Point3d> ordenados = pontosDeCorte
                .OrderBy(ponto =>
                {
                    try
                    {
                        return curva.GetParameterAtPoint(curva.GetClosestPointTo(ponto, false));
                    }
                    catch
                    {
                        return double.MaxValue;
                    }
                })
                .ToList();

            Point3dCollection colecao = new Point3dCollection();
            foreach (Point3d ponto in ordenados)
            {
                colecao.Add(curva.GetClosestPointTo(ponto, false));
            }

            DBObjectCollection pedacos = curva.GetSplitCurves(colecao);
            if (pedacos.Count == 0)
            {
                return;
            }

            BlockTableRecord dono = (BlockTableRecord)tr.GetObject(curva.OwnerId, OpenMode.ForWrite);

            List<ObjectId> novosIds = new List<ObjectId>();
            foreach (DBObject objeto in pedacos)
            {
                Curve? pedaco = objeto as Curve;
                if (pedaco == null)
                {
                    objeto.Dispose();
                    continue;
                }

                bool descartar;
                try
                {
                    descartar = pedaco.GetClosestPointTo(pontoDescartado, false).DistanceTo(pontoDescartado) < 1e-4;
                }
                catch
                {
                    descartar = false;
                }

                double comprimento;
                try
                {
                    comprimento = pedaco.GetDistanceAtParameter(pedaco.EndParam);
                }
                catch
                {
                    comprimento = 0.0;
                }

                if (descartar || comprimento < ComprimentoMinimoPedaco)
                {
                    pedaco.Dispose();
                    continue;
                }

                PlanejadorViasDesenho.CopiarAparenciaEXData(curva, pedaco);
                ObjectId novoId = dono.AppendEntity(pedaco);
                tr.AddNewlyCreatedDBObject(pedaco, true);
                novosIds.Add(novoId);
            }

            curva.UpgradeOpen();
            curva.Erase();

            via.Limites.Remove(registro);
            foreach (ObjectId novoId in novosIds)
            {
                via.Limites.Add(new LimiteRegistrado { Id = novoId, Offset = registro.Offset });
            }
        }
    }
}
