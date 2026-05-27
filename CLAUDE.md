# CLAUDE.md — Rotinas Petrobras

Plugin AutoCAD Civil 3D 2026 em C# (.NET 8). Não é projeto cross-platform, não é web, não é console app.

## Stack

- **TFM:** `net8.0-windows7.0`. Plataforma alvo: **x64** (também compila ARM64/x86, mas debug real é x64).
- **WPF + WinForms** habilitados no mesmo csproj (`UseWPF`, `UseWindowsForms`).
- **Autodesk Managed API** (carregadas como `<Reference>` apontando para `Program Files\Autodesk\AutoCAD 2026\`):
  - Core: `accoremgd`, `acdbmgd`, `acmgd`, `acdbmgdbrep`, `AdWindows`
  - Civil 3D: `AeccDbMgd`, `AeccDynamo`
  - Architecture: `AecBaseMgd`, `AecPropDataMgd`
  - Map/GIS: `Autodesk.Gis.Map.*`, `OSGeo.FDO`, `OSGeo.MapGuide.*`
  - Interop: `Autodesk.AECC.Interop.Roadway`
- **COM:** `AXDBLib`, `AutocadMAP`, `COMSVCSLib`, `WMLSS`, `SystemMonitor`
- **Pacotes (Central Package Management via `Directory.Packages.props`):**
  - IFC: `Xbim.Common`, `Xbim.Essentials`, `Xbim.Ifc`, `Xbim.Ifc2x3/4/4x3`, `Xbim.IO.Esent`, `Xbim.IO.MemoryModel` (todos `6.0.565`)
  - Excel: `ClosedXML 0.105.0`, `EPPlus 8.5.0`, `ExcelDataReader 3.8.0`
  - Outros: `HtmlAgilityPack`, `ICSharpCode.Decompiler`, `Microsoft.Extensions.Options 8.0.2`
- **Build:** `dotnet build` na raiz (csproj principal: `AutomacoesCivil3D.csproj`, sln: `RotinasPetrobras.sln`).
- **Deploy automático:** target `DeployAutomacoesPetrobrasBundle` (em `AutomacoesCivil3D.csproj`) copia o output para `%AppData%\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\Contents\` após cada build. Não há passo manual.

## Layout

Arquivos `.cs` ficam soltos na raiz E em subpastas. Subpastas mais ativas:

- `IFC/` — exportação/importação IFC, mapeamento de Property Sets, Xbim. Arquivos críticos: `IfcInfraConfigEditorService.cs`, `IfcSolidosDrainageBinder.cs`, `Pos_Process_PAV.cs`, `QUANTITIES_IFC.cs`.
- `Rotinas Petrobras/` — comandos específicos Petrobras (drenagem, escavação, estruturas). Entry point: `Main.cs`.
- `Rotinas DNIT/` — exportação IFC para corredores, Property Sets de sinalização.
- `Rotinas PropertySets/` — manipulação de PSets do AutoCAD.
- `Rotinas PAvelar/`, `Rotinas Houer/`, `Rotinas Vale/` — variantes por cliente.
- `Superficies/`, `AUTOMAÇÕES PLUG-IN SOLIDOS/` — sólidos e superfícies Civil 3D.
- `PastaSolidosCorredoresNovaInterfaceLogicaAntiga/` — código **excluído do build** via `<Compile Remove>`. Não tocar.
- `Resources/` — recursos visuais, **excluído do compile** (`<Compile Remove="Resources\**" />`).
- `bin/`, `obj/`, `bin_codex/` — output, ignorar.

Existem múltiplos `.csproj` legados (`*-Copia.csproj`, `*-DESKTOP-MAE3FQ9.csproj`, `*-DESKTOP-5RKDAHT.csproj`) — fruto de sync entre máquinas. **Único csproj ativo é `AutomacoesCivil3D.csproj`.**

## Convenções observadas

- **Idioma:** comentários, mensagens ao usuário (`Editor.WriteMessage`) e nomes de variáveis em **PT-BR**, frequentemente com acentos. Não traduzir para inglês.
- **Nomenclatura de arquivos:** inconsistente — coexistem `PascalCase.cs`, `ALLCAPS.cs`, nomes com espaços e acentos (`SOLIDOS_SUPERFÍCIES.cs`). Manter o padrão local da pasta ao adicionar arquivos; não renomear arquivos existentes sem pedido explícito.
- **Comandos AutoCAD:** declarados via `[CommandMethod("NomeDoComando")]`. Comandos rodam dentro de `using Transaction t = db.TransactionManager.StartTransaction()`.
- **Acesso a documentos:** padrão usa singleton `Manager.DocCivil`, `Manager.DocData`, `Manager.DocEditor` (ver `Manager.cs`). Não criar novos acessos diretos a `Application.DocumentManager.MdiActiveDocument` se já houver helper.
- **Excel:** preferir EPPlus quando já estiver em uso no arquivo; ClosedXML em código mais novo.

## Restrições importantes

- **Não usar APIs cross-platform-only.** Nada de `System.IO.Pipes` em modo Linux, `FileSystem.GetFileSystemEntries` etc. O alvo é Windows + AutoCAD.
- **Não trocar versões em `Directory.Packages.props`** sem pedido explícito — Xbim 6.0.565 e EPPlus 8.5.0 são fixos por compatibilidade Civil 3D 2026.
- **Não tocar nos `.csproj` `-Copia` ou `-DESKTOP-*`** — são backups, edição quebra sync entre máquinas.
- **Não buildar para outras plataformas que não x64** sem motivo claro — Civil 3D é x64-only em runtime.
- **Não rodar `dotnet new`, `dotnet add package`, `dotnet remove`** sem pedido — o csproj é mantido manualmente, com `<COMReference>` e `<Reference HintPath>` específicos.

## Glossário (PT-BR ↔ AutoCAD/Civil 3D)

- **Corredor** = `Corridor` (Civil 3D)
- **Superfície** = `Surface` / `TinSurface`
- **Estrutura** (drenagem) = `Structure` (Civil 3D pipe network node — PV, caixa, boca-de-lobo)
- **Rede** = `Network` (pipe network)
- **Alinhamento** = `Alignment`
- **Perfil** = `Profile`
- **PV** = Poço de Visita (manhole)
- **DRE** = Dispositivo de Drenagem
- **PSet / Property Set** = AutoCAD Property Set Definition
- **Pset (IFC)** = IFC Property Set (Xbim)
- **Cruzeta / Canaleta** = elementos de drenagem urbana
- **Sólidos** = `Solid3d` / sólidos extraídos de corredor
- **Petrobras / DNIT / Houer / PAvelar / Vale** = clientes; cada pasta = entrega para esse cliente.

## Comandos úteis

```bash
dotnet build AutomacoesCivil3D.csproj -c Debug -p:Platform=x64
dotnet build AutomacoesCivil3D.csproj -c Release -p:Platform=x64
```

Após build, o plugin já está deployado em `%AppData%\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\`. Reabrir o Civil 3D para recarregar.

## Quando explorar código

Os arquivos `.cs` são grandes (`IfcSolidosDrainageBinder.cs` tem 84KB, `QUANTITIES_IFC.cs` 49KB, `MaterialExtraction.cs` 28KB). **Use Grep antes de Read.** Para entender uma rotina, busque pelo `[CommandMethod("...")]` correspondente — esse é o entry point.
