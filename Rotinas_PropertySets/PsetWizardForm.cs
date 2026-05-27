/*/ Referências típicas necessárias no projeto:
// - Autodesk.AutoCAD.ApplicationServices
// - Autodesk.AutoCAD.DatabaseServices
// - Autodesk.AutoCAD.EditorInput
// - Autodesk.AutoCAD.Runtime
// - Autodesk.AutoCAD.Windows
// - Autodesk.Civil.ApplicationServices
// - Autodesk.Civil.DatabaseServices
// - Autodesk.Aec.PropertyData.DatabaseServices
// - OfficeOpenXml (EPPlus)
// - System.Windows.Forms

using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using DataTable = System.Data.DataTable;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Label = System.Windows.Forms.Label;
using FlowDirection = System.Windows.Forms.FlowDirection;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace AutomacoesCivil3D
{
    public class PsetsCascataCommand1
    {
        private static PaletteSet _palette;

        [CommandMethod("AWPSet")]
        public void PSetsCascata()
        {
            if (_palette == null)
            {
                _palette = new PaletteSet("Seleção em Cascata - Psets");
                _palette.Style = PaletteSetStyles.ShowAutoHideButton
                               | PaletteSetStyles.ShowCloseButton
                               | PaletteSetStyles.NameEditable
                               | PaletteSetStyles.ShowPropertiesMenu;
                _palette.MinimumSize = new Size(420, 520);

                CascataPsets controle = new();
                _palette.Add("Selecionar", controle);
                _palette.Visible = true;
            }
            else
            {
                if (!_palette.Visible)
                {
                    _palette.Visible = true;
                }
            }

            _palette.Activate(0);
        }
    }

    public class CascataPsets : UserControl
    {
        private TextBox _txtArquivo;
        private Button _btnArquivo;
        private Label _lblStatus;

        private ComboBox _cboB; // Filtro 1 - NOME (Coluna B)
        private ComboBox _cboH; // Filtro 2 - NOME (Coluna H)
        private ComboBox _cboL; // Filtro 3 - NOME (Coluna L)

        private DataGridView _grid;
        private Button _btnAplicar;
        private Button _btnLimpar;

        private DataTable _dados;

        // Mapeamento por NOME DE COLUNA (não por índice), para robustez
        private string _colNomeFiltroB; // Coluna B - nome
        private string _colNomeFiltroH; // Coluna H - nome
        private string _colNomeFiltroL; // Coluna L - nome

        // Colunas para CÓDIGOS (para Psets)
        private string _colNomeCodigoA; // Coluna A - código "CODIGO_ATIVIDADE"
        private string _colNomeCodigoG; // Coluna G - código "CWA"
        private string _colNomeCodigoN; // Coluna N - código "CWP"
        private string _colNomeDisciplina; // Coluna D - código "DISCIPLINA"

        // Strings solicitadas
        private string cwa = string.Empty; // valor selecionado do filtro 1 (B)
        private string cwb = string.Empty; // valor selecionado do filtro 2 (H)
        private string cwc = string.Empty; // valor selecionado do filtro 3 (L)
        private string cwd = string.Empty; // reservado (vazio)
        private string cwe = string.Empty; // reservado (vazio)

        public string Cwa { get { return cwa; } }
        public string Cwb { get { return cwb; } }
        public string Cwc { get { return cwc; } }
        public string Cwd { get { return cwd; } }
        public string Cwe { get { return cwe; } }

        // Valores que irão para os Property Sets (dos CÓDIGOS A/G/N)
        public string Pset_IWP { get; private set; } = string.Empty; // A
        public string Pset_CWA { get; private set; } = string.Empty;            // G
        public string Pset_CWP { get; private set; } = string.Empty;            // N
        public string PsetDescricaoAtividade { get; private set; } = string.Empty; // opcional (L)

        public CascataPsets()
        {
            this.InitializeUi();
            this.WireEvents();
            this.AtualizarStatus("Carregue uma planilha (.xlsx) para começar.");
        }

        private void InitializeUi()
        {
            this.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(8);
            layout.ColumnCount = 2;
            layout.RowCount = 9;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            Label lblArquivo = new Label();
            lblArquivo.Text = "Planilha (.xlsx):";
            lblArquivo.TextAlign = ContentAlignment.MiddleLeft;
            lblArquivo.Dock = DockStyle.Fill;

            _txtArquivo = new TextBox();
            _txtArquivo.ReadOnly = true;
            _txtArquivo.Dock = DockStyle.Fill;

            _btnArquivo = new Button();
            _btnArquivo.Text = "Procurar...";
            _btnArquivo.Dock = DockStyle.Fill;

            Label lblB = new Label();
            lblB.Text = "CÓDIGO CWA";
            lblB.Dock = DockStyle.Fill;
            lblB.TextAlign = ContentAlignment.MiddleLeft;

            Label lblH = new Label();
            lblH.Text = "CÓDIGO CWP";
            lblH.Dock = DockStyle.Fill;
            lblH.TextAlign = ContentAlignment.MiddleLeft;

            Label lblL = new Label();
            lblL.Text = "CÓDIGO IWP";
            lblL.Dock = DockStyle.Fill;
            lblL.TextAlign = ContentAlignment.MiddleLeft;

            _cboB = new ComboBox(); _cboB.DropDownStyle = ComboBoxStyle.DropDownList; _cboB.Dock = DockStyle.Fill;
            _cboH = new ComboBox(); _cboH.DropDownStyle = ComboBoxStyle.DropDownList; _cboH.Dock = DockStyle.Fill;
            _cboL = new ComboBox(); _cboL.DropDownStyle = ComboBoxStyle.DropDownList; _cboL.Dock = DockStyle.Fill;

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.RowHeadersVisible = false;
            _grid.Height = 160;

            FlowLayoutPanel barraBotoes = new FlowLayoutPanel();
            barraBotoes.FlowDirection = FlowDirection.RightToLeft;
            barraBotoes.Dock = DockStyle.Fill;

            _btnAplicar = new Button();
            _btnAplicar.Text = "Aplicar aos Psets";
            _btnAplicar.AutoSize = true;

            _btnLimpar = new Button();
            _btnLimpar.Text = "Limpar";
            _btnLimpar.AutoSize = true;

            barraBotoes.Controls.Add(_btnAplicar);
            barraBotoes.Controls.Add(_btnLimpar);

            _lblStatus = new Label();
            _lblStatus.Text = "";
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.ForeColor = Color.DimGray;

            // Layout
            layout.Controls.Add(lblArquivo, 0, 0);
            layout.Controls.Add(_txtArquivo, 1, 0);
            layout.Controls.Add(_btnArquivo, 1, 1);

            layout.Controls.Add(lblB, 0, 2);
            layout.Controls.Add(_cboB, 1, 2);

            layout.Controls.Add(lblH, 0, 3);
            layout.Controls.Add(_cboH, 1, 3);

            layout.Controls.Add(lblL, 0, 4);
            layout.Controls.Add(_cboL, 1, 4);

            layout.SetColumnSpan(_grid, 2);
            layout.Controls.Add(_grid, 0, 5);

            layout.SetColumnSpan(barraBotoes, 2);
            layout.Controls.Add(barraBotoes, 0, 6);

            layout.SetColumnSpan(_lblStatus, 2);
            layout.Controls.Add(_lblStatus, 0, 7);

            this.Controls.Add(layout);
        }

        private void WireEvents()
        {
            _btnArquivo.Click += OnSelecionarArquivo;
            _cboB.SelectedIndexChanged += (s, e) => AtualizarCascata(1);
            _cboH.SelectedIndexChanged += (s, e) => AtualizarCascata(2);
            _cboL.SelectedIndexChanged += (s, e) => AtualizarCascata(3);
            _btnLimpar.Click += (s, e) => LimparCampos();
            _btnAplicar.Click += OnAplicarPsets;
        }

        private void OnSelecionarArquivo(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Excel (*.xlsx)|*.xlsx|Todos os arquivos (*.*)|*.*";
            ofd.Title = "Selecione a planilha (.xlsx)";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                _txtArquivo.Text = ofd.FileName;
                CarregarDados(ofd.FileName);
            }
        }

        private void CarregarDados(string caminho)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
                {
                    MessageBox.Show("Arquivo inválido.", "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // EPPlus – defina o contexto conforme sua licença
                ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");


                DataTable dados = LerPlanilhaXlsxComEPPlus(caminho, true);

                // Precisamos ter, no mínimo, até a coluna N (índice 13) para alcançar A/G/L/N
                if (dados == null || dados.Columns.Count < 10)
                {
                    MessageBox.Show("A planilha deve conter ao menos 14 colunas (A a N).", "Atenção",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _dados = dados;

                // Mapeamento por nome — garantimos que B/H/L/A/G/N existam
                _colNomeFiltroB = _dados.Columns[1].ColumnName;   // B (NOME)
                _colNomeFiltroH = _dados.Columns[7].ColumnName;   // H (NOME)
                _colNomeFiltroL = _dados.Columns[11].ColumnName;  // L (NOME)
                _colNomeDisciplina = _dados.Columns[3].ColumnName; // D (DISCIPLINA)

                _colNomeCodigoA = _dados.Columns[0].ColumnName;   // A (CWA)
                _colNomeCodigoG = _dados.Columns[6].ColumnName;   // G (CWP)
                _colNomeCodigoN = _dados.Columns[10].ColumnName;  // N (IWP)


                PopularDistinct(_cboB, _colNomeFiltroB, new Dictionary<string, string>());
                _cboH.Items.Clear();
                _cboL.Items.Clear();

                _grid.DataSource = null;
                AtualizarGridAposFiltro();
                AtualizarStringsSelecao(); // Inicializa cwa/cwb/cwc e Pset... de acordo com a primeira linha
                AtualizarStatus("Planilha carregada. Selecione em cascata (B -> H -> L).");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Erro ao carregar planilha: " + ex.Message, "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DataTable LerPlanilhaXlsxComEPPlus(string caminhoArquivo, bool primeiraLinhaEhCabecalho)
        {


            FileInfo fi = new FileInfo(caminhoArquivo);
            DataTable tabela = new DataTable("Planilha");

            using (FileStream fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (ExcelPackage pacote = new ExcelPackage(fs))
                {
                    if (pacote.Workbook == null || pacote.Workbook.Worksheets == null || pacote.Workbook.Worksheets.Count == 0)
                    {
                        throw new System.Exception("O arquivo XLSX não possui worksheets.");
                    }

                    ExcelWorksheet ws = pacote.Workbook.Worksheets[0];
                    if (ws.Dimension == null)
                    {
                        throw new System.Exception("A worksheet está vazia.");
                    }

                    int rIni = ws.Dimension.Start.Row;
                    int cIni = ws.Dimension.Start.Column;
                    int rFim = ws.Dimension.End.Row;
                    int cFim = ws.Dimension.End.Column;

                    // Cabeçalhos
                    for (int c = cIni; c <= cFim; c++)
                    {
                        string nomeColuna;
                        if (primeiraLinhaEhCabecalho)
                        {
                            object v = ws.Cells[rIni, c].Value;
                            nomeColuna = v != null ? v.ToString().Trim() : "Coluna" + c.ToString();
                        }
                        else
                        {
                            nomeColuna = "Coluna" + c.ToString();
                        }

                        // Garantir unicidade em caso de duplicados
                        string nomeFinal = nomeColuna;
                        int sufixo = 1;
                        while (tabela.Columns.Contains(nomeFinal))
                        {
                            nomeFinal = nomeColuna + "_" + sufixo.ToString();
                            sufixo = sufixo + 1;
                        }

                        tabela.Columns.Add(nomeFinal, typeof(string));
                    }

                    // Dados
                    int rStart = primeiraLinhaEhCabecalho ? rIni + 1 : rIni;
                    for (int r = rStart; r <= rFim; r++)
                    {
                        bool linhaVazia = true;
                        DataRow row = tabela.NewRow();
                        int colIndex = 0;
                        for (int c = cIni; c <= cFim; c++)
                        {
                            string texto = ws.Cells[r, c].Text; // .Text preserva formatações
                            if (!string.IsNullOrWhiteSpace(texto))
                            {
                                linhaVazia = false;
                            }
                            row[colIndex] = texto ?? string.Empty;
                            colIndex = colIndex + 1;
                        }

                        if (!linhaVazia)
                        {
                            tabela.Rows.Add(row);
                        }
                    }
                }
            }

            return tabela;
        }

        private void AtualizarCascata(int nivelAlterado)
        {
      
            if (_dados == null)
            {
                return;
            }

            // Inicia com os filtros atuais das seleções visíveis.
            Dictionary<string, string> currentFilters = ObterFiltrosSelecionados();

            // Se o nível alterado for o primeiro (cboB), ou se for a carga inicial,
            // atualiza o segundo ComboBox (cboH).
            if (nivelAlterado <= 1)
            {
                PopularDistinct(_cboH, _colNomeFiltroH, currentFilters);
                // IMPORTANTE: Após _cboH ser populado e ter sua seleção definida programaticamente,
                // precisamos REFRESCAR os filtros para que o valor recém-selecionado de _cboH
                // seja considerado no filtro de _cboL.
                currentFilters = ObterFiltrosSelecionados();
            }

            // Se o nível alterado for o primeiro ou o segundo (cboH),
            // atualiza o terceiro ComboBox (cboL).
            if (nivelAlterado <= 2)
            {
                PopularDistinct(_cboL, _colNomeFiltroL, currentFilters);
                // Não precisa refrescar os filtros novamente, pois _cboL é o último na cascata.
            }

            AtualizarGridAposFiltro();
            AtualizarStringsSelecao(); // Atualiza cwa..cwe e Pset... conforme filtros
            AtualizarStatus(null);
        }
        

        private Dictionary<string, string> ObterFiltrosSelecionados()
        {
            Dictionary<string, string> filtros = new Dictionary<string, string>();

            if (_cboB.SelectedItem != null && !string.IsNullOrWhiteSpace(_cboB.SelectedItem.ToString()))
            {
                filtros[_colNomeFiltroB] = _cboB.SelectedItem.ToString();
            }

            if (_cboH.SelectedItem != null && !string.IsNullOrWhiteSpace(_cboH.SelectedItem.ToString()))
            {
                filtros[_colNomeFiltroH] = _cboH.SelectedItem.ToString();
            }

            if (_cboL.SelectedItem != null && !string.IsNullOrWhiteSpace(_cboL.SelectedItem.ToString()))
            {
                filtros[_colNomeFiltroL] = _cboL.SelectedItem.ToString();
            }

            return filtros;
        }

        private List<DataRow> FiltrarLinhas(Dictionary<string, string> filtros)
        {
            if (_dados == null)
            {
                return new List<DataRow>();
            }

            IEnumerable<DataRow> query = _dados.AsEnumerable();

            foreach (KeyValuePair<string, string> kvp in filtros)
            {
                string coluna = kvp.Key;
                string valor = kvp.Value;
                query = query.Where(r => string.Equals(Convert.ToString(r[coluna]), valor, StringComparison.CurrentCulture));
            }

            List<DataRow> linhas = query.ToList();
            return linhas;
        }

        private void PopularDistinct(ComboBox combo, string coluna, Dictionary<string, string> filtros)
        {
            if (_dados == null)
            {
                return;
            }

            List<DataRow> linhas = FiltrarLinhas(filtros);

            List<string> valores = linhas
                .Select(r => r[coluna] == null ? string.Empty : Convert.ToString(r[coluna]))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            combo.BeginUpdate();
            try
            {
                object selAnterior = combo.SelectedItem;
                combo.Items.Clear();
                foreach (string v in valores)
                {
                    v.ToUpper(); // Normaliza para maiúsculas, se necessário
                    combo.Items.Add(v);
                }

                if (combo.Items.Count > 0)
                {
                    // Se o valor anterior ainda existir, mantém; caso contrário, seleciona o primeiro
                    if (selAnterior != null && valores.Contains(selAnterior.ToString()))
                    {
                        
                        combo.SelectedItem = selAnterior;
                    }
                    else
                    {
                        combo.SelectedIndex = 0;
                    }
                }
                else
                {
                    combo.SelectedItem = null;
                }
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        private void AtualizarGridAposFiltro()
        {
            if (_dados == null)
            {
                _grid.DataSource = null;
                return;
            }

            Dictionary<string, string> filtros = ObterFiltrosSelecionados();
            List<DataRow> linhas = FiltrarLinhas(filtros);

            DataTable preview = _dados.Clone();
            int count = 0;
            foreach (DataRow r in linhas)
            {
                if (count >= 200) break;
                preview.ImportRow(r);
                count = count + 1;
            }
            // Mapeie aqui os índices das colunas que você deseja exibir
            Int32[] idxDesejados = new Int32[] { 1, 7, 11, 0, 6, 10 }; // B, H, L, A, G, N

            // Validação defensiva (evita IndexOutOfRange)
            for (Int32 i = 0; i < idxDesejados.Length; i++)
            {
                if (idxDesejados[i] < 0 || idxDesejados[i] >= _dados.Columns.Count)
                {
                    throw new IndexOutOfRangeException("Índice de coluna inválido: " + idxDesejados[i].ToString());
                }
            }

            // Obtemos os nomes reais das colunas na DataTable (importante para o DataPropertyName)
            String[] nomesColunas = new String[idxDesejados.Length];
            for (Int32 i = 0; i < idxDesejados.Length; i++)
            {
                nomesColunas[i] = _dados.Columns[idxDesejados[i]].ColumnName;
            }

            // Cria um DataView e projeta para uma nova DataTable apenas com as colunas desejadas
            DataView dv = new DataView(_dados);
            DataTable tabelaProjetada = dv.ToTable(false, nomesColunas);

            // Configura o grid com colunas manuais para total controle de ordem e cabeçalho
            _grid.AutoGenerateColumns = false;
            _grid.Columns.Clear();
            for (Int32 i = 0; i < nomesColunas.Length; i++)
            {
                DataGridViewTextBoxColumn coluna = new DataGridViewTextBoxColumn();
                coluna.Name = "col_" + nomesColunas[i];
                
                coluna.DataPropertyName = nomesColunas[i];
                coluna.ReadOnly = true; // ajuste se desejar editar
                coluna.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                _grid.Columns.Add(coluna);
            }



            _grid.DataSource = preview;
        }

        private void AtualizarStringsSelecao()
        {
            // Strings cwa..cwe: capturam o que está selecionado (NOMES)
            cwa = ObterValorSelecionado(_cboB);
            cwb = ObterValorSelecionado(_cboH);
            cwc = ObterValorSelecionado(_cboL);
            cwd = string.Empty;
            cwe = string.Empty;

            // Códigos para Psets: A/G/N da PRIMEIRA linha correspondente aos filtros atuais
            Dictionary<string, string> filtros = ObterFiltrosSelecionados();
            List<DataRow> linhasFiltradas = FiltrarLinhas(filtros);

            if (linhasFiltradas.Any())
            {
                DataRow linha = linhasFiltradas.First();

                Pset_CWA = Convert.ToString(linha[_colNomeCodigoA]); // A
                Pset_CWP = Convert.ToString(linha[_colNomeCodigoG]);            // G
                Pset_IWP = Convert.ToString(linha[_colNomeCodigoN]);            // K
                // Opcional: descrição (pode ser útil)
                PsetDescricaoAtividade = Convert.ToString(linha[_colNomeFiltroL]); // L (mesma coluna de nome)
            }
            else
            {
                Pset_IWP = string.Empty;
                Pset_CWA = string.Empty;
                Pset_CWP = string.Empty;
                PsetDescricaoAtividade = string.Empty;
            }

            Editor docEditor = Manager.DocEditor;
            if (docEditor != null)
            {
                docEditor.WriteMessage(
                    "\n[Debug] Seleções: B='" + cwa + "', H='" + cwb + "', L='" + cwc + "'. " +
                    "Psets: CODIGO_ATIVIDADE='" + Pset_IWP + "', CWA='" + Pset_CWA + "', CWP='" + Pset_CWP + "'.");
            }
        }

        private string ObterValorSelecionado(ComboBox combo)
        {
            if (combo == null) return string.Empty;
            if (combo.SelectedItem == null) return string.Empty;

            DataRowView drv = combo.SelectedItem as DataRowView;
            if (drv != null)
            {
                string displayMember = combo.DisplayMember;
                if (!string.IsNullOrEmpty(displayMember) && drv.Row != null && drv.Row.Table != null && drv.Row.Table.Columns.Contains(displayMember))
                {
                    object valor = drv[displayMember];
                    return valor != null ? Convert.ToString(valor) : string.Empty;
                }

                if (drv.Row != null && drv.Row.Table != null && drv.Row.Table.Columns.Count > 0)
                {
                    object valorPrimeiraColuna = drv[0];
                    return valorPrimeiraColuna != null ? Convert.ToString(valorPrimeiraColuna) : string.Empty;
                }

                return string.Empty;
            }

            return Convert.ToString(combo.SelectedItem);
        }

        private void AtualizarStatus(string textoFixo)
        {
            if (!string.IsNullOrEmpty(textoFixo))
            {
                _lblStatus.Text = textoFixo;
                return;
            }

            if (_dados == null)
            {
                _lblStatus.Text = "Carregue uma planilha para começar.";
                return;
            }

            Dictionary<string, string> filtros = ObterFiltrosSelecionados();
            List<DataRow> linhas = FiltrarLinhas(filtros);

            int total = _dados.Rows.Count;
            int filtrados = linhas.Count;
            _lblStatus.Text = "Registros filtrados: " + filtrados + " de " + total + ".";
        }

        private void LimparCampos()
        {
            _txtArquivo.Text = string.Empty;
            _cboB.Items.Clear();
            _cboH.Items.Clear();
            _cboL.Items.Clear();
            _grid.DataSource = null;
            _dados = null;

            cwa = string.Empty; cwb = string.Empty; cwc = string.Empty; cwd = string.Empty; cwe = string.Empty;
            Pset_IWP = string.Empty; Pset_CWA = string.Empty; Pset_CWP = string.Empty; PsetDescricaoAtividade = string.Empty;

            AtualizarStatus("Carregue uma planilha (.xlsx) para começar.");
        }

        private void OnAplicarPsets(object sender, EventArgs e)
        {
            try
            {
                if (_dados == null)
                {
                    MessageBox.Show("Nenhuma planilha carregada.", "Atenção",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Garante que os valores estão sincronizados com a seleção atual
                AtualizarStringsSelecao();

                if (string.IsNullOrEmpty(Pset_IWP) && string.IsNullOrEmpty(Pset_CWA) && string.IsNullOrEmpty(Pset_CWP))
                {
                    MessageBox.Show("Nenhuma linha correspondente aos filtros atuais para obter os códigos (A/G/N).", "Atenção",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                AplicarNoCivil3D();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Erro ao aplicar: " + ex.Message, "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AplicarNoCivil3D()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelecione os objetos que receberão os Property Sets:";
            PromptSelectionResult psr = docEditor.GetSelection(pso);

            if (psr.Status != PromptStatus.OK || psr.Value == null)
            {
                docEditor.WriteMessage("\nNenhum objeto selecionado. Operação cancelada.");
                return;
            }

            ObjectId[] ids = psr.Value.GetObjectIds();
            int totalAplicado = 0;

            // Nome do Property Set Definition no desenho (ajuste conforme seu template)
            string propSetName = "Códigos AWP";

            using (DocumentLock dl = civilDoc.LockDocument())
            {
                using (Transaction transCad = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        DictionaryPropertySetDefinitions dictDefs = new DictionaryPropertySetDefinitions(db);

                        if (!dictDefs.Has(propSetName, transCad))
                        {
                            docEditor.WriteMessage("\nErro: Property Set Definition '" + propSetName + "' não encontrado no desenho.");
                            transCad.Abort();
                            return;
                        }

                        ObjectId psetDefId = dictDefs.GetAt(propSetName);
                        PropertySetDefinition psetDef = (PropertySetDefinition)transCad.GetObject(psetDefId, OpenMode.ForRead);

                        foreach (ObjectId id in ids)
                        {
                            try
                            {
                                Entity ent = (Entity)transCad.GetObject(id, OpenMode.ForWrite);

                                // Anexa o Pset se necessário
                                PropertyDataServices.AddPropertySet(ent, psetDefId);

                                ObjectId psetId = PropertyDataServices.GetPropertySet(ent, psetDefId);
                                PropertySet pset = (PropertySet)transCad.GetObject(psetId, OpenMode.ForWrite);

                                int idxCodAtv = pset.PropertyNameToId("IWP");
                                pset.SetAt(idxCodAtv, Pset_IWP);

                                int idxCwa = pset.PropertyNameToId("CWA");
                                pset.SetAt(idxCwa, Pset_CWA);

                                int idxCwp = pset.PropertyNameToId("CWP");
                                pset.SetAt(idxCwp, Pset_CWP);

                                int idxEwp= pset.PropertyNameToId("EWP");
                                pset.SetAt(idxEwp, Pset_CWP);

                                int idxPwp = pset.PropertyNameToId("PWP");
                                pset.SetAt(idxPwp, Pset_CWP);

                                int idxSub = pset.PropertyNameToId("SUBAREA");
                                pset.SetAt(idxSub, "AO02");

                                int idxDis = pset.PropertyNameToId("DISCIPLINA");
                                pset.SetAt(idxDis, "Civil");

                                // Se você tiver a propriedade de descrição no Pset:
                                int idxDesc;
                                try
                                {
                                    idxDesc = pset.PropertyNameToId("DESCRICAO_ATIVIDADE");
                                    pset.SetAt(idxDesc, PsetDescricaoAtividade);
                                }
                                catch
                                {
                                    // Se não existir, apenas ignora
                                }

                                totalAplicado = totalAplicado + 1;
                            }
                            catch (System.Exception exObj)
                            {
                                docEditor.WriteMessage("\nFalha em um objeto (" + id.ToString() + "): " + exObj.Message);
                            }
                        }

                        transCad.Commit();
                    }
                    catch (System.Exception exTx)
                    {
                        docEditor.WriteMessage("\nErro na transação: " + exTx.Message);
                        transCad.Abort();
                        throw;
                    }
                }
            }

            docEditor.WriteMessage("\nAplicação concluída. Objetos processados: " + totalAplicado + ".");
            MessageBox.Show("Operação concluída.\nObjetos processados: " + totalAplicado,
                "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }




    }
}*/