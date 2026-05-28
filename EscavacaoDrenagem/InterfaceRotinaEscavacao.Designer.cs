using System.Windows.Forms;

namespace EscavacaoDRE
{
    partial class InterfaceRotinaEscavacao
    {
        private System.ComponentModel.IContainer components = null;

        // Controles
        private System.Windows.Forms.TextBox EspessuraConcretoMagro;
        private System.Windows.Forms.TextBox EspessuraCompactacaoManual;
        private System.Windows.Forms.Button btnAtivar;
        private System.Windows.Forms.Button btnCancelar;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            EspessuraConcretoMagro = new TextBox();
            EspessuraCompactacaoManual = new TextBox();
            btnAtivar = new Button();
            btnCancelar = new Button();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            textBox1 = new TextBox();
            textBox2 = new TextBox();
            label5 = new Label();
            label7 = new Label();
            CBembasamento = new ComboBox();
            comboBox2 = new ComboBox();
            label6 = new Label();
            SuspendLayout();
            // 
            // EspessuraConcretoMagro
            // 
            EspessuraConcretoMagro.Location = new Point(513, 109);
            EspessuraConcretoMagro.Name = "EspessuraConcretoMagro";
            EspessuraConcretoMagro.PlaceholderText = "5";
            EspessuraConcretoMagro.Size = new Size(61, 23);
            EspessuraConcretoMagro.TabIndex = 0;
            EspessuraConcretoMagro.Text = "5";
            // 
            // EspessuraCompactacaoManual
            // 
            EspessuraCompactacaoManual.Location = new Point(513, 215);
            EspessuraCompactacaoManual.Name = "EspessuraCompactacaoManual";
            EspessuraCompactacaoManual.PlaceholderText = "20";
            EspessuraCompactacaoManual.Size = new Size(61, 23);
            EspessuraCompactacaoManual.TabIndex = 1;
            EspessuraCompactacaoManual.Text = "20";
            // 
            // btnAtivar
            // 
            btnAtivar.Location = new Point(427, 297);
            btnAtivar.Name = "btnAtivar";
            btnAtivar.Size = new Size(80, 30);
            btnAtivar.TabIndex = 2;
            btnAtivar.Text = "Iniciar";
            btnAtivar.UseVisualStyleBackColor = true;
            // 
            // btnCancelar
            // 
            btnCancelar.Location = new Point(522, 297);
            btnCancelar.Name = "btnCancelar";
            btnCancelar.Size = new Size(80, 30);
            btnCancelar.TabIndex = 3;
            btnCancelar.Text = "Cancelar";
            btnCancelar.UseVisualStyleBackColor = true;
            btnCancelar.Click += BtnCancelar_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label1.Location = new Point(16, 117);
            label1.Name = "label1";
            label1.Size = new Size(186, 15);
            label1.TabIndex = 4;
            label1.Text = "ESPESSURA DO EMBASAMENTO";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label2.Location = new Point(16, 223);
            label2.Name = "label2";
            label2.Size = new Size(215, 15);
            label2.TabIndex = 5;
            label2.Text = "ESPESSURA COMPACTAÇÃO MANUAL";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(578, 218);
            label3.Name = "label3";
            label3.Size = new Size(24, 15);
            label3.TabIndex = 6;
            label3.Text = "cm";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(578, 112);
            label4.Name = "label4";
            label4.Size = new Size(24, 15);
            label4.TabIndex = 7;
            label4.Text = "cm";
            // 
            // textBox1
            // 
            textBox1.BackColor = SystemColors.Menu;
            textBox1.BorderStyle = BorderStyle.None;
            textBox1.Location = new Point(23, 135);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(481, 16);
            textBox1.TabIndex = 8;
            textBox1.Text = "Por padrão a espessura da camada de embasamento é de 5 cm";
            // 
            // textBox2
            // 
            textBox2.BackColor = SystemColors.Menu;
            textBox2.BorderStyle = BorderStyle.None;
            textBox2.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            textBox2.Location = new Point(23, 241);
            textBox2.Name = "textBox2";
            textBox2.Size = new Size(600, 15);
            textBox2.TabIndex = 9;
            textBox2.Text = "Por padrão a espessura da compactação manual acima da linha superior do tubo é de 20 cm";
            textBox2.TextChanged += textBox2_TextChanged;
            // 
            // label5
            // 
            label5.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label5.Location = new Point(16, 9);
            label5.Name = "label5";
            label5.Size = new Size(488, 44);
            label5.TabIndex = 10;
            label5.Text = "PREENCHA OS DADOS DE MATERIAIS E ESTRUTURAS ESPESSURAS PARA A ESCAVAÇÃO";
            label5.Click += label5_Click;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label7.Location = new Point(16, 65);
            label7.Name = "label7";
            label7.Size = new Size(242, 15);
            label7.TabIndex = 12;
            label7.Text = "ESCOLHA O MATERIAL DE EMBASAMENTO";
            label7.Click += label7_Click;
            // 
            // CBembasamento
            // 
            CBembasamento.DropDownStyle = ComboBoxStyle.DropDownList;
            CBembasamento.DropDownWidth = 300;
            CBembasamento.FormattingEnabled = true;
            CBembasamento.Location = new Point(325, 62);
            CBembasamento.Name = "CBembasamento";
            CBembasamento.Size = new Size(277, 23);
            CBembasamento.TabIndex = 15;
            CBembasamento.SelectedIndexChanged += CBembasamento_SelectedIndexChanged;
            // 
            // comboBox2
            // 
            comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox2.FormattingEnabled = true;
            comboBox2.Location = new Point(325, 178);
            comboBox2.Name = "comboBox2";
            comboBox2.Size = new Size(277, 23);
            comboBox2.TabIndex = 17;
            comboBox2.SelectedIndexChanged += comboBox2_SelectedIndexChanged;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label6.Location = new Point(16, 178);
            label6.Name = "label6";
            label6.Size = new Size(211, 15);
            label6.TabIndex = 16;
            label6.Text = "ESCOLHA O MATERIAL DE REATERRO";
            // 
            // InterfaceRotinaEscavacao
            // 
            AcceptButton = btnAtivar;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = btnCancelar;
            ClientSize = new Size(634, 357);
            Controls.Add(comboBox2);
            Controls.Add(label6);
            Controls.Add(CBembasamento);
            Controls.Add(label7);
            Controls.Add(label5);
            Controls.Add(textBox2);
            Controls.Add(textBox1);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(EspessuraConcretoMagro);
            Controls.Add(EspessuraCompactacaoManual);
            Controls.Add(btnAtivar);
            Controls.Add(btnCancelar);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Name = "InterfaceRotinaEscavacao";
            Text = "Criação de Superfícies de Escavação Drenagem";
            Load += InterfaceRotinaEscavacao_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Label label2;
        private Label label3;
        private Label label4;
        private TextBox textBox1;
        private TextBox textBox2;
        private Label label5;
        private Label label7;
        private ComboBox CBembasamento;
        private ComboBox comboBox2;
        private Label label6;
    }
}