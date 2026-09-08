# Tutorial — Nova Automação de Sólidos de Corredores

> **Nível misto:** este documento começa com uma visão geral do fluxo e, em
> cada etapa, mergulha nos trechos-chave do código C#. O objetivo é entender
> *o que* a rotina faz e *por que* cada decisão foi tomada, sem precisar ler
> os ~5.000 linhas dos arquivos do módulo de uma só vez.

## Comandos expostos

A automação vive na pasta
`PastaSolidosCorredoresNovaInterfaceLogicaAntiga/` e é registrada em
`ExportacaoSolidosCorredores.cs`:

| Comando AutoCAD | Alias | Função |
|---|---|---|
| `ExportarSolidosCorredoresNovaInterface` | `EXSOLIDOSCORR_NI` | Abre a janela WPF e executa a exportação de sólidos. |
| `EXPORTARJSONCODENAMESCORR` | `EXSOLIDOSCORR_JSON` | Apenas sincroniza o catálogo JSON de tradução de CodeNames (sem exportar nada). |

O entry point apenas orquestra: monta os dados, mostra o diálogo, e — se o
usuário confirmar — chama o `service.Execute(...)`:

```csharp
ExportacaoSolidosCorredoresService service = new ExportacaoSolidosCorredoresService();
ExportacaoSolidosCorredoresDialogData dialogData = service.BuildDialogData();

if (dialogData.Corridors.Count == 0)
{
    AcadApp.ShowAlertDialog(dialogData.BlockingIssue);
    return;
}

ExportacaoSolidosCorredoresDialogViewModel viewModel = new(dialogData);
ExportacaoSolidosCorredoresWindow window = new(viewModel);

bool? confirmed = AutoCadWpfDialogHost.ShowModal(window);
if (confirmed != true) { /* cancelado */ return; }

ExportacaoSolidosCorredoresResult result = service.Execute(window.BuildRequest());
AcadApp.ShowAlertDialog(result.BuildSummary());
```

### Mapa do pipeline

```
ExportarSolidosCorredoresNovaInterface
        │
        ▼
 1. BuildDialogData() ........... varredura dos corredores + checagem de PSets
        │
        ▼
   [Janela WPF: usuário escolhe corredores, shapes/links, DWG e CSV de saída]
        │
        ▼
 2. Execute(request)
        ├── SynchronizeCodeNameCatalog() ...... 5. tradução de nomes (JSON)
        ├── corridor.ExportSolids(...) ......... 2. extração de sólidos
        ├── ApplyPropertySets() + PSetSolid() .. 3. PSETs por subassembly
        │       └── ColetarParametrosPorGuidGenerico() .. 4. separação de espessuras
        ├── ExportReportToCsv() ................ 6. geração do CSV
        └── CopyObjectsBetweenDrawings() ....... grava os sólidos no DWG destino
```

Cada caixa numerada corresponde a uma seção abaixo.

---

## Etapa 1 — Varredura dos corredores

**Arquivo:** `ExportacaoSolidosCorredoresService.cs` → `BuildDialogData()`

Antes de qualquer extração, a rotina precisa saber *quais* corredores
existem no desenho ativo e se o DWG tem os Property Sets nativos que o
Civil 3D exige. Isso alimenta a janela WPF.

A varredura percorre a `CorridorCollection` do documento Civil, ignorando
objetos inválidos e **corredores que são referências externas**
(`IsReferenceObject`) — só "corredores locais" podem ser explodidos em
sólidos:

```csharp
foreach (ObjectId item in docCivil.CorridorCollection)
{
    if (!item.IsValid || item.IsNull) continue;

    Corridor corridor = tr.GetObject(item, OpenMode.ForRead) as Corridor;
    if (corridor == null || corridor.IsReferenceObject) continue;

    int shapeCodeCount = SafeGetCodes(() => corridor.GetShapeCodes()).Length;
    int linkCodeCount  = SafeGetCodes(() => corridor.GetLinkCodes()).Length;
    list.Add(new CorridorExportItem(item, corridor.Name,
                                    shapeCodeCount, linkCodeCount, "Local"));
}
list = list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
```

Pontos a observar:

- **`SafeGetCodes`** envolve `GetShapeCodes()`/`GetLinkCodes()` em try/catch
  e devolve um array vazio em caso de erro — corredores meio quebrados não
  derrubam a varredura inteira.
- A contagem de *shape codes* e *link codes* é só informativa: aparece na
  janela para o usuário dimensionar o que será exportado.

### Checagem de Property Sets

Ainda em `BuildDialogData`, dois grupos de PSets são verificados:

```csharp
// Essenciais: PSets NATIVOS do Civil 3D — o corridor.ExportSolids() os anexa
// automaticamente em cada sólido. Se faltam, o DWG está inconsistente.
private static readonly string[] LegacyPropertySets =
{
    "Corridor Identity",
    "Corridor Model Information",
    "Corridor Property Data – User Defined",
    "Corridor Shape Information"
};
```

```csharp
DictionaryPropertySetDefinitions dictionary = new(docData);

// Essenciais (isRequired: true) — bloqueiam a exportação se ausentes
list2.AddRange(LegacyPropertySets.Select(name =>
    new PropertySetStatusInfo(name, isRequired: true,
        TryGetPsetDefinitionId(dictionary, tr, name) != ObjectId.Null)));

// Informativos (isRequired: false) — template LOIN novo; quando faltam,
// são criados depois pelo LoinCivil3DApplier.EnsureResources
string[] loinPsets =
{
    "Pset_A - Dados de Projeto",
    "Pset_B - Informacoes dos Elementos",
    "Pset_C - Propriedades Fisicas",
    "Pset_D - Layer IFC e Classificacao",
    "Pset_Requisitos por Elemento"
};
list2.AddRange(loinPsets.Select(name =>
    new PropertySetStatusInfo(name, isRequired: false,
        TryGetPsetDefinitionId(dictionary, tr, name) != ObjectId.Null)));
```

A distinção **essencial × informativo** é importante:

- Os **4 PSets nativos** são marcados como `isRequired`. Na ViewModel,
  `GetValidationError()` impede a exportação enquanto algum deles estiver
  ausente.
- Os **PSets LOIN** são complementares: a ausência só aparece como aviso
  visual na janela, sem travar.

O `TryGetPsetDefinitionId` ainda lida com **apelidos de nome** (com/sem
acento, com travessão vs. hífen) via o dicionário `PropertySetAliases`,
porque a mesma definição pode aparecer escrita de formas diferentes entre
templates:

```csharp
["Corridor Property Data – User Defined"] = new[]
{
    "Corridor Property Data – User Defined",  // travessão (–)
    "Corridor Property Data - User Defined",  // hífen (-)
    "Corridor Property Data — User Defined"   // travessão longo (—)
}
```

No fim, `BuildDialogData` calcula caminhos sugeridos (DWG de destino e CSV)
a partir do nome do desenho ativo e devolve um
`ExportacaoSolidosCorredoresDialogData` para a janela.

---

## Etapa 2 — Extração de sólidos

**Arquivo:** `ExportacaoSolidosCorredoresService.cs` → `Execute(request)`

Confirmada a janela, `Execute` abre **uma única transação** e percorre os
corredores selecionados. Para cada um, monta a lista de códigos a incluir e
chama o método nativo `Corridor.ExportSolids`.

### Quais códigos entram

`BuildIncludedCodes` une shape codes e/ou link codes conforme as caixas
marcadas pelo usuário, remove vazios e deduplica:

```csharp
public static string[] BuildIncludedCodes(Corridor corridor,
                                          ExportacaoSolidosCorredoresRequest request)
{
    IEnumerable<string> codes = Array.Empty<string>();
    if (request.ExportShapes) codes = codes.Concat(SafeGetCodes(() => corridor.GetShapeCodes()));
    if (request.ExportLinks)  codes = codes.Concat(SafeGetCodes(() => corridor.GetLinkCodes()));
    return codes.Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}
```

Se a lista resultar vazia, o corredor é pulado com um aviso ("não possui
códigos compatíveis com as opções selecionadas").

### A chamada nativa e o despacho por tipo

```csharp
ExportCorridorSolidsParams parms = new()
{
    IncludedCodes = array,
    ExportLinks   = request.ExportLinks,
    ExportShapes  = request.ExportShapes
};

foreach (ObjectId id in corridor.ExportSolids(parms, docData))
{
    if (!id.IsValid || id.IsNull || id.ObjectClass == null) continue;

    if (id.ObjectClass.Name == "AcDb3dSolid")
    {
        Solid3d solid = (Solid3d)tr.GetObject(id, OpenMode.ForWrite);
        ApplyPropertySets(solid, propertySets);   // anexa PSets nativos
        propertySets2.PSetSolid(solid, docData, tr); // preenche PSet_B/C
        exportados.Add(solid.ObjectId);
        result.ExportedSolids++;
        if (request.GenerateReport)
            TryAddReportRow(rows, tr, solid, corridor.Name, "3DSOLID", propertySets, result.Warnings);
    }
    else if (id.ObjectClass.Name == "AcDbBody")
    {
        Body body = (Body)tr.GetObject(id, OpenMode.ForWrite);
        ApplyPropertySets(body, propertySets);
        propertySets2.PSetBody(body, docData, tr);
        exportados.Add(body.ObjectId);
        result.ExportedBodies++;
        if (request.GenerateReport)
            TryAddReportRow(rows, tr, body, corridor.Name, "BODY", propertySets, result.Warnings);
    }
}
```

Observações:

- `ExportSolids` é o motor do Civil 3D — ele gera a geometria 3D a partir
  das *shapes* (volumes fechados, viram `AcDb3dSolid`) e *links* (superfícies,
  podem virar `AcDbBody`). A rotina trata os dois tipos separadamente.
- O Civil 3D **já anexa os PSets nativos** ("Corridor *") em cada sólido —
  por isso eram considerados essenciais na Etapa 1.
- Tudo acontece dentro de uma transação; nada é gravado em disco até o
  `Commit()`.

---

## Etapa 3 — Criação de PSETs por subassembly

**Arquivo:** `PropertySets.cs` → `PSetSolid(...)` / `PSetBody(...)`

Aqui está o coração semântico da rotina: transformar a geometria "burra"
em **objetos com metadados de engenharia**. Há dois passos.

### 3.1. Garantir os PSets anexados (`ApplyPropertySets`)

Antes de preencher qualquer coisa, o service garante que cada PSet nativo
está realmente anexado à entidade. `EnsurePropertySet` é idempotente:
verifica se já existe, tenta anexar, e reconfirma:

```csharp
public static bool EnsurePropertySet(Entity entity, ObjectId definitionId)
{
    try
    {
        ObjectId ps = PropertyDataServices.GetPropertySet(entity, definitionId);
        if (ps != ObjectId.Null && ps.IsValid) return true;   // já existe
    }
    catch { }

    try { PropertyDataServices.AddPropertySet(entity, definitionId); } catch { }

    try
    {
        ObjectId ps = PropertyDataServices.GetPropertySet(entity, definitionId);
        return ps != ObjectId.Null && ps.IsValid;             // reconfirma
    }
    catch { return false; }
}
```

Se algum PSet essencial não puder ser anexado, `ApplyPropertySets` lança
exceção com o nome do PSet e o handle da entidade — falha cedo e clara.

### 3.2. Ler o `Pset_B` e descobrir a subassembly

`PSetSolid` lê do **Pset_B** os campos que identificam a origem do sólido:

```csharp
PropertySet psB = GetOrCreatePropertySet(solid, dictionary, tr,
    "Pset_B - Informações dos Objetos e Elementos",
    "Pset_B - Informacoes dos Objetos e Elementos"); // alias sem acento

if (psB != null)
{
    nomeCamada    = psB.GetAt(psB.PropertyNameToId("CodeName"), solid).ToString();
    nomeSub       = psB.GetAt(psB.PropertyNameToId("SubassemblyName"), solid).ToString();
    nomeCorredor  = psB.GetAt(psB.PropertyNameToId("NomeCorredorSolido"), solid).ToString();
    guid          = psB.GetAt(psB.PropertyNameToId("RegionName"), solid).ToString();
    comprimentoStr= psB.GetAt(psB.PropertyNameToId("Comprimento"), solid).ToString();

    DefinirLayerPorCorredor(solid, docData, tr, nomeCamada);

    values.LengthKm = double.TryParse(comprimentoStr, out double len) ? len / 100.0 : 0.0;

    possuiCategoriaMapeada = TryResolveMappedCodeCategory(nomeCamada, out categoriaCodigo);

    // Coleta os parâmetros geométricos da subassembly que originou este sólido
    ColetarParametrosPorGuidGenerico(guid, nomeCorredor, nomeCamada, nomeSub,
                                     categoriaCodigo, db, tr, values);
}
```

O elo essencial é o **`RegionName` (GUID da região)** mais o
**nome do corredor**: com eles a rotina reabre o corredor de origem,
encontra a região exata e descobre **qual subassembly** gerou o sólido.
É isso que permite buscar as espessuras reais (Etapa 4).

`GetOrCreatePropertySet` aceita **vários nomes candidatos** (com e sem
acento) e, se o PSet não estiver anexado, anexa na hora — daí o "GetOrCreate".

### 3.3. `PSetBody` é a versão enxuta

Bodies (vindos de links) não têm espessura de camada para calcular, então
`PSetBody` só lê `AssemblyName` (do "Corridor Shape Information") e
`CorridorName` (do "Corridor Model Information") e aplica o layer/IFC. Não
preenche o Pset_C físico.

---

## Etapa 4 — Separação de espessuras

**Arquivo:** `PropertySets.cs` → `ColetarParametrosPorGuidGenerico`,
`MapDouble`, `ScoreSubassembly`, e o preenchimento do `Pset_C` em `PSetSolid`.

Esta é a parte mais sutil. Uma subassembly de pavimento expõe **vários
parâmetros de espessura** (`Pave1Depth`, `Pave2Depth`, `BaseDepth`,
`SubBaseDepth`, `Depth`, `SidewalkDepth`, `CFTDepth`...). O desafio é
casar *cada sólido* com a espessura *certa* da camada que ele representa.

### 4.1. O container `ExtractedValues`

Todos os valores extraídos vivem em um objeto **por sólido** (evita estado
estático "vazando" entre sólidos):

```csharp
public class ExtractedValues
{
    public double Width { get; set; }
    public double Pave1Depth { get; set; }
    public double Pave2Depth { get; set; }
    public double BaseDepth { get; set; }
    public double SubBaseDepth { get; set; }
    public double GuiaDepth { get; set; }      // mapeado de "Depth"
    public double PasseioDepth { get; set; }   // mapeado de "SidewalkDepth"
    public double Height { get; set; }         // mapeado de "CFTDepth"
    public double Slope { get; set; }          // fração; convertido p/ % no fim
    public double LengthKm { get; set; }
    public double Area { get; set; }
    public double TotalPavementDepth => Pave1Depth + Pave2Depth;

    public void RecomputeArea() => Area = Width * LengthKm;
}
```

### 4.2. Encontrar a subassembly por GUID e pontuá-la

`ColetarParametrosPorGuidGenerico` percorre o corredor, acha a região pelo
GUID e, **na estação inicial da região**, lista as subassemblies aplicadas.
Como pode haver mais de uma candidata, cada uma recebe uma **pontuação** e
elas são ordenadas da melhor para a pior:

```csharp
AppliedAssembly assembly = bl.GetAppliedAssemblyAtStation(region.StartStation);
var candidates = new List<(Subassembly Subassembly, int Score)>();

foreach (AppliedSubassembly appliedSub in assembly.GetAppliedSubassemblies())
{
    Subassembly sub = (Subassembly)tr.GetObject(appliedSub.SubassemblyId, OpenMode.ForRead);
    int score = ScoreSubassembly(sub, codeName, subassemblyName, categoriaEfetiva);
    candidates.Add((sub, score));
}

candidates.Sort((l, r) => r.Score.CompareTo(l.Score)); // maior score primeiro

foreach (var candidate in candidates)
{
    MapSubassemblyParameters(values, candidate.Subassembly, overwriteExisting: false);
    if (HasEnoughValuesForCategory(values, categoriaEfetiva)) break; // já achei o suficiente
}
```

O **score** combina vários sinais (`ScoreSubassembly`):

- nome da subassembly bate com o solicitado: **+100** (exato) / **+75** (contém);
- nome contém o CodeName: **+20**;
- a *macro* da subassembly (`MacroOrClassName`) bate com a categoria
  (ex.: categoria `PAVIMENTO`/`BASE` → macro contém `LANE`/`SHOULDER`;
  `GUIA` → `CURB`; taludes → `DAYLIGHT`/`SLOPE`/`LINK`): **+35**;
- a **assinatura de parâmetros** existe e é compatível com a categoria
  (`GetParameterSignatureScore`): até **+35 + 10 (Width) + 5 (Slope)**.

A ideia: dar preferência à subassembly que **realmente tem** o parâmetro de
espessura esperado para aquela categoria, não só a que tem nome parecido.

### 4.3. Mapear nomes de parâmetro → campos (`MapDouble`)

`MapSubassemblyParameters` joga cada parâmetro `double` da subassembly em
`MapDouble`, que faz o de-para entre **nomes nativos / em português** e os
campos de `ExtractedValues`. Note que largura/passeio usam comparação
exata (`EqualsAny`) e base/sub-base/CFT usam "contém" (`ContainsAny`):

```csharp
if (EqualsAny(paramName, "Width", "LaneWidth", "ShoulderWidth", "LARGURA"))            v.Width = val;
if (EqualsAny(paramName, "Pave1Depth", "ESPESSURA 1 CAMADA PAVIMENTO"))                v.Pave1Depth = val;
if (EqualsAny(paramName, "Pave2Depth", "ESPESSURA 2 CAMADA PAVIMENTO"))                v.Pave2Depth = val;
if (ContainsAny(paramName, "BaseDepth", "ESPESSURA BASE"))                             v.BaseDepth = val;
if (ContainsAny(paramName, "SubBaseDepth", "SubbaseDepth", "ESPESSURA SUB BASE"))      v.SubBaseDepth = val;
if (EqualsAny(paramName, "Depth"))                                                     v.GuiaDepth = val;
if (EqualsAny(paramName, "SidewalkDepth"))                                             v.PasseioDepth = val;
if (ContainsAny(paramName, "CFTDepth", "ESPESSURA REFORÇO SUBLEITO"))                  v.Height = val;
if (ContainsAny(paramName, "DefaultSlope", "Slope", "ShoulderSlope", "Deflection",
                            "LinkSlope", "INCLINAÇÃO"))                                v.Slope = val;
```

> Como a coleta usa `overwriteExisting: false`, o primeiro candidato
> (maior score) "ganha" cada campo; candidatos seguintes só preenchem o que
> ainda estiver zerado (`IsUnset`). Por isso a ordenação por score importa.

### 4.4. Escolher a espessura certa por categoria

De volta a `PSetSolid`, o **Pset_C** ("Propriedades Físicas") é preenchido
de acordo com a categoria do sólido. Cada família de CodeName usa a
espessura correspondente:

```csharp
void SetCommonPhysicalValues(double height)
{
    SetStr("Largura", values.Width.ToString("F2"));
    SetStr("Altura",  height.ToString("F2"));
    SetSlope();                                   // "Declividade da Pista" / "Inclinação"
    SetArea();                                    // Largura × Comprimento
    SetStr("Comprimento", values.LengthKm.ToString("F2"));
}

if (CodeMatches(..., "PAVIMENTO", "CBUQ", "pave1", "pave2", ...))
    SetCommonPhysicalValues(values.TotalPavementDepth);   // Pave1 + Pave2

if (CodeMatches(..., "BASE", ...) && !CodeMatches(..., "SUB_BASE", ...))
    SetCommonPhysicalValues(values.BaseDepth);

if (CodeMatches(..., "SUB_BASE", "SUBBASE", ...))
    SetCommonPhysicalValues(values.SubBaseDepth);

if (CodeMatches(..., "GUIA", "Rip Rap"))
    SetCommonPhysicalValues(values.GuiaDepth);

if (CodeMatches(..., "CFT", ...))
    SetCommonPhysicalValues(values.Height);

if (CodeMatches(..., "PASSEIO", ...))
    SetCommonPhysicalValues(values.PasseioDepth > 0 ? values.PasseioDepth
                                                    : values.TotalPavementDepth);
// ... TOP, OFFSET_TALUDE, TALUDE_ATERRO/CORTE, BARREIRA, PONTE etc.

if (!psetCPreenchido)               // nenhuma regra bateu
    SetCommonPhysicalValues(GetBestAvailableHeight());  // fallback
```

`GetBestAvailableHeight` é a rede de segurança: se a categoria não casou
com nenhuma regra, usa a primeira espessura não-nula disponível
(pavimento → base → sub-base → CFT → passeio → guia).

A inclinação recebe tratamento especial em `FormatarInclinacaoPsetC`: se o
valor bruto for ≤ 1 (fração), multiplica por 100; arredonda e formata como
`"NN%"`.

---

## Etapa 5 — Tradução de nomes (catálogo de CodeNames)

**Arquivo:** `CodeNameMappingCatalog.cs` (+ `CodeNameCatalog*.cs`)

Os CodeNames do Civil 3D variam muito entre projetos
(`BASE_DE_BRITA_GRADUADA`, `IMPRIMACAO_DE_BASE`, `PAVE1`, `CBUQ`...). A
"tradução" reduz toda essa variedade a um conjunto fixo de **categorias**
(`BASE`, `SUB_BASE`, `PAVIMENTO`, `CFT`, `PASSEIO`, `TALUDE_*`, `TOP`...),
que são justamente as chaves usadas nas regras da Etapa 4.

### 5.1. Três níveis de resolução

`TryGetMappedCategory` consulta, nesta ordem:

1. **Mapa embutido direto** (`BuiltInDirectMappings`) — de-para fixo
   código → categoria:

   ```csharp
   ["BASE_DE_BRITA_GRADUADA"] = "BASE",
   ["IMPRIMACAO_DE_BASE"]     = "BASE",
   ["SUB_BASE_COLCHAO_DRENANTE"] = "SUB_BASE",
   ["PAVE1"] = "PAVIMENTO",  ["CBUQ"] = "PAVIMENTO",
   ["DAYLIGHT_FILL"] = "TALUDE_ATERRO",
   ["DAYLIGHT_CUT"]  = "TALUDE_CORTE",
   ["DATUM"] = "TOP", ["RIP_RAP"] = "TOP", ...
   ```

2. **Catálogo JSON do projeto** (entradas habilitadas, campo `Category`);
3. Se nada casar, devolve o **próprio CodeName** (sem tradução) e marca
   como não mapeado.

A normalização (`NormalizeKey`) remove acentos, troca não-alfanuméricos por
`_` e sobe para maiúsculas — assim `Imprimação de Base` e
`IMPRIMACAO_DE_BASE` colidem na mesma chave.

### 5.2. Sugestão automática de categoria

Para códigos novos, `SuggestCategory` tenta adivinhar por padrão de
substring (depois do mapa direto):

```csharp
if (MatchesPattern(codeName, "ACOSTAMENTO_PAVIMENTO")) return "ACOSTAMENTO_PAVIMENTO";
if (MatchesPattern(codeName, "PAVIMENTO", "PAVIMENTO1", "PAVIMENTO2")) return "PAVIMENTO";
if (MatchesPattern(codeName, "SUB_BASE", "SUBBASE")) return "SUB_BASE";
if (MatchesPattern(codeName, "BASE")) return "BASE";
if (MatchesPattern(codeName, "GUIA")) return "GUIA";
// ... CFT, OFFSET_TALUDE, TALUDE_ATERRO/CORTE, BARREIRA, PONTE, TOP ...
return "NAO_MAPEADO";   // desistiu — vira aviso para o usuário revisar
```

### 5.3. Persistência e merge (o arquivo `*_CodeNames.json`)

`Sync` é idempotente e **preserva edições manuais**:

```csharp
public static CodeNameMappingCatalog Sync(string activeDrawingPath,
                                          IEnumerable<CorridorCodeInfo> corridorInfos)
{
    string fullPath = BuildDefaultPath(activeDrawingPath); // <dwg>_CodeNames.json
    CodeNameCatalogFile gerado = BuildGeneratedFile(activeDrawingPath, corridorInfos);

    CodeNameCatalogFile final = gerado;
    if (File.Exists(fullPath))
        final = Merge(LerJson(fullPath), gerado); // respeita Enabled/Category do usuário

    // só grava se algo mudou (evita reescrever o arquivo à toa)
    ...
    return new CodeNameMappingCatalog(fullPath, final, wasUpdated);
}
```

No `Merge`, para cada código regenerado, se já existia uma entrada com o
mesmo `NormalizeKey`, herdam-se o `Enabled` e o `Category` do arquivo
anterior. Ou seja: o engenheiro pode abrir o JSON, corrigir uma categoria
ou desabilitar um código, e essa decisão **sobrevive** às próximas
sincronizações.

O comando `EXPORTARJSONCODENAMESCORR` roda só este passo e reporta
quantos corredores, quantos CodeNames e quantos ficaram `NAO_MAPEADO`.
Durante a exportação, esses não-mapeados também viram avisos no resultado.

---

## Etapa 6 — Geração do CSV

**Arquivo:** `ExportacaoSolidosCorredoresService.cs` → `BuildReportRow`,
`ExportReportToCsv`.

Se o usuário marcou "gerar relatório", cada sólido/body processado vira uma
linha (`ReportRow`) com os valores **lidos de volta** dos PSets nativos —
um espelho do que foi efetivamente gravado.

### 6.1. Montar a linha

```csharp
ReportRow row = new()
{
    Corridor   = corridorName,
    EntityType = entityType,      // "3DSOLID" ou "BODY"
    Handle     = entity.Handle.ToString(),
    Layer      = entity.Layer
};

foreach (PropertySetBinding binding in propertySets)         // os 4 PSets nativos
{
    PropertySet ps = TryOpenReportPropertySet(tr, entity, binding, warnings);
    if (ps == null) continue;

    foreach (string propName in GetReportPropertyNames(binding, warnings))
    {
        string key = binding.Name + "." + propName;          // ex.: "Corridor Identity.CorridorName"
        if (!row.Values.ContainsKey(key))
            row.Values[key] = TryReadPropertyValue(ps, entity, propName);
    }
}
```

Cada coluna de dado é nomeada `"<NomeDoPSet>.<NomeDaPropriedade>"`. A
leitura é tolerante: `TryReadPropertyValue` tenta `GetAt(id, host)` e, se
falhar, `GetAt(id)`, devolvendo `""` em último caso — nunca quebra a linha.

### 6.2. Cabeçalho dinâmico e escrita

Como PSets diferentes têm propriedades diferentes, o cabeçalho é a **união**
de todas as chaves vistas em todas as linhas:

```csharp
List<string> header = new() { "Corridor", "EntityType", "Handle", "Layer" };
header.AddRange(rows.SelectMany(r => r.Values.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

StringBuilder sb = new();
sb.AppendLine(string.Join(";", header.Select(EscapeCsv)));
foreach (ReportRow row in rows)
{
    var cells = header.Select(col => col switch
    {
        "Corridor"   => EscapeCsv(row.Corridor),
        "EntityType" => EscapeCsv(row.EntityType),
        "Handle"     => EscapeCsv(row.Handle),
        "Layer"      => EscapeCsv(row.Layer),
        _ => EscapeCsv(row.Values.TryGetValue(col, out var v) ? v : string.Empty),
    });
    sb.AppendLine(string.Join(";", cells));
}
File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
```

Detalhes que importam:

- **Separador `;`** (padrão de Excel em locale PT-BR), com `EscapeCsv`
  envolvendo em aspas e duplicando `"` quando o valor contém `;` ou `"`,
  além de trocar quebras de linha por espaço.
- Arquivo gravado em **UTF-8**.
- O nome padrão do CSV inclui timestamp:
  `<dwg>_RelatorioPSET_yyyyMMdd_HHmmss.csv` (`BuildDefaultCsvPath`).

---

## Pós-processamento: gravar no DWG destino

Depois de processar tudo (e gerar o CSV), os sólidos coletados são
**clonados para o DWG de destino** por `Civil3DObjectCopier2`, usando
`WblockCloneObjects` dentro de um `DocumentLock`:

```csharp
destDb.WblockCloneObjects(objetos, modelSpace.ObjectId, map,
                          DuplicateRecordCloning.Replace, false);
destTr.Commit();
destDb.SaveAs(alvo, DwgVersion.Current);
```

Se o DWG de destino não existir, ele é criado na hora. Por fim, se a opção
`RemoveSourceSolidsAfterCopy` estiver ligada (padrão **true** por decisão de
UX — a cópia já foi feita), os sólidos temporários são apagados do desenho
ativo por `ExclusaoObjetos.ApagarSolid3d`.

O `ExportacaoSolidosCorredoresResult.BuildSummary()` então reporta:
corredores processados, sólidos e bodies exportados, caminho do DWG, caminho
do CSV e até 5 avisos (PSets, CodeNames não mapeados, falhas pontuais).

---

## Resumo do fluxo de dados

| Etapa | Entrada | Saída |
|---|---|---|
| 1. Varredura | `CorridorCollection` do desenho | lista de corredores + status dos PSets (janela) |
| 2. Extração | corredores + códigos selecionados | `Solid3d`/`Body` em memória |
| 3. PSETs | sólido + `Pset_B` (CodeName, GUID, corredor) | subassembly de origem identificada |
| 4. Espessuras | subassembly + parâmetros | `Pset_C` físico (largura, altura, área, inclinação) |
| 5. Tradução | CodeName bruto | categoria canônica + JSON persistido |
| 6. CSV | PSets nativos lidos de volta | relatório `.csv` (`;`, UTF-8) |
| Pós | sólidos coletados | DWG destino + limpeza opcional |

## Onde mexer com segurança

- **Novo material/camada?** Adicione o de-para em
  `CodeNameMappingCatalog.BuiltInDirectMappings` (ou via JSON), e — se for
  uma categoria nova — crie a regra correspondente em `PSetSolid`
  (bloco `CodeMatches(...)`) e o reconhecimento de espessura em `MapDouble`.
- **Novo parâmetro de espessura?** Acrescente o nome em `MapDouble` e o
  campo em `ExtractedValues`; ajuste `GetParameterSignatureScore` /
  `HasEnoughValuesForCategory` para a categoria pontuar corretamente.
- **Nova coluna no CSV?** Como o cabeçalho é dinâmico, basta a propriedade
  existir no PSet nativo; nada a fazer no `ExportReportToCsv`.

> ℹ️ Apesar do nome da pasta (`...NovaInterfaceLogicaAntiga`), no
> `AutomacoesCivil3D.csproj` ativo **apenas** `COPIARCORREDOR.cs` é removido
> do build (`<Compile Remove="PastaSolidosCorredoresNovaInterfaceLogicaAntiga\COPIARCORREDOR.cs" />`).
> Todos os arquivos descritos neste tutorial (service, models, `PropertySets.cs`,
> catálogo de CodeNames, copier, etc.) **fazem parte da compilação** — ou seja,
> esta é a lógica ativa da automação.
</content>
</invoke>
