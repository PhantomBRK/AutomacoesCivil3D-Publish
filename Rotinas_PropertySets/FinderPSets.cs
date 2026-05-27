// Referências necessárias no projeto:
// - Autodesk.AutoCAD.ApplicationServices
// - Autodesk.AutoCAD.DatabaseServices
// - Autodesk.AutoCAD.EditorInput
// - Autodesk.AutoCAD.Runtime
// - Autodesk.Civil.ApplicationServices
// - Autodesk.Civil.DatabaseServices
// - Autodesk.Aec.PropertyData
// - Autodesk.Aec.PropertyData.DatabaseServices
// Aliases obrigatórios e conflitos:
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace AutomacoesCivil3D
{
    public class FinderCwaPset
    {
        [CommandMethod("FINDERCWA")]
        public static void SelecionarObjetosMesmoCwa()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;

            string psetName = "Códigos AWP";
            string propName = "CWA";

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione um objeto de referência com Pset \"Códigos AWP\" -> \"CWA\":");
                peo.AllowNone = false;
                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                using (Transaction trans = db.TransactionManager.StartTransaction())
                {
                    Entity entFonte = (Entity)trans.GetObject(per.ObjectId, OpenMode.ForRead);

                    string cwaAlvo = LerValorPsetCwa(entFonte, trans, psetName, propName, db);
                    if (string.IsNullOrWhiteSpace(cwaAlvo))
                    {
                        docEditor.WriteMessage("\nValor CWA não encontrado no Pset especificado.");
                        return;
                    }

                    ObjectIdCollection idsMatch = new ObjectIdCollection();

                    BlockTable bt = (BlockTable)trans.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Entity)))) continue;

                        Entity ent = (Entity)trans.GetObject(id, OpenMode.ForRead);
                        if (ent == null) continue;

                        string cwa = LerValorPsetCwa(ent, trans, psetName, propName, db);
                        if (!string.IsNullOrWhiteSpace(cwa) &&
                            cwa.Equals(cwaAlvo, System.StringComparison.OrdinalIgnoreCase))
                        {
                            idsMatch.Add(id);
                        }
                    }

                    trans.Commit();

                    if (idsMatch.Count == 0)
                    {
                        docEditor.WriteMessage($"\nNenhum objeto com CWA \"{cwaAlvo}\" foi encontrado.");
                        return;
                    }

                    ObjectId[] arr = new ObjectId[idsMatch.Count];
                    idsMatch.CopyTo(arr, 0);
                    docEditor.SetImpliedSelection(arr);
                    docEditor.WriteMessage($"\nSelecionados {arr.Length} objeto(s) com CWA \"{cwaAlvo}\".");
                }
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\nErro: {ex.Message}");
            }
        }

        /// <summary>
        /// Lê o valor string do Pset "Códigos AWP" -> "CWA" do objeto.
        /// Ajuste as chamadas internas caso sua versão do ACA use nomes ligeiramente diferentes.
        /// </summary>
        private static string LerValorPsetCwa(Entity ent, Transaction trans, string psetName, string propName, Database db)
        {
            // Caminho 100% via PropertyData (sem layer/atributo).
            // API típica:
            // 1) PropertyDataServices.GetPropertySets(ent.ObjectId) -> ObjectIdCollection de PropertySet
            // 2) Abrir PropertySet, pegar PropertySetDefinition pelo Id
            // 3) Conferir def.Name == psetName
            // 4) Ler valor da propriedade propName
            string valor = "Nenhuma CWA Igual Encontrada";
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);

            if (dictionary.Has("Códigos AWP", trans))
            {
                ObjectId defIdB = dictionary.GetAt("Códigos AWP");
                PropertySetDefinition defB = (PropertySetDefinition)trans.GetObject(defIdB, OpenMode.ForRead);
                ObjectId psBId = PropertyDataServices.GetPropertySet(ent, defIdB);
                PropertySet psB = (PropertySet)trans.GetObject(psBId, OpenMode.ForRead);


                int pid = psB.PropertyNameToId("CWA");

               string v = psB.GetAt(pid, ent).ToString();

                return v;

            }



          
       

            return valor;
        }
    }
}
