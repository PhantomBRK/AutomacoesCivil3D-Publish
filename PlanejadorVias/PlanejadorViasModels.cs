using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Tipos de elemento que compõem uma seção-tipo de via urbana (Planejador de Vias).
    /// </summary>
    public enum TipoElementoVia
    {
        Faixa,
        Acostamento,
        Estacionamento,
        Ciclovia,
        Canteiro,
        Calcada,
        MeioFio
    }

    /// <summary>
    /// Metadados de apresentação e de comportamento de cada tipo de elemento.
    /// </summary>
    public static class TipoElementoViaInfo
    {
        // Elementos "rodáveis" definem a caixa de pavimento usada nas junções automáticas.
        public static bool EhRodavel(TipoElementoVia tipo)
        {
            return tipo == TipoElementoVia.Faixa
                || tipo == TipoElementoVia.Acostamento
                || tipo == TipoElementoVia.Estacionamento
                || tipo == TipoElementoVia.Ciclovia;
        }

        public static string Rotulo(TipoElementoVia tipo)
        {
            switch (tipo)
            {
                case TipoElementoVia.Faixa: return "Faixa de rolamento";
                case TipoElementoVia.Acostamento: return "Acostamento";
                case TipoElementoVia.Estacionamento: return "Estacionamento";
                case TipoElementoVia.Ciclovia: return "Ciclovia";
                case TipoElementoVia.Canteiro: return "Canteiro";
                case TipoElementoVia.Calcada: return "Calçada";
                case TipoElementoVia.MeioFio: return "Meio-fio";
                default: return tipo.ToString();
            }
        }

        public static TipoElementoVia? DeRotulo(string rotulo)
        {
            foreach (TipoElementoVia tipo in Enum.GetValues<TipoElementoVia>())
            {
                if (string.Equals(Rotulo(tipo), rotulo, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tipo.ToString(), rotulo, StringComparison.OrdinalIgnoreCase))
                {
                    return tipo;
                }
            }

            return null;
        }

        /// <summary>Cor ACI usada nas camadas e no preview do jig.</summary>
        public static short CorAci(TipoElementoVia tipo)
        {
            switch (tipo)
            {
                case TipoElementoVia.Faixa: return 30;          // laranja (bordas de pavimento)
                case TipoElementoVia.Acostamento: return 40;    // amarelo-alaranjado
                case TipoElementoVia.Estacionamento: return 140; // azul acinzentado
                case TipoElementoVia.Ciclovia: return 200;      // magenta suave
                case TipoElementoVia.Canteiro: return 3;        // verde
                case TipoElementoVia.Calcada: return 8;         // cinza
                case TipoElementoVia.MeioFio: return 1;         // vermelho
                default: return 7;
            }
        }

        public static string SufixoCamada(TipoElementoVia tipo)
        {
            switch (tipo)
            {
                case TipoElementoVia.Faixa: return "PAVIMENTO";
                case TipoElementoVia.Acostamento: return "ACOSTAMENTO";
                case TipoElementoVia.Estacionamento: return "ESTACIONAMENTO";
                case TipoElementoVia.Ciclovia: return "CICLOVIA";
                case TipoElementoVia.Canteiro: return "CANTEIRO";
                case TipoElementoVia.Calcada: return "CALCADA";
                case TipoElementoVia.MeioFio: return "MEIO_FIO";
                default: return "GERAL";
            }
        }
    }

    /// <summary>
    /// Um elemento transversal da seção-tipo (largura em metros).
    /// A lista de elementos é ordenada da ESQUERDA para a DIREITA,
    /// olhando no sentido de caminhamento do eixo.
    /// </summary>
    public sealed class ElementoSecaoVia
    {
        public TipoElementoVia Tipo { get; set; } = TipoElementoVia.Faixa;
        public double Largura { get; set; } = 3.60;

        public ElementoSecaoVia Clonar()
        {
            return new ElementoSecaoVia { Tipo = Tipo, Largura = Largura };
        }
    }

    /// <summary>
    /// Limite (linha longitudinal) entre dois elementos da seção, com offset
    /// assinado em relação ao eixo: positivo = esquerda, negativo = direita.
    /// </summary>
    public sealed class LimiteSecaoVia
    {
        public double Offset { get; set; }
        public TipoElementoVia? ElementoInterno { get; set; }
        public TipoElementoVia? ElementoExterno { get; set; }

        /// <summary>Tipo usado para escolher camada/cor do limite.</summary>
        public TipoElementoVia TipoParaCamada
        {
            get
            {
                // O elemento mais "externo" (mais afastado do eixo) define a aparência;
                // na borda externa da via não há elemento externo, usa o interno.
                if (ElementoExterno.HasValue) return PrioridadeVisual(ElementoExterno.Value, ElementoInterno);
                return ElementoInterno ?? TipoElementoVia.Faixa;
            }
        }

        private static TipoElementoVia PrioridadeVisual(TipoElementoVia externo, TipoElementoVia? interno)
        {
            // Meio-fio prevalece visualmente sobre qualquer vizinho.
            if (externo == TipoElementoVia.MeioFio) return TipoElementoVia.MeioFio;
            if (interno == TipoElementoVia.MeioFio) return TipoElementoVia.MeioFio;
            return externo;
        }
    }

    /// <summary>
    /// Seção-tipo completa de uma via de planejamento urbano.
    /// </summary>
    public sealed class SecaoTipoVia
    {
        public string Nome { get; set; } = string.Empty;
        public List<ElementoSecaoVia> Elementos { get; set; } = new List<ElementoSecaoVia>();

        public double LarguraTotal
        {
            get { return Elementos.Sum(e => Math.Max(0.0, e.Largura)); }
        }

        public SecaoTipoVia Clonar()
        {
            return new SecaoTipoVia
            {
                Nome = Nome,
                Elementos = Elementos.Select(e => e.Clonar()).ToList()
            };
        }

        /// <summary>
        /// Limites entre elementos, incluindo as duas bordas externas.
        /// Offsets assinados: positivo = esquerda do eixo (eixo no centro da largura total).
        /// Ordenados da esquerda (maior offset) para a direita (menor offset).
        /// </summary>
        public List<LimiteSecaoVia> ObterLimites()
        {
            List<LimiteSecaoVia> limites = new List<LimiteSecaoVia>();
            double metade = LarguraTotal / 2.0;
            double acumulado = 0.0;

            // Borda externa esquerda (não há elemento externo).
            if (Elementos.Count > 0)
            {
                limites.Add(new LimiteSecaoVia
                {
                    Offset = metade,
                    ElementoInterno = Elementos[0].Tipo,
                    ElementoExterno = null
                });
            }

            for (int i = 0; i < Elementos.Count - 1; i++)
            {
                acumulado += Math.Max(0.0, Elementos[i].Largura);
                limites.Add(new LimiteSecaoVia
                {
                    Offset = metade - acumulado,
                    ElementoInterno = Elementos[i + 1].Tipo,
                    ElementoExterno = Elementos[i].Tipo
                });
            }

            // Borda externa direita.
            if (Elementos.Count > 0)
            {
                limites.Add(new LimiteSecaoVia
                {
                    Offset = -metade,
                    ElementoInterno = Elementos[Elementos.Count - 1].Tipo,
                    ElementoExterno = null
                });
            }

            return limites;
        }

        /// <summary>
        /// Meia-largura da caixa rodável (borda do pavimento) em cada lado.
        /// Retorna 0 quando não há elemento rodável no lado.
        /// esquerda = true para o lado de offset positivo.
        /// </summary>
        public double MeiaLarguraPavimento(bool esquerda)
        {
            double metade = LarguraTotal / 2.0;
            double acumulado = 0.0;

            if (esquerda)
            {
                // Percorre da esquerda para a direita; a borda do pavimento à esquerda
                // é o limite externo (esquerdo) do primeiro elemento rodável.
                for (int i = 0; i < Elementos.Count; i++)
                {
                    double offsetExterno = metade - acumulado;
                    if (TipoElementoViaInfo.EhRodavel(Elementos[i].Tipo))
                    {
                        return Math.Max(0.0, offsetExterno);
                    }

                    acumulado += Math.Max(0.0, Elementos[i].Largura);
                }

                return 0.0;
            }

            // Lado direito: limite externo (direito) do último elemento rodável.
            acumulado = 0.0;
            for (int i = Elementos.Count - 1; i >= 0; i--)
            {
                double offsetExterno = metade - acumulado; // distância a partir da borda direita
                if (TipoElementoViaInfo.EhRodavel(Elementos[i].Tipo))
                {
                    return Math.Max(0.0, offsetExterno);
                }

                acumulado += Math.Max(0.0, Elementos[i].Largura);
            }

            return 0.0;
        }

        /// <summary>
        /// Áreas por tipo de elemento para 1 metro de via (m²/m) — base dos quantitativos.
        /// </summary>
        public Dictionary<TipoElementoVia, double> LargurasPorTipo()
        {
            Dictionary<TipoElementoVia, double> mapa = new Dictionary<TipoElementoVia, double>();
            foreach (ElementoSecaoVia elemento in Elementos)
            {
                double atual;
                mapa.TryGetValue(elemento.Tipo, out atual);
                mapa[elemento.Tipo] = atual + Math.Max(0.0, elemento.Largura);
            }

            return mapa;
        }

        public int ContarElementos(TipoElementoVia tipo)
        {
            return Elementos.Count(e => e.Tipo == tipo);
        }
    }

    /// <summary>
    /// Opções de desenho da via (equivalentes ao painel "Draw road" do planejador).
    /// </summary>
    public sealed class PlanejadorViasOpcoes
    {
        public bool FormarJuncoes { get; set; } = true;
        public double RaioCurvaEixo { get; set; } = 50.0;
        public double RaioMeioFio { get; set; } = 5.0;
    }

    /// <summary>
    /// Estado compartilhado entre a janela modeless e os comandos do AutoCAD.
    /// </summary>
    public static class PlanejadorViasEstado
    {
        public static SecaoTipoVia? SecaoAtual { get; set; }
        public static PlanejadorViasOpcoes Opcoes { get; } = new PlanejadorViasOpcoes();

        public static SecaoTipoVia ObterSecaoOuPadrao()
        {
            if (SecaoAtual != null && SecaoAtual.Elementos.Count > 0)
            {
                return SecaoAtual;
            }

            IReadOnlyList<SecaoTipoVia> secoes = SecoesTipoCatalogo.ObterSecoes();
            return secoes.Count > 0 ? secoes[0] : SecoesTipoCatalogo.CriarBoulevardUrbano();
        }
    }

    /// <summary>
    /// Mapeamento persistido de assemblies para os corredores: nome da seção-tipo →
    /// nome da assembly do desenho, mais a assembly usada nas interseções.
    /// Gravado em %AppData%\AutomacoesCivil3D\PlanejadorVias\Assemblies.json.
    /// </summary>
    public sealed class MapeamentoAssemblies
    {
        private static readonly JsonSerializerOptions JsonOpcoes = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public Dictionary<string, string> PorSecao { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string AssemblyJuncoes { get; set; } = string.Empty;

        public static string ObterCaminhoArquivo()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AutomacoesCivil3D", "PlanejadorVias", "Assemblies.json");
        }

        public static MapeamentoAssemblies Carregar()
        {
            try
            {
                string caminho = ObterCaminhoArquivo();
                if (File.Exists(caminho))
                {
                    MapeamentoAssemblies? lido = JsonSerializer.Deserialize<MapeamentoAssemblies>(
                        File.ReadAllText(caminho), JsonOpcoes);
                    if (lido != null)
                    {
                        lido.PorSecao = new Dictionary<string, string>(lido.PorSecao, StringComparer.OrdinalIgnoreCase);
                        return lido;
                    }
                }
            }
            catch
            {
                // Arquivo corrompido: recomeça vazio.
            }

            return new MapeamentoAssemblies();
        }

        public void Salvar()
        {
            try
            {
                string caminho = ObterCaminhoArquivo();
                string? pasta = Path.GetDirectoryName(caminho);
                if (!string.IsNullOrEmpty(pasta))
                {
                    Directory.CreateDirectory(pasta);
                }

                File.WriteAllText(caminho, JsonSerializer.Serialize(this, JsonOpcoes));
            }
            catch
            {
                // Persistência é conveniência; falha não interrompe o comando.
            }
        }
    }

    /// <summary>
    /// Arquivo JSON persistido com as seções-tipo do usuário.
    /// </summary>
    internal sealed class SecoesTipoArquivo
    {
        public DateTimeOffset AtualizadoUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<SecaoTipoVia> Secoes { get; set; } = new List<SecaoTipoVia>();
    }

    /// <summary>
    /// Catálogo de seções-tipo: padrões embutidos + personalizações do usuário
    /// (JSON em %AppData%\AutomacoesCivil3D\PlanejadorVias\SecoesTipo.json).
    /// </summary>
    public static class SecoesTipoCatalogo
    {
        private static readonly JsonSerializerOptions JsonOpcoes = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static string ObterCaminhoArquivo()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AutomacoesCivil3D", "PlanejadorVias", "SecoesTipo.json");
        }

        public static SecaoTipoVia CriarBoulevardUrbano()
        {
            return new SecaoTipoVia
            {
                Nome = "Boulevard Urbano",
                Elementos = new List<ElementoSecaoVia>
                {
                    Elemento(TipoElementoVia.Calcada, 2.50),
                    Elemento(TipoElementoVia.MeioFio, 0.40),
                    Elemento(TipoElementoVia.Faixa, 3.60),
                    Elemento(TipoElementoVia.Faixa, 3.60),
                    Elemento(TipoElementoVia.Canteiro, 4.00),
                    Elemento(TipoElementoVia.Faixa, 3.60),
                    Elemento(TipoElementoVia.Faixa, 3.60),
                    Elemento(TipoElementoVia.MeioFio, 0.40),
                    Elemento(TipoElementoVia.Calcada, 2.50)
                }
            };
        }

        public static SecaoTipoVia CriarColetoraPrimaria()
        {
            return new SecaoTipoVia
            {
                Nome = "Coletora Primária",
                Elementos = new List<ElementoSecaoVia>
                {
                    Elemento(TipoElementoVia.Calcada, 2.50),
                    Elemento(TipoElementoVia.MeioFio, 0.40),
                    Elemento(TipoElementoVia.Faixa, 3.60),
                    Elemento(TipoElementoVia.Faixa, 3.60),
                    Elemento(TipoElementoVia.MeioFio, 0.40),
                    Elemento(TipoElementoVia.Calcada, 2.50)
                }
            };
        }

        public static SecaoTipoVia CriarViaLocal()
        {
            return new SecaoTipoVia
            {
                Nome = "Via Local",
                Elementos = new List<ElementoSecaoVia>
                {
                    Elemento(TipoElementoVia.Calcada, 2.00),
                    Elemento(TipoElementoVia.MeioFio, 0.30),
                    Elemento(TipoElementoVia.Faixa, 3.00),
                    Elemento(TipoElementoVia.Faixa, 3.00),
                    Elemento(TipoElementoVia.MeioFio, 0.30),
                    Elemento(TipoElementoVia.Calcada, 2.00)
                }
            };
        }

        private static List<SecaoTipoVia> CriarPadroes()
        {
            return new List<SecaoTipoVia>
            {
                CriarBoulevardUrbano(),
                CriarColetoraPrimaria(),
                CriarViaLocal()
            };
        }

        /// <summary>
        /// Padrões embutidos mesclados com as seções do usuário (usuário prevalece pelo nome).
        /// </summary>
        public static IReadOnlyList<SecaoTipoVia> ObterSecoes()
        {
            Dictionary<string, SecaoTipoVia> combinado = new Dictionary<string, SecaoTipoVia>(StringComparer.OrdinalIgnoreCase);
            foreach (SecaoTipoVia secao in CriarPadroes())
            {
                combinado[secao.Nome] = secao;
            }

            foreach (SecaoTipoVia secao in CarregarDoUsuario())
            {
                if (string.IsNullOrWhiteSpace(secao.Nome) || secao.Elementos.Count == 0)
                {
                    continue;
                }

                combinado[secao.Nome] = secao;
            }

            return combinado.Values
                .OrderBy(s => s.Nome, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static SecaoTipoVia? ObterPorNome(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
            {
                return null;
            }

            return ObterSecoes().FirstOrDefault(s => string.Equals(s.Nome, nome, StringComparison.OrdinalIgnoreCase));
        }

        public static bool EhPadrao(string nome)
        {
            return CriarPadroes().Any(s => string.Equals(s.Nome, nome, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Grava (cria/substitui) uma seção do usuário no JSON.</summary>
        public static void SalvarSecao(SecaoTipoVia secao)
        {
            if (secao == null || string.IsNullOrWhiteSpace(secao.Nome) || secao.Elementos.Count == 0)
            {
                throw new ArgumentException("Seção-tipo inválida: informe nome e ao menos um elemento.");
            }

            List<SecaoTipoVia> doUsuario = CarregarDoUsuario();
            doUsuario.RemoveAll(s => string.Equals(s.Nome, secao.Nome, StringComparison.OrdinalIgnoreCase));
            doUsuario.Add(secao.Clonar());
            GravarDoUsuario(doUsuario);
        }

        /// <summary>
        /// Remove uma seção do usuário. Para seções padrão, remover a personalização
        /// restaura o padrão embutido.
        /// </summary>
        public static void ExcluirSecao(string nome)
        {
            List<SecaoTipoVia> doUsuario = CarregarDoUsuario();
            doUsuario.RemoveAll(s => string.Equals(s.Nome, nome, StringComparison.OrdinalIgnoreCase));
            GravarDoUsuario(doUsuario);
        }

        private static List<SecaoTipoVia> CarregarDoUsuario()
        {
            try
            {
                string caminho = ObterCaminhoArquivo();
                if (!File.Exists(caminho))
                {
                    return new List<SecaoTipoVia>();
                }

                string json = File.ReadAllText(caminho);
                SecoesTipoArquivo? arquivo = JsonSerializer.Deserialize<SecoesTipoArquivo>(json, JsonOpcoes);
                return arquivo?.Secoes ?? new List<SecaoTipoVia>();
            }
            catch
            {
                // Arquivo corrompido não deve derrubar o comando; segue com os padrões.
                return new List<SecaoTipoVia>();
            }
        }

        private static void GravarDoUsuario(List<SecaoTipoVia> secoes)
        {
            string caminho = ObterCaminhoArquivo();
            string? pasta = Path.GetDirectoryName(caminho);
            if (!string.IsNullOrEmpty(pasta))
            {
                Directory.CreateDirectory(pasta);
            }

            SecoesTipoArquivo arquivo = new SecoesTipoArquivo
            {
                AtualizadoUtc = DateTimeOffset.UtcNow,
                Secoes = secoes
            };

            File.WriteAllText(caminho, JsonSerializer.Serialize(arquivo, JsonOpcoes));
        }

        private static ElementoSecaoVia Elemento(TipoElementoVia tipo, double largura)
        {
            return new ElementoSecaoVia { Tipo = tipo, Largura = largura };
        }
    }
}
