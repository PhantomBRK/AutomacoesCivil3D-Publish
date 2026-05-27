using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.IO;
using System.Text;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D.ifc4x3
{
    public static class IfcDrenagemPostCommand_Ifc4x3
    {
        [CommandMethod("SIFC_DRE_POST_4X3", CommandFlags.Session)]
        public static void SifcDrePost4x3()
        {
            IfcDrePostTrace.Reset();
            IfcDrePostTrace.Write("command-enter");

            Document civilDoc = Application.DocumentManager.MdiActiveDocument;
            if (civilDoc == null)
            {
                IfcDrePostTrace.Write("command-no-active-document");
                Application.ShowAlertDialog("Nao ha desenho ativo para executar o pos-processamento IFC de drenagem.");
                return;
            }

            Editor docEditor = civilDoc.Editor;

            try
            {
                string inputIfcPath = PromptIfcOpen(civilDoc, docEditor);
                if (string.IsNullOrWhiteSpace(inputIfcPath))
                {
                    IfcDrePostTrace.Write("command-cancel-open");
                    return;
                }

                string outputIfcPath = PromptIfcSave(civilDoc, docEditor, inputIfcPath);
                if (string.IsNullOrWhiteSpace(outputIfcPath))
                {
                    IfcDrePostTrace.Write("command-cancel-save");
                    return;
                }

                IfcDrePostTrace.Write("command-dispatch-worker", $"input={inputIfcPath} | output={outputIfcPath}");
                IfcDrenagemPost_Ifc4x3.RunPostProcessing(docEditor, inputIfcPath, outputIfcPath);
                IfcDrePostTrace.Write("command-success");
            }
            catch (Exception ex)
            {
                IfcDrePostTrace.Write("command-autocad-exception", FormatExceptionChain(ex));
                docEditor.WriteMessage($"\n[AutoCAD] Erro: {ex.Message}\n");
            }
            catch (System.Exception ex)
            {
                IfcDrePostTrace.Write("command-dotnet-exception", FormatExceptionChain(ex));
                docEditor.WriteMessage($"\n[.NET] Erro: {FormatExceptionChain(ex)}\n");
            }
        }

        private static string PromptIfcOpen(Document civilDoc, Editor docEditor)
        {
            try
            {
                PromptOpenFileOptions options = new PromptOpenFileOptions("\nSelecione o IFC 4x3 de drenagem (entrada):")
                {
                    Filter = "IFC (*.ifc)|*.ifc"
                };

                IfcDrePostTrace.Write("dialog-open-show", civilDoc?.Name);
                PromptFileNameResult result = docEditor.GetFileNameForOpen(options);
                string selected = result.Status == PromptStatus.OK
                    ? result.StringResult
                    : string.Empty;
                IfcDrePostTrace.Write("dialog-open-close", string.IsNullOrWhiteSpace(selected) ? "cancelled" : selected);
                return selected;
            }
            catch (System.Exception ex)
            {
                IfcDrePostTrace.Write("dialog-open-exception", FormatExceptionChain(ex));
                docEditor.WriteMessage($"\n[AVISO] Falha ao abrir dialogo de selecao de IFC: {ex.Message}");
                return string.Empty;
            }
        }

        private static string PromptIfcSave(Document civilDoc, Editor docEditor, string inputIfcPath)
        {
            try
            {
                string baseName = string.IsNullOrWhiteSpace(inputIfcPath)
                    ? "IFC_DRE_POS_PROCESSADO.ifc"
                    : Path.GetFileNameWithoutExtension(inputIfcPath) + "_POST_DRE.ifc";

                PromptSaveFileOptions options = new PromptSaveFileOptions("\nSalvar IFC 4x3 de drenagem (pos-processado):")
                {
                    Filter           = "IFC (*.ifc)|*.ifc",
                    InitialFileName  = baseName
                };

                IfcDrePostTrace.Write("dialog-save-show", inputIfcPath);
                PromptFileNameResult result = docEditor.GetFileNameForSave(options);
                string selected = result.Status == PromptStatus.OK
                    ? result.StringResult
                    : string.Empty;
                IfcDrePostTrace.Write("dialog-save-close", string.IsNullOrWhiteSpace(selected) ? "cancelled" : selected);
                return selected;
            }
            catch (System.Exception ex)
            {
                IfcDrePostTrace.Write("dialog-save-exception", FormatExceptionChain(ex));
                docEditor.WriteMessage($"\n[AVISO] Falha ao abrir dialogo para salvar IFC: {ex.Message}");
                return string.Empty;
            }
        }

        private static string FormatExceptionChain(System.Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            while (ex != null)
            {
                if (sb.Length > 0)
                    sb.Append(" | INNER: ");
                sb.Append(ex.GetType().Name);
                sb.Append(": ");
                sb.Append(ex.Message);
                ex = ex.InnerException;
            }
            return sb.ToString();
        }
    }

    internal static class IfcDrePostTrace
    {
        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutomacoesCivil3D",
            "Logs");
        private static readonly string LogPath = Path.Combine(LogDirectory, "ifc-dre-post.log");

        internal static string CurrentLogPath => LogPath;

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
                Write("trace-reset");
            }
        }

        internal static void Write(string stage, string details = null)
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = string.IsNullOrWhiteSpace(details)
                    ? $"{timestamp} | {stage}"
                    : $"{timestamp} | {stage} | {details}";
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
