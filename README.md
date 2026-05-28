# ERGECivil3DPlugin --- AutomacoesCivil3D (Diretrizes Petrobras) {#ergecivil3dplugin-automacoescivil3d-diretrizes-petrobras}

O **ERGECivil3DPlugin** (implementado no projeto AutomacoesCivil3D) é uma solução de engenharia de software de alta performance, concebida para atuar como o braço tecnológico estratégico na implementação de processos BIM dentro do ecossistema de infraestrutura industrial da Petrobras.

Desenvolvido sobre a arquitetura moderna do **.NET 8** (net8.0-windows7.0) e utilizando o poder da linguagem **C# 14.0**, este plugin estende as capacidades nativas do **Autodesk Civil 3D (versões 2025 e 2026+)**, transformando fluxos de trabalho tradicionais em processos automatizados, precisos e orientados a metadados.

A solução foi arquitetada para enfrentar os desafios críticos da engenharia de infraestrutura, consolidando-se em quatro pilares fundamentais:

1.  **Extração Inteligente de Sólidos 3D:** Coordenação multidisciplinar de elementos complexos de infraestrutura e drenagem.

2.  **Processamento Geométrico Avançado de Superfícies:** Algoritmos para extração de quantitativos (QTO) de terraplenagem e pavimentação.

3.  **Gestão de Metadados e Property Sets:** Modelagem da informação sob o padrão AWP (*Advanced Work Packaging*) e regras de governança de dados.

4.  **Interoperabilidade IFC:** Exportação e mapeamento nativo via ecossistema robusto **Xbim 6.0**, garantindo total compatibilidade com o formato **IFC4x3**.

Ao unificar interfaces de usuário sofisticadas em **WPF** com a robustez da **AutoCAD/Civil 3D .NET API**, o projeto assegura eficiência operacional e total conformidade com os manuais de entrega técnica e padrões normativos da Petrobras.

## 1. Visão Geral e Stack Técnica {#visão-geral-e-stack-técnica}

O projeto foi construído para ser resiliente a mudanças de versão de APIs proprietárias, isolando a lógica de negócios da dependência direta de uma única versão do AutoCAD Civil 3D.

### 🛠️ Stack e Frameworks {#stack-e-frameworks}

| **Item** | **Valor** | **Observação** |
|----|----|----|
| **Target Framework** | net8.0-windows7.0 | Compatibilidade garantida com Civil 3D 2025 e 2026+ |
| **Linguagem** | C# 14.0 | Uso de recursos de produtividade modernos (pattern matching avançado, collection expressions) |
| **UI Engine** | WPF & WinForms | MVVM para janelas flutuantes e Palettes nativas para AutoCAD |
| **Arquitetura UI** | MVVM / SOA | Desacoplamento rígido entre lógica de engenharia e renderização de interface |
| **Plataforma** | x64 | Civil 3D não oferece suporte para arquiteturas AnyCPU |
| **Versões Suportadas** | Civil 3D 2025 & 2026 | Arquitetura preparada para transições de APIs subsequentes |

## 2. Gerenciamento de Dependências {#gerenciamento-de-dependências}

Para evitar a fragmentação de versões de pacotes em soluções complexas, o projeto utiliza o **Central Package Management (CPM)** por meio de um arquivo centralizado de propriedades.

### 2.1. Configuração CPM (Directory.Packages.props) {#configuração-cpm-directory.packages.props}

Todas as versões de pacotes gerenciados do ecossistema do plugin são declaradas de forma centralizada:

\<Project\>\
\<PropertyGroup\>\
\<ManagePackageVersionsCentrally\>true\</ManagePackageVersionsCentrally\>\
\</PropertyGroup\>\
\<ItemGroup\>\
\<!\-- Mapeamento e Interoperabilidade IFC (Xbim) \--\>\
\<PackageVersion Include=\"Xbim.Common\" Version=\"6.0.578\" /\>\
\<PackageVersion Include=\"Xbim.Essentials\" Version=\"6.0.578\" /\>\
\<PackageVersion Include=\"Xbim.Ifc\" Version=\"6.0.578\" /\>\
\<PackageVersion Include=\"Xbim.Ifc4\" Version=\"6.0.578\" /\>\
\<PackageVersion Include=\"Xbim.IO.Esent\" Version=\"6.0.578\" /\>\
\<PackageVersion Include=\"Xbim.IO.MemoryModel\" Version=\"6.0.578\" /\>\
\
\<!\-- Manipulação de Planilhas e Relatórios \--\>\
\<PackageVersion Include=\"EPPlus\" Version=\"8.5.0\" /\>\
\<PackageVersion Include=\"ClosedXML\" Version=\"0.105.0\" /\>\
\<PackageVersion Include=\"ExcelDataReader\" Version=\"3.8.0\" /\>\
\
\<!\-- Parser e Utilitários de Terceiros \--\>\
\<PackageVersion Include=\"HtmlAgilityPack\" Version=\"1.11.x\" /\>\
\<PackageVersion Include=\"ICSharpCode.Decompiler\" Version=\"8.x\" /\>\
\<PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.3\" /\>\
\<PackageVersion Include=\"Microsoft.Extensions.Options\" Version=\"8.0.2\" /\>\
\<PackageVersion Include=\"CommunityToolkit.Mvvm\" Version=\"8.2.2\" /\>\
\</ItemGroup\>\
\</Project\>

### 2.2. Referências Locais do SDK (Autodesk) e Arquivos de Apoio {#referências-locais-do-sdk-autodesk-e-arquivos-de-apoio}

Estas bibliotecas são instaladas de forma local ou mapeadas a partir da pasta de instalação padrão da Autodesk (C:\Program Files\Autodesk\AutoCAD 2026\\.

> ⚠️ **Importante:** Sempre configure a propriedade \<Private\>False\</Private\> (*Copy Local = False*) nestas referências para evitar o empacotamento de DLLs nativas do Civil 3D, o que gera falhas graves de carregamento de tipo (*Type Load Exceptions*) em tempo de execução.

- **AcCoreMgd.dll / AcDbMgd.dll / AcMgd.dll**: Núcleo da API AutoCAD.

- **AeccDbMgd.dll**: Banco de dados e manipulação de entidades de infraestrutura do Civil 3D (Alinhamentos, Corredores, Redes de Pressão e Gravidade).

- **AecBaseMgd.dll / AecPropDataMgd.dll**: Motor de metadados, dicionários de extensão e Property Sets.

- **AecModelerMgd.dll**: Algoritmos de modelagem geométrica de sólidos e massas 3D.

- **SOLIDOS.dll**: Biblioteca de interoperabilidade do plugin terceirizado SOLIDOS para controle de drenagem.

- **Microsoft.Office.Interop.Excel.dll** (armazenado em refs/): Biblioteca local utilizada para fluxos legados de exportação direta do Excel.

## 3. Estrutura da Solução (Árvore de Projeto) {#estrutura-da-solução-árvore-de-projeto}

Abaixo é apresentada a organização de arquivos e pastas no Visual Studio, otimizada para desacoplamento e portabilidade de módulos:

Solution \'RotinasPetrobras\' (ou \'ERGECivil3DPlugin\')\
├── Directory.Packages.props \# Gerenciamento Centralizado de Versões (CPM)\
├── App.config \# Redirecionamentos de Binding de Assemblies\
└── AutomacoesCivil3D (Project)\
├── Properties/ \# Configurações de Assembly e Recursos\
├── refs/ \# DLLs Locais de Interoperabilidade\
│ └── Microsoft.Office.Interop.Excel.dll\
│\
├── Automacoes_Solidos/ \# Módulo de Automações de Drenagem (Petrobras N-0038)\
│ ├── AjusteConexoesJusante.cs \# Correção de declividade e cotas de fundo\
│ ├── OsnapPresents.cs \# Snap points para agilizar desenho técnico\
│ └── SolidosShowroomPetrobras.cs \# Visualizador e inserção de peças padronizadas\
│\
├── EscavacaoDrenagem/ \# Cálculos de Geotecnia e Terraplenagem\
│ ├── Main.cs \# Comando \'EscavacaoDrenagem\'\
│ └── RotinaSuperfícieEscavaçãoDRE.cs \# Algoritmo de projeção de taludes de valas\
│\
├── IFC/ \# Interoperabilidade BIM IFC4x3 (Motor Xbim)\
│ ├── IfcAplicarMapeamentoJson.cs \# Deserializador de esquemas de mapeamento\
│ ├── IfcInfraConfigEditorWindow.xaml \# Painel administrativo WPF para IFC\
│ ├── IfcDrainagePsetSeeder.cs \# Injeção de propriedades IFC em redes de tubos\
│ └── XbimBootstrap.cs \# Inicializador de instâncias e sessões XBIM\
│\
├── PastaSolidosCorredoresNovaInterfaceLogicaAntiga/ \# Modelagem Linear Avançada\
│ ├── ExportacaoSolidosCorredores.cs \# Varredura de seções transversais para sólidos 3D\
│ ├── SPLITCORRIDORREGIONS.cs \# Divisão automatizada de regiões com base em marcos\
│ └── EDITAR_FREQUENCIA_CORREDOR.cs \# Ajustes de inserção de seções em curvas\
│\
├── QtoSuperficies/ \# Módulo de Quantificação de Superfícies (MVVM)\
│ ├── QtoSuperficiesWindow.xaml \# View de parametrização de materiais e camadas\
│ ├── QtoSuperficiesService.cs \# Algoritmos de cálculo de volumes (Corte/Aterro)\
│ ├── QtoSuperficiesTemplates.cs \# Modelos de saída estruturada\
│ └── QtoSuperficiesCommand.cs \# Registrador de comando de QTO\
│\
├── Rotinas_PropertySets/ \# Gestão e Modelagem de Atributos (AWP)\
│ ├── PsetViewerControl.cs \# Palete lateral do AutoCAD para auditoria de dados\
│ ├── PsetWizardForm.cs \# Assistente para preenchimento de Property Sets\
│ └── CascataPSets.cs \# Filtros de seleção dependentes\
│\
├── Superficies/ \# Módulo de Processamento Geométrico de Malhas\
│ ├── EXTRAIR_SOLIDOS.cs \# Geração de sólidos a partir de triangulações\
│ └── LIMPARTRIAGULACAOSUPERFICIES.cs \# Algoritmo de limpeza de triângulos externos à fronteira\
│\
├── ClassePrincipal.cs \# Ponto de Entrada (IExtensionApplication)\
├── Manager.cs \# Singleton de Gerenciamento de Contexto Ativo\
└── CodesSpecific.cs \# Mapeador de códigos de pontos, links e formas (Corridors)

## 4. Padrões de Projeto e Convenções de Código {#padrões-de-projeto-e-convenções-de-código}

Para garantir a legibilidade técnica e a escalabilidade por múltiplos engenheiros de software, a solução adota convenções estritas.

### 4.1. Convenções Linguísticas e Comunicação {#convenções-linguísticas-e-comunicação}

- **Interface e Mensagens:** Mensagens impressas no console do AutoCAD via Editor.WriteMessage(), caixas de diálogo, logs e comentários de código devem ser em **Português (PT-BR)**, incluindo acentuação correta.

- **Nomenclatura de Comandos:** Comandos registrados com a diretiva \[CommandMethod(\"NOME_COMANDO\")\] devem ser em caixa alta e utilizar termos técnicos claros.

### 4.2. Padrão de Gerenciamento de Contexto (Manager.cs) {#padrão-de-gerenciamento-de-contexto-manager.cs}

Evita-se o acoplamento de chamadas estáticas do sistema operacional através do uso do Singleton Manager. Todo acesso ao documento ativo do Civil 3D deve seguir este padrão:

using Autodesk.AutoCAD.ApplicationServices;\
using Autodesk.Civil.ApplicationServices;\
\
public sealed class Manager\
{\
private static readonly Manager \_instance = new Manager();\
public static Manager Instance =\> \_instance;\
\
public static CivilDocument DocCivil =\> CivilApplication.ActiveDocument;\
public static Document DocCad =\> Application.DocumentManager.MdiActiveDocument;\
public static Database DocData =\> DocCad.Database;\
public static Editor DocEditor =\> DocCad.Editor;\
}

### 4.3. Padrão de Escopo de Transações {#padrão-de-escopo-de-transações}

Para evitar corrupção do banco de dados do AutoCAD (Database) e garantir que transações falhas voltem ao estado original sem causar *crashes* na aplicação, utiliza-se a declaração using simplificada:

public void ExecutarOperacao()\
{\
var db = Manager.DocData;\
using var trans = db.TransactionManager.StartTransaction();\
\
try\
{\
// Operações no Banco de Dados do AutoCAD\...\
\
trans.Commit();\
}\
catch (System.Exception ex)\
{\
Manager.DocEditor.WriteMessage(\$\"\n\[ERRO\] Operação cancelada: {ex.Message}\");\
// O Rollback é implícito ao sair do escopo sem chamar o Commit()\
}\
}

### 4.4. Glossário Técnico {#glossário-técnico}

Para padronizar o mapeamento de termos de engenharia da infraestrutura brasileira para o domínio de APIs nativas em inglês, adota-se:

| **Termo em Português** | **Termo Técnico Autodesk** | **Aplicação no Plugin** |
|----|----|----|
| **Corredor** | Corridor | Modelagem de vias de acesso industriais |
| **Superfície** | TinSurface / Surface | Malhas de terreno primitivo e terraplenagem |
| **Estrutura / PV** | Structure | Poços de Visita e Caixas de Passagem de drenagem |
| **Rede** | PipeNetwork | Rede coletora de gravidade |
| **Alinhamento** | Alignment | Eixo de referência horizontal |
| **Perfil** | Profile | Eixo de referência vertical (greide) |
| **PSet** | PropertySet | Metadados atribuídos a objetos no AutoCAD |
| **Cruzeta / Canaleta** | Entidades personalizadas / Sólidos | Dispositivos de Drenagem Urbana e de Área Industrial |

## 5. Diretrizes Normativas: Petrobras N-0038 (Drenagem On-Site) {#diretrizes-normativas-petrobras-n-0038-drenagem-on-site}

As ferramentas presentes na pasta Automacoes_Solidos seguem rigorosamente os critérios geométricos e de vazão exigidos pela **Norma Técnica Petrobras N-0038 (Projeto de Drenagem de Áreas Industriais)**.

### 5.1. Dimensionamento Hidráulico e Escoamento Superficial {#dimensionamento-hidráulico-e-escoamento-superficial}

O plugin auxilia na verificação da capacidade de escoamento de canaletas e cruzetas sob regime de fluxo uniforme, aplicando a Fórmula de Manning:

![](media/image4.png){width="6.458333333333333in" height="0.5705905511811024in"}Onde:

- ![](media/image6.png){width="0.17252624671916011in" height="0.26386373578302713in"} é a velocidade média do escoamento (![](media/image14.png){width="0.3959962817147856in" height="0.2709448818897638in"}).

- ![](media/image5.png){width="0.12858158355205598in" height="0.2785925196850394in"} é o coeficiente de rugosidade de Manning do material (por exemplo, concreto liso ![](media/image11.png){width="0.9023436132983377in" height="0.269666447944007in"}).

- ![](media/image2.png){width="0.25976596675415575in" height="0.2701563867016623in"} é o raio hidráulico (![](media/image7.png){width="0.18815069991251093in" height="0.27177384076990374in"}), calculado como a razão entre a área molhada (![](media/image1.png){width="0.16080708661417323in" height="0.2787325021872266in"}) e o perímetro molhado (![](media/image9.png){width="0.1673173665791776in" height="0.2718908573928259in"}): ![](media/image12.png){width="0.7137040682414698in" height="0.26893153980752404in"}.

- ![](media/image8.png){width="0.14371719160104987in" height="0.26690398075240596in"} é a declividade de fundo da canaleta ou tubulação (![](media/image10.png){width="0.48356189851268594in" height="0.27331692913385824in"}).

### 5.2. Mapeamento de Vazão para Combate a Incêndio {#mapeamento-de-vazão-para-combate-a-incêndio}

O módulo de drenagem fornece rotinas para mapeamento de áreas de contribuição de bacias, calculando a vazão de projeto combinando a vazão pluvial gerada pelo método racional e a vazão de água de combate a incêndio exigida para o cenário industrial:

![](media/image3.png){width="6.458333333333333in" height="0.4765376202974628in"}As estruturas (PVs e caixas) são marcadas com metadados indicando o atendimento aos limites de capacidade hidráulica previstos pela norma N-0038, impedindo transbordamentos e minimizando o risco de dispersão de contaminantes de processos químicos industriais.

## 6. Guia de Manutenção e Migração Multi-versão (2025 ![](media/image13.png){width="0.3025721784776903in" height="0.36517279090113736in"} 2026+) {#guia-de-manutenção-e-migração-multi-versão-2025-2026}

O plugin foi projetado de forma que o suporte a novas versões anuais do Civil 3D exija o mínimo de alteração possível de código fonte, alterando apenas referências no nível de build do MSBuild.

### 6.1. Condicionamento Automatizado do Arquivo .csproj {#condicionamento-automatizado-do-arquivo-.csproj}

O arquivo de projeto .csproj contém diretivas condicionais para chavear as referências corretas das DLLs de SDK de cada versão, bem como ajustar a pasta de destino final com base na configuração do Visual Studio:

\<Project Sdk=\"Microsoft.NET.Sdk\"\>\
\<PropertyGroup\>\
\<TargetFramework\>net8.0-windows7.0\</TargetFramework\>\
\<UseWpf\>true\</UseWpf\>\
\<UseWindowsForms\>true\</UseWindowsForms\>\
\<Platforms\>x64\</Platforms\>\
\<Configurations\>Debug2025;Release2025;Debug2026;Release2026\</Configurations\>\
\</PropertyGroup\>\
\
\<!\-- Constantes de Compilação Condicional \--\>\
\<PropertyGroup Condition=\"\'\$(Configuration)\' == \'Debug2025\' Or \'\$(Configuration)\' == \'Release2025\'\"\>\
\<DefineConstants\>CIVIL3D_2025\</DefineConstants\>\
\<AcadDir\>C:\Program Files\Autodesk\AutoCAD 2025\</AcadDir\>\
\</PropertyGroup\>\
\<PropertyGroup Condition=\"\'\$(Configuration)\' == \'Debug2026\' Or \'\$(Configuration)\' == \'Release2026\'\"\>\
\<DefineConstants\>CIVIL3D_2026\</DefineConstants\>\
\<AcadDir\>C:\Program Files\Autodesk\AutoCAD 2026\</AcadDir\>\
\</PropertyGroup\>\
\
\<!\-- Referências do SDK Autodesk 2025 \--\>\
\<ItemGroup Condition=\"\'\$(Configuration)\' == \'Debug2025\' Or \'\$(Configuration)\' == \'Release2025\'\"\>\
\<Reference Include=\"AcCoreMgd\"\>\<HintPath\>\$(AcadDir)\AcCoreMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AcDbMgd\"\>\<HintPath\>\$(AcadDir)\AcDbMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AcMgd\"\>\<HintPath\>\$(AcadDir)\AcMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AeccDbMgd\"\>\<HintPath\>\$(AcadDir)\AeccDbMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AecBaseMgd\"\>\<HintPath\>\$(AcadDir)\AecBaseMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AecPropDataMgd\"\>\<HintPath\>\$(AcadDir)\AecPropDataMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AecModelerMgd\"\>\<HintPath\>\$(AcadDir)\AecModelerMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\</ItemGroup\>\
\
\<!\-- Referências do SDK Autodesk 2026 \--\>\
\<ItemGroup Condition=\"\'\$(Configuration)\' == \'Debug2026\' Or \'\$(Configuration)\' == \'Release2026\'\"\>\
\<Reference Include=\"AcCoreMgd\"\>\<HintPath\>\$(AcadDir)\AcCoreMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AcDbMgd\"\>\<HintPath\>\$(AcadDir)\AcDbMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AcMgd\"\>\<HintPath\>\$(AcadDir)\AcMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AeccDbMgd\"\>\<HintPath\>\$(AcadDir)\AeccDbMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AecBaseMgd\"\>\<HintPath\>\$(AcadDir)\AecBaseMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AecPropDataMgd\"\>\<HintPath\>\$(AcadDir)\AecPropDataMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\<Reference Include=\"AecModelerMgd\"\>\<HintPath\>\$(AcadDir)\AecModelerMgd.dll\</HintPath\>\<Private\>False\</Private\>\</Reference\>\
\</ItemGroup\>\
\</Project\>

### 6.2. Compatibilização do Manifesto do Pacote (PackageContents.xml) {#compatibilização-do-manifesto-do-pacote-packagecontents.xml}

Para carregar o plugin automaticamente nas novas versões utilizando a tecnologia Autoloader da Autodesk, adicione o mapeamento de séries no arquivo manifest:

\<?xml version=\"1.0\" encoding=\"utf-8\"?\>\
\<ApplicationPackage SchemaVersion=\"1.0\" Version=\"2025.1.0\" Name=\"ERGE Civil 3D Plugin\" AppCode=\"ERGE.Civil3D\"\>\
\<RuntimeRequirements SeriesMin=\"R25.0\" SeriesMax=\"R26.0\" Platform=\"AutoCAD\*\" /\>\
\<Components\>\
\<RuntimeEntry OS=\"Win64\" Platform=\"Civil3D\" SeriesMin=\"R25.0\" SeriesMax=\"R26.0\" AppName=\"AutomacoesCivil3D\" ModuleName=\"./Contents/AutomacoesCivil3D.dll\" /\>\
\</Components\>\
\</ApplicationPackage\>

- R25.0 corresponde à versão de engenharia interna do AutoCAD Civil 3D 2025.

- R26.0 corresponde à versão de engenharia interna do AutoCAD Civil 3D 2026.

## 7. Pipeline de Build, Compilação e Deploy Automático {#pipeline-de-build-compilação-e-deploy-automático}

A automação de build reduz o retrabalho manual do desenvolvedor e o tempo de depuração em máquina local.

### 7.1. Compilação via Linha de Comando (CLI) {#compilação-via-linha-de-comando-cli}

Para construir o projeto via terminal apontando para a pasta padrão do Civil 3D 2026:

\# Executa compilação em modo de Depuração (Debug)\
dotnet build AutomacoesCivil3D.csproj -c Debug2026 -p:Platform=x64\
\
\# Executa compilação em modo de Produção (Release)\
dotnet build AutomacoesCivil3D.csproj -c Release2026 -p:Platform=x64

Se o ambiente local possuir o Autodesk Civil 3D instalado em um caminho personalizado (por exemplo, em uma unidade D:\\ ou sob outra pasta de rede), sobrescreva a propriedade AcadDir diretamente no comando:

dotnet build AutomacoesCivil3D.csproj -c Debug2026 -p:Platform=x64 -p:AcadDir=\"D:\Autodesk\AutoCAD 2026\"

### 7.2. Deploy de Teste Automatizado (Post-Build Target) {#deploy-de-teste-automatizado-post-build-target}

Para acelerar o ciclo de depuração (\"*code-build-test*\"), o arquivo .csproj possui uma tarefa pós-compilação configurada que move os novos arquivos binários diretamente para a pasta monitorada pelo mecanismo Autoloader do AutoCAD:

\<Target Name=\"DeployAutomacoesPetrobrasBundle\" AfterTargets=\"Build\" Condition=\"\'\$(Configuration)\' == \'Debug2025\' Or \'\$(Configuration)\' == \'Debug2026\'\"\>\
\<PropertyGroup\>\
\<BundleFolder\>\$(APPDATA)\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\Contents\\/BundleFolder\>\
\</PropertyGroup\>\
\<ItemGroup\>\
\<OutputFiles Include=\"\$(OutputPath)\\\*\\.\*\" /\>\
\</ItemGroup\>\
\<!\-- Copia de forma recursiva os assemblies compilados se o bundle existir \--\>\
\<Copy SourceFiles=\"@(OutputFiles)\" DestinationFiles=\"@(OutputFiles-\>\'\$(BundleFolder)%(RecursiveDir)%(Filename)%(Extension)\')\" SkipUnchangedFiles=\"true\" /\>\
\</Target\>

## 8. Segurança de Código, Ofuscação e Distribuição {#segurança-de-código-ofuscação-e-distribuição}

A propriedade intelectual das rotinas de cálculo de volume de terraplenagem e regras de negócios industriais deve ser protegida antes da entrega técnica oficial.

\[Código C# Compilado (Release)\]\
│\
▼\
┌─────────────────┐\
│ .NET Reactor │ ◄─── (Ofuscação de Classes, Métodos e Criptografia de Strings)\
└────────┬────────┘\
│\
▼\
┌─────────────────┐\
│ Assinatura │ ◄─── (Certificado Digital para prevenir bloqueios no AutoCAD)\
│ Digital (Sign) │\
└────────┬────────┘\
│\
▼\
┌─────────────────┐\
│ Inno Setup 6 │ ◄─── (Geração do Pacote Executável .exe)\
└─────────────────┘

### 8.1. Ofuscação com .NET Reactor {#ofuscação-com-.net-reactor}

As compilações configuradas como Release executam uma tarefa de linha de comando integrada ao .NET Reactor para aplicar as seguintes proteções:

1.  **Ofuscação de Fluxo de Controle:** Impede que decompiladores (como o *ILSpy* ou *dnSpy*) decodifiquem algoritmos lógicos e fórmulas.

2.  **Criptografia de Strings:** Oculta segredos de strings e queries internas.

3.  **Remoção de Metadados Públicos:** Suprime assinaturas de membros e tipos internos desnecessários para a execução do plugin.

### 8.2. Assinatura Digital do Assembly {#assinatura-digital-do-assembly}

O assembly resultante (AutomacoesCivil3D.dll) deve ser assinado digitalmente usando uma autoridade certificadora corporativa Petrobras ou AC raiz confiável pelo Windows. Isso evita que o AutoCAD mostre alertas de segurança de \"Editor Não Confiável\" ao inicializar e previne o bloqueio de segurança em computadores de produção de parceiros.

## 9. Empacotamento e Geração do Instalador (Inno Setup) {#empacotamento-e-geração-do-instalador-inno-setup}

O processo de empacotamento compila todos os arquivos necessários para execução e dependências em um único instalador autônomo executável (.exe).

### 9.1. Estrutura de Distribuição de Arquivos {#estrutura-de-distribuição-de-arquivos}

O instalador gera a seguinte topologia de diretórios no computador do usuário final:

%APPDATA%\Autodesk\ApplicationPlugins\ERGE.Civil3D.bundle\\

- PackageContents.xml (Arquivo raiz de manifesto de carregamento)

- Contents/

  - AutomacoesCivil3D.dll (Assembly principal ofuscado e assinado)

  - *DLLs de dependência:* Xbim.\*.dll, EPPlus.dll, ClosedXML.dll, etc.

- Resources/

  - Templates/ (Arquivos .dwt corporativos para suporte técnico)

  - Stylesheets/ (Modelos .xsl para geração de relatórios de QTO)

  - Icons/ (Imagens da Ribbon de comandos do plugin)

### 9.2. Script Completo do Inno Setup (erge-plugin-installer.iss) {#script-completo-do-inno-setup-erge-plugin-installer.iss}

Utilize este script para compilar o executável no Inno Setup 6+:

\[Setup\]\
AppName=ERGE Civil 3D Plugin\
AppVersion=2026.1.0\
AppPublisher=ERGE Petrobras Tech Team\
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\ERGE.Civil3D.bundle\
DisableDirPage=yes\
OutputBaseFilename=Setup_ERGE_Civil3D_2026\
Compression=lzma\
SolidCompression=yes\
ArchitecturesInstallIn64BitMode=x64\
\
\[Files\]\
; Manifesto do Pacote Autoloader\
Source: \"..\bundle\PackageContents.xml\"; DestDir: \"{app}\"; Flags: ignoreversion\
\
; DLLs Principais e Dependências NuGet (Contents)\
Source: \"..\bin\Release2026\Contents\\\"; DestDir: \"{app}\Contents\"; Flags: ignoreversion recursesubdirs\
\
; Arquivos de Apoio, Templates de Desenho e Ícones\
Source: \"..\bundle\Resources\\\"; DestDir: \"{app}\Resources\"; Flags: ignoreversion recursesubdirs\
\
\[Code\]\
// Verifica se o Civil 3D 2025 está instalado no sistema\
function IsCivil3D2025Installed(): Boolean;\
begin\
Result := RegKeyExists(HKEY_LOCAL_MACHINE, \'SOFTWARE\Autodesk\AutoCAD\R25.0\ACAD-8100:409\');\
end;\
\
// Verifica se o Civil 3D 2026 está instalado no sistema\
function IsCivil3D2026Installed(): Boolean;\
begin\
Result := RegKeyExists(HKEY_LOCAL_MACHINE, \'SOFTWARE\Autodesk\AutoCAD\R26.0\ACAD-9100:409\');\
end;\
\
// Evento disparado no início da execução do instalador\
function InitializeSetup(): Boolean;\
begin\
Result := true;\
if not (IsCivil3D2025Installed() or IsCivil3D2026Installed()) then\
begin\
MsgBox(\'Aviso: Não foi detectada nenhuma instalação oficial do Autodesk Civil 3D (2025 ou 2026) neste computador.\' + \#13#10 +\
\'O plugin será instalado, mas pode não funcionar até que um ambiente compatível seja configurado.\', mbInformation, MB_OK);\
end;\
end;

### 9.3. Automação via MSBuild (Post-Build Event) {#automação-via-msbuild-post-build-event}

Para integrar a geração automática do instalador ao processo de compilação do Visual Studio, configure este comando no evento pós-build (Post-Build Event) do projeto quando compilado sob o perfil de produção (Release2026):

\"C:\Program Files (x86)\Inno Setup 6\ISCC.exe\" \"\$(ProjectDir)installer\erge-plugin-installer.iss\"
