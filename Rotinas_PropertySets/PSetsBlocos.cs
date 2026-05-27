using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Globalization;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace AutomacoesCivil3D
{
    public class Comandos
    {
        [CommandMethod("PsetsPlacas")]
        public static void ColetarAtributosParaPsets()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;

            string targetLayerName = "sinC_VERTICAL_BIM-implantar";

            try
            {
                using (DocumentLock docLock = civilDoc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has(targetLayerName))
                    {
                        docEditor.WriteMessage($"\nLayer '{targetLayerName}' não encontrado.");
                        tr.Abort();
                        return;
                    }

                    DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);

                    ObjectId psA = GetPsetId(dict, "A - Dados do Projeto", tr, docEditor);
                    ObjectId psB = GetPsetId(dict, "B - Informações dos Objetos e Elementos", tr, docEditor);
                    ObjectId psC = GetPsetId(dict, "C - Propriedades Fisicas dos Objetos e Elementos", tr, docEditor);
                    ObjectId psD = GetPsetId(dict, "D - Propriedades Geográficas", tr, docEditor);
                    ObjectId psE = GetPsetId(dict, "E - Requisitos Específicos de Projeto", tr, docEditor);

                    if (psA.IsNull || psB.IsNull || psC.IsNull || psD.IsNull || psE.IsNull)
                    {
                        docEditor.WriteMessage("\nDefinições de Pset ausentes. Interrompido.");
                        tr.Abort();
                        return;
                    }

                    TypedValue[] tv = new TypedValue[]
                    {
                        new TypedValue((int)DxfCode.Start, "INSERT"),
                        new TypedValue((int)DxfCode.LayerName, targetLayerName)
                    };
                    SelectionFilter sf = new SelectionFilter(tv);
                    PromptSelectionResult psr = docEditor.SelectAll(sf);
                    if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                    {
                        docEditor.WriteMessage($"\nNenhum bloco no layer '{targetLayerName}'.");
                        tr.Abort();
                        return;
                    }

                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so.ObjectId.IsNull) continue;

                        BlockReference br = (BlockReference)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (br.IsErased) continue;

                        // posição
                        string px = br.Position.X.ToString("F3", CultureInfo.InvariantCulture);
                        string py = br.Position.Y.ToString("F3", CultureInfo.InvariantCulture);
                        string pz = br.Position.Z.ToString("F3", CultureInfo.InvariantCulture);

                        // reset attrs
                        string attrName = "";
                        string attrPArea = "";
                        string attrPDim = "";
                        string attrPSuporte = "";
                        string attrPSubstrato = "";
                        string attrSAltura = "";
                        string attrSMaterial = "";

                        try
                        {
                            AttributeCollection ac = br.AttributeCollection;
                            foreach (ObjectId aId in ac)
                            {
                                AttributeReference ar = (AttributeReference)tr.GetObject(aId, OpenMode.ForRead);
                                string tag = NormalizeTag(ar.Tag);
                                if (tag == "NAME") attrName = ar.TextString;
                                else if (tag == "PAREA") attrPArea = ar.TextString;
                                else if (tag == "PDIMENSAO" || tag == "PDIMENSAOMM" || tag == "PDIAMETRO" || tag == "SDIAMETRO") attrPDim = ar.TextString;
                                else if (tag == "PSUPORTE") attrPSuporte = ar.TextString;
                                else if (tag == "PSUBSTRATO") attrPSubstrato = ar.TextString;
                                else if (tag == "SALTURA") attrSAltura = ar.TextString;
                                else if (tag == "SMATERIAL") attrSMaterial = ar.TextString;
                            }
                        }
                        catch { /* ignora blocos sem atributos */ }

                        // abre para escrita antes de anexar psets
                        br.UpgradeOpen();

                        // anexa psets com proteção
                        SafeAddPset(br, psA);
                        SafeAddPset(br, psB);
                        SafeAddPset(br, psC);
                        SafeAddPset(br, psD);
                        SafeAddPset(br, psE);

                        // B
                        ObjectId idB = PropertyDataServices.GetPropertySet(br, psB);
                        if (!idB.IsNull)
                        {
                            PropertySet pB = (PropertySet)tr.GetObject(idB, OpenMode.ForWrite);
                            SafeSet(pB, "Código_do_Objeto", attrName);
                            SafeSet(pB, "EstaqueamentoInicial", "-");
                            SafeSet(pB, "EstaqueamentoFinal", "-");
                        }

                        // C
                        ObjectId idC = PropertyDataServices.GetPropertySet(br, psC);
                        if (!idC.IsNull)
                        {
                            PropertySet pC = (PropertySet)tr.GetObject(idC, OpenMode.ForWrite);
                            SafeSet(pC, "Altura", attrSAltura);
                            SafeSet(pC, "Área", attrPArea);
                            SafeSet(pC, "Diâmetro", attrPDim);
                            // SafeSet(pC, "Suporte", attrPSuporte); // habilite se existir
                        }

                        // D
                        ObjectId idD = PropertyDataServices.GetPropertySet(br, psD);
                        if (!idD.IsNull)
                        {
                            PropertySet pD = (PropertySet)tr.GetObject(idD, OpenMode.ForWrite);
                            SafeSet(pD, "Coordenada_Eixo_X", px);
                            SafeSet(pD, "Coordenada_Eixo_Y", py);
                            SafeSet(pD, "Coordenada_Eixo_Z", pz);
                        }

                        // E
                        ObjectId idE = PropertyDataServices.GetPropertySet(br, psE);
                        if (!idE.IsNull)
                        {
                            PropertySet pE = (PropertySet)tr.GetObject(idE, OpenMode.ForWrite);
                            string mat = string.IsNullOrWhiteSpace(attrSMaterial) ? attrPSubstrato : attrSMaterial;
                            SafeSet(pE, "Material", mat);
                        }
                    }

                    tr.Commit();
                    docEditor.WriteMessage("\nConcluído sem erros fatais.");
                }
            }
            catch (System.Exception ex)
            {
                Editor logEd = Manager.DocEditor;
                logEd.WriteMessage($"\nErro: {ex.Message}");
                logEd.WriteMessage($"\nStack: {SafeStack(ex)}");
            }
        }

        private static ObjectId GetPsetId(DictionaryPropertySetDefinitions dict, string name, Transaction tr, Editor ed)
        {
            try
            {
                ObjectId id = dict.GetAt(name);
                if (!id.IsNull) return id;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPset '{name}' ausente: {ex.Message}");
            }
            return ObjectId.Null;
        }

        private static void SafeAddPset(Entity ent, ObjectId defId)
        {
            if (defId.IsNull) return;
            try
            {
                PropertyDataServices.AddPropertySet(ent, defId);
            }
            catch { /* evita crash nativo se entidade não suportar */ }
        }

        private static void SafeSet(PropertySet pset, string propName, object value)
        {
            try
            {
                int idx = pset.PropertyNameToId(propName);
                if (idx >= 0) pset.SetAt(idx, value ?? "");
            }
            catch { /* ignora propriedades inexistentes ou tipos incompatíveis */ }
        }

        private static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "";
            string s = tag.Trim().ToUpperInvariant()
                .Replace("(", "").Replace(")", "")
                .Replace("_", "").Replace("-", "").Replace(" ", "");
            string formD = s.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char ch in formD)
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string SafeStack(System.Exception ex)
        {
            string st = ex.StackTrace ?? "";
            if (st.Length > 600) st = st.Substring(0, 600);
            return st;
        }
    }
}
