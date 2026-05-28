# AutomacoesCivil3D — Rotinas Petrobras

Plugin para **AutoCAD Civil 3D 2026** em C# (.NET 8), focado em automações de:

- **Drenagem ON-SITE Petrobras seguindo a norma N-0038** (PVs, canaletas, cruzetas, vazão para combate a incêndio)
- **Exportação IFC** de corredores, superfícies e estruturas, com mapeamento de Property Sets (Xbim)
- **Corredores Civil 3D** — cópia, edição de frequência, seccionamento por alinhamentos, exclusão de pontas
- **Property Sets do AutoCAD** — wizard, exportação JSON, cascata, drenagem, sinalização
- **Quantitativo de superfícies** (PAV/TRP) com saída CSV/Excel

> Este repositório contém um recorte do plugin completo (Automações Sólidos, IFC, PastaSolidosCorredores, PropertySets + dependências do `QtoSuperficiesCommand`). Outras pastas do projeto interno (Rotinas DNIT, Houer, PAvelar, Vale, LOIN, etc.) não estão incluídas.

## Stack

- **TFM:** `net8.0-windows7.0` — plataforma alvo **x64** (Civil 3D só roda 64-bit)
- **WPF + WinForms** habilitados
- **Autodesk Managed API** (Civil 3D 2026, Architecture, Map/COM)
- **Pacotes NuGet** (Central Package Management via `Directory.Packages.props`):
  - IFC: Xbim 6.0.578 (Common, Essentials, Ifc, Ifc2x3/4/4x3, IO.Esent, IO.MemoryModel)
  - Excel: ClosedXML 0.105.0, EPPlus 8.5.0, ExcelDataReader 3.8.0
  - Outros: HtmlAgilityPack, ICSharpCode.Decompiler, Microsoft.Extensions.Options 8.0.2, Microsoft.VisualBasic

## Pré-requisitos

1. **Windows 10+** (x64)
2. **Civil 3D 2026** instalado em `C:\Program Files\Autodesk\AutoCAD 2026\`
   - Se estiver em outro caminho, sobrescreva a propriedade MSBuild `AcadDir` (veja seção Build)
3. **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
4. **Microsoft Office (Excel)** instalado (para `Microsoft.Office.Interop.Excel`)

## Build

```powershell
dotnet build AutomacoesCivil3D.csproj -c Debug -p:Platform=x64
# ou Release:
dotnet build AutomacoesCivil3D.csproj -c Release -p:Platform=x64
```

Caminho customizado do Civil 3D:

```powershell
dotnet build AutomacoesCivil3D.csproj -c Debug -p:Platform=x64 -p:AcadDir="D:\Autodesk\AutoCAD 2026"
```

## Deploy automático

O target `DeployAutomacoesPetrobrasBundle` (em `AutomacoesCivil3D.csproj`) copia o output para:

```
%AppData%\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\Contents\
```

após cada build, **se a pasta `.bundle/Contents/` já existir** (criação manual na primeira vez). Reabrir o Civil 3D para recarregar.

## Estrutura

```
.
├── AutomacoesCivil3D.csproj             # Projeto principal
├── RotinasPetrobras.sln                 # Solução
├── Directory.Packages.props             # CPM — versões centralizadas
├── App.config                           # Binding redirects
├── Manager.cs                           # Singleton (DocCivil, DocEditor, DocData, DocCad)
├── QtoSuperficies*.cs                   # Quantitativo por superfícies
├── refs/
│   └── Microsoft.Office.Interop.Excel.dll
├── Automacoes_Solidos/                  # Sólidos: cruzetas, canaletas, vazão, ajuste de conexões
├── IFC/                                 # Exportação IFC + Pset Seeders + Xbim bootstrap
├── PastaSolidosCorredoresNovaInterfaceLogicaAntiga/
│                                        # Cópia/edição de corredores, exportação de sólidos
└── Rotinas_PropertySets/                # PSets AutoCAD: wizard, cascata, drenagem, sinalização
```

## Convenções

- Comentários, mensagens (`Editor.WriteMessage`) e nomes em **PT-BR**, com acentos.
- Comandos AutoCAD declarados via `[CommandMethod("NOME_COMANDO")]`.
- Padrão de transação:

```csharp
using Transaction t = db.TransactionManager.StartTransaction();
// ...
t.Commit();
```

- Acesso ao documento via `Manager.DocCivil`, `Manager.DocData`, `Manager.DocEditor`, `Manager.DocCad`.

## Glossário

| PT-BR              | AutoCAD/Civil 3D                       |
|--------------------|----------------------------------------|
| Corredor           | `Corridor`                             |
| Superfície         | `Surface` / `TinSurface`               |
| Estrutura          | `Structure` (PV, caixa, boca-de-lobo)  |
| Rede               | `Network` (pipe network)               |
| Alinhamento        | `Alignment`                            |
| Perfil             | `Profile`                              |
| PV                 | Poço de Visita                         |
| DRE                | Dispositivo de Drenagem                |
| PSet               | AutoCAD Property Set Definition        |
| Pset (IFC)         | IFC Property Set (Xbim)                |
| Cruzeta / Canaleta | Elementos de drenagem urbana           |

## Licença

Código de uso interno. Não redistribuir DLLs Autodesk (licença proprietária — ver `.gitignore`).
