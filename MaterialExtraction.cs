using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Microsoft.Office.Interop.Excel;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D.Rotinas_Petrobras
{
    public class MaterialSummary
    {
        public string? Codigo { get; set; }
        public string? Name { get; set; }
        public double TotalVolume { get; set; }
        public double TotalArea { get; set; }
    }

    public class XmlMaterialParser
    {
        Document doc = Manager.DocCad;
        Editor ed = Manager.DocEditor;
        Database docData = Manager.DocData;
        CivilDocument civilDoc = Manager.DocCivil;
        double areas = 0.0;
        List<double> regions = new List<double>();
        List<string> alinhamentos = new List<string>();
        
        

        public List<MaterialSummary> ParseAndSumMaterials(string xmlFilePath)
        {


            LaneWidthChecker laneWidthChecker = new LaneWidthChecker();
            XDocument xmlDoc = XDocument.Load(xmlFilePath);
            


            var materialEntries = xmlDoc.Descendants("MaterialCrossSect")
                .Select(m =>
                {
                    string fullName = m.Attribute("name")?.Value ?? "DESCONHECIDO";
                    fullName = fullName.Trim();

                    string codigo = string.Empty;
                    string nome = fullName.ToUpperInvariant();

                    // tenta extrair código no formato 99.9999.99 no início, se existir
                    Match match = Regex.Match(fullName, @"^\s*(\d{2}\.\d{4}\.\d{2})\s+(.*)$");
                    if (match.Success)
                    {
                        codigo = match.Groups[1].Value.Trim().ToUpperInvariant();
                        nome = match.Groups[2].Value.Trim().ToUpperInvariant();
                    }

                    XElement alignment = m.Ancestors("Alignment").FirstOrDefault();
                    string alignmentName = alignment?.Attribute("name")?.Value ?? string.Empty;

                    double area = 0.0;
                    LaneAreas laneAreas = laneWidthChecker.GetLaneWidthFromAlignment(alignmentName);


                    double volume = 0.0;
                    double.TryParse(m.Attribute("volume")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out volume);

                    return new
                    {
                        Codigo = codigo,
                        Nome = nome,
                        Volume = volume,
                        Area = area
                    };
                });

            // agora o agrupamento é por NOME; o código é apenas um atributo auxiliar
            List<MaterialSummary> lista = materialEntries
                .GroupBy(m => m.Nome)
                .Select(g => new MaterialSummary
                {
                    Codigo = g.Select(x => x.Codigo)
                              .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
                    Name = g.Key,
                    TotalVolume = g.Sum(x => x.Volume),
                    TotalArea = g.Sum(x => x.Area) / 2.6 // mesma correção que você já usava
                })
                .ToList();

            return lista;
        }
    }

    public class CsvExporter
    {
        private static string ResolveAreaQuantity(MaterialSummary item, double legacyVolumeDivisor)
        {
            double quantity = item.TotalArea > 0.0
                ? item.TotalArea
                : item.TotalVolume / legacyVolumeDivisor;

            return quantity.ToString("F2", CultureInfo.InvariantCulture);
        }

        public static void ExportToCsvTrp(List<MaterialSummary> data, string csvPath)
        {
            var normalizedData = data.Select(m => new
            {
                Key = (m.Name ?? string.Empty).Trim().ToUpperInvariant(),
                Material = m
            });

            var groupedData = normalizedData
                .GroupBy(x => x.Key)
                .Select(g => new MaterialSummary
                {
                    Codigo = g.Select(x => x.Material.Codigo)
                              .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
                    Name = g.Key,
                    TotalVolume = g.Sum(x => x.Material.TotalVolume),
                    TotalArea = g.Sum(x => x.Material.TotalArea),
                })
                .OrderBy(m => m.Name)
                .ToList();

            using (StreamWriter writer = new StreamWriter(csvPath, false, Encoding.GetEncoding("ISO-8859-1")))
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;
                Database docData = doc.Database;
                CivilDocument civilDoc = Manager.DocCivil;

                writer.WriteLine("Servico; Unidade de Medida; Quantidade");

                foreach (MaterialSummary item in groupedData)
                {
                    string nome = (item.Name ?? string.Empty).Trim().ToUpperInvariant();

                    string familia = string.Empty;
                    string servico = string.Empty;
                    string subitem = string.Empty;
                    string condicaoExecucao = string.Empty;
                    string unidadeMedida = string.Empty;
                    string quantidade = string.Empty;
                    string codigoComposicao = item.Codigo ?? string.Empty;

                    switch (nome)
                    {
                        case "LIMPEZA TERRENO (MECANIZADO)":
                            familia = "Supressão Vegetal";
                            servico = "Limpeza de terreno (MECANIZADO)";
                            subitem = "Mecanizado";
                            condicaoExecucao = "DMT - 200 a 400 m";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.3);
                            break;

                        case "ESCAVAÇAO MATERIAL 1º  CATEGORIA":
                            familia = "Corte";
                            servico = "Escavação carga e transporte na obra - material 1ª categoria";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "DMT - 200 a 400 m";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "ESCAVAÇAO MATERIAL 2º  CATEGORIA":
                            familia = "Corte";
                            servico = "Escavação carga e transporte na obra - material 2ª categoria";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "DMT - 200 a 400 m";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "ESCAVAÇAO MATERIAL 3º  CATEGORIA":
                            familia = "Corte";
                            servico = "Escavação carga e transporte na obra - material 3ª categoria";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "DMT - 200 a 400 m";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "COMPACTAÇÃO DE ATERRO 95% PROCTOR NORMAL":
                            familia = "Aterro";
                            servico = "Compactação de aterro 95% Proctor Normal (exclusive o material)";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "DMT - 200 a 400 m";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "COMPACTAÇÃO DE ATERRO 100% PROCTOR NORMAL":
                            familia = "Aterro";
                            servico = "Compactação de aterro 100% Proctor Normal (exclusive material)";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "DMT - 200 a 400 m";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "CARGA/DESCARGA E TRANSPORTE DE SOLO CONTAMINADO PARA BOTA-FORA":
                            familia = "Bota Fora";
                            servico = "Carga/Descarga e Transporte de solo contaminado para bota-fora";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "CARGA/DESCARGA E TRANSPORTE DE SOLO PARA BOTA-FORA":
                            familia = "Bota Fora";
                            servico = "Carga/Descarga e Transporte de solo para bota-fora";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "EMPRÉSTIMO DE MATERIAL DE JAZIDA":
                            familia = "Aterro";
                            servico = "Empréstimo de material de jazida";
                            subitem = "Não Aplicável";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;
                    }

                    if (string.IsNullOrWhiteSpace(servico) ||
                        string.IsNullOrWhiteSpace(unidadeMedida) ||
                        string.IsNullOrWhiteSpace(quantidade))
                    {
                        continue; // material não mapeado
                    }

                    string unidade = string.Empty;
                    string anexo = string.Empty;
                    string itemPPU = string.Empty;
                    string subcontrato = string.Empty;
                    codigoComposicao = (codigoComposicao ?? string.Empty).Trim().Replace(",", ";");
                    string documentoReferencia = Path.GetFileNameWithoutExtension(docData.Filename);
                    familia = familia.Trim().Replace(",", ";");
                    servico = servico.Trim().Replace(",", ";");
                    unidadeMedida = unidadeMedida.Trim().Replace(",", ";");
                    quantidade = quantidade.Trim().Replace(",", ";");
                    subitem = subitem.Trim().Replace(",", ";");
                    condicaoExecucao = condicaoExecucao.Trim().Replace(",", ";");
                    string transporteLocal = string.Empty;
                    string transporteExterno = string.Empty;

                    writer.WriteLine($"{servico};{unidadeMedida};{quantidade}");
                }
            }
        }

        public static void ExportToCsvPav(List<MaterialSummary> data, string csvPath)
        {
            var normalizedData = data.Select(m => new
            {
                Key = (m.Name ?? string.Empty).Trim().ToUpperInvariant(),
                Material = m
            });

            var groupedData = normalizedData
                .GroupBy(x => x.Key)
                .Select(g => new MaterialSummary
                {
                    Codigo = g.Select(x => x.Material.Codigo)
                              .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
                    Name = g.Key,
                    TotalVolume = g.Sum(x => x.Material.TotalVolume),
                    TotalArea = g.Sum(x => x.Material.TotalArea),
                })
                .OrderBy(m => m.Name)
                .ToList();

            using (StreamWriter writer = new StreamWriter(csvPath, false, Encoding.GetEncoding("ISO-8859-1")))
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;
                Database docData = doc.Database;
                CivilDocument civilDoc = Manager.DocCivil;

                writer.WriteLine("Servico; Unidade de Medida; Quantidade");

                foreach (MaterialSummary item in groupedData)
                {
                    string nome = (item.Name ?? string.Empty).Trim().ToUpperInvariant();

                    string familia = string.Empty;
                    string servico = string.Empty;
                    string subitem = string.Empty;
                    string condicaoExecucao = string.Empty;
                    string unidadeMedida = string.Empty;
                    string quantidade = string.Empty;
                    string codigoComposicao = item.Codigo ?? string.Empty;

                    switch (nome)
                    {
                        case "ARMADURA DE AÇO - AÇO CA 60":
                            familia = "Armaduras";
                            servico = "ARMADURA DE AÇO - AÇO CA 60";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "kg";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "BASE BRITA GRADUADA":
                            familia = "Bases e Sub-Bases";
                            servico = "BASE DE BRITA GRADUADA";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "IMPRIMAÇÃO DE BASE":
                            familia = "Pinturas e Imprimacao";
                            servico = "IMPRIMAÇÃO DE BASE";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.01);
                            break;

                        case "PAVIMENTO ASFÁLTICO - CBUQ - CAP":
                            familia = "Pavimentos";
                            servico = "PAVIMENTO ASFÁLTICO - CBUQ - CAP";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "PAVIMENTO ASFÁLTICO - TSD":
                            familia = "Pavimentos";
                            servico = "PAVIMENTO ASFÁLTICO - TSD";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.01);
                            break;

                        case "PAVIMENTO ASFÁLTICO - TSS":
                            familia = "Pavimentos";
                            servico = "PAVIMENTO ASFÁLTICO - TSS";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.01);
                            break;

                        case "PAVIMENTO ASFÁLTICO - TST":
                            familia = "Pavimentos";
                            servico = "PAVIMENTO ASFÁLTICO - TST";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.01);
                            break;

                        case "PAVIMENTO DE BLOCOS INTERTRAVADOS":
                            familia = "Pavimentos";
                            servico = "PAVIMENTO DE BLOCOS INTERTRAVADOS";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.01);
                            break;

                        case "PAVIMENTO DE CONCRETO ARMADO FCK = 30 MPA":
                            familia = "Pavimentos";
                            servico = "PAVIMENTO DE CONCRETO ARMADO FCK = 30 MPA";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "PINTURA DE LIGAÇÃO":
                            familia = "Pinturas e Imprimacao";
                            servico = "PINTURA DE LIGAÇÃO";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.01);
                            break;

                        case "PLANTIO DE GRAMA TIPO ESMERALDA":
                            familia = "Paisagismo";
                            servico = "PLANTIO DE GRAMA TIPO ESMERALDA";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.1);
                            break;

                        case "PLANTIO DE GRAMA TIPO SÃO CARLOS/BATATAIS":
                            familia = "Paisagismo";
                            servico = "PLANTIO DE GRAMA TIPO SÃO CARLOS/BATATAIS";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.1);
                            break;

                        case "REFORÇO DO SUB LEITO":
                            familia = "Bases e Sub-Bases";
                            servico = "REFORÇO DO SUB LEITO";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;

                        case "REGULARIZAÇÃO E COMPACTAÇÃO DE SUBLEITO":
                            familia = "Bases e Sub-Bases";
                            servico = "REGULARIZAÇÃO E COMPACTAÇÃO DE SUBLEITO";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            quantidade = ResolveAreaQuantity(item, 0.01);
                            break;

                        
                        case "SUB BASE/COLCHÃO DRENANTE":
                            familia = "Bases e Sub-Bases";
                            servico = "SUB BASE /COLCHÃO DRENANTE";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            quantidade = item.TotalVolume.ToString("F2", CultureInfo.InvariantCulture);
                            break;
                    }

                    if (string.IsNullOrWhiteSpace(servico) ||
                        string.IsNullOrWhiteSpace(unidadeMedida) ||
                        string.IsNullOrWhiteSpace(quantidade))
                    {
                        continue; // material não mapeado
                    }

                    string unidade = string.Empty;
                    string anexo = string.Empty;
                    string itemPPU = string.Empty;
                    string subcontrato = string.Empty;
                    codigoComposicao = (codigoComposicao ?? string.Empty).Trim().Replace(",", ";");
                    string documentoReferencia = Path.GetFileNameWithoutExtension(docData.Filename);
                    familia = familia.Trim().Replace(",", ";");
                    servico = servico.Trim().Replace(",", ";");
                    unidadeMedida = unidadeMedida.Trim().Replace(",", ";");
                    quantidade = quantidade.Trim().Replace(",", ";");
                    subitem = subitem.Trim().Replace(",", ";");
                    condicaoExecucao = condicaoExecucao.Trim().Replace(",", ";");

                    writer.WriteLine($"{servico};{unidadeMedida};{quantidade}");
                }
            }
        }
    }

    public class MaterialExtration
    {
        [CommandMethod("ExportToCsvPav")]
        public static void ExportToCsvPav()
        {
            TesteReportQuantities volumeReport = new TesteReportQuantities();
            volumeReport.ExportSampleLineVolumes();
            Editor ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
            string reportsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Relatorios_Civil3D");
            string csvPathPavimentacao = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Resultados_Materiais_Pavimentação.csv");

            try
            {
                XmlMaterialParser parser = new XmlMaterialParser();
                List<MaterialSummary> allResults = new List<MaterialSummary>();

                foreach (string xmlFile in Directory.EnumerateFiles(reportsDir, "*.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        ed.WriteMessage($"\nProcessando arquivo: {xmlFile}");
                        List<MaterialSummary> results = parser.ParseAndSumMaterials(xmlFile);
                        allResults.AddRange(results);
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage($"\nErro ao processar arquivo {xmlFile}: {ex.Message}");
                    }
                }

                if (allResults.Count > 0)
                {
                    CsvExporter.ExportToCsvPav(allResults, csvPathPavimentacao);
                    ed.WriteMessage($"\nCSV gerado com sucesso: {csvPathPavimentacao}");
                    ed.WriteMessage($"\nTotal de registros processados (por nome): {allResults.GroupBy(m => m.Name).Count()}");

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = csvPathPavimentacao,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
                else
                {
                    ed.WriteMessage("\nNenhum arquivo XML válido foi encontrado.");
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nErro geral: {ex.Message}");
            }
        }

        [CommandMethod("ExportToCsvTrp")]
        public static void ExportToCsvTrp()
        {

            TesteReportQuantities volumeReport = new TesteReportQuantities();
            volumeReport.ExportSampleLineVolumes();
            Editor ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
            string reportsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Relatorios_Civil3D");
            string csvPathTerraplenagem = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Resultados_Materiais_Terraplenagem.csv");

            try
            {
                XmlMaterialParser parser = new XmlMaterialParser();
                List<MaterialSummary> allResults = new List<MaterialSummary>();

                foreach (string xmlFile in Directory.EnumerateFiles(reportsDir, "*.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        ed.WriteMessage($"\nProcessando arquivo: {xmlFile}");
                        List<MaterialSummary> results = parser.ParseAndSumMaterials(xmlFile);
                        allResults.AddRange(results);
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage($"\nErro ao processar arquivo {xmlFile}: {ex.Message}");
                    }
                }

                if (allResults.Count > 0)
                {
                    CsvExporter.ExportToCsvTrp(allResults, csvPathTerraplenagem);
                    ed.WriteMessage($"\nCSV gerado com sucesso: {csvPathTerraplenagem}");
                    ed.WriteMessage($"\nTotal de registros processados (por nome): {allResults.GroupBy(m => m.Name).Count()}");

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = csvPathTerraplenagem,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
                else
                {
                    ed.WriteMessage("\nNenhum arquivo XML válido foi encontrado.");
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nErro geral: {ex.Message}");
            }
        }
    }
}
