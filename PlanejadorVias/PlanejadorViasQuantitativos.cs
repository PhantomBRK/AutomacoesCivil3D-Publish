using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutomacoesCivil3D
{
    /// <summary>Linha do quantitativo de uma via.</summary>
    public sealed class QuantitativoVia
    {
        public Guid ViaId { get; set; }
        public string Secao { get; set; } = string.Empty;
        public double Comprimento { get; set; }
        public double LarguraTotal { get; set; }
        public Dictionary<TipoElementoVia, double> AreaPorTipo { get; } = new Dictionary<TipoElementoVia, double>();
        public double ComprimentoMeioFio { get; set; }
    }

    /// <summary>
    /// Take-off de planejamento das vias criadas pelo Planejador de Vias:
    /// comprimento de eixo × larguras da seção-tipo. Valores aproximados —
    /// as caixas das junções não são descontadas (nível de estudo preliminar).
    /// </summary>
    public static class PlanejadorViasQuantitativos
    {
        public static List<QuantitativoVia> Calcular(Transaction tr, Database db, out List<string> avisos)
        {
            avisos = new List<string>();
            List<QuantitativoVia> resultado = new List<QuantitativoVia>();

            Dictionary<Guid, ViaRegistrada> vias = PlanejadorViasJuncoes.CarregarVias(tr, db);
            foreach (ViaRegistrada via in vias.Values.OrderBy(v => v.Secao, StringComparer.OrdinalIgnoreCase))
            {
                Curve? eixo = tr.GetObject(via.EixoId, OpenMode.ForRead, false) as Curve;
                if (eixo == null || eixo.IsErased)
                {
                    continue;
                }

                double comprimento;
                try
                {
                    comprimento = eixo.GetDistanceAtParameter(eixo.EndParam);
                }
                catch
                {
                    continue;
                }

                QuantitativoVia quantitativo = new QuantitativoVia
                {
                    ViaId = via.Id,
                    Secao = string.IsNullOrWhiteSpace(via.Secao) ? "(sem seção)" : via.Secao,
                    Comprimento = comprimento
                };

                SecaoTipoVia? secao = SecoesTipoCatalogo.ObterPorNome(via.Secao);
                if (secao == null)
                {
                    avisos.Add($"Seção-tipo \"{via.Secao}\" não encontrada no catálogo: via contabilizada só em comprimento.");
                }
                else
                {
                    quantitativo.LarguraTotal = secao.LarguraTotal;
                    foreach (KeyValuePair<TipoElementoVia, double> par in secao.LargurasPorTipo())
                    {
                        quantitativo.AreaPorTipo[par.Key] = par.Value * comprimento;
                    }

                    quantitativo.ComprimentoMeioFio = secao.ContarElementos(TipoElementoVia.MeioFio) * comprimento;
                }

                resultado.Add(quantitativo);
            }

            return resultado;
        }

        public static string MontarResumo(List<QuantitativoVia> quantitativos)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("QUANTITATIVOS DO PLANEJADOR DE VIAS (aproximados — junções não descontadas)");
            sb.AppendLine();

            if (quantitativos.Count == 0)
            {
                sb.AppendLine("Nenhuma via do Planejador encontrada no desenho.");
                return sb.ToString();
            }

            double comprimentoTotal = quantitativos.Sum(q => q.Comprimento);
            sb.AppendLine($"Vias: {quantitativos.Count}   Comprimento total de eixo: {comprimentoTotal:0.00} m");
            sb.AppendLine();

            Dictionary<TipoElementoVia, double> areaTotal = new Dictionary<TipoElementoVia, double>();
            double meioFioTotal = 0.0;
            foreach (QuantitativoVia q in quantitativos)
            {
                foreach (KeyValuePair<TipoElementoVia, double> par in q.AreaPorTipo)
                {
                    double atual;
                    areaTotal.TryGetValue(par.Key, out atual);
                    areaTotal[par.Key] = atual + par.Value;
                }

                meioFioTotal += q.ComprimentoMeioFio;
            }

            foreach (KeyValuePair<TipoElementoVia, double> par in areaTotal.OrderBy(p => TipoElementoViaInfo.Rotulo(p.Key)))
            {
                sb.AppendLine($"  {TipoElementoViaInfo.Rotulo(par.Key)}: {par.Value:N2} m²");
            }

            if (meioFioTotal > 0)
            {
                sb.AppendLine($"  Meio-fio (comprimento): {meioFioTotal:N2} m");
            }

            return sb.ToString();
        }

        /// <summary>CSV com separador ';' e vírgula decimal (padrão Excel PT-BR).</summary>
        public static string MontarCsv(List<QuantitativoVia> quantitativos)
        {
            CultureInfo cultura = new CultureInfo("pt-BR");
            List<TipoElementoVia> tipos = quantitativos
                .SelectMany(q => q.AreaPorTipo.Keys)
                .Distinct()
                .OrderBy(t => TipoElementoViaInfo.Rotulo(t))
                .ToList();

            StringBuilder sb = new StringBuilder();
            sb.Append("Via;Secao-tipo;Comprimento do eixo (m);Largura total (m)");
            foreach (TipoElementoVia tipo in tipos)
            {
                sb.Append(';').Append("Area ").Append(TipoElementoViaInfo.Rotulo(tipo)).Append(" (m2)");
            }

            sb.Append(";Meio-fio (m)");
            sb.AppendLine();

            int indice = 1;
            foreach (QuantitativoVia q in quantitativos)
            {
                sb.Append("VIA_").Append(indice.ToString("000", cultura));
                sb.Append(';').Append(q.Secao.Replace(';', ','));
                sb.Append(';').Append(q.Comprimento.ToString("0.00", cultura));
                sb.Append(';').Append(q.LarguraTotal.ToString("0.00", cultura));

                foreach (TipoElementoVia tipo in tipos)
                {
                    double area;
                    q.AreaPorTipo.TryGetValue(tipo, out area);
                    sb.Append(';').Append(area.ToString("0.00", cultura));
                }

                sb.Append(';').Append(q.ComprimentoMeioFio.ToString("0.00", cultura));
                sb.AppendLine();
                indice++;
            }

            return sb.ToString();
        }
    }
}
