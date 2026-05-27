/*/ Referências típicas necessárias no projeto:
// - Autodesk.AutoCAD.ApplicationServices
// - Autodesk.AutoCAD.DatabaseServices
// - Autodesk.AutoCAD.EditorInput
// - Autodesk.AutoCAD.Runtime
// - Autodesk.AutoCAD.Windows
// - Autodesk.Civil.ApplicationServices
// - Autodesk.Civil.DatabaseServices
// - System.Windows.Forms
// - System.Data
// - System.Drawing
// - System.Core
// Observação: para .xlsx ver nota ao final.

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
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DataTable = System.Data.DataTable;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Excel = Microsoft.Office.Interop.Excel;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using FlowDirection = System.Windows.Forms.FlowDirection;
using Label = System.Windows.Forms.Label;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace AutomacoesCivil3D
{
    public class PsetsCascataCommand11
    {
        private static PaletteSet _palette;

        [CommandMethod("PSetsCascata")]
        public void PSetsCascata1()
        {
            if (_palette == null)
            {
                _palette = new PaletteSet("Seleção em Cascata - Psets");
                _palette.Style =
                    PaletteSetStyles.ShowAutoHideButton |
                    PaletteSetStyles.ShowCloseButton |
                    PaletteSetStyles.NameEditable |
                    PaletteSetStyles.ShowPropertiesMenu;

                _palette.MinimumSize = new Size(420, 520);
                CascataPsets controle = new CascataPsets();
                _palette.Add("Selecionar", controle);
                _palette.Visible = true;
            }
            else
            {
                if (!_palette.Visible)
                    _palette.Visible = true;
            }

            _palette.Activate(0);
        }
    }

    public class CascataPsetsControl1 : UserControl
    {
        private TextBox _txtArquivo;
        private Button _btnArquivo;
        private Label _lblStatus;

        private ComboBox _cbo1;
        private ComboBox _cbo2;
        private ComboBox _cbo3;
        private ComboBox _cbo4;
        private CheckedListBox _lst5; // última coluna com múltipla seleção

        private DataGridView _grid;
        private Button _btnAplicar;
        private Button _btnLimpar;

        private DataTable _dados;
        private string[] _colunas = new string[5];

      
        // Sua tabela base e a view filtrada (se você já tiver, aproveite as existentes)
        private DataTable _tabela;
        private DataView _viewFiltradaAtual;

        // As 5 strings solicitadas
        private string cwa = string.Empty;
        private string cwb = string.Empty;
        private string cwc = string.Empty;
        private string cwd = string.Empty;
        private string cwe = string.Empty;

        // Exposição pública somente leitura (opcional)
        public string Cwa { get { return this.cwa; } }
        public string Cwb { get { return this.cwb; } }
        public string Cwc { get { return this.cwc; } }
        public string Cwd { get { return this.cwd; } }
        public string Cwe { get { return this.cwe; } }

        


        public CascataPsetsControl1()
        {
            InitializeUi();
            WireEvents();
        }

        private void InitializeUi()
        {
            this.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(8);
            layout.ColumnCount = 2;
            layout.RowCount = 10;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            Label lblArquivo = new Label();
            lblArquivo.Text = "Planilha (.csv ou .xlsx):";
            lblArquivo.TextAlign = ContentAlignment.MiddleLeft;
            lblArquivo.Dock = DockStyle.Fill;

            _txtArquivo = new TextBox();
            _txtArquivo.Dock = DockStyle.Fill;
            _txtArquivo.ReadOnly = true;

            _btnArquivo = new Button();
            _btnArquivo.Text = "Procurar...";
            _btnArquivo.Dock = DockStyle.Fill;

            _cbo1 = new ComboBox();
            _cbo2 = new ComboBox();
            _cbo3 = new ComboBox();
            _cbo4 = new ComboBox();
            _cbo1.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbo2.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbo3.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbo4.DropDownStyle = ComboBoxStyle.DropDownList;
            
            _cbo1.Dock = DockStyle.Fill;
            _cbo2.Dock = DockStyle.Fill;
            _cbo3.Dock = DockStyle.Fill;
            _cbo4.Dock = DockStyle.Fill;
            

            Label lbl1 = new Label(); lbl1.Text = "Coluna 1:"; lbl1.Dock = DockStyle.Fill; lbl1.TextAlign = ContentAlignment.MiddleLeft;
            Label lbl2 = new Label(); lbl2.Text = "Coluna 2:"; lbl2.Dock = DockStyle.Fill; lbl2.TextAlign = ContentAlignment.MiddleLeft;
            Label lbl3 = new Label(); lbl3.Text = "Coluna 3:"; lbl3.Dock = DockStyle.Fill; lbl3.TextAlign = ContentAlignment.MiddleLeft;
            Label lbl4 = new Label(); lbl4.Text = "Coluna 4:"; lbl4.Dock = DockStyle.Fill; lbl4.TextAlign = ContentAlignment.MiddleLeft;

            Label lbl5 = new Label(); lbl5.Text = "Coluna 5 (múltipla escolha):"; lbl5.Dock = DockStyle.Fill; lbl5.TextAlign = ContentAlignment.MiddleLeft;
            _lst5 = new CheckedListBox();
            _lst5.Dock = DockStyle.Fill;
            _lst5.CheckOnClick = true;

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
            _lblStatus.Text = "Carregue uma planilha para começar.";
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.ForeColor = Color.DimGray;

            // Linhas do layout
            // Linha 0: label arquivo (col 0) + textbox (col 1)
            layout.Controls.Add(lblArquivo, 0, 0);
            layout.Controls.Add(_txtArquivo, 1, 0);

            // Linha 1: botão procurar (col 1)
            layout.Controls.Add(_btnArquivo, 1, 1);

            // Linha 2: Coluna 1
            layout.Controls.Add(lbl1, 0, 2);
            layout.Controls.Add(_cbo1, 1, 2);

            // Linha 3: Coluna 2
            layout.Controls.Add(lbl2, 0, 3);
            layout.Controls.Add(_cbo2, 1, 3);

            // Linha 4: Coluna 3
            layout.Controls.Add(lbl3, 0, 4);
            layout.Controls.Add(_cbo3, 1, 4);

            // Linha 5: Coluna 4
            layout.Controls.Add(lbl4, 0, 5);
            layout.Controls.Add(_cbo4, 1, 5);

            // Linha 6: Coluna 5 (CheckedListBox)
            layout.Controls.Add(lbl5, 0, 6);
            layout.Controls.Add(_lst5, 1, 6);

            // Linha 7: Grid
            layout.SetColumnSpan(_grid, 2);
            layout.Controls.Add(_grid, 0, 7);

            // Linha 8: Barra de botões
            layout.SetColumnSpan(barraBotoes, 2);
            layout.Controls.Add(barraBotoes, 0, 8);

            // Linha 9: Status
            layout.SetColumnSpan(_lblStatus, 2);
            layout.Controls.Add(_lblStatus, 0, 9);

            // Ajustar alturas
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20)); // 6 (lista)
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // 7 (grid)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 8 (botões)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 9 (status)

            this.Controls.Add(layout);
        }

        private void WireEvents()
        {
            _btnArquivo.Click += OnSelecionarArquivo;
            _cbo1.SelectedIndexChanged += (s, e) => AtualizarCascata(1);
            _cbo2.SelectedIndexChanged += (s, e) => AtualizarCascata(2);
            _cbo3.SelectedIndexChanged += (s, e) => AtualizarCascata(3);
            _cbo4.SelectedIndexChanged += (s, e) => AtualizarCascata(4);
            _btnLimpar.Click += (s, e) => LimparCampos();
            _btnAplicar.Click += OnAplicarPsets;
        }

        private void OnSelecionarArquivo(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Planilhas (*.csv;*.xlsx)|*.csv;*.xlsx|Todos os arquivos (*.*)|*.*";
            ofd.Title = "Selecione a planilha com 5 colunas";

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
                DataTable dados = null;
                string ext = Path.GetExtension(caminho).ToLowerInvariant();

                if (ext == ".xlsx")
                {
                    dados = LerPlanilhaXlsxComEPPlus(caminho, true, 5);
                }
                else
                {
                    MessageBox.Show("Formato não suportado. Use .csv (ou .xlsx com suporte habilitado).",
                        "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (dados == null || dados.Columns.Count < 5)
                {
                    MessageBox.Show("A planilha deve conter pelo menos 5 colunas.", "Atenção",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _dados = dados;

                // Definir nomes das 5 colunas (usando o cabeçalho da planilha)
                _colunas[0] = _dados.Columns[0].ColumnName;
                _colunas[1] = _dados.Columns[1].ColumnName;
                _colunas[2] = _dados.Columns[2].ColumnName;
                _colunas[3] = _dados.Columns[3].ColumnName;
                _colunas[4] = _dados.Columns[4].ColumnName;

                PopularDistinct(_cbo1, _colunas[0], new Dictionary<string, string>());
                _cbo2.Items.Clear();
                _cbo3.Items.Clear();
                _cbo4.Items.Clear();
                _lst5.Items.Clear();

                _grid.DataSource = null;
                AtualizarGridAposFiltro();

                _lblStatus.Text = "Planilha carregada. Selecione os valores em cascata.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao carregar planilha: " + ex.Message, "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private DataTable LerPlanilhaXlsxComEPPlus(string caminhoArquivo, bool primeiraLinhaEhCabecalho, int maxColunas)
        {
            // ATENÇÃO: ajuste a licença conforme sua realidade
            // LicenseContext.NonCommercial apenas se seu uso for compatível com a licença não comercial do EPPlus.
            // Se você possui licença comercial, substitua por LicenseContext.Commercial.
            // Configure EPPlus para uso não-comercial
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

            FileInfo arquivo = new FileInfo(caminhoArquivo);
            DataTable tabela = new DataTable("Planilha");

            using (ExcelPackage pacote = new ExcelPackage(arquivo))
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

                int linhaInicio = ws.Dimension.Start.Row;
                int colunaInicio = ws.Dimension.Start.Column;
                int linhaFim = ws.Dimension.End.Row;
                int colunaFim = ws.Dimension.End.Column;

                int colunasLidas = Math.Min(maxColunas, colunaFim - colunaInicio + 1);

                // Configurar colunas do DataTable
                if (primeiraLinhaEhCabecalho)
                {
                    // Ler cabeçalhos da primeira linha
                    // Garante nomes únicos se houver duplicidade
                    string[] nomes = new string[colunasLidas];
                    for (int c = 0; c < colunasLidas; c++)
                    {
                        object valorCab = ws.Cells[linhaInicio, colunaInicio + c].Value;
                        string nomeColuna = valorCab != null ? valorCab.ToString().Trim() : string.Empty;
                        if (string.IsNullOrWhiteSpace(nomeColuna))
                        {
                            nomeColuna = "Coluna" + (c + 1).ToString();
                        }

                        // Garantir unicidade
                        nomeColuna = GerarNomeDeColunaUnico(tabela, nomeColuna);
                        nomes[c] = nomeColuna;
                        tabela.Columns.Add(nomeColuna, typeof(string));
                    }

                    // Dados começam na próxima linha
                    for (int r = linhaInicio + 1; r <= linhaFim; r++)
                    {
                        // Ignorar linhas totalmente vazias nas colunas alvo
                        if (LinhaVazia(ws, r, colunaInicio, colunasLidas))
                        {
                            continue;
                        }

                        DataRow linha = tabela.NewRow();
                        for (int c = 0; c < colunasLidas; c++)
                        {
                            object v = ws.Cells[r, colunaInicio + c].Value;
                            string texto = v != null ? v.ToString() : string.Empty;
                            linha[c] = texto;
                        }
                        tabela.Rows.Add(linha);
                    }
                }
                else
                {
                    // Sem cabeçalho: criar nomes padrão
                    for (int c = 0; c < colunasLidas; c++)
                    {
                        string nomeColuna = "Coluna" + (c + 1).ToString();
                        tabela.Columns.Add(nomeColuna, typeof(string));
                    }

                    for (int r = linhaInicio; r <= linhaFim; r++)
                    {
                        if (LinhaVazia(ws, r, colunaInicio, colunasLidas))
                        {
                            continue;
                        }

                        DataRow linha = tabela.NewRow();
                        for (int c = 0; c < colunasLidas; c++)
                        {
                            object v = ws.Cells[r, colunaInicio + c].Value;
                            string texto = v != null ? v.ToString() : string.Empty;
                            linha[c] = texto;
                        }
                        tabela.Rows.Add(linha);
                    }
                }
            }

            return tabela;
        }

        private bool LinhaVazia(ExcelWorksheet ws, int linha, int colunaInicio, int colunasLidas)
        {
            for (int c = 0; c < colunasLidas; c++)
            {
                object v = ws.Cells[linha, colunaInicio + c].Value;
                if (v != null && !string.IsNullOrWhiteSpace(v.ToString()))
                {
                    return false;
                }
            }
            return true;
        }

        private string GerarNomeDeColunaUnico(DataTable tabela, string nomeDesejado)
        {
            string nome = nomeDesejado;
            int sufixo = 1;
            while (tabela.Columns.Contains(nome))
            {
                nome = nomeDesejado + "_" + sufixo.ToString();
                sufixo++;
            }
            return nome;
        }
    

        private void AtualizarCascata(int nivelAlterado)
        {
            if (_dados == null)
                return;

            Dictionary<string, string> filtros = ObterFiltrosSelecionados();

            if (nivelAlterado <= 1) PopularDistinct(_cbo2, _colunas[1], filtros);
            if (nivelAlterado <= 2) PopularDistinct(_cbo3, _colunas[2], filtros);
            if (nivelAlterado <= 3) PopularDistinct(_cbo4, _colunas[3], filtros);

            // Última coluna (CheckedListBox) com base nos filtros correntes
            PopularDistinctLista(_lst5, _colunas[4], filtros);

            AtualizarGridAposFiltro();
            AtualizarStatus();
        }

        private Dictionary<string, string> ObterFiltrosSelecionados()
        {
            Dictionary<string, string> filtros = new Dictionary<string, string>();

            if (_cbo1.SelectedItem != null && !string.IsNullOrWhiteSpace(_cbo1.SelectedItem.ToString()))
                filtros[_colunas[0]] = _cbo1.SelectedItem.ToString();

            if (_cbo2.SelectedItem != null && !string.IsNullOrWhiteSpace(_cbo2.SelectedItem.ToString()))
                filtros[_colunas[1]] = _cbo2.SelectedItem.ToString();

            if (_cbo3.SelectedItem != null && !string.IsNullOrWhiteSpace(_cbo3.SelectedItem.ToString()))
                filtros[_colunas[2]] = _cbo3.SelectedItem.ToString();

            if (_cbo4.SelectedItem != null && !string.IsNullOrWhiteSpace(_cbo4.SelectedItem.ToString()))
                filtros[_colunas[3]] = _cbo4.SelectedItem.ToString();

            this.AtualizarStringsSelecao();

            

            return filtros;
        }

        private void PopularDistinct(ComboBox combo, string coluna, Dictionary<string, string> filtros)
        {
            if (_dados == null)
                return;

            List<DataRow> linhas = FiltrarLinhas(filtros);

#pragma warning disable CS8619 // A anulabilidade de tipos de referência no valor não corresponde ao tipo de destino.
            List<string> valores = linhas
                .Select(r => r[columnName: coluna] == null ? string.Empty : Convert.ToString(r[coluna]))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
#pragma warning restore CS8619 // A anulabilidade de tipos de referência no valor não corresponde ao tipo de destino.

            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (string v in valores)
                {
                    combo.Items.Add(v);
                }
                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        private void PopularDistinctLista(CheckedListBox lista, string coluna, Dictionary<string, string> filtros)
        {
            if (_dados == null)
                return;

            List<DataRow> linhas = FiltrarLinhas(filtros);

#pragma warning disable CS8619 // A anulabilidade de tipos de referência no valor não corresponde ao tipo de destino.
            List<string> valores = linhas
                .Select(r => r[coluna] == null ? string.Empty : Convert.ToString(r[coluna]))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
#pragma warning restore CS8619 // A anulabilidade de tipos de referência no valor não corresponde ao tipo de destino.

            lista.BeginUpdate();
            try
            {
                lista.Items.Clear();
                foreach (string v in valores)
                {
                    lista.Items.Add(v, false);
                }
                lista.EndUpdate();
            }
            finally
            {
                lista.EndUpdate();
            }
        }

        private List<DataRow> FiltrarLinhas(Dictionary<string, string> filtros)
        {
            if (_dados == null)
                return new List<DataRow>();

            IEnumerable<DataRow> query = _dados.AsEnumerable();

            foreach (KeyValuePair<string, string> kvp in filtros)
            {
                string col = kvp.Key;
                string val = kvp.Value;
                query = query.Where(r => string.Equals(
                    Convert.ToString(r[col]), val, StringComparison.CurrentCulture));
            }

            List<DataRow> linhas = query.ToList();
            return linhas;
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

            // Exibir as primeiras 200 linhas filtradas para pré-visualização
            DataTable preview = _dados.Clone();
            int count = 0;
            foreach (DataRow r in linhas)
            {
                if (count >= 200) break;
                preview.ImportRow(r);
                count++;
            }

            _grid.DataSource = preview;
        }

        private void AtualizarStatus()
        {
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
            _cbo1.Items.Clear();
            _cbo2.Items.Clear();
            _cbo3.Items.Clear();
            _cbo4.Items.Clear();
            _lst5.Items.Clear();
            _grid.DataSource = null;
            _dados = null;
            _lblStatus.Text = "Carregue uma planilha para começar.";
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

                List<string> valoresSelecionadosUltimaColuna = new List<string>();
                foreach (object item in _lst5.CheckedItems)
                {
                    if (item != null)
                        valoresSelecionadosUltimaColuna.Add(item.ToString());
                }

                if (valoresSelecionadosUltimaColuna.Count == 0)
                {
                    DialogResult dr = MessageBox.Show(
                        "Nenhum valor selecionado na última coluna. Deseja aplicar para todos os valores atualmente listados?",
                        "Confirmar",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (dr == DialogResult.Yes)
                    {
                        foreach (object item in _lst5.Items)
                        {
                            if (item != null)
                                valoresSelecionadosUltimaColuna.Add(item.ToString());
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                Dictionary<string, string> filtros = ObterFiltrosSelecionados();
                AplicarNoCivil3D(filtros, valoresSelecionadosUltimaColuna);

            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao aplicar: " + ex.Message, "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AtualizarStringsSelecao()
        {
            this.cwa = this.ObterValorSelecionado(this._cbo1);
            this.cwb = this.ObterValorSelecionado(this._cbo2);
            this.cwc = this.ObterValorSelecionado(this._cbo3);
            this.cwd = this.ObterValorSelecionado(this._cbo4);
            Editor docEditor = Manager.DocEditor;
            docEditor.WriteMessage(cwa + " - " + cwb + " - " + cwc + " - " + cwd + "\n");
            //this.cwe = this.ObterValorSelecionado(this._cbo5);
        }

        private void AplicarNoCivil3D(Dictionary<string, string> filtros, List<string> valoresUltima)
        {
            // Obter objetos alvo via seleção do usuário
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

            SelectionSet sel = psr.Value;
            ObjectId[] ids = sel.GetObjectIds();

            // Aqui preparamos a lista de registros finais com base nos filtros + última coluna selecionada
            List<DataRow> baseFiltrada = FiltrarLinhas(filtros);
            List<DataRow> linhasFinais = baseFiltrada
                .Where(r => valoresUltima.Contains(Convert.ToString(r[_colunas[4]])))
                .ToList();

            int totalAplicado = 0;

            docEditor.WriteMessage("\nIniciando aplicação de Property Sets...");

            using (DocumentLock dl = civilDoc.LockDocument()) { 

            using (Transaction transCad = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {


                    // Exemplo de leitura do objeto de destino (Entity) com explicitação de tipo:
                    Solid3d ent = (Solid3d)transCad.GetObject(id, OpenMode.ForWrite);

                    

                    DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                    // Nome do property set (ajuste conforme o seu projeto)
                    string propSetNameB = "Códigos AWP";

                    // Obtem o ID do propertySetDefinition a partir do dicionário
                    ObjectId propSetIdA = dictionary.GetAt(propSetNameB);

                    PropertySetDefinition propSetDefB = (PropertySetDefinition)transCad.GetObject(propSetIdA, OpenMode.ForWrite);


                    DictionaryPropertySetDefinitions dictionary1 = new DictionaryPropertySetDefinitions(db);


                    docEditor.WriteMessage(cwa + " - " + cwb + " - " + cwc + " - " + cwd + "\n");


                    PropertyDataServices.AddPropertySet(ent, propSetIdA);

                        if (dictionary1.Has(propSetNameB, transCad))
                        {

                            // Obtem o ID do propertySetDefinition a partir do dicionário
                            ObjectId propSetId = dictionary1.GetAt(propSetNameB);
                            // Obter o objeto PropertySetDefinition
                            PropertySetDefinition propSetDef = (PropertySetDefinition)transCad.GetObject(propSetId, OpenMode.ForWrite);

                            // Obtem o ID da propriedade associada ao property set
                            ObjectId psets = PropertyDataServices.GetPropertySet(ent, propSetId);
                            // Obtem o objeto PropertySet associado ao ID
                            PropertySet pset = (PropertySet)transCad.GetObject(psets, OpenMode.ForWrite);

                            int index1 = pset.PropertyNameToId("CODIGO_ATIVIDADE");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index1, cwa);

                            int index2 = pset.PropertyNameToId("CWA");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index2, cwb);

                            int index3 = pset.PropertyNameToId("CWP");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index3, cwc);

                            int index4 = pset.PropertyNameToId("DESCRICAO_ATIVIDADE");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index4, cwd);

                            /*int index5 = pset.PropertyNameToId("DISCIPLINA");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index5, baseFiltrada[4]);

                            int index6 = pset.PropertyNameToId("EWP");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index6, baseFiltrada[5]);

                            int index7 = pset.PropertyNameToId("IWP");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index7, baseFiltrada[6]);

                            int index8 = pset.PropertyNameToId("PWP");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index8, baseFiltrada[7]);*/

                            /*int index9 = pset.PropertyNameToId("SUBAREA");
                            // Define o valor do campo do property set altura
                            pset.SetAt(index9, "AO02");


                        }




                    

                        
                    

                    
                 



                    totalAplicado++;
                }

                transCad.Commit();
            }
               
            
            }


            docEditor.WriteMessage("\nAplicação concluída. Objetos processados: " + totalAplicado + ". Registros base associados: " + linhasFinais.Count + ".");
            MessageBox.Show("Operação concluída.\nObjetos processados: " + totalAplicado + "\nLinhas finais na filtragem: " + linhasFinais.Count,
                "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }



        private string ObterValorSelecionado(ComboBox combo)
        {
            if (combo == null)
            {
                return string.Empty;
            }

            if (combo.SelectedItem == null)
            {
                return string.Empty;
            }

            DataRowView drv = combo.SelectedItem as DataRowView;
            if (drv != null)
            {
                // Se você definiu DisplayMember, usa-o como chave
                string displayMember = combo.DisplayMember;
                if (!string.IsNullOrEmpty(displayMember) && drv.Row != null && drv.Row.Table != null && drv.Row.Table.Columns.Contains(displayMember))
                {
                    object valor = drv[displayMember];
                    return valor != null ? Convert.ToString(valor) : string.Empty;
                }

                // Fallback: primeira coluna
                if (drv.Row != null && drv.Row.Table != null && drv.Row.Table.Columns.Count > 0)
                {
                    object valorPrimeiraColuna = drv[0];
                    return valorPrimeiraColuna != null ? Convert.ToString(valorPrimeiraColuna) : string.Empty;
                }

                return string.Empty;
            }

            return Convert.ToString(combo.SelectedItem);
        }
    }
}*/