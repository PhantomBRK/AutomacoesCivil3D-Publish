// Referências típicas necessárias no projeto:
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
using DataColumn = System.Data.DataColumn;

namespace AutomacoesCivil3D
{

    public static class AwpViewIo
    {
        private static string ResolveWorksheetName(IEnumerable<string> names)
        {
            // 1) tenta "Dados Psets"; 2) cai para primeira aba disponível
            foreach (string n in names) if (string.Equals(n, "Dados Psets", System.StringComparison.OrdinalIgnoreCase)) return n;
            foreach (string n in names) return n; // primeira
            throw new System.InvalidOperationException("XLSX sem abas.");
        }
    }

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

                CascataPsetsControl controle = new CascataPsetsControl();
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

    public class CascataPsetsControl : UserControl
    {
        private TextBox _txtArquivo;
        private Button _btnArquivo;
        private Label _lblStatus;

        private ComboBox _cboB;        // CWA (nome)
        private ComboBox _cboH;        // CWP (nome)
        private ComboBox _cboIWPDesc;  // IWP (descrição)
        private ComboBox _cboIWPCode;  // IWP (código)

        private DataGridView _grid;
        private Button _btnAplicar;
        private Button _btnLimpar;

        private DataTable _dados;

        // Nomes de coluna
        private string _colNomeFiltroB;           // B  CWA Nome
        private string _colNomeFiltroH;           // H  CWP Nome
        private string _colNomeFiltroL_IWPDesc;   // L  IWP Descrição
        private string _colNomeFiltroK_IWPCode;   // K  IWP Código
        private string _colNomeCodigoA;           // A  CWA Código
        private string _colNomeCodigoG;           // G  CWP Código
        private string _colNomeCodigoI_EWP;       // I  EWP
        private string _colNomeCodigoJ_PWP;       // J  PWP

        // Seleções correntes para aplicação nos PSETs
        public string Pset_IWP { get; private set; } = string.Empty;
        public string Pset_CWA { get; private set; } = string.Empty;
        public string Pset_CWP { get; private set; } = string.Empty;
        public string Pset_EWP { get; private set; } = string.Empty;
        public string Pset_PWP { get; private set; } = string.Empty;
        public string PsetDescricaoAtividade { get; private set; } = string.Empty;

        // Flag anti-reentrada durante bind programático
        private bool _binding;

        public CascataPsetsControl()
        {
            InitializeUi();
            WireEvents();
            AtualizarStatus("Carregue uma planilha (.xlsx) para começar.");
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

            layout.Controls.Add(new Label() { Text = "Planilha (.xlsx):", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            _txtArquivo = new TextBox() { ReadOnly = true, Dock = DockStyle.Fill };
            layout.Controls.Add(_txtArquivo, 1, 0);
            _btnArquivo = new Button() { Text = "Procurar...", Dock = DockStyle.Fill };
            layout.Controls.Add(_btnArquivo, 1, 1);

            layout.Controls.Add(new Label() { Text = "CÓDIGO CWA", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            _cboB = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            layout.Controls.Add(_cboB, 1, 2);

            layout.Controls.Add(new Label() { Text = "CÓDIGO CWP", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            _cboH = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            layout.Controls.Add(_cboH, 1, 3);

            layout.Controls.Add(new Label() { Text = "DESCRIÇÃO IWP", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            _cboIWPDesc = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            layout.Controls.Add(_cboIWPDesc, 1, 4);

            layout.Controls.Add(new Label() { Text = "CÓDIGO IWP", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            _cboIWPCode = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            layout.Controls.Add(_cboIWPCode, 1, 5);

            _grid = new DataGridView()
            {
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                Height = 160,
                Dock = DockStyle.Fill
            };
            layout.SetColumnSpan(_grid, 2);
            layout.Controls.Add(_grid, 0, 6);

            FlowLayoutPanel barraBotoes = new FlowLayoutPanel() { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            _btnAplicar = new Button() { Text = "Aplicar aos Psets", AutoSize = true };
            _btnLimpar = new Button() { Text = "Limpar", AutoSize = true };
            barraBotoes.Controls.Add(_btnAplicar);
            barraBotoes.Controls.Add(_btnLimpar);
            layout.SetColumnSpan(barraBotoes, 2);
            layout.Controls.Add(barraBotoes, 0, 7);

            _lblStatus = new Label() { Text = "", Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            layout.SetColumnSpan(_lblStatus, 2);
            layout.Controls.Add(_lblStatus, 0, 8);

            this.Controls.Add(layout);
        }

        private void WireEvents()
        {
            _btnArquivo.Click += OnSelecionarArquivo;

            _cboB.SelectedIndexChanged += (s, e) => { if (!_binding) AtualizarCascata(1); };
            _cboH.SelectedIndexChanged += (s, e) => { if (!_binding) AtualizarCascata(2); };
            _cboIWPDesc.SelectedIndexChanged += (s, e) => { if (!_binding) AtualizarCascata(3); };
            _cboIWPCode.SelectedIndexChanged += (s, e) => { if (!_binding) AtualizarCascata(4); };

            _btnLimpar.Click += (s, e) => LimparCampos();
            _btnAplicar.Click += OnAplicarPsets;
        }

        private void OnSelecionarArquivo(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Excel (*.xlsx)|*.xlsx|Excel (*.xlsm)|*.xlsm|Todos os arquivos (*.*)|*.*";
            ofd.Title = "Selecione a planilha";

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
                ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

                _dados = LerPlanilhaXlsxComEPPlus(caminho, true, 0);

                if (_dados == null || _dados.Columns.Count < 12)
                {
                    MessageBox.Show("A planilha deve conter A,B,G,H,I,J,K,L.", "Atenção", MessageBoxButtons.OK);
                    return;
                }

                _colNomeFiltroB = _dados.Columns[1].ColumnName;
                _colNomeFiltroH = _dados.Columns[7].ColumnName;
                _colNomeFiltroL_IWPDesc = _dados.Columns[11].ColumnName;
                _colNomeFiltroK_IWPCode = _dados.Columns[10].ColumnName;

                _colNomeCodigoA = _dados.Columns[0].ColumnName;
                _colNomeCodigoG = _dados.Columns[6].ColumnName;
                _colNomeCodigoI_EWP = _dados.Columns[8].ColumnName;
                _colNomeCodigoJ_PWP = _dados.Columns[9].ColumnName;

                _binding = true;
                PopularDistinct(_cboB, _colNomeFiltroB, new Dictionary<string, string>());
                _cboH.Items.Clear();
                _cboIWPDesc.Items.Clear();
                _cboIWPCode.Items.Clear();
                if (_cboB.Items.Count > 0) _cboB.SelectedIndex = 0;
                _binding = false;

                // Encadeia tudo com os valores recém definidos
                AtualizarCascata(1);
                AtualizarStatus("Planilha carregada. Selecione CWA → CWP → IWP.");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Erro ao carregar planilha: " + ex.Message, "Erro", MessageBoxButtons.OK);
            }
        }

        private DataTable LerPlanilhaXlsxComEPPlus(string caminhoArquivo, bool primeiraLinhaEhCabecalho, int sheetIndex)
        {
            FileInfo fi = new FileInfo(caminhoArquivo);
            DataTable tabela = new DataTable("Planilha");

            using (FileStream fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ExcelPackage pacote = new ExcelPackage(fs))
            {
                if (pacote.Workbook == null || pacote.Workbook.Worksheets == null || pacote.Workbook.Worksheets.Count <= sheetIndex)
                    throw new System.Exception($"Worksheet {sheetIndex} inexistente.");

                ExcelWorksheet ws = pacote.Workbook.Worksheets[sheetIndex];
                if (ws.Dimension == null) return tabela;

                int rIni = ws.Dimension.Start.Row;
                int cIni = ws.Dimension.Start.Column;
                int rFim = ws.Dimension.End.Row;
                int cFim = ws.Dimension.End.Column;

                for (int c = cIni; c <= cFim; c++)
                {
                    string nome = primeiraLinhaEhCabecalho ? (ws.Cells[rIni, c].Value?.ToString().Trim() ?? $"Coluna{c}") : $"Coluna{c}";
                    string final = nome; int sx = 1;
                    while (tabela.Columns.Contains(final)) { final = nome + "_" + sx; sx++; }
                    tabela.Columns.Add(final, typeof(string));
                }

                int rStart = primeiraLinhaEhCabecalho ? rIni + 1 : rIni;
                for (int r = rStart; r <= rFim; r++)
                {
                    bool vazia = true;
                    DataRow row = tabela.NewRow();
                    int colIndex = 0;
                    for (int c = cIni; c <= cFim; c++)
                    {
                        string texto = ws.Cells[r, c].Text;
                        if (!string.IsNullOrWhiteSpace(texto)) vazia = false;
                        row[colIndex] = texto ?? string.Empty;
                        colIndex++;
                    }
                    if (!vazia) tabela.Rows.Add(row);
                }
            }
            return tabela;
        }

        // ========================= CASCATA =========================

        private void AtualizarCascata(int nivelAlterado)
        {
            if (_dados == null) return;

            _binding = true;

            // 1) CWA -> CWP
            if (nivelAlterado <= 1)
            {
                Dictionary<string, string> f1 = ObterFiltrosSelecionados(_colNomeFiltroH, _colNomeFiltroL_IWPDesc, _colNomeFiltroK_IWPCode);
                PopularDistinct(_cboH, _colNomeFiltroH, f1);
                if (_cboH.Items.Count > 0) _cboH.SelectedIndex = 0; else _cboH.SelectedIndex = -1;
            }

            // 2) CWP -> IWP Desc
            if (nivelAlterado <= 2)
            {
                Dictionary<string, string> f2 = ObterFiltrosSelecionados(_colNomeFiltroL_IWPDesc, _colNomeFiltroK_IWPCode);
                PopularDistinct(_cboIWPDesc, _colNomeFiltroL_IWPDesc, f2);
                if (_cboIWPDesc.Items.Count > 0) _cboIWPDesc.SelectedIndex = 0; else _cboIWPDesc.SelectedIndex = -1;
            }

            // 3) IWP Desc -> IWP Code
            if (nivelAlterado <= 3)
            {
                Dictionary<string, string> f3 = ObterFiltrosSelecionados(_colNomeFiltroK_IWPCode);
                PopularDistinct(_cboIWPCode, _colNomeFiltroK_IWPCode, f3);
                if (_cboIWPCode.Items.Count > 0) _cboIWPCode.SelectedIndex = 0; else _cboIWPCode.SelectedIndex = -1;
            }

            _binding = false;

            AtualizarStringsSelecao();
            AtualizarGridAposFiltro();
            AtualizarStatus(null);
        }

        private Dictionary<string, string> ObterFiltrosSelecionados(params string[] excluir)
        {
            Dictionary<string, string> filtros = new Dictionary<string, string>();
            AdicionarFiltroSeSelecionado(filtros, _colNomeFiltroB, _cboB);
            AdicionarFiltroSeSelecionado(filtros, _colNomeFiltroH, _cboH);
            AdicionarFiltroSeSelecionado(filtros, _colNomeFiltroL_IWPDesc, _cboIWPDesc);
            AdicionarFiltroSeSelecionado(filtros, _colNomeFiltroK_IWPCode, _cboIWPCode);

            if (excluir != null)
            {
                foreach (string col in excluir) if (!string.IsNullOrEmpty(col)) filtros.Remove(col);
            }
            return filtros;
        }

        private void AdicionarFiltroSeSelecionado(Dictionary<string, string> filtros, string colunaNome, ComboBox combo)
        {
            if (combo.SelectedItem != null && !string.IsNullOrWhiteSpace(combo.SelectedItem.ToString()))
            {
                filtros[colunaNome] = combo.SelectedItem.ToString();
            }
        }

        private List<DataRow> FiltrarLinhas(Dictionary<string, string> filtros)
        {
            if (_dados == null) return new List<DataRow>();
            IEnumerable<DataRow> q = _dados.AsEnumerable();
            foreach (KeyValuePair<string, string> kv in filtros)
            {
                string coluna = kv.Key;
                string valor = kv.Value;
                q = q.Where(r => string.Equals(Convert.ToString(r[coluna]), valor, StringComparison.CurrentCulture));
            }
            return q.ToList();
        }

        private void PopularDistinct(ComboBox combo, string coluna, Dictionary<string, string> filtros)
        {
            if (_dados == null) return;

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
                combo.Items.Clear();
                foreach (string v in valores) combo.Items.Add(v);
            }
            finally { combo.EndUpdate(); }
        }

        // ========================= GRID / STATUS =========================

        private void AtualizarGridAposFiltro()
        {
            if (_dados == null)
            {
                _grid.DataSource = null;
                return;
            }

            Dictionary<string, string> filtros = ObterFiltrosSelecionados();
            List<DataRow> linhas = FiltrarLinhas(filtros);

            DataTable preview = new DataTable();
            preview.Columns.Add(_colNomeFiltroB, typeof(string));
            preview.Columns.Add(_colNomeFiltroH, typeof(string));
            preview.Columns.Add(_colNomeFiltroL_IWPDesc, typeof(string));
            preview.Columns.Add(_colNomeFiltroK_IWPCode, typeof(string));
            preview.Columns.Add(_colNomeCodigoA, typeof(string));
            preview.Columns.Add(_colNomeCodigoG, typeof(string));
            preview.Columns.Add(_colNomeCodigoI_EWP, typeof(string));
            preview.Columns.Add(_colNomeCodigoJ_PWP, typeof(string));

            int count = 0;
            foreach (DataRow r in linhas)
            {
                if (count >= 200) break;

                DataRow n = preview.NewRow();
                n[_colNomeFiltroB] = r[_colNomeFiltroB];
                n[_colNomeFiltroH] = r[_colNomeFiltroH];
                n[_colNomeFiltroL_IWPDesc] = r[_colNomeFiltroL_IWPDesc];
                n[_colNomeFiltroK_IWPCode] = r[_colNomeFiltroK_IWPCode];
                n[_colNomeCodigoA] = r[_colNomeCodigoA];
                n[_colNomeCodigoG] = r[_colNomeCodigoG];
                n[_colNomeCodigoI_EWP] = r[_colNomeCodigoI_EWP];
                n[_colNomeCodigoJ_PWP] = r[_colNomeCodigoJ_PWP];
                preview.Rows.Add(n);
                count++;
            }

            _grid.AutoGenerateColumns = false;
            _grid.Columns.Clear();

            foreach (DataColumn dc in preview.Columns)
            {
                DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
                col.Name = "col_" + dc.ColumnName;
                col.HeaderText = dc.ColumnName;
                col.DataPropertyName = dc.ColumnName;
                col.ReadOnly = true;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                _grid.Columns.Add(col);
            }

            _grid.DataSource = preview;
        }

        private void AtualizarStringsSelecao()
        {
            string currentIwpCode = ObterValorSelecionado(_cboIWPCode);
            Pset_IWP = currentIwpCode;

            Dictionary<string, string> filtros = ObterFiltrosSelecionados();
            List<DataRow> linhas = FiltrarLinhas(filtros);

            if (linhas.Any())
            {
                DataRow linha = linhas.First();
                Pset_CWA = Convert.ToString(linha[_colNomeCodigoA]);
                Pset_CWP = Convert.ToString(linha[_colNomeCodigoG]);
                PsetDescricaoAtividade = Convert.ToString(linha[_colNomeFiltroL_IWPDesc]);
                Pset_EWP = Convert.ToString(linha[_colNomeCodigoI_EWP]);
                Pset_PWP = Convert.ToString(linha[_colNomeCodigoJ_PWP]);
            }
            else
            {
                Pset_CWA = string.Empty;
                Pset_CWP = string.Empty;
                PsetDescricaoAtividade = string.Empty;
                Pset_EWP = string.Empty;
                Pset_PWP = string.Empty;
            }
        }

        private string ObterValorSelecionado(ComboBox combo)
        {
            if (combo == null || combo.SelectedItem == null) return string.Empty;
            return Convert.ToString(combo.SelectedItem);
        }

        private void AtualizarStatus(string textoFixo)
        {
            if (!string.IsNullOrEmpty(textoFixo))
            {
                _lblStatus.Text = textoFixo;
                return;
            }

            if (_dados != null)
            {
                Dictionary<string, string> filtros = ObterFiltrosSelecionados();
                List<DataRow> linhas = FiltrarLinhas(filtros);
                _lblStatus.Text = "Registros filtrados: " + linhas.Count.ToString() + " de " + _dados.Rows.Count.ToString() + ".";
            }
            else
            {
                _lblStatus.Text = "Carregue uma planilha para começar.";
            }
        }

        private void LimparCampos()
        {
            _txtArquivo.Text = string.Empty;

            _binding = true;
            _cboB.Items.Clear();
            _cboH.Items.Clear();
            _cboIWPDesc.Items.Clear();
            _cboIWPCode.Items.Clear();
            _binding = false;

            _grid.DataSource = null;
            _dados = null;

            Pset_IWP = string.Empty; Pset_CWA = string.Empty; Pset_CWP = string.Empty;
            Pset_EWP = string.Empty; Pset_PWP = string.Empty; PsetDescricaoAtividade = string.Empty;

            AtualizarStatus("Carregue uma planilha (.xlsx) para começar.");
        }

        // ========================= APLICAR PSETS =========================

        private void OnAplicarPsets(object sender, EventArgs e)
        {
            try
            {
                if (_dados == null)
                {
                    MessageBox.Show("Nenhuma planilha carregada.", "Atenção", MessageBoxButtons.OK);
                    return;
                }

                AtualizarStringsSelecao();

                if (string.IsNullOrEmpty(Pset_IWP) && string.IsNullOrEmpty(Pset_CWA) && string.IsNullOrEmpty(Pset_CWP)
                    && string.IsNullOrEmpty(Pset_EWP) && string.IsNullOrEmpty(Pset_PWP))
                {
                    MessageBox.Show("Seleção incompleta para aplicar códigos.", "Atenção", MessageBoxButtons.OK);
                    return;
                }

                AplicarNoCivil3D();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Erro ao aplicar: " + ex.Message, "Erro", MessageBoxButtons.OK);
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
            string propSetName = "Códigos AWP";

            using (DocumentLock dl = civilDoc.LockDocument())
            using (Transaction transCad = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DictionaryPropertySetDefinitions dictDefs = new DictionaryPropertySetDefinitions(db);
                    if (!dictDefs.Has(propSetName, transCad))
                    {
                        docEditor.WriteMessage("\nErro: PSD '" + propSetName + "' não encontrado.");
                        transCad.Abort();
                        return;
                    }

                    ObjectId psetDefId = dictDefs.GetAt(propSetName);

                    string layerName = "AWP - " + PsetDescricaoAtividade;
                    ObjectId layerId = ObjectId.Null;

                    if (!string.IsNullOrEmpty(PsetDescricaoAtividade))
                    {
                        LayerTable lt = (LayerTable)transCad.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (lt.Has(layerName))
                        {
                            layerId = lt[layerName];
                        }
                        else
                        {
                            lt.UpgradeOpen();
                            LayerTableRecord ltr = new LayerTableRecord();
                            ltr.Name = layerName;
                            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                            ltr.IsOff = false;
                            ltr.IsLocked = false;
                            layerId = lt.Add(ltr);
                            transCad.AddNewlyCreatedDBObject(ltr, true);
                            lt.DowngradeOpen();
                            docEditor.WriteMessage("\nLayer '" + layerName + "' criado.");
                        }
                    }

                    foreach (ObjectId id in ids)
                    {
                        try
                        {
                            Entity ent = (Entity)transCad.GetObject(id, OpenMode.ForWrite);
                            if (layerId != ObjectId.Null) ent.LayerId = layerId;

                            PropertyDataServices.AddPropertySet(ent, psetDefId);
                            PropertySet pset = (PropertySet)transCad.GetObject(PropertyDataServices.GetPropertySet(ent, psetDefId), OpenMode.ForWrite);

                            SetProperty(pset, "IWP", Pset_IWP, docEditor);
                            SetProperty(pset, "CWA", Pset_CWA, docEditor);
                            SetProperty(pset, "CWP", Pset_CWP, docEditor);
                            SetProperty(pset, "EWP", Pset_EWP, docEditor);
                            SetProperty(pset, "PWP", Pset_PWP, docEditor);
                            SetProperty(pset, "DESCRICAO_ATIVIDADE", PsetDescricaoAtividade, docEditor);
                            SetProperty(pset, "SUBAREA", "AO02", docEditor);

                            totalAplicado++;
                        }
                        catch (System.Exception exObj)
                        {
                            docEditor.WriteMessage("\nFalha em objeto " + id.ToString() + ": " + exObj.Message);
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

            docEditor.WriteMessage("\nAplicação concluída. Objetos processados: " + totalAplicado.ToString() + ".");
            MessageBox.Show("Operação concluída.\nObjetos processados: " + totalAplicado.ToString(), "Concluído", MessageBoxButtons.OK);
        }

        private void SetProperty(PropertySet pset, string propertyName, string value, Editor editor)
        {
            try
            {
                int propId = pset.PropertyNameToId(propertyName);
                pset.SetAt(propId, value);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n[AVISO] Propriedade '" + propertyName + "' não encontrada ou erro ao definir: " + ex.Message);
            }
        }
    }



}

