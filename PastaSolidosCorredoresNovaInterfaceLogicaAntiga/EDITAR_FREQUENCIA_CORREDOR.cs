using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

using AutomacoesCivil3D;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    // Wrapper público para aparecer no CheckedListBox e carregar o ObjectId junto
    public class CorridorListItem
    {
        public ObjectId CorridorId { get; private set; }
        public string CorridorName { get; private set; }

        public CorridorListItem(ObjectId corridorId, string corridorName)
        {
            CorridorId = corridorId;
            CorridorName = corridorName;
        }

        public override string ToString()
        {
            return CorridorName;
        }
    }

    public class CorridorFrequencies
    {
        [CommandMethod("APPLY_REGION_FREQS_ALL")]
        public void ApplyFrequenciesToAllCorridorRegions()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            try
            {
                List<CorridorListItem> corridors = GetCorridorsList(civilDoc, civilDb);
                if (corridors.Count == 0)
                {
                    docEditor.WriteMessage("\nNenhum corredor encontrado no desenho.");
                    return;
                }

                int freqTangentes;
                int freqCurvas;
                int freqEspirais;
                List<ObjectId> selectedCorridors;

                if (!PedirFrequencias(docEditor, corridors, out freqTangentes, out freqCurvas, out freqEspirais, out selectedCorridors))
                {
                    return;
                }

                if (selectedCorridors.Count == 0)
                {
                    docEditor.WriteMessage("\nNenhum corredor selecionado.");
                    return;
                }

                using (DocumentLock docLock = civilDoc.LockDocument())
                {
                    Database db = civilDoc.Database;
                    using (Transaction trans = db.TransactionManager.StartTransaction())
                    {
                        int corridorsProcessed = 0;
                        int baselinesProcessed = 0;
                        int regionsUpdated = 0;

                        // Mesma lógica de antes: só filtra a lista de corredores
                        foreach (ObjectId corridorId in selectedCorridors)
                        {
                            if (corridorId.IsNull || !corridorId.IsValid)
                            {
                                continue;
                            }

                            Corridor corridor = (Corridor)trans.GetObject(corridorId, OpenMode.ForWrite);

                            BaselineCollection baselines = corridor.Baselines;
                            foreach (Baseline baseline in baselines)
                            {
                                baselinesProcessed++;

                                BaselineRegionCollection regionCollection = baseline.BaselineRegions;
                                foreach (BaselineRegion region in regionCollection)
                                {
                                    AppliedAssemblySetting settings = region.AppliedAssemblySetting;

                                    settings.FrequencyAlongTangents = freqTangentes;
                                    settings.FrequencyAlongCurves = freqCurvas;
                                    settings.FrequencyAlongSpirals = freqEspirais;
                                    settings.FrequencyAlongProfileCurves = freqCurvas;
                                    settings.FrequencyAlongTargetCurves = freqTangentes;

                                    regionsUpdated++;
                                }
                            }

                            // Se quiser forçar rebuild, descomente:
                            // corridor.Rebuild();

                            corridorsProcessed++;
                        }

                        trans.Commit();

                        docEditor.WriteMessage(
                            $"\nFrequências aplicadas em {regionsUpdated} regiões, {baselinesProcessed} baselines, {corridorsProcessed} corredores.");
                    }
                }
            }
            catch (Exception aex)
            {
                docEditor.WriteMessage($"\nErro AutoCAD: {aex.Message}");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\nErro: {ex.Message}");
            }
        }

        public static List<CorridorListItem> GetCorridorsList(Document civilDoc, CivilDocument civilDb)
        {
            List<CorridorListItem> items = new List<CorridorListItem>();

            Database db = civilDoc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId corridorId in civilDb.CorridorCollection)
                {
                    if (corridorId.IsNull || !corridorId.IsValid)
                    {
                        continue;
                    }

                    Corridor corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForRead);
                    string name = corridor.Name;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "<SemNome>";
                    }

                    items.Add(new CorridorListItem(corridorId, name));
                }

                tr.Commit();
            }

            items.Sort((a, b) => string.Compare(a.CorridorName, b.CorridorName, StringComparison.OrdinalIgnoreCase));
            return items;
        }

        public bool PedirFrequencias(
            Editor docEditor,
            List<CorridorListItem> corridors,
            out int freqTangentes,
            out int freqCurvas,
            out int freqEspirais,
            out List<ObjectId> selectedCorridors)
        {
            freqTangentes = 5;
            freqCurvas = 5;
            freqEspirais = 5;
            selectedCorridors = new List<ObjectId>();

            using (FrequencySettingsForm form = new FrequencySettingsForm(corridors))
            {
                DialogResult result = Application.ShowModalDialog(form);
                if (result != DialogResult.OK)
                {
                    docEditor.WriteMessage("\nComando cancelado pelo usuário.");
                    return false;
                }

                freqTangentes = form.FrequencyTangents;
                freqCurvas = form.FrequencyCurves;
                freqEspirais = form.FrequencySpirals;
                selectedCorridors = form.GetSelectedCorridorIds();

                return true;
            }
        }
    }

    public class FrequencySettingsForm : Form
    {
        private TextBox txtTangentes;
        private TextBox txtCurvas;
        private TextBox txtEspirais;

        private CheckedListBox clbCorridors;

        private Button btnAll;
        private Button btnNone;
        private Button btnOk;
        private Button btnCancelar;

        private List<CorridorListItem> corridorItems;

        public int FrequencyTangents { get; private set; } = 5;
        public int FrequencyCurves { get; private set; } = 5;
        public int FrequencySpirals { get; private set; } = 5;

        public FrequencySettingsForm(List<CorridorListItem> corridors)
        {
            corridorItems = corridors;

            this.Text = "Frequências dos Corredores";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.Width = 520;
            this.Height = 480;

            InicializarComponentes();
            CarregarCorredoresMarcados();
        }

        public List<ObjectId> GetSelectedCorridorIds()
        {
            List<ObjectId> ids = new List<ObjectId>();

            for (int i = 0; i < clbCorridors.CheckedItems.Count; i++)
            {
                CorridorListItem item = (CorridorListItem)clbCorridors.CheckedItems[i];
                ids.Add(item.CorridorId);
            }

            return ids;
        }

        public void InicializarComponentes()
        {
            System.Windows.Forms.Label lblTangentes = new System.Windows.Forms.Label();
            lblTangentes.Text = "Tangentes (m):";
            lblTangentes.Left = 10;
            lblTangentes.Top = 15;
            lblTangentes.Width = 120;

            txtTangentes = new TextBox();
            txtTangentes.Left = 150;
            txtTangentes.Top = 12;
            txtTangentes.Width = 80;
            txtTangentes.Text = "5";

            System.Windows.Forms.Label lblCurvas = new System.Windows.Forms.Label();
            lblCurvas.Text = "Curvas (m):";
            lblCurvas.Left = 250;
            lblCurvas.Top = 15;
            lblCurvas.Width = 90;

            txtCurvas = new TextBox();
            txtCurvas.Left = 340;
            txtCurvas.Top = 12;
            txtCurvas.Width = 80;
            txtCurvas.Text = "5";

            System.Windows.Forms.Label lblEspirais = new System.Windows.Forms.Label();
            lblEspirais.Text = "Espirais (m):";
            lblEspirais.Left = 10;
            lblEspirais.Top = 45;
            lblEspirais.Width = 120;

            txtEspirais = new TextBox();
            txtEspirais.Left = 150;
            txtEspirais.Top = 42;
            txtEspirais.Width = 80;
            txtEspirais.Text = "5";

            System.Windows.Forms.Label lblCorridors = new System.Windows.Forms.Label();
            lblCorridors.Text = "Corredores (marque quais serão ajustados):";
            lblCorridors.Left = 10;
            lblCorridors.Top = 80;
            lblCorridors.Width = 480;

            clbCorridors = new CheckedListBox();
            clbCorridors.Left = 10;
            clbCorridors.Top = 105;
            clbCorridors.Width = 490;
            clbCorridors.Height = 280;
            clbCorridors.CheckOnClick = true;

            btnAll = new Button();
            btnAll.Text = "Marcar todos";
            btnAll.Left = 10;
            btnAll.Top = 395;
            btnAll.Width = 110;
            btnAll.Click += BtnAll_Click;

            btnNone = new Button();
            btnNone.Text = "Desmarcar";
            btnNone.Left = 130;
            btnNone.Top = 395;
            btnNone.Width = 110;
            btnNone.Click += BtnNone_Click;

            btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.Left = 300;
            btnOk.Top = 395;
            btnOk.Width = 90;
            btnOk.DialogResult = DialogResult.None;
            btnOk.Click += BtnOk_Click;

            btnCancelar = new Button();
            btnCancelar.Text = "Cancelar";
            btnCancelar.Left = 410;
            btnCancelar.Top = 395;
            btnCancelar.Width = 90;
            btnCancelar.DialogResult = DialogResult.Cancel;
            btnCancelar.Click += BtnCancelar_Click;

            this.Controls.Add(lblTangentes);
            this.Controls.Add(txtTangentes);
            this.Controls.Add(lblCurvas);
            this.Controls.Add(txtCurvas);
            this.Controls.Add(lblEspirais);
            this.Controls.Add(txtEspirais);

            this.Controls.Add(lblCorridors);
            this.Controls.Add(clbCorridors);

            this.Controls.Add(btnAll);
            this.Controls.Add(btnNone);
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancelar);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancelar;
        }

        public void CarregarCorredoresMarcados()
        {
            clbCorridors.Items.Clear();

            for (int i = 0; i < corridorItems.Count; i++)
            {
                CorridorListItem item = corridorItems[i];
                clbCorridors.Items.Add(item, true); // todos marcados por padrão
            }
        }

        public void BtnAll_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < clbCorridors.Items.Count; i++)
            {
                clbCorridors.SetItemChecked(i, true);
            }
        }

        public void BtnNone_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < clbCorridors.Items.Count; i++)
            {
                clbCorridors.SetItemChecked(i, false);
            }
        }

        public void BtnOk_Click(object sender, EventArgs e)
        {
            int fTan;
            int fCur;
            int fEsp;

            if (!int.TryParse(txtTangentes.Text, out fTan) || fTan <= 0)
            {
                MessageBox.Show("Informe um valor numérico positivo para Tangentes.", "Aviso",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTangentes.Focus();
                return;
            }

            if (!int.TryParse(txtCurvas.Text, out fCur) || fCur <= 0)
            {
                MessageBox.Show("Informe um valor numérico positivo para Curvas.", "Aviso",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCurvas.Focus();
                return;
            }

            if (!int.TryParse(txtEspirais.Text, out fEsp) || fEsp <= 0)
            {
                MessageBox.Show("Informe um valor numérico positivo para Espirais.", "Aviso",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtEspirais.Focus();
                return;
            }

            if (clbCorridors.CheckedItems.Count == 0)
            {
                MessageBox.Show("Marque pelo menos 1 corredor.", "Aviso",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            FrequencyTangents = fTan;
            FrequencyCurves = fCur;
            FrequencySpirals = fEsp;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        public void BtnCancelar_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}


