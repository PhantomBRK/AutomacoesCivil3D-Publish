using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Civil = Autodesk.Civil.DatabaseServices;
using CivilRt = Autodesk.Civil.Runtime;

namespace AutomacoesCivil3D
{
    /// <summary>Resultado da geração automática de corredores.</summary>
    public sealed class ResultadoCorredores
    {
        public int Alinhamentos { get; set; }
        public int Perfis { get; set; }
        public int Assemblies { get; set; }
        public int Corredores { get; set; }
        public List<string> Avisos { get; } = new List<string>();
    }

    /// <summary>
    /// Geração automática da cadeia Civil 3D — Alinhamento → Perfil → Assembly →
    /// Corredor — para cada via do Planejador de Vias. O greide pode seguir uma
    /// superfície de terreno (PVIs amostrados) ou ficar plano na cota 0; as regiões
    /// do corredor param nas caixas das junções formadas em planta.
    /// </summary>
    public static class PlanejadorViasCorredores
    {
        private const double PassoPerfilMetros = 20.0;
        private const double ComprimentoMinimoRegiao = 1.0;
        private const double ComprimentoCurvaVertical = 40.0; // parábola simétrica nos PVIs
        private const double RaioSuavizacaoGreide = 15.0;     // média móvel da cota do terreno
        private const double FolgaPviJuncao = 12.0;           // amostras regulares afastadas das junções

        public static ResultadoCorredores CriarCorredores(
            Transaction tr,
            Database db,
            CivilDocument civilDoc,
            ObjectId superficieId,
            double raioMeioFio,
            Editor? editor,
            MapeamentoAssemblies mapeamento)
        {
            ResultadoCorredores resultado = new ResultadoCorredores();

            Dictionary<Guid, ViaRegistrada> vias = PlanejadorViasJuncoes.CarregarVias(tr, db);
            if (vias.Count == 0)
            {
                resultado.Avisos.Add("Nenhuma via do Planejador encontrada no desenho.");
                return resultado;
            }

            // Assemblies são reutilizadas entre vias com a mesma seção-tipo.
            Dictionary<string, ObjectId> assembliesPorSecao = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            int posicaoAssembly = 0;

            // Cota única por ponto de cruzamento: as duas vias recebem um PVI com a
            // MESMA cota na junção, eliminando degraus entre os corredores.
            Dictionary<(long, long), double> cotasDasJuncoes = new Dictionary<(long, long), double>();

            foreach (ViaRegistrada via in vias.Values.OrderBy(v => v.Secao, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    CriarParaVia(tr, db, civilDoc, via, vias, superficieId, raioMeioFio,
                        assembliesPorSecao, ref posicaoAssembly, resultado, editor, mapeamento,
                        cotasDasJuncoes);
                }
                catch (System.Exception ex)
                {
                    resultado.Avisos.Add($"Via \"{via.Secao}\" ({via.Id.ToString("N").Substring(0, 8)}): {ex.Message}");
                }
            }

            return resultado;
        }

        // ------------------------------------------------------------------
        // Uma via: alinhamento + perfil + assembly + corredor
        // ------------------------------------------------------------------

        private static void CriarParaVia(
            Transaction tr,
            Database db,
            CivilDocument civilDoc,
            ViaRegistrada via,
            Dictionary<Guid, ViaRegistrada> todasVias,
            ObjectId superficieId,
            double raioMeioFio,
            Dictionary<string, ObjectId> assembliesPorSecao,
            ref int posicaoAssembly,
            ResultadoCorredores resultado,
            Editor? editor,
            MapeamentoAssemblies mapeamento,
            Dictionary<(long, long), double> cotasDasJuncoes)
        {
            Polyline? eixo = tr.GetObject(via.EixoId, OpenMode.ForRead, false) as Polyline;
            if (eixo == null || eixo.IsErased)
            {
                return;
            }

            string idCurto = via.Id.ToString("N").Substring(0, 8).ToUpperInvariant();
            string nomeVia = "VIA_" + idCurto;

            // 1. Alinhamento a partir do eixo (a polyline original é mantida).
            Civil.PolylineOptions opcoesPolyline = new Civil.PolylineOptions
            {
                AddCurvesBetweenTangents = false,
                EraseExistingEntities = false,
                PlineId = via.EixoId
            };

            ObjectId alinhamentoId;
            try
            {
                alinhamentoId = Civil.Alignment.Create(
                    civilDoc,
                    opcoesPolyline,
                    "AL_" + nomeVia,
                    ObjectId.Null, // sem site
                    db.Clayer,
                    PrimeiroItem(civilDoc.Styles.AlignmentStyles),
                    PrimeiroItem(civilDoc.Styles.LabelSetStyles.AlignmentLabelSetStyles));
            }
            catch (ArgumentException)
            {
                resultado.Avisos.Add($"{nomeVia}: alinhamento já existe — via pulada (apague AL_{nomeVia} para regerar).");
                return;
            }

            resultado.Alinhamentos++;
            Civil.Alignment alinhamento = (Civil.Alignment)tr.GetObject(alinhamentoId, OpenMode.ForRead);

            // 2. Perfil de greide (colado ao terreno ou plano na cota 0).
            ObjectId perfilId = Civil.Profile.CreateByLayout(
                "PF_" + nomeVia,
                alinhamentoId,
                db.Clayer,
                PrimeiroItem(civilDoc.Styles.ProfileStyles),
                PrimeiroItem(civilDoc.Styles.LabelSetStyles.ProfileLabelSetStyles));

            Civil.Profile perfil = (Civil.Profile)tr.GetObject(perfilId, OpenMode.ForWrite);
            List<(double Estacao, double Cota)> pvisDeJuncao = PVIsDeJuncao(
                tr, via, todasVias, alinhamento, superficieId, cotasDasJuncoes);
            AdicionarPVIs(tr, perfil, alinhamento, superficieId, nomeVia, resultado.Avisos, pvisDeJuncao);
            resultado.Perfis++;

            // 3. Assembly da seção-tipo: mapeamento do usuário → seleção na tela →
            //    montagem automática (opcional). Sem assembly, o corredor não é criado.
            ObjectId assemblyId = ResolverAssemblyDaSecao(
                tr, civilDoc, via.Secao, assembliesPorSecao, ref posicaoAssembly, resultado, editor, mapeamento);
            if (assemblyId.IsNull)
            {
                resultado.Avisos.Add($"{nomeVia} (\"{via.Secao}\"): assembly não definida — alinhamento e perfil " +
                    "criados, corredor NÃO. Use PLANVIAS_DEFINIR_ASSEMBLIES e rode novamente.");
                return;
            }

            // 4. Corredor com regiões paradas nas junções.
            ObjectId corredorId = civilDoc.CorridorCollection.Add("COR_" + nomeVia);
            Civil.Corridor corredor = (Civil.Corridor)tr.GetObject(corredorId, OpenMode.ForWrite);
            Civil.Baseline baseline = corredor.Baselines.Add("BL_" + nomeVia, alinhamentoId, perfilId);

            List<(double Inicio, double Fim)> intervalos = CalcularIntervalosDeRegiao(
                tr, via, todasVias, alinhamento, raioMeioFio);

            int numeroRegiao = 1;
            foreach ((double inicio, double fim) in intervalos)
            {
                baseline.BaselineRegions.Add($"RG_{numeroRegiao++}", assemblyId, inicio, fim);
            }

            if (numeroRegiao == 1)
            {
                baseline.BaselineRegions.Add("RG_1", assemblyId,
                    alinhamento.StartingStation, alinhamento.EndingStation);
            }

            try
            {
                corredor.Rebuild();
            }
            catch (System.Exception ex)
            {
                resultado.Avisos.Add($"{nomeVia}: corredor criado, mas o rebuild falhou ({ex.Message}). " +
                    "Confira a assembly e reconstrua manualmente.");
            }

            resultado.Corredores++;
        }

        // ------------------------------------------------------------------
        // Perfil: PVIs amostrados na superfície (ou plano na cota 0)
        // ------------------------------------------------------------------

        /// <summary>
        /// Estações e cotas dos cruzamentos desta via com as demais. A cota de cada
        /// cruzamento é única (dicionário por ponto), garantindo que as duas vias se
        /// encontrem exatamente na mesma altura na junção.
        /// </summary>
        internal static List<(double Estacao, double Cota)> PVIsDeJuncao(
            Transaction tr,
            ViaRegistrada via,
            Dictionary<Guid, ViaRegistrada> todasVias,
            Civil.Alignment alinhamento,
            ObjectId superficieId,
            Dictionary<(long, long), double> cotasDasJuncoes)
        {
            List<(double, double)> resultado = new List<(double, double)>();

            Civil.Surface? superficie = null;
            if (!superficieId.IsNull)
            {
                superficie = tr.GetObject(superficieId, OpenMode.ForRead, false) as Civil.Surface;
            }

            Curve? eixo = tr.GetObject(via.EixoId, OpenMode.ForRead, false) as Curve;
            if (eixo == null)
            {
                return resultado;
            }

            foreach (ViaRegistrada outra in todasVias.Values.Where(v => v.Id != via.Id))
            {
                Curve? eixoOutra = tr.GetObject(outra.EixoId, OpenMode.ForRead, false) as Curve;
                if (eixoOutra == null || eixoOutra.IsErased)
                {
                    continue;
                }

                Point3dCollection pontos = new Point3dCollection();
                try
                {
                    eixo.IntersectWith(eixoOutra, Intersect.OnBothOperands, pontos, IntPtr.Zero, IntPtr.Zero);
                }
                catch
                {
                    continue;
                }

                foreach (Point3d ponto in pontos)
                {
                    try
                    {
                        double estacao = 0.0;
                        double offset = 0.0;
                        alinhamento.StationOffset(ponto.X, ponto.Y, ref estacao, ref offset);

                        (long, long) chave = ((long)Math.Round(ponto.X * 100.0), (long)Math.Round(ponto.Y * 100.0));
                        double cota;
                        if (!cotasDasJuncoes.TryGetValue(chave, out cota))
                        {
                            cota = superficie != null
                                ? superficie.FindElevationAtXY(ponto.X, ponto.Y)
                                : 0.0;
                            cotasDasJuncoes[chave] = cota;
                        }

                        resultado.Add((estacao, cota));
                    }
                    catch
                    {
                    }
                }
            }

            return resultado
                .OrderBy(p => p.Item1)
                .ToList();
        }

        internal static void AdicionarPVIs(
            Transaction tr,
            Civil.Profile perfil,
            Civil.Alignment alinhamento,
            ObjectId superficieId,
            string nomeVia,
            List<string> avisos,
            List<(double Estacao, double Cota)> pvisDeJuncao)
        {
            double inicio = alinhamento.StartingStation;
            double fim = alinhamento.EndingStation;

            Civil.Surface? superficie = null;
            if (!superficieId.IsNull)
            {
                superficie = tr.GetObject(superficieId, OpenMode.ForRead, false) as Civil.Surface;
            }

            List<(double Estacao, double Cota)> pvis = new List<(double, double)>();

            if (superficie != null)
            {
                for (double estacao = inicio; estacao < fim + PassoPerfilMetros / 2.0; estacao += PassoPerfilMetros)
                {
                    double estacaoLimitada = Math.Min(estacao, fim);

                    // Perto das junções quem manda é o PVI da junção (cota compartilhada).
                    bool pertoDeJuncao = pvisDeJuncao.Any(j => Math.Abs(j.Estacao - estacaoLimitada) < FolgaPviJuncao);
                    if (!pertoDeJuncao)
                    {
                        double cota;
                        if (TentarCotaSuavizada(superficie, alinhamento, estacaoLimitada, inicio, fim, out cota))
                        {
                            pvis.Add((estacaoLimitada, cota));
                        }
                    }

                    if (estacaoLimitada >= fim)
                    {
                        break;
                    }
                }
            }

            // PVIs das junções entram sempre (cota única entre as vias).
            pvis.AddRange(pvisDeJuncao.Where(j => j.Estacao > inicio + 0.5 && j.Estacao < fim - 0.5));

            if (pvis.Count < 2)
            {
                if (superficie != null)
                {
                    avisos.Add($"{nomeVia}: eixo fora da superfície — greide plano na cota 0 aplicado.");
                }

                pvis.Clear();
                pvis.Add((inicio, 0.0));
                pvis.Add((fim, 0.0));
            }

            // Ordena e remove estações praticamente coincidentes.
            List<(double Estacao, double Cota)> ordenados = pvis
                .OrderBy(p => p.Estacao)
                .ToList();
            List<(double Estacao, double Cota)> finais = new List<(double, double)>();
            foreach ((double estacao, double cota) in ordenados)
            {
                if (finais.Count == 0 || estacao - finais[finais.Count - 1].Estacao > 1.0)
                {
                    finais.Add((estacao, cota));
                }
            }

            for (int i = 0; i < finais.Count; i++)
            {
                (double estacao, double cota) = finais[i];
                try
                {
                    bool interno = i > 0 && i < finais.Count - 1;
                    if (interno)
                    {
                        // Curva vertical parabólica no PVI, limitada pela distância aos
                        // vizinhos para as curvas não se sobreporem.
                        double distanciaVizinhos = Math.Min(
                            estacao - finais[i - 1].Estacao,
                            finais[i + 1].Estacao - estacao);
                        double comprimentoCurva = Math.Min(ComprimentoCurvaVertical, 0.8 * distanciaVizinhos);

                        if (comprimentoCurva >= 5.0)
                        {
                            perfil.PVIs.AddPVISymParabola(estacao, cota, comprimentoCurva);
                            continue;
                        }
                    }

                    perfil.PVIs.AddPVI(estacao, cota);
                }
                catch
                {
                    try
                    {
                        perfil.PVIs.AddPVI(estacao, cota);
                    }
                    catch
                    {
                        // PVI coincidente/inválido: segue com os demais.
                    }
                }
            }
        }

        /// <summary>
        /// Cota do terreno na estação com média móvel (±RaioSuavizacaoGreide) para o
        /// greide não copiar cada ruído da superfície.
        /// </summary>
        private static bool TentarCotaSuavizada(
            Civil.Surface superficie,
            Civil.Alignment alinhamento,
            double estacao,
            double inicio,
            double fim,
            out double cota)
        {
            cota = 0.0;
            double soma = 0.0;
            int amostras = 0;

            foreach (double deslocamento in new[] { -RaioSuavizacaoGreide, 0.0, RaioSuavizacaoGreide })
            {
                double estacaoAmostra = Math.Max(inicio, Math.Min(fim, estacao + deslocamento));
                try
                {
                    double x = 0.0;
                    double y = 0.0;
                    alinhamento.PointLocation(estacaoAmostra, 0.0, ref x, ref y);
                    soma += superficie.FindElevationAtXY(x, y);
                    amostras++;
                }
                catch
                {
                    // Amostra fora da superfície: ignora.
                }
            }

            if (amostras == 0)
            {
                return false;
            }

            cota = soma / amostras;
            return true;
        }

        // ------------------------------------------------------------------
        // Assembly montada automaticamente a partir da seção-tipo
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolve a assembly de uma seção-tipo na ordem: mapeamento salvo do usuário →
        /// assembly homônima no desenho → seleção interativa na tela (memorizada) →
        /// montagem automática (apenas se o usuário escolher). Null = sem assembly.
        /// </summary>
        public static ObjectId ResolverAssemblyDaSecao(
            Transaction tr,
            CivilDocument civilDoc,
            string nomeSecao,
            Dictionary<string, ObjectId> cache,
            ref int posicaoAssembly,
            ResultadoCorredores resultado,
            Editor? editor,
            MapeamentoAssemblies mapeamento)
        {
            string chave = string.IsNullOrWhiteSpace(nomeSecao) ? "(sem seção)" : nomeSecao;

            ObjectId emCache;
            if (cache.TryGetValue(chave, out emCache))
            {
                return emCache;
            }

            // a) Mapeamento persistido do usuário.
            string? nomeMapeado;
            if (mapeamento.PorSecao.TryGetValue(chave, out nomeMapeado) && !string.IsNullOrWhiteSpace(nomeMapeado))
            {
                ObjectId mapeada = LocalizarAssemblyPorNome(tr, civilDoc, nomeMapeado);
                if (!mapeada.IsNull)
                {
                    cache[chave] = mapeada;
                    return mapeada;
                }

                resultado.Avisos.Add($"Assembly mapeada \"{nomeMapeado}\" (seção \"{chave}\") não existe neste desenho.");
            }

            // b) Assembly homônima já existente no desenho.
            ObjectId homonima = LocalizarAssemblyPorNome(tr, civilDoc, chave);
            if (!homonima.IsNull)
            {
                cache[chave] = homonima;
                return homonima;
            }

            // c) Seleção interativa (memorizada no mapeamento para as próximas vezes).
            if (editor != null)
            {
                ObjectId selecionada = SelecionarAssembly(
                    tr, editor,
                    $"\nSelecione a ASSEMBLY para a seção \"{chave}\"",
                    out bool quisAutomatica);

                if (!selecionada.IsNull)
                {
                    Civil.Assembly? assemblySelecionada =
                        tr.GetObject(selecionada, OpenMode.ForRead, false) as Civil.Assembly;
                    if (assemblySelecionada != null)
                    {
                        mapeamento.PorSecao[chave] = assemblySelecionada.Name;
                        mapeamento.Salvar();
                    }

                    cache[chave] = selecionada;
                    return selecionada;
                }

                if (!quisAutomatica)
                {
                    cache[chave] = ObjectId.Null;
                    return ObjectId.Null; // usuário pulou
                }
            }

            // d) Montagem automática com o catálogo stock (melhor esforço).
            ObjectId automatica = ObterOuCriarAssembly(tr, civilDoc, nomeSecao, cache, ref posicaoAssembly, resultado);
            cache[chave] = automatica;
            return automatica;
        }

        /// <summary>
        /// Prompt de seleção de assembly com opções [Automatica/Pular].
        /// Retorna Null quando o usuário pula; quisAutomatica indica a keyword.
        /// </summary>
        public static ObjectId SelecionarAssembly(Transaction tr, Editor editor, string mensagem, out bool quisAutomatica)
        {
            quisAutomatica = false;

            PromptEntityOptions opcoes = new PromptEntityOptions(
                mensagem + " ou [Automatica/Pular] <Pular>: ");
            opcoes.SetRejectMessage("\nIsso não é uma assembly do Civil 3D.");
            opcoes.AddAllowedClass(typeof(Civil.Assembly), false);
            opcoes.Keywords.Add("Automatica");
            opcoes.Keywords.Add("Pular");
            opcoes.AllowNone = true;

            while (true)
            {
                PromptEntityResult selecao = editor.GetEntity(opcoes);
                if (selecao.Status == PromptStatus.OK)
                {
                    return selecao.ObjectId;
                }

                if (selecao.Status == PromptStatus.Keyword)
                {
                    if (selecao.StringResult == "Automatica")
                    {
                        quisAutomatica = true;
                    }

                    return ObjectId.Null;
                }

                return ObjectId.Null; // Enter/Esc = pular
            }
        }

        private static ObjectId ObterOuCriarAssembly(
            Transaction tr,
            CivilDocument civilDoc,
            string nomeSecao,
            Dictionary<string, ObjectId> cache,
            ref int posicaoAssembly,
            ResultadoCorredores resultado)
        {
            string chave = string.IsNullOrWhiteSpace(nomeSecao) ? "(sem seção)" : nomeSecao;

            ObjectId existenteCache;
            if (cache.TryGetValue(chave, out existenteCache))
            {
                return existenteCache;
            }

            // a) Assembly já existente no desenho com o nome da seção (feita pelo usuário).
            ObjectId existente = LocalizarAssemblyPorNome(tr, civilDoc, chave);
            if (!existente.IsNull)
            {
                cache[chave] = existente;
                return existente;
            }

            // b) Montagem automática com subassemblies de catálogo.
            SecaoTipoVia? secao = SecoesTipoCatalogo.ObterPorNome(nomeSecao);
            string nomeAssembly = SanitizarNome("ASM_" + chave);
            Point3d posicao = new Point3d(0.0, -25.0 * (++posicaoAssembly), 0.0);

            Civil.AssemblyType tipo = secao != null && TemCanteiroCentral(secao)
                ? Civil.AssemblyType.DividedCrownedRoad
                : Civil.AssemblyType.UndividedCrownedRoad;

            ObjectId assemblyId = civilDoc.AssemblyCollection.Add(nomeAssembly, tipo, posicao);
            resultado.Assemblies++;

            if (secao == null)
            {
                resultado.Avisos.Add($"Seção \"{chave}\" não encontrada no catálogo: assembly \"{nomeAssembly}\" " +
                    "criada vazia — insira as subassemblies e reconstrua os corredores.");
                cache[chave] = assemblyId;
                return assemblyId;
            }

            try
            {
                MontarSubassemblies(tr, civilDoc, assemblyId, secao, posicao, resultado.Avisos);
            }
            catch (System.Exception ex)
            {
                resultado.Avisos.Add($"Assembly \"{nomeAssembly}\": montagem automática parcial ({ex.Message}). " +
                    "Complete pela paleta de subassemblies e reconstrua os corredores.");
            }

            cache[chave] = assemblyId;
            return assemblyId;
        }

        /// <summary>Canteiro central = elemento Canteiro cuja faixa de offsets cruza o eixo.</summary>
        private static bool TemCanteiroCentral(SecaoTipoVia secao)
        {
            double metade = secao.LarguraTotal / 2.0;
            double acumulado = 0.0;
            foreach (ElementoSecaoVia elemento in secao.Elementos)
            {
                double esquerda = metade - acumulado;
                acumulado += Math.Max(0.0, elemento.Largura);
                double direita = metade - acumulado;
                if (elemento.Tipo == TipoElementoVia.Canteiro && esquerda > 1e-6 && direita < -1e-6)
                {
                    return true;
                }
            }

            return false;
        }

        private static ObjectId LocalizarAssemblyPorNome(Transaction tr, CivilDocument civilDoc, string nome)
        {
            try
            {
                foreach (object item in (IEnumerable)civilDoc.AssemblyCollection)
                {
                    ObjectId id = ObjectId.Null;
                    if (item is ObjectId objectId)
                    {
                        id = objectId;
                    }
                    else if (item is Civil.Assembly direto)
                    {
                        id = direto.ObjectId;
                    }

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

        /// <summary>
        /// Monta o lado direito da seção (do eixo para fora) com subassemblies de
        /// catálogo e espelha para o lado esquerdo. Elemento central (canteiro)
        /// entra com meia largura em cada lado.
        /// </summary>
        private static void MontarSubassemblies(
            Transaction tr,
            CivilDocument civilDoc,
            ObjectId assemblyId,
            SecaoTipoVia secao,
            Point3d posicaoBase,
            List<string> avisos)
        {
            List<(TipoElementoVia Tipo, double Largura)> ladoDireito = ExtrairLadoDireito(secao);
            if (ladoDireito.Count == 0)
            {
                avisos.Add($"Seção \"{secao.Nome}\": nenhum elemento no lado direito — assembly vazia.");
                return;
            }

            Civil.Assembly assembly = (Civil.Assembly)tr.GetObject(assemblyId, OpenMode.ForWrite);

            List<ObjectId> criadosDireita = new List<ObjectId>();
            Civil.Subassembly? anterior = null;
            int indice = 0;

            foreach ((TipoElementoVia tipo, double largura) in ladoDireito)
            {
                indice++;
                string stock = NomeStock(tipo);
                string? classe = ResolverClasseStock(stock);
                if (classe == null)
                {
                    avisos.Add($"Subassembly de catálogo \"{stock}\" não localizada — " +
                        $"elemento {TipoElementoViaInfo.Rotulo(tipo)} omitido da assembly.");
                    continue;
                }

                string nomeSub = SanitizarNome($"SA_{secao.Nome}_D{indice}_{stock}");
                ObjectId subId;
                try
                {
                    subId = civilDoc.SubassemblyCollection.ImportStockSubassembly(
                        nomeSub,
                        classe,
                        new Point3d(posicaoBase.X + 30.0, posicaoBase.Y - 5.0 * indice, 0.0));
                }
                catch (System.Exception ex)
                {
                    avisos.Add($"Falha ao importar \"{stock}\" ({ex.Message}) — elemento omitido.");
                    continue;
                }

                Civil.Subassembly sub = (Civil.Subassembly)tr.GetObject(subId, OpenMode.ForWrite);
                DefinirParametroDouble(sub, "Width", largura);

                if (anterior == null)
                {
                    assembly.AddSubassembly(subId);
                }
                else
                {
                    Civil.Point? ancora = PontoExterno(anterior);
                    if (ancora != null)
                    {
                        assembly.AddSubassembly(subId, ancora);
                    }
                    else
                    {
                        assembly.AddSubassembly(subId);
                    }
                }

                criadosDireita.Add(subId);
                anterior = sub;
            }

            // Espelha a cadeia para o lado esquerdo (seções do Planejador são simétricas;
            // seções assimétricas ficam aproximadas e são apontadas em aviso).
            foreach (ObjectId subId in criadosDireita)
            {
                try
                {
                    assembly.MirrorSubassembly(subId);
                }
                catch (System.Exception ex)
                {
                    avisos.Add($"Espelhamento de subassembly falhou ({ex.Message}) — complete o lado esquerdo manualmente.");
                    break;
                }
            }

            if (EhAssimetrica(secao))
            {
                avisos.Add($"Seção \"{secao.Nome}\" é assimétrica: a assembly foi montada espelhando o lado direito — ajuste o lado esquerdo se necessário.");
            }
        }

        /// <summary>Elementos do lado direito (offset negativo), do eixo para fora.</summary>
        private static List<(TipoElementoVia Tipo, double Largura)> ExtrairLadoDireito(SecaoTipoVia secao)
        {
            List<(TipoElementoVia, double)> resultado = new List<(TipoElementoVia, double)>();
            double metade = secao.LarguraTotal / 2.0;
            double acumulado = 0.0;

            foreach (ElementoSecaoVia elemento in secao.Elementos)
            {
                double bordaEsquerda = metade - acumulado;             // offset assinado (esquerda +)
                acumulado += Math.Max(0.0, elemento.Largura);
                double bordaDireita = metade - acumulado;

                // Trecho do elemento no semi-plano direito (offsets <= 0).
                double larguraDireita = Math.Min(bordaEsquerda, 0.0) - bordaDireita;
                if (larguraDireita > 1e-6)
                {
                    resultado.Add((elemento.Tipo, larguraDireita));
                }
            }

            return resultado;
        }

        private static bool EhAssimetrica(SecaoTipoVia secao)
        {
            List<ElementoSecaoVia> elementos = secao.Elementos;
            for (int i = 0; i < elementos.Count / 2; i++)
            {
                ElementoSecaoVia a = elementos[i];
                ElementoSecaoVia b = elementos[elementos.Count - 1 - i];
                if (a.Tipo != b.Tipo || Math.Abs(a.Largura - b.Largura) > 1e-6)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NomeStock(TipoElementoVia tipo)
        {
            switch (tipo)
            {
                case TipoElementoVia.MeioFio: return "BasicCurbAndGutter";
                case TipoElementoVia.Calcada: return "BasicSideWalk";
                default: return "BasicLane"; // faixa, acostamento, estacionamento, ciclovia, canteiro
            }
        }

        /// <summary>
        /// Descobre o nome completo da classe da subassembly de catálogo por reflexão
        /// no C3DStockSubassemblies.dll da instalação do Civil 3D.
        /// </summary>
        private static string? ResolverClasseStock(string nomeStock)
        {
            try
            {
                System.Reflection.Assembly? stockAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.IndexOf("StockSubassemblies", StringComparison.OrdinalIgnoreCase) >= 0);

                if (stockAssembly == null)
                {
                    string? pastaAcad = Path.GetDirectoryName(
                        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
                    if (!string.IsNullOrEmpty(pastaAcad))
                    {
                        string caminho = Path.Combine(pastaAcad, "C3D", "C3DStockSubassemblies.dll");
                        if (File.Exists(caminho))
                        {
                            stockAssembly = System.Reflection.Assembly.LoadFrom(caminho);
                        }
                    }
                }

                if (stockAssembly != null)
                {
                    Type? tipo = stockAssembly.GetTypes().FirstOrDefault(t =>
                        string.Equals(t.Name, nomeStock, StringComparison.OrdinalIgnoreCase));
                    if (tipo != null)
                    {
                        return tipo.FullName;
                    }
                }
            }
            catch
            {
            }

            // Fallback: convenção histórica dos stocks .NET do Civil 3D.
            return "Subassembly." + nomeStock;
        }

        private static void DefinirParametroDouble(Civil.Subassembly sub, string nomeParametro, double valor)
        {
            try
            {
                foreach (CivilRt.ParamDouble parametro in sub.ParamsDouble)
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
                // Parâmetro indisponível: mantém o padrão da subassembly.
            }
        }

        /// <summary>Ponto de conexão mais externo de uma subassembly (códigos P4/P3/P2).</summary>
        private static Civil.Point? PontoExterno(Civil.Subassembly sub)
        {
            try
            {
                Civil.Point? candidato = null;
                foreach (string codigoPreferido in new[] { "P4", "P3", "P2" })
                {
                    foreach (Civil.Point ponto in sub.Points)
                    {
                        candidato ??= ponto;
                        foreach (string codigo in ponto.Codes)
                        {
                            if (string.Equals(codigo, codigoPreferido, StringComparison.OrdinalIgnoreCase))
                            {
                                return ponto;
                            }
                        }
                    }
                }

                return candidato;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Regiões do corredor paradas nas caixas das junções
        // ------------------------------------------------------------------

        private static List<(double Inicio, double Fim)> CalcularIntervalosDeRegiao(
            Transaction tr,
            ViaRegistrada via,
            Dictionary<Guid, ViaRegistrada> todasVias,
            Civil.Alignment alinhamento,
            double raioMeioFio)
        {
            double inicio = alinhamento.StartingStation;
            double fim = alinhamento.EndingStation;

            List<(double Estacao, double Folga)> cortes = new List<(double, double)>();

            Curve? eixo = tr.GetObject(via.EixoId, OpenMode.ForRead, false) as Curve;
            if (eixo != null)
            {
                foreach (ViaRegistrada outra in todasVias.Values.Where(v => v.Id != via.Id))
                {
                    Curve? eixoOutra = tr.GetObject(outra.EixoId, OpenMode.ForRead, false) as Curve;
                    if (eixoOutra == null || eixoOutra.IsErased)
                    {
                        continue;
                    }

                    Point3dCollection pontos = new Point3dCollection();
                    try
                    {
                        eixo.IntersectWith(eixoOutra, Intersect.OnBothOperands, pontos, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch
                    {
                        continue;
                    }

                    double folga = Math.Max(outra.WEsq, outra.WDir) + raioMeioFio + 0.5;
                    foreach (Point3d ponto in pontos)
                    {
                        try
                        {
                            double estacao = 0.0;
                            double offset = 0.0;
                            alinhamento.StationOffset(ponto.X, ponto.Y, ref estacao, ref offset);
                            cortes.Add((estacao, folga));
                        }
                        catch
                        {
                        }
                    }
                }
            }

            List<(double, double)> intervalos = new List<(double, double)> { (inicio, fim) };
            foreach ((double estacao, double folga) in cortes.OrderBy(c => c.Estacao))
            {
                double abertoInicio = estacao - folga;
                double abertoFim = estacao + folga;

                List<(double, double)> novos = new List<(double, double)>();
                foreach ((double a, double b) in intervalos)
                {
                    if (abertoFim <= a || abertoInicio >= b)
                    {
                        novos.Add((a, b));
                        continue;
                    }

                    if (abertoInicio > a)
                    {
                        novos.Add((a, abertoInicio));
                    }

                    if (abertoFim < b)
                    {
                        novos.Add((abertoFim, b));
                    }
                }

                intervalos = novos;
            }

            return intervalos.Where(i => i.Item2 - i.Item1 >= ComprimentoMinimoRegiao).ToList();
        }

        // ------------------------------------------------------------------
        // Utilitários
        // ------------------------------------------------------------------

        /// <summary>Primeiro ObjectId de uma coleção de estilos (ou Null se vazia).</summary>
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

        private static string SanitizarNome(string nome)
        {
            string limpo = Regex.Replace(nome, "[<>/\\\\\"':;\\?\\*\\|,=`]", "_").Trim();
            return limpo.Length > 60 ? limpo.Substring(0, 60) : limpo;
        }
    }

    /// <summary>Comando de geração automática de corredores do Planejador de Vias.</summary>
    public class PlanejadorViasCorredoresCommand
    {
        [CommandMethod("PLANVIAS_CRIAR_CORREDORES")]
        public void CriarCorredores()
        {
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                CivilDocument civilDoc = Manager.DocCivil;

                // Superfície do terreno para o greide (opcional).
                PromptEntityOptions opcoes = new PromptEntityOptions(
                    "\nSelecione a superfície do terreno para o greide (Enter = greide plano na cota 0): ");
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
                    editor.WriteMessage("\nGeração de corredores cancelada.");
                    return;
                }

                MapeamentoAssemblies mapeamento = MapeamentoAssemblies.Carregar();

                ResultadoCorredores resultado;
                ResultadoCorredores resultadoJuncoes;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    resultado = PlanejadorViasCorredores.CriarCorredores(
                        tr, db, civilDoc, superficieId, PlanejadorViasEstado.Opcoes.RaioMeioFio,
                        editor, mapeamento);

                    // Interseções: baselines nos retornos de meio-fio, amarradas aos
                    // greides recém-criados das vias.
                    resultadoJuncoes = PlanejadorViasCorredoresJuncoes.CriarCorredoresDeJuncoes(
                        tr, db, civilDoc, superficieId, editor, mapeamento);

                    tr.Commit();
                }

                editor.WriteMessage(
                    $"\nCorredores do Planejador de Vias: {resultado.Alinhamentos} alinhamento(s), " +
                    $"{resultado.Perfis} perfil(is), {resultado.Assemblies} assembly(ies) nova(s), " +
                    $"{resultado.Corredores} corredor(es) criado(s).");
                editor.WriteMessage(
                    $"\nInterseções: {resultadoJuncoes.Alinhamentos} retorno(s) de meio-fio processado(s) " +
                    $"no corredor \"{PlanejadorViasCorredoresJuncoes.NomeCorredorJuncoes}\".");
                resultado.Avisos.AddRange(resultadoJuncoes.Avisos);

                foreach (string aviso in resultado.Avisos.Distinct().Take(15))
                {
                    editor.WriteMessage($"\n  Aviso: {aviso}");
                }

                if (resultado.Avisos.Count > 15)
                {
                    editor.WriteMessage($"\n  (+{resultado.Avisos.Count - 15} avisos omitidos)");
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD ao criar corredores: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro ao criar corredores: {ex.Message}");
            }
        }

        /// <summary>
        /// Regera os greides das vias já processadas SEM recriar nada: limpa os PVIs
        /// dos perfis PF_VIA_* existentes, reaplica o greide novo (curvas verticais,
        /// suavização e cota única nas junções) e reconstrói os corredores.
        /// </summary>
        [CommandMethod("PLANVIAS_REGERAR_GREIDES")]
        public void RegerarGreides()
        {
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                CivilDocument civilDoc = Manager.DocCivil;

                PromptEntityOptions opcoes = new PromptEntityOptions(
                    "\nSelecione a superfície do terreno para o greide (Enter = greide plano na cota 0): ");
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
                    editor.WriteMessage("\nRegeneração cancelada.");
                    return;
                }

                int regerados = 0;
                List<string> avisos = new List<string>();

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Dictionary<Guid, ViaRegistrada> vias = PlanejadorViasJuncoes.CarregarVias(tr, db);
                    Dictionary<(long, long), double> cotasDasJuncoes = new Dictionary<(long, long), double>();

                    foreach (ViaRegistrada via in vias.Values)
                    {
                        try
                        {
                            string idCurto = via.Id.ToString("N").Substring(0, 8).ToUpperInvariant();
                            Civil.Alignment? alinhamento = LocalizarAlinhamento(tr, civilDoc, "AL_VIA_" + idCurto);
                            if (alinhamento == null)
                            {
                                continue; // via ainda sem corredor: use PLANVIAS_CRIAR_CORREDORES
                            }

                            Civil.Profile? perfil = null;
                            foreach (ObjectId perfilId in alinhamento.GetProfileIds())
                            {
                                Civil.Profile? candidato = tr.GetObject(perfilId, OpenMode.ForRead, false) as Civil.Profile;
                                if (candidato != null &&
                                    candidato.Name.StartsWith("PF_VIA_", StringComparison.OrdinalIgnoreCase))
                                {
                                    perfil = candidato;
                                    break;
                                }
                            }

                            if (perfil == null)
                            {
                                avisos.Add($"VIA_{idCurto}: perfil PF_VIA_ não encontrado — pulada.");
                                continue;
                            }

                            perfil.UpgradeOpen();
                            LimparPVIs(perfil);

                            List<(double Estacao, double Cota)> pvisDeJuncao = PlanejadorViasCorredores.PVIsDeJuncao(
                                tr, via, vias, alinhamento, superficieId, cotasDasJuncoes);
                            PlanejadorViasCorredores.AdicionarPVIs(tr, perfil, alinhamento, superficieId, "VIA_" + idCurto,
                                avisos, pvisDeJuncao);

                            RebuildCorredorPorNome(tr, civilDoc, "COR_VIA_" + idCurto, avisos);
                            regerados++;
                        }
                        catch (System.Exception ex)
                        {
                            avisos.Add($"Via {via.Id.ToString("N").Substring(0, 8)}: {ex.Message}");
                        }
                    }

                    RebuildCorredorPorNome(tr, civilDoc, PlanejadorViasCorredoresJuncoes.NomeCorredorJuncoes, avisos);
                    tr.Commit();
                }

                editor.WriteMessage($"\n{regerados} greide(s) regenerado(s) com curvas verticais, suavização " +
                    "e cotas casadas nas junções; corredores reconstruídos.");
                foreach (string aviso in avisos.Distinct().Take(12))
                {
                    editor.WriteMessage($"\n  Aviso: {aviso}");
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD ao regerar greides: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro ao regerar greides: {ex.Message}");
            }
        }

        private static void LimparPVIs(Civil.Profile perfil)
        {
            List<Civil.ProfilePVI> existentes = new List<Civil.ProfilePVI>();
            foreach (Civil.ProfilePVI pvi in perfil.PVIs)
            {
                existentes.Add(pvi);
            }

            foreach (Civil.ProfilePVI pvi in existentes)
            {
                try
                {
                    perfil.PVIs.Remove(pvi);
                }
                catch
                {
                    // Alguns PVIs de extremidade podem resistir; os novos sobrescrevem.
                }
            }
        }

        private static Civil.Alignment? LocalizarAlinhamento(Transaction tr, CivilDocument civilDoc, string nome)
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

        private static void RebuildCorredorPorNome(Transaction tr, CivilDocument civilDoc, string nome, List<string> avisos)
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
                        corredor.UpgradeOpen();
                        corredor.Rebuild();
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                avisos.Add($"Rebuild de \"{nome}\" falhou ({ex.Message}).");
            }
        }

        /// <summary>
        /// Define/atualiza o conjunto de assemblies usadas nos corredores: uma por
        /// seção-tipo presente no desenho e uma para as interseções. As escolhas são
        /// memorizadas em %AppData% e valem para as próximas gerações.
        /// </summary>
        [CommandMethod("PLANVIAS_DEFINIR_ASSEMBLIES")]
        public void DefinirAssemblies()
        {
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                CivilDocument civilDoc = Manager.DocCivil;
                MapeamentoAssemblies mapeamento = MapeamentoAssemblies.Carregar();

                List<string> secoes;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    secoes = PlanejadorViasJuncoes.CarregarVias(tr, db).Values
                        .Select(v => string.IsNullOrWhiteSpace(v.Secao) ? "(sem seção)" : v.Secao)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (secoes.Count == 0)
                    {
                        secoes = SecoesTipoCatalogo.ObterSecoes().Select(s => s.Nome).ToList();
                    }

                    foreach (string secao in secoes)
                    {
                        string? atual;
                        mapeamento.PorSecao.TryGetValue(secao, out atual);
                        string sufixoAtual = string.IsNullOrWhiteSpace(atual) ? "(nenhuma)" : atual!;

                        bool quisAutomatica;
                        ObjectId escolhida = PlanejadorViasCorredores.SelecionarAssembly(
                            tr, editor,
                            $"\nSeção \"{secao}\" — assembly atual: {sufixoAtual}. Selecione a nova (Enter mantém)",
                            out quisAutomatica);

                        if (!escolhida.IsNull)
                        {
                            Civil.Assembly? assembly = tr.GetObject(escolhida, OpenMode.ForRead, false) as Civil.Assembly;
                            if (assembly != null)
                            {
                                mapeamento.PorSecao[secao] = assembly.Name;
                                editor.WriteMessage($"\n  \"{secao}\" → \"{assembly.Name}\".");
                            }
                        }
                        else if (quisAutomatica)
                        {
                            mapeamento.PorSecao.Remove(secao);
                            editor.WriteMessage($"\n  \"{secao}\" → montagem automática.");
                        }
                    }

                    // Assembly das interseções.
                    string atualJn = string.IsNullOrWhiteSpace(mapeamento.AssemblyJuncoes)
                        ? "(nenhuma)" : mapeamento.AssemblyJuncoes;
                    bool quisAutomaticaJn;
                    ObjectId escolhidaJn = PlanejadorViasCorredores.SelecionarAssembly(
                        tr, editor,
                        $"\nINTERSEÇÕES — assembly atual: {atualJn}. Selecione a nova (Enter mantém)",
                        out quisAutomaticaJn);

                    if (!escolhidaJn.IsNull)
                    {
                        Civil.Assembly? assemblyJn = tr.GetObject(escolhidaJn, OpenMode.ForRead, false) as Civil.Assembly;
                        if (assemblyJn != null)
                        {
                            mapeamento.AssemblyJuncoes = assemblyJn.Name;
                            editor.WriteMessage($"\n  Interseções → \"{assemblyJn.Name}\".");
                        }
                    }
                    else if (quisAutomaticaJn)
                    {
                        mapeamento.AssemblyJuncoes = string.Empty;
                        editor.WriteMessage("\n  Interseções → montagem automática.");
                    }

                    tr.Commit();
                }

                mapeamento.Salvar();
                editor.WriteMessage($"\nMapeamento salvo em {MapeamentoAssemblies.ObterCaminhoArquivo()}");
                editor.WriteMessage("\nDica: monte a assembly das interseções com uma pista LARGA no lado direito " +
                    "(ela preenche do meio-fio até o centro do cruzamento).");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD ao definir assemblies: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro ao definir assemblies: {ex.Message}");
            }
        }
    }
}
