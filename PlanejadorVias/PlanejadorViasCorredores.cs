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

        public static ResultadoCorredores CriarCorredores(
            Transaction tr,
            Database db,
            CivilDocument civilDoc,
            ObjectId superficieId,
            double raioMeioFio)
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

            foreach (ViaRegistrada via in vias.Values.OrderBy(v => v.Secao, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    CriarParaVia(tr, db, civilDoc, via, vias, superficieId, raioMeioFio,
                        assembliesPorSecao, ref posicaoAssembly, resultado);
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
            ResultadoCorredores resultado)
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
            AdicionarPVIs(tr, perfil, alinhamento, superficieId, nomeVia, resultado.Avisos);
            resultado.Perfis++;

            // 3. Assembly da seção-tipo (reutilizada por seção).
            ObjectId assemblyId = ObterOuCriarAssembly(
                tr, civilDoc, via.Secao, assembliesPorSecao, ref posicaoAssembly, resultado);

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

        private static void AdicionarPVIs(
            Transaction tr,
            Civil.Profile perfil,
            Civil.Alignment alinhamento,
            ObjectId superficieId,
            string nomeVia,
            List<string> avisos)
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
                    try
                    {
                        double x = 0.0;
                        double y = 0.0;
                        alinhamento.PointLocation(estacaoLimitada, 0.0, ref x, ref y);
                        double cota = superficie.FindElevationAtXY(x, y);
                        pvis.Add((estacaoLimitada, cota));
                    }
                    catch
                    {
                        // Estação fora da superfície: ignora este PVI.
                    }

                    if (estacaoLimitada >= fim)
                    {
                        break;
                    }
                }
            }

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

            foreach ((double estacao, double cota) in pvis)
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

        // ------------------------------------------------------------------
        // Assembly montada automaticamente a partir da seção-tipo
        // ------------------------------------------------------------------

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

                ResultadoCorredores resultado;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    resultado = PlanejadorViasCorredores.CriarCorredores(
                        tr, db, civilDoc, superficieId, PlanejadorViasEstado.Opcoes.RaioMeioFio);
                    tr.Commit();
                }

                editor.WriteMessage(
                    $"\nCorredores do Planejador de Vias: {resultado.Alinhamentos} alinhamento(s), " +
                    $"{resultado.Perfis} perfil(is), {resultado.Assemblies} assembly(ies) nova(s), " +
                    $"{resultado.Corredores} corredor(es) criado(s).");

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
    }
}
