using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace AutomacoesCivil3D
{
    public class SolidosPatchSbdQtoSmecEstruturado
    {
        private const string TargetConstructorName = "TUBO DE FERRO FUNDIDO CLASSE K-7";
        private const int RootActivitiesParentId = 2;
        private const string QtoSequenceDisplayName = "QTO SMEC";

        [CommandMethod("SOL_PATCH_SBD_QTO_SMEC_ESTRUTURADO")]
        public void PatchSbdQtoSmecEstruturado()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            try
            {
                string arquivoEntrada = SelecionarArquivoEntrada();
                if (string.IsNullOrWhiteSpace(arquivoEntrada))
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                string arquivoSaida = SelecionarArquivoSaida(arquivoEntrada);
                if (string.IsNullOrWhiteSpace(arquivoSaida))
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                string arquivoLog = Path.ChangeExtension(arquivoSaida, ".log.txt");

                bool alterou = ExecutarPatch(arquivoEntrada, arquivoSaida, arquivoLog);

                if (alterou)
                {
                    docEditor.WriteMessage("\nPatch aplicado com sucesso.");
                    docEditor.WriteMessage("\nArquivo gerado: " + arquivoSaida);
                    docEditor.WriteMessage("\nLog: " + arquivoLog);
                }
                else
                {
                    docEditor.WriteMessage("\nNenhuma alteração foi aplicada.");
                    docEditor.WriteMessage("\nVeja o log: " + arquivoLog);
                }
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage("\nErro AutoCAD: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage("\nErro: " + ex.Message);
            }
        }

        private static string SelecionarArquivoEntrada()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Selecione o arquivo SBD";
            openFileDialog.Filter = "Arquivo de Seção (*.sbd)|*.sbd|Arquivo de Seção (*.secb)|*.secb|Todos os arquivos (*.*)|*.*";
            openFileDialog.Multiselect = false;

            DialogResult dialogResult = openFileDialog.ShowDialog();
            if (dialogResult != DialogResult.OK)
            {
                return string.Empty;
            }

            return openFileDialog.FileName;
        }

        private static string SelecionarArquivoSaida(string arquivoEntrada)
        {
            string diretorio = Path.GetDirectoryName(arquivoEntrada);
            string nomeBase = Path.GetFileNameWithoutExtension(arquivoEntrada);
            string extensao = Path.GetExtension(arquivoEntrada);

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "Salvar arquivo SBD alterado";
            saveFileDialog.Filter = "Arquivo de Seção (*.sbd)|*.sbd|Arquivo de Seção (*.secb)|*.secb|Todos os arquivos (*.*)|*.*";
            saveFileDialog.FileName = nomeBase + "_QTO_SMEC" + extensao;
            saveFileDialog.InitialDirectory = diretorio;

            DialogResult dialogResult = saveFileDialog.ShowDialog();
            if (dialogResult != DialogResult.OK)
            {
                return string.Empty;
            }

            return saveFileDialog.FileName;
        }

        private static bool ExecutarPatch(string arquivoEntrada, string arquivoSaida, string arquivoLog)
        {
            List<string> linhasLog = new List<string>();
            Database database = new Database(false, true);

            try
            {
                database.ReadDwgFile(arquivoEntrada, FileOpenMode.OpenForReadAndAllShare, false, string.Empty);
                database.CloseInput(true);

                using (Transaction transCad = database.TransactionManager.StartTransaction())
                {
                    DBDictionary nod = (DBDictionary)transCad.GetObject(database.NamedObjectsDictionaryId, OpenMode.ForRead);
                    Xrecord targetXrecord = LocalizarConstrutor(nod, transCad, linhasLog);

                    if (targetXrecord == null)
                    {
                        linhasLog.Add("Construtor alvo não encontrado.");
                        transCad.Abort();
                        File.WriteAllLines(arquivoLog, linhasLog, Encoding.UTF8);
                        return false;
                    }

                    TypedValue[] typedValuesOriginais = targetXrecord.Data.AsArray();
                    List<GroupInfo> grupos = ExtrairGrupos(typedValuesOriginais);

                    if (grupos.Count == 0)
                    {
                        linhasLog.Add("Nenhum grupo SOLIDOS foi encontrado no Xrecord.");
                        transCad.Abort();
                        File.WriteAllLines(arquivoLog, linhasLog, Encoding.UTF8);
                        return false;
                    }

                    int maxId = ObterMaiorId(grupos);
                    int nextId = maxId + 1;
                    int nextTopLevelSequenceIndex = ObterProximoSequenceIndexRaiz(grupos, RootActivitiesParentId);

                    linhasLog.Add("Maior id encontrado: " + maxId.ToString(CultureInfo.InvariantCulture));
                    linhasLog.Add("Próximo id: " + nextId.ToString(CultureInfo.InvariantCulture));
                    linhasLog.Add("Próximo sequenceindex raiz: " + nextTopLevelSequenceIndex.ToString(CultureInfo.InvariantCulture));

                    List<int> gruposParaRemover = ObterIndicesGruposQtoExistentes(grupos, linhasLog);

                    List<TypedValue> novosTypedValues = new List<TypedValue>();
                    int ultimoIndiceGrupoAtividadeMantido = -1;
                    int i;

                    for (i = 0; i < grupos.Count; i++)
                    {
                        if (gruposParaRemover.Contains(i))
                        {
                            continue;
                        }

                        GroupInfo groupInfo = grupos[i];
                        novosTypedValues.AddRange(groupInfo.TypedValues);

                        if (groupInfo.GroupName.StartsWith("SOLIDOS.Activity", StringComparison.Ordinal))
                        {
                            ultimoIndiceGrupoAtividadeMantido = novosTypedValues.Count;
                        }
                    }

                    List<TypedValue> blocoQto = CriarBlocoQto(ref nextId, nextTopLevelSequenceIndex, RootActivitiesParentId);
                    linhasLog.Add("Quantidade de TypedValues do bloco novo: " + blocoQto.Count.ToString(CultureInfo.InvariantCulture));

                    if (ultimoIndiceGrupoAtividadeMantido < 0)
                    {
                        linhasLog.Add("Não foi encontrado bloco de atividade para servir como ponto de inserção.");
                        transCad.Abort();
                        File.WriteAllLines(arquivoLog, linhasLog, Encoding.UTF8);
                        return false;
                    }

                    novosTypedValues.InsertRange(ultimoIndiceGrupoAtividadeMantido, blocoQto);

                    targetXrecord.UpgradeOpen();
                    targetXrecord.Data = new ResultBuffer(novosTypedValues.ToArray());
                    targetXrecord.DowngradeOpen();

                    transCad.Commit();
                    database.SaveAs(arquivoSaida, DwgVersion.Current);

                    linhasLog.Add("Patch aplicado com sucesso.");
                    File.WriteAllLines(arquivoLog, linhasLog, Encoding.UTF8);
                    return true;
                }
            }
            finally
            {
                database.Dispose();
            }
        }

        private static Xrecord LocalizarConstrutor(DBDictionary nod, Transaction transCad, List<string> linhasLog)
        {
            DBDictionary solidosDictionary = null;

            foreach (DBDictionaryEntry dbDictionaryEntry in nod)
            {
                if (!string.Equals(dbDictionaryEntry.Key, "SOLIDOS_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                solidosDictionary = (DBDictionary)transCad.GetObject(dbDictionaryEntry.Value, OpenMode.ForRead);
                break;
            }

            if (solidosDictionary == null)
            {
                linhasLog.Add("Dicionário SOLIDOS_ não encontrado.");
                return null;
            }

            foreach (DBDictionaryEntry dbDictionaryEntry in solidosDictionary)
            {
                if (!dbDictionaryEntry.Key.StartsWith("SolGravityPointConstructor_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Xrecord xrecord = transCad.GetObject(dbDictionaryEntry.Value, OpenMode.ForRead) as Xrecord;
                if (xrecord == null || xrecord.Data == null)
                {
                    continue;
                }

                TypedValue[] typedValues = xrecord.Data.AsArray();
                string nome = ObterValorStringPorChave(typedValues, "Name|System.String");
                string tipo = ObterPrimeiraString(typedValues, 1000);

                linhasLog.Add("Inspecionando: " + dbDictionaryEntry.Key + " | Name=" + nome);

                if (string.Equals(nome, TargetConstructorName, StringComparison.OrdinalIgnoreCase))
                {
                    linhasLog.Add("Construtor alvo localizado em: " + dbDictionaryEntry.Key);
                    return xrecord;
                }
            }

            return null;
        }

        private static string ObterPrimeiraString(TypedValue[] typedValues, int typeCode)
        {
            int i;
            for (i = 0; i < typedValues.Length; i++)
            {
                if (typedValues[i].TypeCode == typeCode && typedValues[i].Value is string)
                {
                    return (string)typedValues[i].Value;
                }
            }

            return string.Empty;
        }

        private static string ObterValorStringPorChave(TypedValue[] typedValues, string chave)
        {
            int i;
            for (i = 0; i < typedValues.Length - 1; i++)
            {
                if (typedValues[i].TypeCode == 1000 &&
                    typedValues[i].Value is string &&
                    string.Equals((string)typedValues[i].Value, chave, StringComparison.Ordinal))
                {
                    TypedValue typedValueSeguinte = typedValues[i + 1];
                    if (typedValueSeguinte.Value is string)
                    {
                        return (string)typedValueSeguinte.Value;
                    }
                }
            }

            return string.Empty;
        }

        private static List<GroupInfo> ExtrairGrupos(TypedValue[] typedValues)
        {
            List<GroupInfo> grupos = new List<GroupInfo>();
            int i = 0;

            while (i < typedValues.Length)
            {
                if (typedValues[i].TypeCode == 102 &&
                    typedValues[i].Value is string &&
                    ((string)typedValues[i].Value).StartsWith("{SOLIDOS.", StringComparison.Ordinal))
                {
                    int inicio = i;
                    int fim = -1;
                    int j;

                    for (j = i + 1; j < typedValues.Length; j++)
                    {
                        if (typedValues[j].TypeCode == 102 &&
                            typedValues[j].Value is string &&
                            string.Equals((string)typedValues[j].Value, "}", StringComparison.Ordinal))
                        {
                            fim = j;
                            break;
                        }
                    }

                    if (fim < 0)
                    {
                        break;
                    }

                    List<TypedValue> grupoTypedValues = new List<TypedValue>();
                    int k;
                    for (k = inicio; k <= fim; k++)
                    {
                        grupoTypedValues.Add(typedValues[k]);
                    }

                    GroupInfo groupInfo = new GroupInfo();
                    groupInfo.StartIndex = inicio;
                    groupInfo.EndIndex = fim;
                    groupInfo.TypedValues = grupoTypedValues;
                    groupInfo.GroupName = ((string)typedValues[inicio].Value).Substring(1);

                    groupInfo.Id = ObterIntDoGrupo(grupoTypedValues, "id");
                    groupInfo.ParentId = ObterIntDoGrupo(grupoTypedValues, "parentid");
                    groupInfo.SequenceIndex = ObterIntDoGrupo(grupoTypedValues, "sequenceindex");
                    groupInfo.DisplayName = ObterStringDoGrupo(grupoTypedValues, "DisplayName|System.String");

                    grupos.Add(groupInfo);
                    i = fim + 1;
                    continue;
                }

                i++;
            }

            return grupos;
        }

        private static int ObterIntDoGrupo(List<TypedValue> grupo, string chave)
        {
            int i;
            for (i = 0; i < grupo.Count - 1; i++)
            {
                if (grupo[i].TypeCode == 1000 &&
                    grupo[i].Value is string &&
                    string.Equals((string)grupo[i].Value, chave, StringComparison.Ordinal))
                {
                    TypedValue typedValueValor = grupo[i + 1];

                    if (typedValueValor.TypeCode == 90 && typedValueValor.Value is int)
                    {
                        return (int)typedValueValor.Value;
                    }

                    if (typedValueValor.TypeCode == 70 && typedValueValor.Value is short)
                    {
                        return (short)typedValueValor.Value;
                    }
                }
            }

            return -1;
        }

        private static string ObterStringDoGrupo(List<TypedValue> grupo, string chave)
        {
            int i;
            for (i = 0; i < grupo.Count - 1; i++)
            {
                if (grupo[i].TypeCode == 1000 &&
                    grupo[i].Value is string &&
                    string.Equals((string)grupo[i].Value, chave, StringComparison.Ordinal))
                {
                    TypedValue typedValueValor = grupo[i + 1];
                    if (typedValueValor.Value is string)
                    {
                        return (string)typedValueValor.Value;
                    }
                }
            }

            return string.Empty;
        }

        private static int ObterMaiorId(List<GroupInfo> grupos)
        {
            int maxId = 0;
            int i;

            for (i = 0; i < grupos.Count; i++)
            {
                if (grupos[i].Id > maxId)
                {
                    maxId = grupos[i].Id;
                }
            }

            return maxId;
        }

        private static int ObterProximoSequenceIndexRaiz(List<GroupInfo> grupos, int parentIdRaiz)
        {
            int maiorSequenceIndex = -1;
            int i;

            for (i = 0; i < grupos.Count; i++)
            {
                if (!string.Equals(grupos[i].GroupName, "SOLIDOS.ActivitySequence", StringComparison.Ordinal))
                {
                    continue;
                }

                if (grupos[i].ParentId != parentIdRaiz)
                {
                    continue;
                }

                if (grupos[i].SequenceIndex > maiorSequenceIndex)
                {
                    maiorSequenceIndex = grupos[i].SequenceIndex;
                }
            }

            return maiorSequenceIndex + 1;
        }

        private static List<int> ObterIndicesGruposQtoExistentes(List<GroupInfo> grupos, List<string> linhasLog)
        {
            List<int> indicesParaRemover = new List<int>();
            int qtoSequenceId = -1;
            int i;

            for (i = 0; i < grupos.Count; i++)
            {
                if (!string.Equals(grupos[i].GroupName, "SOLIDOS.ActivitySequence", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(grupos[i].DisplayName, QtoSequenceDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    qtoSequenceId = grupos[i].Id;
                    indicesParaRemover.Add(i);
                    linhasLog.Add("QTO SMEC existente encontrado. SequenceId=" + qtoSequenceId.ToString(CultureInfo.InvariantCulture));
                    break;
                }
            }

            if (qtoSequenceId < 0)
            {
                return indicesParaRemover;
            }

            for (i = 0; i < grupos.Count; i++)
            {
                if (grupos[i].ParentId == qtoSequenceId)
                {
                    indicesParaRemover.Add(i);
                }
            }

            return indicesParaRemover;
        }

        private static List<TypedValue> CriarBlocoQto(ref int nextId, int sequenceIndexRaiz, int parentIdRaiz)
        {
            List<TypedValue> typedValues = new List<TypedValue>();

            int sequenceId = nextId++;
            int sequenceBoxId = nextId++;

            typedValues.AddRange(CriarGroupActivitySequence(sequenceId, parentIdRaiz, sequenceIndexRaiz, "80,780", QtoSequenceDisplayName, string.Empty));
            typedValues.AddRange(CriarGroupActivitySequenceBox(sequenceBoxId, sequenceId, -1, string.Empty, QtoSequenceDisplayName, string.Empty));

            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 0, "16.103611898395002,15.147769184374141", "StartTopElevation", "Points(0).Z"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 1, "16.103611898395002,45.14776918437417", "StartInvertElevation", "Points(0).Z-AlturaInicial"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 2, "16.103611898395002,75.14776918437417", "EndTopElevation", "Points(Points.Count -1 ).Z"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 3, "16.103611898395002,105.14776918437417", "EndInvertElevation", "Points(Points.Count -1 ).Z-AlturaSaida"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 4, "20,30", "LargExt", "2*Parede+Largura"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 5, "20,60", "LargVala", "If(LargExt<=0.4,0.8,If(LargExt>0.8,LargExt+0.4,LargExt+0.6))"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 6, "20,90", "EspConcMagro", "0.05"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 7, "20,120", "ProfValaM", "StartTopElevation-StartInvertElevation+Parede+EspConcMagro"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 8, "20,150", "ProfValaJ", "EndTopElevation-EndInvertElevation+Parede+EspConcMagro"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 9, "20,180", "SecValaM", "If(ProfValaM<=1.25,LargVala*ProfValaM,(LargVala+ProfValaM)*ProfValaM)"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 10, "20,210", "SecValaJ", "If(ProfValaJ<=1.25,LargVala*ProfValaJ,ProfValaJ*(LargVala+ProfValaJ))"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 11, "20,240", "AltMedCanal", "((StartTopElevation-StartInvertElevation+Parede)+(EndTopElevation-EndInvertElevation+Parede))/2"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 12, "20,270", "AreaApiloam", "Comprimento*LargVala"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 13, "20,300", "VolEscav", "((SecValaM+SecValaJ)/2)*Comprimento"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 14, "20,330", "VolConcMagro", "(LargExt+0.1)*EspConcMagro*Comprimento"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 15, "20,360", "VolCanal", "AltMedCanal*LargExt*Comprimento"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 16, "20,390", "VolReaterro", "If(VolEscav-VolConcMagro-VolCanal<0,0,VolEscav-VolConcMagro-VolCanal)"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 17, "20,420", "MassaEspBF", "1.8"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 18, "20,450", "VolBotaFora", "If(VolEscav-VolReaterro<0,0,VolEscav-VolReaterro)"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 19, "20,480", "MassaBotaFora", "VolBotaFora*MassaEspBF"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 20, "20,510", "VolConcCanal", "((LargExt*AltMedCanal)-(Largura*(AltMedCanal-Parede)))*Comprimento"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 21, "20,540", "TaxaAco", "50"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 22, "20,570", "MassaAco", "VolConcCanal*TaxaAco"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 23, "20,600", "AreaForma", "(AltMedCanal*Comprimento*2)+((AltMedCanal-Parede)*Comprimento*2)"));
            typedValues.AddRange(CriarGroupActivitySetOutputParam(nextId++, sequenceId, 24, "20,630", "TaxaForma", "If(Math.Abs(VolConcCanal)<1e-8,0,AreaForma/VolConcCanal)"));

            return typedValues;
        }

        private static List<TypedValue> CriarGroupActivitySequence(int id, int parentId, int sequenceIndex, string location, string displayName, string description)
        {
            List<TypedValue> typedValues = new List<TypedValue>();

            typedValues.Add(new TypedValue(102, "{SOLIDOS.ActivitySequence"));
            typedValues.Add(new TypedValue(1000, "location|System.String"));
            typedValues.Add(new TypedValue(1, location));
            typedValues.Add(new TypedValue(1000, "DisplayName|System.String"));
            typedValues.Add(new TypedValue(1, displayName));
            typedValues.Add(new TypedValue(1000, "Description|System.String"));
            typedValues.Add(new TypedValue(1, description));
            typedValues.Add(new TypedValue(1000, "parentid"));
            typedValues.Add(new TypedValue(90, parentId));
            typedValues.Add(new TypedValue(1000, "id"));
            typedValues.Add(new TypedValue(90, id));
            typedValues.Add(new TypedValue(1000, "sequenceindex"));
            typedValues.Add(new TypedValue(90, sequenceIndex));
            typedValues.Add(new TypedValue(102, "}"));

            return typedValues;
        }

        private static List<TypedValue> CriarGroupActivitySequenceBox(int id, int parentId, int sequenceIndex, string location, string displayName, string description)
        {
            List<TypedValue> typedValues = new List<TypedValue>();

            typedValues.Add(new TypedValue(102, "{SOLIDOS.ActivitySequenceBox"));
            typedValues.Add(new TypedValue(1000, "DisplayName|System.String"));
            typedValues.Add(new TypedValue(1, displayName));
            typedValues.Add(new TypedValue(1000, "Description|System.String"));
            typedValues.Add(new TypedValue(1, description));
            typedValues.Add(new TypedValue(1000, "parentid"));
            typedValues.Add(new TypedValue(90, parentId));
            typedValues.Add(new TypedValue(1000, "id"));
            typedValues.Add(new TypedValue(90, id));
            typedValues.Add(new TypedValue(1000, "sequenceindex"));
            typedValues.Add(new TypedValue(90, sequenceIndex));
            typedValues.Add(new TypedValue(1000, "location|System.String"));
            typedValues.Add(new TypedValue(1, location));
            typedValues.Add(new TypedValue(102, "}"));

            return typedValues;
        }

        private static List<TypedValue> CriarGroupActivitySetOutputParam(int id, int parentId, int sequenceIndex, string location, string propName, string value)
        {
            List<TypedValue> typedValues = new List<TypedValue>();
            string displayName = propName + "=" + value;

            typedValues.Add(new TypedValue(102, "{SOLIDOS.ActivitySetOutPutParam"));
            typedValues.Add(new TypedValue(1000, "PropName|System.String"));
            typedValues.Add(new TypedValue(1, propName));
            typedValues.Add(new TypedValue(1000, "Value|System.String"));
            typedValues.Add(new TypedValue(1, value));
            typedValues.Add(new TypedValue(1000, "DisplayName|System.String"));
            typedValues.Add(new TypedValue(1, displayName));
            typedValues.Add(new TypedValue(1000, "Description|System.String"));
            typedValues.Add(new TypedValue(1, string.Empty));
            typedValues.Add(new TypedValue(1000, "parentid"));
            typedValues.Add(new TypedValue(90, parentId));
            typedValues.Add(new TypedValue(1000, "id"));
            typedValues.Add(new TypedValue(90, id));
            typedValues.Add(new TypedValue(1000, "sequenceindex"));
            typedValues.Add(new TypedValue(90, sequenceIndex));
            typedValues.Add(new TypedValue(1000, "location|System.String"));
            typedValues.Add(new TypedValue(1, location));
            typedValues.Add(new TypedValue(102, "}"));

            return typedValues;
        }

        private class GroupInfo
        {
            public int StartIndex;
            public int EndIndex;
            public string GroupName;
            public int Id;
            public int ParentId;
            public int SequenceIndex;
            public string DisplayName;
            public List<TypedValue> TypedValues;
        }
    }
}