using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;            // PaletteSet
using Autodesk.Windows;                    // Ribbon
using Assembly = System.Reflection.Assembly;



namespace AutomacoesCivil3D.PastaSolidosCorredores
{


    internal sealed class CommandInfo
    {
        public string Name;        // nome do comando (GlobalName)
        public string Group;       // GroupName do CommandMethod, senão namespace
        public string Help;        // DescriptionAttribute se existir
        public Bitmap Icon16;
        public Bitmap Icon32;
    }

    public class UiLauncher
    {
        private static Autodesk.AutoCAD.Windows.PaletteSet _palette;
        private static ToolspaceControl _control;
        private static readonly Guid PaletteGuid = new Guid("8940F3E5-9A9F-4E77-9B3D-5C1BBB7A8B2E");

        // ======= Comandos =======
        [CommandMethod("RP_UI")]
        public void ShowUi()
        {
            EnsurePalette();
            _palette.Visible = true;
        }

        [CommandMethod("RP_UI_REFRESH")]
        public void RefreshUi()
        {
            EnsurePalette(rescan: true);
            _palette.Visible = true;
        }

        [CommandMethod("RP_RIBBON")]
        public void BuildRibbon()
        {
            var cmds = DiscoverCommands();
            BuildOrUpdateRibbon(cmds);
        }

        // ======= Infra =======
        private static void EnsurePalette(bool rescan = false)
        {
            if (_palette == null)
            {
                _palette = new Autodesk.AutoCAD.Windows.PaletteSet("AUTOMAÇÕES C3D", PaletteGuid)
                {
                    Style = PaletteSetStyles.ShowCloseButton
                          | PaletteSetStyles.ShowPropertiesMenu
                          | PaletteSetStyles.NameEditable,
                    MinimumSize = new Size(260, 300)
                };
            }

            if (_control == null || rescan)
            {
                var cmds = DiscoverCommands();
                var newCtl = new ToolspaceControl(cmds);
                if (_control == null)
                {
                    _control = newCtl;
                    _palette.Add("Painel", _control);
                }
                else
                {
                    // substitui a page 0
                    _palette.Remove(0);
                    _control.Dispose();
                    _control = newCtl;
                    _palette.Add("Painel", _control);
                }
            }
        }

        private static List<CommandInfo> DiscoverCommands()
        {
            var list = new List<CommandInfo>();

            // Assemblies “da solução”: mesma pasta do assembly atual
            var execAsm = Assembly.GetExecutingAssembly();
            string baseDir = Path.GetDirectoryName(execAsm.Location) ?? "";

            var candidateAsms = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a =>
                {
                    try
                    {
                        var loc = a.Location;
                        return !string.IsNullOrEmpty(loc) && Path.GetDirectoryName(loc)?.Equals(baseDir, StringComparison.OrdinalIgnoreCase) == true;
                    }
                    catch { return false; }
                })
                .Distinct()
                .ToList();

            foreach (var asm in candidateAsms)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    MethodInfo[] methods;
                    try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance); }
                    catch { continue; }

                    foreach (var m in methods)
                    {
                        var attrs = m.GetCustomAttributes(typeof(CommandMethodAttribute), true) as CommandMethodAttribute[];
                        if (attrs == null || attrs.Length == 0) continue;

                        foreach (var a in attrs)
                        {
                            string name = SafeGet(a, "GlobalName") ?? SafeGetCtorFirstArg(a) ?? m.Name;
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            string group = SafeGet(a, "GroupName");
                            if (string.IsNullOrWhiteSpace(group))
                                group = t.Namespace ?? "Geral";

                            string help = m.GetCustomAttributes(typeof(DescriptionAttribute), true)
                                           .OfType<DescriptionAttribute>()
                                           .FirstOrDefault()?.Description ?? "";

                            var icon16 = TryLoadPng(asm, $"{name}_16.png") ?? TryLoadPng(asm, "default16.png") ?? MakeIcon(16);
                            var icon32 = TryLoadPng(asm, $"{name}_32.png") ?? TryLoadPng(asm, "default32.png") ?? MakeIcon(32);

                            list.Add(new CommandInfo
                            {
                                Name = name,
                                Group = group,
                                Help = help,
                                Icon16 = icon16,
                                Icon32 = icon32
                            });
                        }
                    }
                }
            }

            // remove duplicados pelo nome (fica o primeiro)
            return list
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Reflection helpers para compatibilidade de versões do AutoCAD
        private static string SafeGet(CommandMethodAttribute a, string propName)
        {
            try
            {
                var p = a.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (p == null) return null;
                var v = p.GetValue(a, null) as string;
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
            catch { return null; }
        }
        private static string SafeGetCtorFirstArg(CommandMethodAttribute a)
        {
            // quando atributo foi usado como [CommandMethod("NOME")]
            try
            {
                var f = typeof(CommandMethodAttribute).GetField("m_globalName", BindingFlags.NonPublic | BindingFlags.Instance);
                var s = f?.GetValue(a) as string;
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch { return null; }
        }

        private static Bitmap TryLoadPng(Assembly asm, string resName)
        {
            try
            {
                string full = asm.GetManifestResourceNames()
                                 .FirstOrDefault(n => n.EndsWith(resName, StringComparison.OrdinalIgnoreCase));
                if (full == null) return null;
                using (var s = asm.GetManifestResourceStream(full))
                    return new Bitmap(s);
            }
            catch { return null; }
        }

        private static Bitmap MakeIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(50, 90, 160));
                using var pen = new Pen(Color.White, Math.Max(2, size / 8));
                g.DrawRectangle(pen, size / 6, size / 6, size - size / 3, size - size / 3);
            }
            return bmp;
        }

        private static void BuildOrUpdateRibbon(List<CommandInfo> cmds)
        {
            try
            {
                var rc = ComponentManager.Ribbon;
                


                // Tab
                var tab = rc.Tabs.FirstOrDefault(t => t.Title == "Rotinas")
                          ?? new RibbonTab { Title = "Rotinas", Id = "RotinasPetrobrasTab" };

                if (!rc.Tabs.Contains(tab))
                    rc.Tabs.Add(tab);

                // Limpa/recria painéis
                tab.Panels.Clear();

                // Agrupa por Group
                foreach (var g in cmds.GroupBy(c => c.Group))
                {
                    var panelSrc = new RibbonPanelSource { Title = g.Key };
                    var panel = new RibbonPanel { Source = panelSrc };
                    tab.Panels.Add(panel);

                    foreach (var c in g)
                    {
                        var btn = new RibbonButton
                        {
                            Text = c.Name,                          
                            ShowText = true,
                            ShowImage = true,     
                            ToolTip = string.IsNullOrWhiteSpace(c.Help) ? c.Name : c.Help
                        };
                        btn.CommandHandler = new RibbonCmdHandler(c.Name);
                        panelSrc.Items.Add(btn);
                    }
                }
                tab.IsActive = true;
            }
            catch { /* ignora problemas de Ribbon em ambientes sem UI */ }
        }

      
    }

    internal class RibbonCmdHandler : System.Windows.Input.ICommand
    {
        private readonly string _cmd;
        public RibbonCmdHandler(string cmd) => _cmd = cmd;
        public bool CanExecute(object parameter) => true;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter) => CmdExec.Run(_cmd);
    }

    internal static class CmdExec
    {
        public static void Run(string cmd)
        {
            var doc = AutomacoesCivil3D.Manager.DocCad;
            // envia o comando como se digitado (com espaço ao final)
            doc.SendStringToExecute(cmd + " ", true, false, true);
        }
    }

    // ======= Controle do Toolspace (Palette) =======
    internal class ToolspaceControl : UserControl
    {
        private readonly List<CommandInfo> _all;
        private readonly TextBox _search;
        private readonly TabControl _tabs;

        public ToolspaceControl(List<CommandInfo> commands)
        {
            _all = commands ?? new List<CommandInfo>();
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(37, 37, 38);

            _search = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Buscar comando...",
                Margin = new Padding(6),
            };
            _search.TextChanged += (_, __) => RebuildTabs();

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.Normal
            };

            Controls.Add(_tabs);
            Controls.Add(_search);

            RebuildTabs();
        }

        private void RebuildTabs()
        {
            var term = _search.Text?.Trim() ?? "";
            var list = string.IsNullOrEmpty(term)
                ? _all
                : _all.Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                               || c.Group.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

            _tabs.SuspendLayout();
            _tabs.TabPages.Clear();

            foreach (var g in list.GroupBy(c => c.Group)
                                  .OrderBy(gr => gr.Key, StringComparer.OrdinalIgnoreCase))
            {
                var page = new TabPage(g.Key);
                var panel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    WrapContents = true
                };

                foreach (var c in g.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var btn = MakeButton(c);
                    panel.Controls.Add(btn);
                }

                page.Controls.Add(panel);
                _tabs.TabPages.Add(page);
            }
            _tabs.ResumeLayout();
        }

        private Control MakeButton(CommandInfo c)
        {
            var btn = new Button
            {
                Text = c.Name,
                TextAlign = ContentAlignment.BottomCenter,
                ImageAlign = ContentAlignment.TopCenter,
                Size = new Size(110, 90),
                Margin = new Padding(6),
                Tag = c.Name
            };
            if (c.Icon32 != null) btn.Image = c.Icon32;
            var tip = new ToolTip();
            tip.SetToolTip(btn, string.IsNullOrWhiteSpace(c.Help) ? c.Name : c.Help);

            btn.Click += (_, __) => CmdExec.Run(c.Name);
            return btn;
        }
    }
}
