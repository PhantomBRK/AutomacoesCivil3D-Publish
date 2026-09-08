using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Civil = Autodesk.Civil.DatabaseServices;

namespace AutomacoesCivil3D
{
    /// <summary>Um retorno de meio-fio (anel da borda de pavimento) apto a virar baseline.</summary>
    internal sealed class RetornoDeJuncao
    {
        public ObjectId ArcoId { get; set; }
        public Guid ViaA { get; set; }
        public Guid ViaB { get; set; }
        public Point3d Centro { get; set; }
        public double Raio { get; set; }
        public Point3d PontoInicial { get; set; }
        public Point3d PontoFinal { get; set; }
        public Point3d PontoMedio { get; set; }
        public double VarreduraGraus { get; set; }
        public Point3d? PontoCentroJuncao { get; set; } // cruzamento dos eixos (P)
        public string Identificador { get; set; } = string.Empty; // handle do arco
    }

    /// <summary>
    /// Corredores das interseções do Planejador de Vias: cada arco de retorno de
    /// meio-fio (borda de pavimento) vira uma baseline com perfil amarrado nas cotas
    /// dos greides das vias, e uma assembly de pista voltada para dentro preenche o
    /// miolo do cruzamento. Todos os retornos entram em um único corredor
    /// "COR_JUNCOES_PLANVIAS"; reexecutar pula retornos já processados.
    /// </summary>
    public static class PlanejadorViasCorredoresJuncoes
    {
        public const string NomeCorredorJuncoes = "COR_JUNCOES_PLANVIAS";
        private const double VarreduraMinimaGraus = 5.0;
        private const double DeclividadePista = 0.02; // caimento padrão das BasicLane (-2%)

        public static ResultadoCorredores CriarCorredoresDeJuncoes(
            Transaction tr,
            Database db,
            CivilDocument civilDoc,
            ObjectId superficieId,
            Editor? editor,
            MapeamentoAssemblies mapeamento)
        {
            ResultadoCorredores resultado = new ResultadoCorredores();

            Dictionary<Guid, ViaRegistrada> vias = PlanejadorViasJuncoes.CarregarVias(tr, db);
            List<RetornoDeJuncao> retornos = ColetarRetornos(tr, db, vias);
            if (retornos.Count == 0)
            {
                resultado.Avisos.Add("Nenhum retorno de meio-fio de junção encontrado — desenhe vias que se cruzem antes.");
                return resultado;
            }

            // Assembly das interseções: mapeamento do usuário → seleção na tela →
            // automática (só se pedida) → pular as interseções por completo.
            bool usarAutomatica = false;
            ObjectId assemblyJuncoes = ObjectId.Null;

            if (!string.IsNullOrWhiteSpace(mapeamento.AssemblyJuncoes))
            {
                assemblyJuncoes = LocalizarAssemblyPorNome(tr, civilDoc, mapeamento.AssemblyJuncoes);
                if (assemblyJuncoes.IsNull)
                {
                    resultado.Avisos.Add($"Assembly das interseções \"{mapeamento.AssemblyJuncoes}\" não existe neste desenho.");
                }
            }

            if (assemblyJuncoes.IsNull && editor != null)
            {
                assemblyJuncoes = PlanejadorViasCorredores.SelecionarAssembly(
                    tr, editor,
                    "\nSelecione a ASSEMBLY das INTERSEÇÕES (pista larga no lado direito)",
                    out usarAutomatica);

                if (!assemblyJuncoes.IsNull)
                {
                    Civil.Assembly? escolhida = tr.GetObject(assemblyJuncoes, OpenMode.ForRead, false) as Civil.Assembly;
                    if (escolhida != null)
                    {
                        mapeamento.AssemblyJuncoes = escolhida.Name;
                        mapeamento.Salvar();
                    }
                }
            }
            else if (assemblyJuncoes.IsNull && editor == null)
            {
                usarAutomatica = true;
            }

            if (assemblyJuncoes.IsNull && !usarAutomatica)
            {
                resultado.Avisos.Add("Interseções puladas: nenhuma assembly definida. " +
                    "Use PLANVIAS_DEFINIR_ASSEMBLIES e rode PLANVIAS_CORREDORES_JUNCOES.");
                return resultado;
            }

            // Corredor único das junções (criado sob demanda).
            Civil.Corridor? corredor = LocalizarCorredorPorNome(tr, civilDoc, NomeCorredorJuncoes);
            bool corredorNovo = false;
            if (corredor == null)
            {
                ObjectId corredorId = civilDoc.CorridorCollection.Add(NomeCorredorJuncoes);
                corredor = (Civil.Corridor)tr.GetObject(corredorId, OpenMode.ForWrite);
                corredorNovo = true;
            }
            else
            {
                corredor.UpgradeOpen();
            }

            HashSet<string> baselinesExistentes = ColetarNomesDeBaselines(corredor);
            Dictionary<string, ObjectId> assembliesPorLargura = new Dictionary<string, ObjectId>();
            int criados = 0;

            foreach (RetornoDeJuncao retorno in retornos)
            {
                string nomeBaseline = "BL_JN_" + retorno.Identificador;
                if (baselinesExistentes.Contains(nomeBaseline))
                {
                    continue; // já processado em execução anterior
                }

                try
                {
                    if (CriarBaselineDoRetorno(tr, db, civilDoc, corredor, retorno, vias,
                        superficieId, assembliesPorLargura, nomeBaseline, resultado,
                        assemblyJuncoes, usarAutomatica))
                    {
                        criados++;
                    }
                }
                catch (System.Exception ex)
                {
                    resultado.Avisos.Add($"Retorno {retorno.Identificador}: {ex.Message}");
                }
            }

            if (criados > 0)
            {
                try
                {
                    corredor.Rebuild();
                }
                catch (System.Exception ex)
                {
                    resultado.Avisos.Add($"Rebuild do corredor de junções falhou ({ex.Message}) — reconstrua manualmente.");
                }

                resultado.Corredores = corredorNovo ? 1 : 0;
            }
            else if (corredorNovo)
            {
                resultado.Avisos.Add("Nenhum retorno novo para processar.");
            }

            resultado.Alinhamentos += criados;
            resultado.Perfis += criados;
            return resultado;
        }

        // ------------------------------------------------------------------
        // Coleta dos arcos de retorno (anel da borda de pavimento)
        // ------------------------------------------------------------------

        private static List<RetornoDeJuncao> ColetarRetornos(Transaction tr, Database db, Dictionary<Guid, ViaRegistrada> vias)
        {
            List<RetornoDeJuncao> todos = new List<RetornoDeJuncao>();

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in modelSpace)
            {
                Arc? arco = tr.GetObject(id, OpenMode.ForRead, false) as Arc;
                if (arco == null || arco.IsErased)
                {
                    continue;
                }

                Guid viaA;
                Guid viaB;
                if (!LerXDataJuncao(arco, out viaA, out viaB))
                {
                    continue;
                }

                double varredura = PlanejadorViasGeometria.VarreduraCcw(arco.StartAngle, arco.EndAngle) * 180.0 / Math.PI;
                if (varredura < VarreduraMinimaGraus)
                {
                    continue;
                }

                double parametroMedio = (arco.StartParam + arco.EndParam) / 2.0;
                todos.Add(new RetornoDeJuncao
                {
                    ArcoId = id,
                    ViaA = viaA,
                    ViaB = viaB,
                    Centro = arco.Center,
                    Raio = arco.Radius,
                    PontoInicial = arco.StartPoint,
                    PontoFinal = arco.EndPoint,
                    PontoMedio = arco.GetPointAtParameter(parametroMedio),
                    VarreduraGraus = varredura,
                    Identificador = arco.Handle.ToString()
                });
            }

            // Cada junção gera arcos por anel (borda, fundo do meio-fio, calçada).
            // A baseline usa só o anel da BORDA DE PAVIMENTO. Anéis concêntricos:
            // borda = maior raio da família (mesmo centro). Anéis com fillet
            // independente (bandas de profundidades diferentes): centros próximos e
            // mesmo raio — desempata pelo arco de centro mais próximo do ponto médio
            // interno (o mais "para dentro" da junção é a borda).
            List<RetornoDeJuncao> porCentroExato = todos
                .GroupBy(r => (Math.Round(r.Centro.X, 3), Math.Round(r.Centro.Y, 3)))
                .Select(g => g.OrderByDescending(r => r.Raio).First())
                .ToList();

            foreach (RetornoDeJuncao candidato in porCentroExato)
            {
                candidato.PontoCentroJuncao = LocalizarCentroDaJuncao(tr, vias, candidato);
            }

            List<RetornoDeJuncao> resultado = new List<RetornoDeJuncao>();
            foreach (RetornoDeJuncao candidato in porCentroExato
                .OrderBy(r => r.PontoCentroJuncao.HasValue
                    ? r.PontoMedio.DistanceTo(r.PontoCentroJuncao.Value)
                    : double.MaxValue))
            {
                bool duplicado = resultado.Any(r =>
                    r.ViaA == candidato.ViaA &&
                    r.ViaB == candidato.ViaB &&
                    r.Centro.DistanceTo(candidato.Centro) < 5.0);

                if (!duplicado)
                {
                    resultado.Add(candidato);
                }
            }

            return resultado;
        }

        private static bool LerXDataJuncao(Entity entidade, out Guid viaA, out Guid viaB)
        {
            viaA = Guid.Empty;
            viaB = Guid.Empty;

            ResultBuffer? rb = entidade.GetXDataForApplication(PlanejadorViasDesenho.RegApp);
            if (rb == null)
            {
                return false;
            }

            try
            {
                List<string> textos = new List<string>();
                foreach (TypedValue valor in rb)
                {
                    if (valor.TypeCode == (int)DxfCode.ExtendedDataAsciiString && valor.Value is string s)
                    {
                        textos.Add(s);
                    }
                }

                if (textos.Count < 3 || textos[0] != PlanejadorViasDesenho.PapelJuncao)
                {
                    return false;
                }

                Guid.TryParseExact(textos[1], "N", out viaA);
                Guid.TryParseExact(textos[2], "N", out viaB);
                return true;
            }
            finally
            {
                rb.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // Baseline de um retorno: alinhamento + perfil + região com pista interna
        // ------------------------------------------------------------------

        private static bool CriarBaselineDoRetorno(
            Transaction tr,
            Database db,
            CivilDocument civilDoc,
            Civil.Corridor corredor,
            RetornoDeJuncao retorno,
            Dictionary<Guid, ViaRegistrada> vias,
            ObjectId superficieId,
            Dictionary<string, ObjectId> assembliesPorLargura,
            string nomeBaseline,
            ResultadoCorredores resultado,
            ObjectId assemblyFixa,
            bool usarAutomatica)
        {
            // Cotas nas duas pontas ANTES de criar qualquer coisa: se não amarrarem
            // nos greides das vias nem na superfície, o retorno é pulado — evita
            // interseções sem sentido penduradas na cota 0.
            double cotaInicio;
            double cotaFim;
            bool okInicio = TentarCotaNaBorda(tr, civilDoc, vias, retorno, retorno.PontoFinal, superficieId, out cotaInicio);
            bool okFim = TentarCotaNaBorda(tr, civilDoc, vias, retorno, retorno.PontoInicial, superficieId, out cotaFim);
            if (!okInicio || !okFim)
            {
                resultado.Avisos.Add($"Retorno {retorno.Identificador} pulado: sem greide das vias nem superfície " +
                    "para amarrar as cotas. Rode PLANVIAS_CRIAR_CORREDORES antes.");
                return false;
            }

            // Assembly de pista para dentro do cruzamento: a definida pelo usuário
            // ou, se ele optou pela automática, uma por faixa de largura.
            ObjectId assemblyId = assemblyFixa;
            if (assemblyId.IsNull && usarAutomatica)
            {
                double largura = LarguraAteOCentro(tr, vias, retorno);
                assemblyId = ObterAssemblyDePista(tr, civilDoc, largura, assembliesPorLargura, resultado);
            }

            if (assemblyId.IsNull)
            {
                resultado.Avisos.Add($"Retorno {retorno.Identificador} pulado: sem assembly das interseções.");
                return false;
            }

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // Polyline temporária INVERTIDA (fim → início): o arco é CCW e o interior
            // da junção fica à esquerda; invertendo, o interior fica à DIREITA, que é
            // o lado padrão das subassemblies — a pista aponta para dentro do
            // cruzamento sem depender do parâmetro "Side".
            double varredura = PlanejadorViasGeometria.VarreduraCcw(
                PlanejadorViasGeometria.AnguloDe(new Vector2d(
                    retorno.PontoInicial.X - retorno.Centro.X, retorno.PontoInicial.Y - retorno.Centro.Y)),
                PlanejadorViasGeometria.AnguloDe(new Vector2d(
                    retorno.PontoFinal.X - retorno.Centro.X, retorno.PontoFinal.Y - retorno.Centro.Y)));
            double bulgeInvertido = -Math.Tan(varredura / 4.0);

            Polyline polylineTemporaria = new Polyline();
            polylineTemporaria.AddVertexAt(0,
                new Point2d(retorno.PontoFinal.X, retorno.PontoFinal.Y), bulgeInvertido, 0.0, 0.0);
            polylineTemporaria.AddVertexAt(1,
                new Point2d(retorno.PontoInicial.X, retorno.PontoInicial.Y), 0.0, 0.0, 0.0);
            polylineTemporaria.SetDatabaseDefaults(db);
            modelSpace.AppendEntity(polylineTemporaria);
            tr.AddNewlyCreatedDBObject(polylineTemporaria, true);

            Civil.PolylineOptions opcoes = new Civil.PolylineOptions
            {
                AddCurvesBetweenTangents = false,
                EraseExistingEntities = true, // consome a polyline temporária
                PlineId = polylineTemporaria.ObjectId
            };

            ObjectId alinhamentoId = Civil.Alignment.Create(
                civilDoc,
                opcoes,
                "AL_JN_" + retorno.Identificador,
                ObjectId.Null,
                db.Clayer,
                PrimeiroItem(civilDoc.Styles.AlignmentStyles),
                PrimeiroItem(civilDoc.Styles.LabelSetStyles.AlignmentLabelSetStyles));

            Civil.Alignment alinhamento = (Civil.Alignment)tr.GetObject(alinhamentoId, OpenMode.ForRead);

            // Perfil: cotas nas pontas amarradas aos greides das vias (menos o
            // caimento da pista até a borda), interpolação reta no meio.
            ObjectId perfilId = Civil.Profile.CreateByLayout(
                "PF_JN_" + retorno.Identificador,
                alinhamentoId,
                db.Clayer,
                PrimeiroItem(civilDoc.Styles.ProfileStyles),
                PrimeiroItem(civilDoc.Styles.LabelSetStyles.ProfileLabelSetStyles));

            Civil.Profile perfil = (Civil.Profile)tr.GetObject(perfilId, OpenMode.ForWrite);
            perfil.PVIs.AddPVI(alinhamento.StartingStation, cotaInicio);
            perfil.PVIs.AddPVI(alinhamento.EndingStation, cotaFim);

            Civil.Baseline baseline = corredor.Baselines.Add(nomeBaseline, alinhamentoId, perfilId);
            baseline.BaselineRegions.Add("RG_1", assemblyId,
                alinhamento.StartingStation, alinhamento.EndingStation);

            return true;
        }

        /// <summary>
        /// Cota da borda de pavimento no ponto de tangência: greide da via dona da
        /// borda menos o caimento até a borda; sem greide, cai para a superfície.
        /// Retorna false quando nenhuma fonte confiável está disponível.
        /// </summary>
        private static bool TentarCotaNaBorda(
            Transaction tr,
            CivilDocument civilDoc,
            Dictionary<Guid, ViaRegistrada> vias,
            RetornoDeJuncao retorno,
            Point3d ponto,
            ObjectId superficieId,
            out double cota)
        {
            cota = 0.0;
            foreach (Guid viaId in new[] { retorno.ViaA, retorno.ViaB })
            {
                ViaRegistrada? via;
                if (!vias.TryGetValue(viaId, out via))
                {
                    continue;
                }

                string idCurto = via.Id.ToString("N").Substring(0, 8).ToUpperInvariant();
                Civil.Alignment? alinhamentoVia = LocalizarAlinhamentoPorNome(tr, civilDoc, "AL_VIA_" + idCurto);
                if (alinhamentoVia == null)
                {
                    continue;
                }

                try
                {
                    double estacao = 0.0;
                    double offset = 0.0;
                    alinhamentoVia.StationOffset(ponto.X, ponto.Y, ref estacao, ref offset);

                    double meiaLargura = via.MeiaLargura(offset >= 0 ? 1 : -1);
                    if (meiaLargura < 1e-6 || Math.Abs(Math.Abs(offset) - meiaLargura) > 0.6)
                    {
                        continue; // o ponto não está na borda desta via
                    }

                    foreach (ObjectId perfilId in alinhamentoVia.GetProfileIds())
                    {
                        Civil.Profile? perfilVia = tr.GetObject(perfilId, OpenMode.ForRead, false) as Civil.Profile;
                        if (perfilVia == null || !perfilVia.Name.StartsWith("PF_VIA_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        cota = perfilVia.ElevationAt(estacao) - DeclividadePista * Math.Abs(offset);
                        return true;
                    }
                }
                catch
                {
                }
            }

            // Sem greides das vias: usa a superfície de terreno, se houver.
            if (!superficieId.IsNull)
            {
                try
                {
                    Civil.Surface? superficie = tr.GetObject(superficieId, OpenMode.ForRead, false) as Civil.Surface;
                    if (superficie != null)
                    {
                        cota = superficie.FindElevationAtXY(ponto.X, ponto.Y);
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        /// <summary>Cruzamento dos eixos das duas vias mais próximo do arco (P).</summary>
        private static Point3d? LocalizarCentroDaJuncao(Transaction tr, Dictionary<Guid, ViaRegistrada> vias, RetornoDeJuncao retorno)
        {
            try
            {
                ViaRegistrada? viaA;
                ViaRegistrada? viaB;
                if (!vias.TryGetValue(retorno.ViaA, out viaA) || !vias.TryGetValue(retorno.ViaB, out viaB))
                {
                    return null;
                }

                Curve? eixoA = tr.GetObject(viaA.EixoId, OpenMode.ForRead, false) as Curve;
                Curve? eixoB = tr.GetObject(viaB.EixoId, OpenMode.ForRead, false) as Curve;
                if (eixoA == null || eixoB == null)
                {
                    return null;
                }

                Point3dCollection pontos = new Point3dCollection();
                eixoA.IntersectWith(eixoB, Intersect.OnBothOperands, pontos, IntPtr.Zero, IntPtr.Zero);

                Point3d? maisProximo = null;
                double melhor = double.MaxValue;
                foreach (Point3d ponto in pontos)
                {
                    double distancia = ponto.DistanceTo(retorno.PontoMedio);
                    if (distancia < melhor)
                    {
                        melhor = distancia;
                        maisProximo = ponto;
                    }
                }

                return maisProximo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Largura da pista interna: do arco até o cruzamento dos eixos + folga.</summary>
        private static double LarguraAteOCentro(Transaction tr, Dictionary<Guid, ViaRegistrada> vias, RetornoDeJuncao retorno)
        {
            Point3d? centro = retorno.PontoCentroJuncao ?? LocalizarCentroDaJuncao(tr, vias, retorno);
            if (centro.HasValue)
            {
                return Math.Max(3.0, retorno.PontoMedio.DistanceTo(centro.Value) + 1.0);
            }

            return 12.0; // largura de segurança quando o centro não é localizável
        }

        private static ObjectId ObterAssemblyDePista(
            Transaction tr,
            CivilDocument civilDoc,
            double largura,
            Dictionary<string, ObjectId> cache,
            ResultadoCorredores resultado)
        {
            // Larguras em degraus de 0,5 m para reutilizar assemblies entre retornos.
            double larguraArredondada = Math.Ceiling(largura * 2.0) / 2.0;
            string chave = larguraArredondada.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string nome = "ASM_JN_PISTA_" + chave.Replace('.', '_');

            ObjectId emCache;
            if (cache.TryGetValue(chave, out emCache))
            {
                return emCache;
            }

            ObjectId existente = LocalizarAssemblyPorNome(tr, civilDoc, nome);
            if (!existente.IsNull)
            {
                cache[chave] = existente;
                return existente;
            }

            ObjectId assemblyId = civilDoc.AssemblyCollection.Add(
                nome,
                Civil.AssemblyType.Other,
                new Point3d(60.0, -25.0 * (cache.Count + 1), 0.0));
            resultado.Assemblies++;

            try
            {
                Civil.Assembly assembly = (Civil.Assembly)tr.GetObject(assemblyId, OpenMode.ForWrite);
                string? classe = ResolverClasseStock("BasicLane");
                if (classe != null)
                {
                    ObjectId subId = civilDoc.SubassemblyCollection.ImportStockSubassembly(
                        "SA_" + nome,
                        classe,
                        new Point3d(90.0, -25.0 * cache.Count, 0.0));

                    Civil.Subassembly sub = (Civil.Subassembly)tr.GetObject(subId, OpenMode.ForWrite);
                    DefinirParametroDouble(sub, "Width", larguraArredondada);
                    assembly.AddSubassembly(subId);
                }
                else
                {
                    resultado.Avisos.Add($"Assembly \"{nome}\": BasicLane não localizada no catálogo — complete manualmente.");
                }
            }
            catch (System.Exception ex)
            {
                resultado.Avisos.Add($"Assembly \"{nome}\": montagem parcial ({ex.Message}).");
            }

            cache[chave] = assemblyId;
            return assemblyId;
        }

        // ------------------------------------------------------------------
        // Localização por nome e utilitários (espelham PlanejadorViasCorredores)
        // ------------------------------------------------------------------

        private static Civil.Corridor? LocalizarCorredorPorNome(Transaction tr, CivilDocument civilDoc, string nome)
        {
            try
            {
                foreach (object item in (IEnumerable)civilDoc.CorridorCollection)
                {
                    ObjectId id = item is ObjectId objectId
                        ? objectId
                        : item is Civil.Corridor direto ? direto.ObjectId : ObjectId.Null;
                    if (id.IsNull)
                    {
                        continue;
                    }

                    Civil.Corridor? corredor = tr.GetObject(id, OpenMode.ForRead, false) as Civil.Corridor;
                    if (corredor != null && string.Equals(corredor.Name, nome, StringComparison.OrdinalIgnoreCase))
                    {
                        return corredor;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static Civil.Alignment? LocalizarAlinhamentoPorNome(Transaction tr, CivilDocument civilDoc, string nome)
        {
            try
            {
                foreach (ObjectId id in civilDoc.GetAlignmentIds())
                {
                    Civil.Alignment? alinhamento = tr.GetObject(id, OpenMode.ForRead, false) as Civil.Alignment;
                    if (alinhamento != null && string.Equals(alinhamento.Name, nome, StringComparison.OrdinalIgnoreCase))
                    {
                        return alinhamento;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static ObjectId LocalizarAssemblyPorNome(Transaction tr, CivilDocument civilDoc, string nome)
        {
            try
            {
                foreach (object item in (IEnumerable)civilDoc.AssemblyCollection)
                {
                    ObjectId id = item is ObjectId objectId
                        ? objectId
                        : item is Civil.Assembly direto ? direto.ObjectId : ObjectId.Null;
                    if (id.IsNull)
                    {
                        continue;
                    }

                    Civil.Assembly? assembly = tr.GetObject(id, OpenMode.ForRead, false) as Civil.Assembly;
                    if (assembly != null && string.Equals(assembly.Name, nome, StringComparison.OrdinalIgnoreCase))
                    {
                        return id;
                    }
                }
            }
            catch
            {
            }

            return ObjectId.Null;
        }

        private static HashSet<string> ColetarNomesDeBaselines(Civil.Corridor corredor)
        {
            HashSet<string> nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Civil.Baseline baseline in corredor.Baselines)
                {
                    nomes.Add(baseline.Name);
                }
            }
            catch
            {
            }

            return nomes;
        }

        private static string? ResolverClasseStock(string nomeStock)
        {
            try
            {
                System.Reflection.Assembly? stockAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.IndexOf("StockSubassemblies", StringComparison.OrdinalIgnoreCase) >= 0);

                if (stockAssembly == null)
                {
                    string? pastaAcad = System.IO.Path.GetDirectoryName(
                        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
                    if (!string.IsNullOrEmpty(pastaAcad))
                    {
                        string caminho = System.IO.Path.Combine(pastaAcad, "C3D", "C3DStockSubassemblies.dll");
                        if (System.IO.File.Exists(caminho))
                        {
                            stockAssembly = System.Reflection.Assembly.LoadFrom(caminho);
                        }
                    }
                }

                Type? tipo = stockAssembly?.GetTypes().FirstOrDefault(t =>
                    string.Equals(t.Name, nomeStock, StringComparison.OrdinalIgnoreCase));
                if (tipo != null)
                {
                    return tipo.FullName;
                }
            }
            catch
            {
            }

            return "Subassembly." + nomeStock;
        }

        private static void DefinirParametroDouble(Civil.Subassembly sub, string nomeParametro, double valor)
        {
            try
            {
                foreach (Autodesk.Civil.Runtime.ParamDouble parametro in sub.ParamsDouble)
                {
                    string display = parametro.DisplayName ?? string.Empty;
                    if (display.IndexOf(nomeParametro, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        parametro.Value = valor;
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static ObjectId PrimeiroItem(IEnumerable colecao)
        {
            try
            {
                foreach (object item in colecao)
                {
                    if (item is ObjectId id && !id.IsNull)
                    {
                        return id;
                    }
                }
            }
            catch
            {
            }

            return ObjectId.Null;
        }
    }

    /// <summary>Comando dedicado aos corredores das interseções (reexecutável).</summary>
    public class PlanejadorViasCorredoresJuncoesCommand
    {
        [CommandMethod("PLANVIAS_CORREDORES_JUNCOES")]
        public void CriarCorredoresDeJuncoes()
        {
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                CivilDocument civilDoc = Manager.DocCivil;

                PromptEntityOptions opcoes = new PromptEntityOptions(
                    "\nSelecione a superfície do terreno (Enter = amarrar só nos greides das vias): ");
                opcoes.SetRejectMessage("\nSelecione uma superfície do Civil 3D.");
                opcoes.AddAllowedClass(typeof(Civil.Surface), false);
                opcoes.AllowNone = true;

                ObjectId superficieId = ObjectId.Null;
                PromptEntityResult selecao = editor.GetEntity(opcoes);
                if (selecao.Status == PromptStatus.OK)
                {
                    superficieId = selecao.ObjectId;
                }
                else if (selecao.Status != PromptStatus.None)
                {
                    editor.WriteMessage("\nGeração cancelada.");
                    return;
                }

                MapeamentoAssemblies mapeamento = MapeamentoAssemblies.Carregar();

                ResultadoCorredores resultado;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    resultado = PlanejadorViasCorredoresJuncoes.CriarCorredoresDeJuncoes(
                        tr, db, civilDoc, superficieId, editor, mapeamento);
                    tr.Commit();
                }

                editor.WriteMessage(
                    $"\nInterseções: {resultado.Alinhamentos} retorno(s) de meio-fio processado(s) no corredor " +
                    $"\"{PlanejadorViasCorredoresJuncoes.NomeCorredorJuncoes}\".");

                foreach (string aviso in resultado.Avisos.Distinct().Take(15))
                {
                    editor.WriteMessage($"\n  Aviso: {aviso}");
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD nas interseções: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro nas interseções: {ex.Message}");
            }
        }
    }
}
