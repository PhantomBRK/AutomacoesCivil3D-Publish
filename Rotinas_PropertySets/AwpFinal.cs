using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using ClosedXML.Excel; // Adicione esta referência
using System;
using System.Data;
using System.Drawing;
using System.IO; // Para Path e File
using System.Linq;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DataTable = System.Data.DataTable;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using FlowDirection = System.Windows.Forms.FlowDirection; // Para evitar conflito com System.Exception

namespace AutomacoesCivil3D.Rotinas_PropertySets
{
    public class PsetViewerControl : UserControl
    {
        private DataGridView _dataGridView;
        private Button _btnCarregarObjetos;
        private Label _lblStatus;
        private Button _btnExportXlsx; // Novo botão de Exportar
        private Button _btnImportXlsx; // Novo botão de Importar
        private TableLayoutPanel layout;
        private FlowLayoutPanel panelExportImport;
        private const string PROPSET_NAME = "Códigos AWP";

        public PsetViewerControl()
        {
            InitializeComponent();
            WireEvents();
            _lblStatus.Text = "Clique em 'Carregar Objetos' para listar os Psets.";
        }

        private void InitializeComponent()
        {
            layout = new TableLayoutPanel();
            _btnCarregarObjetos = new Button();
            _dataGridView = new DataGridView();
            panelExportImport = new FlowLayoutPanel();
            _btnImportXlsx = new Button();
            _btnExportXlsx = new Button();
            _lblStatus = new Label();
            layout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_dataGridView).BeginInit();
            panelExportImport.SuspendLayout();
            SuspendLayout();
            // 
            // layout
            // 
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            layout.Controls.Add(_btnCarregarObjetos, 0, 0);
            layout.Controls.Add(_dataGridView, 0, 1);
            layout.Controls.Add(panelExportImport, 0, 2);
            layout.Controls.Add(_lblStatus, 0, 3);
            layout.Dock = DockStyle.Fill;
            layout.Location = new Point(8, 8);
            layout.Name = "layout";
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle());
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle());
            layout.RowStyles.Add(new RowStyle());
            layout.Size = new Size(1523, 699);
            layout.TabIndex = 0;
            // 
            // _btnCarregarObjetos
            // 
            _btnCarregarObjetos.Dock = DockStyle.Fill;
            _btnCarregarObjetos.Location = new Point(3, 3);
            _btnCarregarObjetos.Name = "_btnCarregarObjetos";
            _btnCarregarObjetos.Size = new Size(1517, 23);
            _btnCarregarObjetos.TabIndex = 0;
            _btnCarregarObjetos.Text = "Carregar Objetos";
            // 
            // _dataGridView
            // 
            _dataGridView.AllowUserToAddRows = false;
            _dataGridView.AllowUserToDeleteRows = false;
            _dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            _dataGridView.Dock = DockStyle.Fill;
            _dataGridView.Location = new Point(3, 32);
            _dataGridView.Name = "_dataGridView";
            _dataGridView.RowHeadersVisible = false;
            _dataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dataGridView.Size = new Size(1517, 535);
            _dataGridView.TabIndex = 1;
            // 
            // panelExportImport
            // 
            panelExportImport.Controls.Add(_btnImportXlsx);
            panelExportImport.Controls.Add(_btnExportXlsx);
            panelExportImport.Dock = DockStyle.Fill;
            panelExportImport.FlowDirection = FlowDirection.RightToLeft;
            panelExportImport.Location = new Point(3, 573);
            panelExportImport.Name = "panelExportImport";
            panelExportImport.Padding = new Padding(0, 5, 0, 0);
            panelExportImport.Size = new Size(1517, 100);
            panelExportImport.TabIndex = 2;
            // 
            // _btnImportXlsx
            // 
            _btnImportXlsx.AutoSize = true;
            _btnImportXlsx.Location = new Point(1393, 8);
            _btnImportXlsx.Name = "_btnImportXlsx";
            _btnImportXlsx.Size = new Size(121, 25);
            _btnImportXlsx.TabIndex = 0;
            _btnImportXlsx.Text = "Importar Alterações";
            // 
            // _btnExportXlsx
            // 
            _btnExportXlsx.AutoSize = true;
            _btnExportXlsx.Location = new Point(1299, 8);
            _btnExportXlsx.Name = "_btnExportXlsx";
            _btnExportXlsx.Size = new Size(88, 25);
            _btnExportXlsx.TabIndex = 1;
            _btnExportXlsx.Text = "Exportar Lista";
            // 
            // _lblStatus
            // 
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.ForeColor = Color.DimGray;
            _lblStatus.Location = new Point(3, 676);
            _lblStatus.Name = "_lblStatus";
            _lblStatus.Size = new Size(1517, 23);
            _lblStatus.TabIndex = 3;
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;
            // 
            // PsetViewerControl
            // 
            Controls.Add(layout);
            Name = "PsetViewerControl";
            Padding = new Padding(8);
            Size = new Size(1539, 715);
            layout.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_dataGridView).EndInit();
            panelExportImport.ResumeLayout(false);
            panelExportImport.PerformLayout();
            ResumeLayout(false);
        }

        private void WireEvents()
        {
            _btnCarregarObjetos.Click += OnCarregarObjetosClick;
            _dataGridView.CellDoubleClick += OnDataGridViewCellDoubleClick;
            _dataGridView.CellEndEdit += OnDataGridViewCellEndEdit; // Evento para edição
            _btnExportXlsx.Click += OnExportXlsxClick; // Evento para Exportar
            _btnImportXlsx.Click += OnImportXlsxClick; // Evento para Importar
        }

        private void OnCarregarObjetosClick(object sender, EventArgs e)
        {
            LoadPsetObjects();
        }

        private void LoadPsetObjects()
        {
            _lblStatus.Text = "Carregando objetos com Psets...";
            _dataGridView.DataSource = null;
            System.Windows.Forms.Application.DoEvents();

            Document doc = Manager.DocCad; // Usando Manager.DocCad
            if (doc == null)
            {
                _lblStatus.Text = "Nenhum desenho aberto.";
                return;
            }

            Database db = doc.Database;
            Editor ed = Manager.DocEditor; // Usando Manager.DocEditor
            DataTable psetDataTable = CreatePsetDataTableSchema();
            int psetsFound = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        DictionaryPropertySetDefinitions dictDefs = new DictionaryPropertySetDefinitions(db);
                        if (!dictDefs.Has(PROPSET_NAME, tr))
                        {
                            _lblStatus.Text = $"Property Set Definition '{PROPSET_NAME}' não encontrado no desenho.";
                            tr.Abort();
                            return;
                        }
                        ObjectId psetDefId = dictDefs.GetAt(PROPSET_NAME);

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                        foreach (ObjectId objId in btr)
                        {
                            if (objId.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Entity))))
                            {
                                try
                                {
                                    Entity ent = (Entity)tr.GetObject(objId, OpenMode.ForRead);
                                    // Tenta obter o PropertySet. Se o objeto não tiver o PSet anexado,
                                    // GetPropertySet lançará uma exceção, que será capturada e o objeto pulado.
                                    PropertySet pset = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(ent, psetDefId), OpenMode.ForRead);

                                    DataRow row = psetDataTable.NewRow();
                                    row["ID do Objeto"] = objId.ToString(); // Guarda o ObjectId como string para recuperação (Handle)
                                    row["Layer"] = ent.Layer;
                                    row["DESCRICAO_ATIVIDADE"] = GetPsetValue(pset, "DESCRICAO_ATIVIDADE");

                                    // Colunas de Descrição CWA/CWP (não gravadas no Pset, exibir placeholder)
                                    //row["Descrição CWA"] = "(Não gravado no Pset)";
                                    //row["Descrição CWP"] = "(Não gravado no Pset)";

                                    // Preencher valores das propriedades usando a lógica robusta
                                    row["CWA"] = GetPsetValue(pset, "CWA");
                                    row["CWP"] = GetPsetValue(pset, "CWP");
                                    row["EWP"] = GetPsetValue(pset, "EWP");
                                    row["PWP"] = GetPsetValue(pset, "PWP");                                  
                                    row["IWP"] = GetPsetValue(pset, "IWP");
                                    row["SUBAREA"] = GetPsetValue(pset, "SUBAREA");

                                    psetDataTable.Rows.Add(row);
                                    psetsFound++;
                                }
                                catch (Exception)
                                {
                                    // Pula objetos que não têm o PSet ou que dão erro de acesso
                                }
                            }
                        }
                        tr.Commit();
                    }
                    catch (Exception ex)
                    {
                        _lblStatus.Text = $"Erro ao carregar Psets: {ex.Message}";
                        ed.WriteMessage($"\nErro ao carregar Psets: {ex.Message}");
                        tr.Abort();
                        return;
                    }
                }
            }

            _dataGridView.DataSource = psetDataTable;
            _lblStatus.Text = $"Psets carregados: {psetsFound} objetos encontrados.";

            SetReadOnlyColumns(); // Define as colunas somente leitura após carregar os dados
        }

        private DataTable CreatePsetDataTableSchema()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("ID do Objeto", typeof(string));
            dt.Columns.Add("Layer", typeof(string));
            dt.Columns.Add("DESCRICAO_ATIVIDADE", typeof(string));
            dt.Columns.Add("CWA", typeof(string));
            dt.Columns.Add("CWP", typeof(string));
            dt.Columns.Add("EWP", typeof(string));
            dt.Columns.Add("PWP", typeof(string));
            dt.Columns.Add("IWP", typeof(string));
            dt.Columns.Add("SUBAREA", typeof(string));
            return dt;
        }

        /// <summary>
        /// Obtém o valor de uma propriedade de um PropertySet.
        /// Retorna string.Empty se a propriedade não existir ou for nula.
        /// </summary>
        private string GetPsetValue(PropertySet pset, string propertyName)
        {
            try
            {
                int propIndex = pset.PropertyNameToId(propertyName);
                object value = pset.GetAt(propIndex);
                return value?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void OnDataGridViewCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _dataGridView.Rows[e.RowIndex].IsNewRow) return;

            Document doc = Manager.DocCad; // Usando Manager.DocCad
            if (doc == null) return;

            Editor ed = Manager.DocEditor; // Usando Manager.DocEditor
            Database db = doc.Database;

            string objectIdString = _dataGridView.Rows[e.RowIndex].Cells["ID do Objeto"].Value?.ToString();

            if (string.IsNullOrEmpty(objectIdString))
            {
                ed.WriteMessage("\nErro: ID do objeto não encontrado na linha selecionada.");
                return;
            }

            try
            {
                string cleanedObjectIdString = objectIdString.Replace("(", "").Replace(")", "");
                long objectHandle = long.Parse(cleanedObjectIdString);
                IntPtr intPtr = new IntPtr(objectHandle);
                ObjectId objId = new ObjectId(intPtr);

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        if (objId.IsErased || objId.IsNull || !objId.IsValid)
                        {
                            ed.WriteMessage("\nObjeto selecionado não existe mais no desenho ou é inválido.");
                            return;
                        }

                        

                        using (SelectionSet ss = SelectionSet.FromObjectIds(new ObjectId[] { objId }))
                        {
                            ed.SetImpliedSelection(ss);

                            Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                            if (ent != null && ent.GeometricExtents.MinPoint.X != Double.NaN)
                            {
                                ZoomToObject(ent); // Usando o novo método auxiliar ZoomToObject
                            }
                            else
                            {
                                ed.WriteMessage("\nNão foi possível aplicar o zoom. Objeto sem extensões geométricas válidas.");
                            }
                        }
                        tr.Commit();
                    }
                }
            }
            catch (FormatException)
            {
                ed.WriteMessage($"\nErro de formato ao converter ID do objeto: '{objectIdString}'. Verifique a integridade dos dados.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro ao selecionar objeto: {ex.Message}");
            }
        }

        // NOVO MÉTODO: Define quais colunas são somente leitura
        private void SetReadOnlyColumns()
        {
            if (_dataGridView.Columns.Contains("ID do Objeto"))
                _dataGridView.Columns["ID do Objeto"].ReadOnly = true;
            if (_dataGridView.Columns.Contains("Layer"))
                _dataGridView.Columns["Layer"].ReadOnly = true;
            if (_dataGridView.Columns.Contains("Descrição CWA"))
                _dataGridView.Columns["Descrição CWA"].ReadOnly = true;
            if (_dataGridView.Columns.Contains("Descrição CWP"))
                _dataGridView.Columns["Descrição CWP"].ReadOnly = true;
        }

        // NOVO MÉTODO: Lidar com o fim da edição de uma célula
        private void OnDataGridViewCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // Garante que uma linha e coluna válidas foram editadas
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            Document doc = Manager.DocCad; // Usando Manager.DocCad
            if (doc == null)
            {
                _lblStatus.Text = "Erro: Nenhum desenho aberto para salvar alterações.";
                return;
            }

            Database db = doc.Database;
            Editor ed = Manager.DocEditor; // Usando Manager.DocEditor

            // Obtém o ID do Objeto da linha editada
            string objectIdString = _dataGridView.Rows[e.RowIndex].Cells["ID do Objeto"].Value?.ToString();
            if (string.IsNullOrEmpty(objectIdString))
            {
                _lblStatus.Text = "Erro: Não foi possível obter o ID do objeto da linha editada.";
                return;
            }

            // Obtém o nome da coluna (que é o nome da propriedade no PSet)
            string propertyName = _dataGridView.Columns[e.ColumnIndex].HeaderText; // Usamos HeaderText pois é o nome da propriedade no Pset

            // Obtém o novo valor digitado
            string newValue = _dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;

            try
            {
                string cleanedObjectIdString = objectIdString.Replace("(", "").Replace(")", "");
                long objectHandle = long.Parse(cleanedObjectIdString);
                ObjectId objId = new ObjectId(new IntPtr(objectHandle));

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // Valida se o objeto ainda é válido
                        if (objId.IsErased || objId.IsNull || !objId.IsValid)
                        {
                            _lblStatus.Text = "Erro: Objeto não encontrado no desenho para salvar alteração.";
                            tr.Abort();
                            return;
                        }

                        // Abre a entidade para escrita
                        Entity ent = (Entity)tr.GetObject(objId, OpenMode.ForWrite);

                        // Obtém a definição do Property Set (necessária para obter o ID do PSet)
                        DictionaryPropertySetDefinitions dictDefs = new DictionaryPropertySetDefinitions(db);
                        if (!dictDefs.Has(PROPSET_NAME, tr))
                        {
                            _lblStatus.Text = $"Erro: Property Set Definition '{PROPSET_NAME}' não encontrado.";
                            tr.Abort();
                            return;
                        }
                        ObjectId psetDefId = dictDefs.GetAt(PROPSET_NAME);

                        // Tenta obter o PropertySet anexado ao objeto para escrita
                        PropertySet pset = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(ent, psetDefId), OpenMode.ForWrite);

                        // Aplica o novo valor ao Property Set
                        SetProperty(pset, propertyName, newValue, ed);

                        tr.Commit();
                        _lblStatus.Text = $"'{propertyName}' do objeto {objId.ToString()} atualizado para '{newValue}'.";
                    }
                }
            }
            catch (FormatException)
            {
                _lblStatus.Text = $"Erro de formato ao converter ID do objeto: '{objectIdString}'.";
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = $"Erro ao salvar '{propertyName}': {ex.Message}";
                ed.WriteMessage($"\nErro ao salvar '{propertyName}' do objeto {objectIdString}: {ex.Message}");
            }
        }

        /// <summary>
        /// Método auxiliar para aplicar uma propriedade a um PropertySet de forma segura.
        /// Reutilizado da rotina de aplicação.
        /// </summary>
        private void SetProperty(PropertySet pset, string propertyName, string value, Editor editor)
        {
            try
            {
                int propId = pset.PropertyNameToId(propertyName);
                pset.SetAt(propId, value);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\n[AVISO] Propriedade '{propertyName}' não encontrada ou erro ao definir: {ex.Message}");
                throw; // Re-lança para ser capturada pelo handler principal
            }
        }

        // --- Métodos Auxiliares para Navegação e Zoom (reutilizados da sua base de código) ---
        public void ZoomToObject(Entity ent)
        {
            Document doc = Manager.DocCad;
            Database db = doc.Database;
            Editor ed = Manager.DocEditor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (ent != null)
                {
                    Extents3d ext = ent.GeometricExtents;
                    ext.TransformBy(ed.CurrentUserCoordinateSystem);
                    Point3d min = ext.MinPoint;
                    Point3d max = ext.MaxPoint;

                    ZoomToWindow(ed, min, max);
                }
                tr.Commit();
            }
        }

        private void ZoomToWindow(Editor ed, Point3d min, Point3d max)
        {
            Matrix3d matWcsToDcs = ed.CurrentUserCoordinateSystem.Inverse();
            Point3d minDcs = min.TransformBy(matWcsToDcs);
            Point3d maxDcs = max.TransformBy(matWcsToDcs);

            ViewTableRecord view = new ViewTableRecord();
            view.CenterPoint = new Point2d((minDcs.X + maxDcs.X) / 2, (minDcs.Y + maxDcs.Y) / 2);
            view.Height = maxDcs.Y - minDcs.Y + 5;
            view.Width = maxDcs.X - minDcs.X + 5;

            ed.SetCurrentView(view);
        }

        // --- Implementação do Objetivo Secundário 2.3: Exportar/Importar XLSX ---

        private void OnExportXlsxClick(object sender, EventArgs e)
        {
            if (_dataGridView.DataSource == null || _dataGridView.Rows.Count == 0)
            {
                MessageBox.Show("Não há dados para exportar. Carregue os objetos primeiro.", "Exportar XLSX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                
                sfd.Filter = "Planilha Excel (*.xlsx)|*.xlsx";
                sfd.FileName = $"Psets_Exportados_{Path.GetFileNameWithoutExtension(Manager.DocCad.Name)}.xlsx";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    ExportPsetListToXlsx(sfd.FileName);
                }
            }
        }

        private void ExportPsetListToXlsx(string filePath)
        {
            try
            {
                System.Data.DataTable dataToExport = _dataGridView.DataSource as DataTable;
                if (dataToExport == null)
                {
                    _lblStatus.Text = "Erro: Fonte de dados do grid não é um DataTable.";
                    return;
                }

                using (XLWorkbook wb = new XLWorkbook())
                {
                    IXLWorksheet ws = wb.Worksheets.Add("Dados Psets");

                    // Copia os dados do DataTable para a planilha
                    ws.Cell(1, 1).InsertTable(dataToExport, false); // false para não usar primeira linha como cabeçalho de tabela

                    // Formatação: ajusta largura das colunas
                    ws.Columns().AdjustToContents();
                    
                    wb.SaveAs(filePath);
                }

                _lblStatus.Text = $"Dados exportados com sucesso para: {filePath}";
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = $"Erro ao exportar dados para XLSX: {ex.Message}";
                Manager.DocEditor.WriteMessage($"\nErro ao exportar XLSX: {ex.Message}");
            }
        }

        private void OnImportXlsxClick(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Planilha Excel (*.xlsx)|*.xlsx";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    ImportPsetListFromXlsx(ofd.FileName);
                }
            }
        }

        private void ImportPsetListFromXlsx(string filePath)
        {
            Document doc = Manager.DocCad; // Usando Manager.DocCad
            if (doc == null)
            {
                _lblStatus.Text = "Nenhum desenho aberto para importar alterações.";
                return;
            }

            Database db = doc.Database;
            Editor ed = Manager.DocEditor; // Usando Manager.DocEditor

            int totalProcessed = 0;
            int totalUpdated = 0;
            int totalErrors = 0;
            System.Collections.Generic.List<string> errorLog = new System.Collections.Generic.List<string>();

            try
            {
                using (XLWorkbook wb = new XLWorkbook(filePath))
                {
                    IXLWorksheet ws = wb.Worksheets.Worksheet("Dados Psets"); // Nome da aba padrão na exportação

                    // Assume que a primeira linha contém os cabeçalhos
                    // Encontra a coluna "ID do Objeto" e as colunas editáveis
                    int idColIndex = -1;
                    System.Collections.Generic.Dictionary<string, int> editableCols = new System.Collections.Generic.Dictionary<string, int>();

                    var headerRow = ws.Row(1);
                    for (int c = 1; c <= headerRow.LastCellUsed().WorksheetColumn().ColumnNumber(); c++)
                    {
                        string headerText = headerRow.Cell(c).GetString();
                        if (headerText == "ID do Objeto")
                        {
                            idColIndex = c;
                        }
                        // Verifica se a coluna é uma das editáveis
                        if (headerText == "CWA" ||
                            headerText == "CWP" ||
                            headerText == "EWP" ||
                            headerText == "PWP" ||
                            headerText == "DESCRICAO_ATIVIDADE" ||
                            headerText == "IWP" ||
                            headerText == "SUBAREA")
                        {
                            editableCols[headerText] = c;
                        }
                    }

                    if (idColIndex == -1)
                    {
                        _lblStatus.Text = "Erro: A planilha não contém a coluna 'ID do Objeto'.";
                        return;
                    }

                    using (DocumentLock docLock = doc.LockDocument())
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            DictionaryPropertySetDefinitions dictDefs = new DictionaryPropertySetDefinitions(db);
                            if (!dictDefs.Has(PROPSET_NAME, tr))
                            {
                                _lblStatus.Text = $"Erro: Property Set Definition '{PROPSET_NAME}' não encontrado no desenho.";
                                tr.Abort();
                                return;
                            }
                            ObjectId psetDefId = dictDefs.GetAt(PROPSET_NAME);

                            // Itera sobre as linhas de dados (a partir da segunda linha)
                            for (int row = 2; row <= ws.LastRowUsed().RowNumber(); row++)
                            {
                                totalProcessed++;
                                string objectIdString = ws.Cell(row, idColIndex).GetString();

                                if (string.IsNullOrEmpty(objectIdString))
                                {
                                    errorLog.Add($"Linha {row}: ID do Objeto vazio. Pulando.");
                                    totalErrors++;
                                    continue;
                                }

                                try
                                {
                                    string cleanedObjectIdString = objectIdString.Replace("(", "").Replace(")", "");
                                    long objectHandle = long.Parse(cleanedObjectIdString);
                                    ObjectId objId = new ObjectId(new IntPtr(objectHandle));

                                    if (objId.IsErased || objId.IsNull || !objId.IsValid)
                                    {
                                        errorLog.Add($"Linha {row}: Objeto {objectIdString} não encontrado ou inválido no desenho. Pulando.");
                                        totalErrors++;
                                        continue;
                                    }

                                    // Abre a entidade e o Pset para escrita
                                    Entity ent = (Entity)tr.GetObject(objId, OpenMode.ForWrite);
                                    PropertySet pset = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(ent, psetDefId), OpenMode.ForWrite);

                                    bool rowUpdated = false;
                                    foreach (var colEntry in editableCols)
                                    {
                                        string propertyName = colEntry.Key;
                                        int colIndex = colEntry.Value;
                                        string newValue = ws.Cell(row, colIndex).GetString();

                                        try
                                        {
                                            SetProperty(pset, propertyName, newValue, ed); // Reutiliza o método de set
                                            rowUpdated = true;
                                        }
                                        catch (System.Exception exProp)
                                        {
                                            errorLog.Add($"Linha {row}, Propriedade '{propertyName}': Erro ao atualizar Pset para objeto {objectIdString}: {exProp.Message}");
                                            totalErrors++;
                                        }
                                    }

                                    if (rowUpdated)
                                    {
                                        totalUpdated++;
                                    }
                                }
                                catch (FormatException)
                                {
                                    errorLog.Add($"Linha {row}: Erro de formato no ID do Objeto '{objectIdString}'. Pulando.");
                                    totalErrors++;
                                }
                                catch (System.Exception ex)
                                {
                                    errorLog.Add($"Linha {row}: Erro ao processar objeto {objectIdString}: {ex.Message}");
                                    totalErrors++;
                                }
                            }
                            tr.Commit(); // Commita as alterações de todos os objetos
                        }
                    }
                }

                _lblStatus.Text = $"Importação concluída. Atualizados: {totalUpdated}, Erros: {totalErrors}.";
                ed.WriteMessage($"\nImportação XLSX concluída: Atualizados {totalUpdated}, Erros {totalErrors}.");

                if (totalErrors > 0)
                {
                    string logFilePath = Path.Combine(Path.GetDirectoryName(filePath) ?? "", "ImportPsetLog.txt");
                    File.WriteAllLines(logFilePath, errorLog);
                    MessageBox.Show($"Importação concluída com {totalErrors} erros. Verifique o log em:\n{logFilePath}", "Importar XLSX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                LoadPsetObjects(); // Recarrega o grid para refletir as alterações
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = $"Erro geral na importação do XLSX: {ex.Message}";
                ed.WriteMessage($"\nErro geral na importação XLSX: {ex.Message}");
            }
        }
    }

}
