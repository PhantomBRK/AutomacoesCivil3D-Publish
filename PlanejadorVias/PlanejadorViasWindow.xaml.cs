using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Janela modeless do Planejador de Vias: editor de seções-tipo com preview
    /// e disparo dos comandos de desenho/quantitativos no AutoCAD.
    /// </summary>
    public partial class PlanejadorViasWindow : Window
    {
        private readonly ObservableCollection<ElementoSecaoViewModel> _elementos =
            new ObservableCollection<ElementoSecaoViewModel>();

        private bool _carregandoSecao;

        public List<string> TiposDisponiveis { get; } =
            Enum.GetValues<TipoElementoVia>().Select(TipoElementoViaInfo.Rotulo).ToList();

        public PlanejadorViasWindow()
        {
            InitializeComponent();
            GridElementos.ItemsSource = _elementos;
            _elementos.CollectionChanged += (_, _) => AoMudarEditor();
            PreencherComboSecoes(null);
        }

        // ------------------------------------------------------------------
        // Seções-tipo
        // ------------------------------------------------------------------

        private void PreencherComboSecoes(string? selecionar)
        {
            _carregandoSecao = true;
            try
            {
                List<string> nomes = SecoesTipoCatalogo.ObterSecoes().Select(s => s.Nome).ToList();
                ComboSecoes.ItemsSource = nomes;

                string? alvo = selecionar != null && nomes.Contains(selecionar)
                    ? selecionar
                    : nomes.FirstOrDefault();
                ComboSecoes.SelectedItem = alvo;
            }
            finally
            {
                _carregandoSecao = false;
            }

            CarregarSecaoSelecionada();
        }

        private void ComboSecoes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_carregandoSecao)
            {
                return;
            }

            CarregarSecaoSelecionada();
        }

        private void CarregarSecaoSelecionada()
        {
            string? nome = ComboSecoes.SelectedItem as string;
            SecaoTipoVia? secao = nome != null ? SecoesTipoCatalogo.ObterPorNome(nome) : null;

            _carregandoSecao = true;
            try
            {
                _elementos.Clear();
                if (secao != null)
                {
                    TextoNomeSecao.Text = secao.Nome;
                    foreach (ElementoSecaoVia elemento in secao.Elementos)
                    {
                        AdicionarViewModel(elemento.Tipo, elemento.Largura, -1);
                    }
                }
            }
            finally
            {
                _carregandoSecao = false;
            }

            AoMudarEditor();
        }

        private void SalvarSecao_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SecaoTipoVia secao = MontarSecaoDoEditor(exigirNome: true);
                SecoesTipoCatalogo.SalvarSecao(secao);
                PreencherComboSecoes(secao.Nome);
                MessageBox.Show(this, $"Seção-tipo \"{secao.Nome}\" salva.", "Planejador de Vias",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Planejador de Vias", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExcluirSecao_Click(object sender, RoutedEventArgs e)
        {
            string? nome = ComboSecoes.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(nome))
            {
                return;
            }

            try
            {
                bool ehPadrao = SecoesTipoCatalogo.EhPadrao(nome);
                SecoesTipoCatalogo.ExcluirSecao(nome);
                PreencherComboSecoes(ehPadrao ? nome : null);

                string mensagem = ehPadrao
                    ? $"Personalização de \"{nome}\" removida — padrão embutido restaurado."
                    : $"Seção-tipo \"{nome}\" excluída.";
                MessageBox.Show(this, mensagem, "Planejador de Vias", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Planejador de Vias", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------------------------------------------
        // Edição dos elementos
        // ------------------------------------------------------------------

        private void AdicionarViewModel(TipoElementoVia tipo, double largura, int posicao)
        {
            ElementoSecaoViewModel vm = new ElementoSecaoViewModel
            {
                TipoRotulo = TipoElementoViaInfo.Rotulo(tipo),
                LarguraTexto = largura.ToString("0.##", CultureInfo.CurrentCulture)
            };
            vm.PropertyChanged += ElementoAlterado;

            if (posicao < 0 || posicao > _elementos.Count)
            {
                _elementos.Add(vm);
            }
            else
            {
                _elementos.Insert(posicao, vm);
            }
        }

        private void ElementoAlterado(object? sender, PropertyChangedEventArgs e)
        {
            AoMudarEditor();
        }

        private void AdicionarElemento_Click(object sender, RoutedEventArgs e)
        {
            string? tag = (sender as Button)?.Tag as string;
            TipoElementoVia tipo;
            if (tag == null || !Enum.TryParse(tag, out tipo))
            {
                return;
            }

            int posicao = GridElementos.SelectedIndex >= 0 ? GridElementos.SelectedIndex + 1 : -1;
            AdicionarViewModel(tipo, LarguraPadrao(tipo), posicao);
        }

        private static double LarguraPadrao(TipoElementoVia tipo)
        {
            switch (tipo)
            {
                case TipoElementoVia.Faixa: return 3.60;
                case TipoElementoVia.Acostamento: return 2.50;
                case TipoElementoVia.Estacionamento: return 2.50;
                case TipoElementoVia.Ciclovia: return 1.50;
                case TipoElementoVia.Canteiro: return 2.00;
                case TipoElementoVia.Calcada: return 2.00;
                case TipoElementoVia.MeioFio: return 0.40;
                default: return 1.00;
            }
        }

        private void RemoverElemento_Click(object sender, RoutedEventArgs e)
        {
            int indice = GridElementos.SelectedIndex;
            if (indice >= 0 && indice < _elementos.Count)
            {
                _elementos[indice].PropertyChanged -= ElementoAlterado;
                _elementos.RemoveAt(indice);
            }
        }

        private void MoverEsquerda_Click(object sender, RoutedEventArgs e)
        {
            int indice = GridElementos.SelectedIndex;
            if (indice > 0)
            {
                _elementos.Move(indice, indice - 1);
                GridElementos.SelectedIndex = indice - 1;
            }
        }

        private void MoverDireita_Click(object sender, RoutedEventArgs e)
        {
            int indice = GridElementos.SelectedIndex;
            if (indice >= 0 && indice < _elementos.Count - 1)
            {
                _elementos.Move(indice, indice + 1);
                GridElementos.SelectedIndex = indice + 1;
            }
        }

        // ------------------------------------------------------------------
        // Montagem da seção e estado compartilhado
        // ------------------------------------------------------------------

        private SecaoTipoVia MontarSecaoDoEditor(bool exigirNome)
        {
            string nome = TextoNomeSecao.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nome))
            {
                if (exigirNome)
                {
                    throw new InvalidOperationException("Informe um nome para a seção-tipo.");
                }

                nome = (ComboSecoes.SelectedItem as string) ?? "Seção sem nome";
            }

            SecaoTipoVia secao = new SecaoTipoVia { Nome = nome };
            foreach (ElementoSecaoViewModel vm in _elementos)
            {
                TipoElementoVia? tipo = TipoElementoViaInfo.DeRotulo(vm.TipoRotulo);
                double largura = ParseLargura(vm.LarguraTexto);
                if (tipo == null || largura <= 0)
                {
                    throw new InvalidOperationException(
                        "Há elementos inválidos na seção (tipo não reconhecido ou largura não positiva).");
                }

                secao.Elementos.Add(new ElementoSecaoVia { Tipo = tipo.Value, Largura = largura });
            }

            if (secao.Elementos.Count == 0)
            {
                throw new InvalidOperationException("A seção-tipo precisa de ao menos um elemento.");
            }

            return secao;
        }

        private static double ParseLargura(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return 0;
            }

            double valor;
            if (double.TryParse(texto.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out valor))
            {
                return valor;
            }

            return 0;
        }

        private bool AtualizarEstado()
        {
            try
            {
                SecaoTipoVia secao = MontarSecaoDoEditor(exigirNome: false);
                PlanejadorViasEstado.SecaoAtual = secao;

                PlanejadorViasEstado.Opcoes.FormarJuncoes = CheckJuncoes.IsChecked == true;

                double raioEixo = ParseLargura(TextoRaioEixo.Text);
                PlanejadorViasEstado.Opcoes.RaioCurvaEixo = raioEixo > 0 ? raioEixo : 0.0;

                double raioMeioFio = ParseLargura(TextoRaioMeioFio.Text);
                PlanejadorViasEstado.Opcoes.RaioMeioFio = raioMeioFio > 0.1 ? raioMeioFio : 5.0;

                return true;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Planejador de Vias", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Ações (disparam comandos no AutoCAD; a janela permanece aberta)
        // ------------------------------------------------------------------

        private void DesenharVia_Click(object sender, RoutedEventArgs e)
        {
            if (AtualizarEstado())
            {
                ExecutarComando("PLANVIAS_DESENHAR");
            }
        }

        private void ViasDePolylines_Click(object sender, RoutedEventArgs e)
        {
            if (AtualizarEstado())
            {
                ExecutarComando("PLANVIAS_DE_POLYLINES");
            }
        }

        private void Quantitativos_Click(object sender, RoutedEventArgs e)
        {
            ExecutarComando("PLANVIAS_QUANTITATIVOS");
        }

        private void DefinirAssemblies_Click(object sender, RoutedEventArgs e)
        {
            ExecutarComando("PLANVIAS_DEFINIR_ASSEMBLIES");
        }

        private void GerarCorredores_Click(object sender, RoutedEventArgs e)
        {
            // O raio do meio-fio define a folga das regiões do corredor nas junções.
            if (AtualizarEstado())
            {
                ExecutarComando("PLANVIAS_CRIAR_CORREDORES");
            }
        }

        private void ExecutarComando(string nomeComando)
        {
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Document documento =
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (documento == null)
                {
                    MessageBox.Show(this, "Nenhum desenho aberto no AutoCAD.", "Planejador de Vias",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                documento.SendStringToExecute(nomeComando + " ", true, false, true);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "Falha ao acionar o comando: " + ex.Message, "Planejador de Vias",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        private void CanvasPreview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DesenharPreview();
        }

        private void AoMudarEditor()
        {
            if (_carregandoSecao)
            {
                return;
            }

            DesenharPreview();
        }

        private void DesenharPreview()
        {
            if (CanvasPreview == null)
            {
                return;
            }

            CanvasPreview.Children.Clear();

            List<(TipoElementoVia Tipo, double Largura)> elementos = _elementos
                .Select(vm => (Tipo: TipoElementoViaInfo.DeRotulo(vm.TipoRotulo), Largura: ParseLargura(vm.LarguraTexto)))
                .Where(par => par.Tipo != null && par.Largura > 0)
                .Select(par => (par.Tipo!.Value, par.Largura))
                .ToList();

            double larguraTotal = elementos.Sum(par => par.Item2);
            TextoLarguraTotal.Text = larguraTotal > 0
                ? $"Largura total: {larguraTotal.ToString("0.00", CultureInfo.CurrentCulture)} m"
                : string.Empty;

            double larguraCanvas = CanvasPreview.ActualWidth;
            if (larguraCanvas < 60)
            {
                larguraCanvas = 900;
            }

            if (elementos.Count == 0 || larguraTotal <= 0)
            {
                TextBlock vazio = new TextBlock
                {
                    Text = "Adicione elementos à seção para visualizar",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x9B, 0xA8)),
                    FontSize = 13
                };
                Canvas.SetLeft(vazio, 20);
                Canvas.SetTop(vazio, 84);
                CanvasPreview.Children.Add(vazio);
                return;
            }

            double margem = 26;
            double escala = (larguraCanvas - 2 * margem) / larguraTotal;
            double baseY = 138;

            // Linha do terreno.
            Line chao = new Line
            {
                X1 = margem - 10,
                X2 = margem + larguraTotal * escala + 10,
                Y1 = baseY,
                Y2 = baseY,
                Stroke = new SolidColorBrush(Color.FromRgb(0x59, 0x70, 0x7F)),
                StrokeThickness = 1
            };
            CanvasPreview.Children.Add(chao);

            double x = margem;
            foreach ((TipoElementoVia tipo, double largura) in elementos)
            {
                double larguraPx = largura * escala;
                double alturaPx = AlturaPreview(tipo);

                Rectangle retangulo = new Rectangle
                {
                    Width = Math.Max(1.0, larguraPx - 1.0),
                    Height = alturaPx,
                    Fill = new SolidColorBrush(CorPreview(tipo)),
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    ToolTip = $"{TipoElementoViaInfo.Rotulo(tipo)} — {largura.ToString("0.00", CultureInfo.CurrentCulture)} m"
                };
                Canvas.SetLeft(retangulo, x);
                Canvas.SetTop(retangulo, baseY - alturaPx);
                CanvasPreview.Children.Add(retangulo);

                if (larguraPx > 26)
                {
                    TextBlock rotuloLargura = new TextBlock
                    {
                        Text = largura.ToString("0.00", CultureInfo.CurrentCulture),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xD7, 0xE1, 0xE8)),
                        FontSize = 10,
                        Width = larguraPx,
                        TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(rotuloLargura, x);
                    Canvas.SetTop(rotuloLargura, baseY + 6);
                    CanvasPreview.Children.Add(rotuloLargura);
                }

                x += larguraPx;
            }

            // Eixo (linha tracejada no centro da largura total).
            double centroX = margem + larguraTotal * escala / 2.0;
            Line eixo = new Line
            {
                X1 = centroX,
                X2 = centroX,
                Y1 = 22,
                Y2 = baseY + 24,
                Stroke = new SolidColorBrush(Color.FromRgb(0x6F, 0xC4, 0x86)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 6, 4 }
            };
            CanvasPreview.Children.Add(eixo);
        }

        private static double AlturaPreview(TipoElementoVia tipo)
        {
            switch (tipo)
            {
                case TipoElementoVia.MeioFio: return 42;
                case TipoElementoVia.Calcada: return 34;
                case TipoElementoVia.Canteiro: return 36;
                default: return 26; // elementos rodáveis
            }
        }

        private static Color CorPreview(TipoElementoVia tipo)
        {
            switch (tipo)
            {
                case TipoElementoVia.Faixa: return Color.FromRgb(0x37, 0x44, 0x4E);
                case TipoElementoVia.Acostamento: return Color.FromRgb(0x46, 0x54, 0x5F);
                case TipoElementoVia.Estacionamento: return Color.FromRgb(0x3F, 0x56, 0x68);
                case TipoElementoVia.Ciclovia: return Color.FromRgb(0x7E, 0x4A, 0x66);
                case TipoElementoVia.Canteiro: return Color.FromRgb(0x3F, 0x7A, 0x4C);
                case TipoElementoVia.Calcada: return Color.FromRgb(0x97, 0xA1, 0xAA);
                case TipoElementoVia.MeioFio: return Color.FromRgb(0xC2, 0x50, 0x4A);
                default: return Color.FromRgb(0x60, 0x60, 0x60);
            }
        }
    }

    /// <summary>Linha editável do grid de elementos.</summary>
    public sealed class ElementoSecaoViewModel : INotifyPropertyChanged
    {
        private string _tipoRotulo = string.Empty;
        private string _larguraTexto = string.Empty;

        public string TipoRotulo
        {
            get { return _tipoRotulo; }
            set
            {
                if (_tipoRotulo != value)
                {
                    _tipoRotulo = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TipoRotulo)));
                }
            }
        }

        public string LarguraTexto
        {
            get { return _larguraTexto; }
            set
            {
                if (_larguraTexto != value)
                {
                    _larguraTexto = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LarguraTexto)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
