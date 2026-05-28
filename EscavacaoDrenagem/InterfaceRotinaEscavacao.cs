using System;
using System.Windows.Forms;

namespace EscavacaoDRE
{
    public partial class InterfaceRotinaEscavacao : Form
    {



        public InterfaceRotinaEscavacao()
        {
            InitializeComponent();

            // Adiciona eventos aos botões
            btnAtivar.Click += BtnAtivar_Click;
            btnCancelar.Click += BtnCancelar_Click;

            // Validação em tempo real (opcional)
            EspessuraConcretoMagro.KeyPress += TxtCampo_KeyPress;
            EspessuraCompactacaoManual.KeyPress += TxtCampo_KeyPress;
        }

        public bool OK { get; set; }
        public double EspessuraConcreto { get; set; }
        public double EspessuraCompact { get; set; }

        //Cria lista de Materiais de Embasamento
        public List<string> Embasamento = new List<string>();

        //Cria lista de Materiais de Reaterro
        public List<string> MaterialReaterro = new List<string>();

        //Metodo para pegar a superficie selecionada
        public string EmbasamentoSelecionado
        {
            get
            {
                return Embasamento[CBembasamento.SelectedIndex];
            }
        }

        public string ReaterroSelecionado
        {
            get
            {
                return MaterialReaterro[comboBox2.SelectedIndex];
            }
        }




        // Permite apenas números, vírgula, ponto e backspace
        private void TxtCampo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',')
            {
                e.Handled = true;
            }

            if (e.KeyChar == ',')
            {
                e.KeyChar = '.';
            }
        }


        private void CBEmbasamento()
        {
            var index = 0;
            List<string> materialEmbasamento = new List<string>();
            materialEmbasamento.Add("Concreto");
            materialEmbasamento.Add("Pó de Pedra");
            materialEmbasamento.Add("Areia");

            foreach (string material in materialEmbasamento)
            {
                CBembasamento.Items.Add(material);
                Embasamento.Add(material);

                if (index == 0)
                {
                    CBembasamento.SelectedIndex = CBembasamento.Items.IndexOf(material);
                    index++;

                }

            }


        }

        private void CBReaterro()
        {
            var index2 = 0;
            List<string> materialReaterro = new List<string>();
            materialReaterro.Add("Envelopamento de Concreto");
            materialReaterro.Add("Pó de Pedra");
            materialReaterro.Add("Areia");
            materialReaterro.Add("Material de Jazida");
            materialReaterro.Add("Material da Escavação");

            foreach (string materialR in materialReaterro)
            {
                comboBox2.Items.Add(materialR);
                MaterialReaterro.Add(materialR);

                if (index2 == 0)
                {
                    comboBox2.SelectedIndex = comboBox2.Items.IndexOf(materialR);
                    index2++;

                }

            }


        }

        private void BtnAtivar_Click(object sender, EventArgs e)
        {
            // Tenta ler valores. Se vazio ou inválido, usa padrão.



            if (!double.TryParse(EspessuraConcretoMagro.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double espConcretoMagro))
                espConcretoMagro = 5.0;

            if (!double.TryParse(EspessuraCompactacaoManual.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double espCompactManual))
                espCompactManual = 20.0;


            EspessuraCompact = espCompactManual / 100;
            EspessuraConcreto = espConcretoMagro / 100;



            // Aqui você pode seguir com o que precisar fazer com os valores:
            MessageBox.Show(
                $"Espessura do Embasamento: {espConcretoMagro} cm\n" +
                $"Espessura do Offset de Compactação: {espCompactManual} cm",
                "Valores inseridos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            OK = true;
            this.Close();
            // Por exemplo: salvar, usar em cálculo, etc.
        }

        private void BtnCancelar_Click(object sender, EventArgs e)
        {
            EspessuraConcretoMagro.Text = string.Empty;
            EspessuraCompactacaoManual.Text = string.Empty;
            OK = false;
            // Se quiser fechar o formulário:
            this.Close();
        }

        private void InterfaceRotinaEscavacao_Load(object sender, EventArgs e)
        {
            CBEmbasamento();
            CBReaterro();

        }




















        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void CBembasamento_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}