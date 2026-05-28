using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;
using DataTable = System.Data.DataTable;
using Exception = System.Exception;

namespace AutomacoesCivil3D
{
    public class PsetViewerControl : UserControl
    {
        private DataGridView _dataGridView;
        private Button _btnCarregarObjetos;
        private Label _lblStatus;
        private TableLayoutPanel layout;
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
            _lblStatus = new Label();
            layout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_dataGridView).BeginInit();
            SuspendLayout();
            // 
            // layout
            // 
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            layout.Controls.Add(_btnCarregarObjetos, 0, 0);
            layout.Controls.Add(_dataGridView, 0, 1);
            layout.Controls.Add(_lblStatus, 0, 2);
            layout.Dock = DockStyle.Fill;
            layout.Location = new Point(8, 8);
            layout.Name = "layout";
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle());
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle());
            layout.Size = new Size(1523, 702);
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
            _dataGridView.Size = new Size(1517, 644);
            _dataGridView.TabIndex = 1;
            // 
            // _lblStatus
            // 
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.ForeColor = Color.DimGray;
            _lblStatus.Location = new Point(3, 679);
            _lblStatus.Name = "_lblStatus";
            _lblStatus.Size = new Size(1517, 23);
            _lblStatus.TabIndex = 2;
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;
            // 
            // PsetViewerControl
            // 
            Controls.Add(layout);
            Name = "PsetViewerControl";
            Padding = new Padding(8);
            Size = new Size(1539, 718);
            layout.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_dataGridView).EndInit();
            ResumeLayout(false);
        }

        private void WireEvents()
        {
            _btnCarregarObjetos.Click += OnCarregarObjetosClick;
            _dataGridView.CellDoubleClick += OnDataGridViewCellDoubleClick;
            _dataGridView.CellEndEdit += OnDataGridViewCellEndEdit; // NOVO EVENTO PARA EDIÇÃO
        }

        private void OnCarregarObjetosClick(object sender, EventArgs e)
        {
            LoadPsetObjects();
        }

        private void LoadPsetObjects()
        {
            _lblStatus.Text = "Carregando objetos com Psets...";
            _dataGridView.DataSource = null;
            Application.DoEvents();

            Document doc = Manager.DocCad;
            if (doc == null)
            {
                _lblStatus.Text = "Nenhum desenho aberto.";
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;
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
                                    // Tenta obter o PropertySet. Se o objeto não tiver o PSet anexado, esta linha lançará uma exceção,
                                    // que será capturada pelo catch externo, e o objeto será pulado.
                                    PropertySet pset = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet((Entity)tr.GetObject(objId, OpenMode.ForRead), psetDefId), OpenMode.ForRead);

                                    DataRow row = psetDataTable.NewRow();
                                    row["ID do Objeto"] = objId.ToString(); // Guarda o ObjectId como string para recuperação
                                    row["Layer"] = ((Entity)tr.GetObject(objId, OpenMode.ForRead)).Layer;

                                    // Colunas de Descrição CWA/CWP (ainda não gravadas no Pset diretamente)
                                    row["ATIVIDADE"] = GetPsetValue(pset, "DESCRICAO_ATIVIDADE");

                                    // Preencher valores das propriedades usando a lógica robusta
                                    row["CWA"] = GetPsetValue(pset, "CWA");
                                    row["CWP"] = GetPsetValue(pset, "CWP");
                                    row["EWP"] = GetPsetValue(pset, "EWP");
                                    row["PWP"] = GetPsetValue(pset, "PWP");                                  
                                    row["IWP"] = GetPsetValue(pset, "IWP");
                                    row["SubÁrea"] = GetPsetValue(pset, "SUBAREA");

                                    psetDataTable.Rows.Add(row);
                                    psetsFound++;
                                }
                                catch (Exception ex)
                                {
                                    // Este catch é importante para pular objetos que não têm o PSet ou que dão erro de acesso
                                    // ed.WriteMessage($"\nErro ao processar objeto {objId.ToString()}: {ex.Message}"); // Comentado para não poluir
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
            dt.Columns.Add("Atividade", typeof(string));
            dt.Columns.Add("ID do Objeto", typeof(string));
            dt.Columns.Add("Layer", typeof(string));          
            dt.Columns.Add("CWA", typeof(string));          
            dt.Columns.Add("CWP", typeof(string));
            dt.Columns.Add("EWP", typeof(string));
            dt.Columns.Add("PWP", typeof(string));
            dt.Columns.Add("IWP", typeof(string));
            dt.Columns.Add("SubÁrea", typeof(string));
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

            Document doc = Manager.DocCad;
            if (doc == null) return;

            Editor ed = doc.Editor;
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
                                ZoomToObject(ent);
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

            Document doc = Manager.DocCad;
            if (doc == null)
            {
                _lblStatus.Text = "Erro: Nenhum desenho aberto para salvar alterações.";
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Obtém o ID do Objeto da linha editada
            string objectIdString = _dataGridView.Rows[e.RowIndex].Cells["ID do Objeto"].Value?.ToString();
            if (string.IsNullOrEmpty(objectIdString))
            {
                _lblStatus.Text = "Erro: Não foi possível obter o ID do objeto da linha editada.";
                return;
            }

            // Obtém o nome da coluna (que é o nome da propriedade no PSet)
            string propertyName = _dataGridView.Columns[e.ColumnIndex].Name.Replace("col_", ""); // Remove prefixo se houver
            // Para as colunas do Pset, o nome da coluna do DataGridView é o cabeçalho, que corresponde ao nome da propriedade do Pset
            propertyName = _dataGridView.Columns[e.ColumnIndex].HeaderText;

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


        public void ZoomToObject(Entity ent)
        {
            Document doc = Manager.DocCad;
            Database db = doc.Database;
            Editor ed = doc.Editor;

           

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
               
                if (ent != null)
                {
                    // Pega os limites do objeto
                    Extents3d ext = ent.GeometricExtents;

                    // Expande um pouco a área de zoom para não cortar
                    ext.TransformBy(ed.CurrentUserCoordinateSystem);
                    Point3d min = ext.MinPoint;
                    Point3d max = ext.MaxPoint;

                    // Ajusta a view
                    ZoomToWindow(ed, min, max);
                }

                tr.Commit();
            }
        }

        private void ZoomToWindow(Editor ed, Point3d min, Point3d max)
        {

           
            // Calcula centro e altura
            Matrix3d matWcsToDcs = ed.CurrentUserCoordinateSystem.Inverse();
            Point3d minDcs = min.TransformBy(matWcsToDcs);
            Point3d maxDcs = max.TransformBy(matWcsToDcs);

            // Cria view
            ViewTableRecord view = new ViewTableRecord();
            view.CenterPoint = new Point2d((minDcs.X + maxDcs.X) / 2, (minDcs.Y + maxDcs.Y) / 2);
            view.Height = maxDcs.Y - minDcs.Y + 5;
            view.Width = maxDcs.X - minDcs.X + 5;

            ed.SetCurrentView(view);
        }
    }
}