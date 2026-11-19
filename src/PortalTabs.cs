using Advanced_Combat_Tracker;
using Roxyz.Act.UI;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RoxyzsActPlugin
{
    public sealed partial class Portal
    {
        void ApplyLanguage(IActPluginV1 plugin, TabPage page)
        {
            var code = PluginLanguagePage.Codes[prefs.Language];
            var setter = plugin.GetType().GetMethod("SetHostedLanguage", new[] { typeof(string) });
            if (setter != null && !setter.IsStatic && setter.ReturnType == typeof(void))
            {
                setter.Invoke(plugin, new object[] { code });
                return;
            }
            // Older plugins can rebuild the selector synchronously when its value changes.
            if (ApplyLegacyLanguage(page)) ApplyLegacyLanguage(page);
        }

        bool ApplyLegacyLanguage(Control parent)
        {
            bool changed = false;
            foreach (Control child in parent.Controls.Cast<Control>().ToArray())
            {
                if (child.IsDisposed) continue;
                if (child is ComboBox combo && combo.Items.Count == 4)
                {
                    var options = combo.Items.Cast<object>().Select(combo.GetItemText).ToArray();
                    int index = Array.IndexOf(options, PluginLanguagePage.Names[prefs.Language]);
                    if (!PluginLanguagePage.Names.All(options.Contains))
                    {
                        var codes = combo.Items.Cast<object>().Select(option =>
                            option?.GetType().GetProperty("Code")?.GetValue(option) as string).ToArray();
                        index = PluginLanguagePage.Codes.All(codes.Contains)
                            ? Array.IndexOf(codes, PluginLanguagePage.Codes[prefs.Language]) : -1;
                    }
                    if (index >= 0)
                    {
                        combo.Enabled = false;
                        if (combo.SelectedIndex != index) { combo.SelectedIndex = index; changed = true; }
                    }
                }
                if (!child.IsDisposed) changed |= ApplyLegacyLanguage(child);
            }
            return changed;
        }

        string TabName(Entry item) => prefs.TabNames.TryGetValue(item.Repo, out var name) &&
            !string.IsNullOrWhiteSpace(name) ? name : item.Name;

        void ClearHighlights()
        {
            updatedRepos.Clear();
            RefreshList();
        }

        void AddRenameControls(Control bar)
        {
            var caption = L("Tab name", "タブ表示名", "标签名称", "탭 이름");
            bar.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0,7,0,0) });
            var input = new TextBox { Width = 140, MaxLength = 80, AccessibleName = caption };
            bar.Controls.Add(input);
            list.SelectedIndexChanged += (s,e) => input.Text = Selected == null ? "" : TabName(Selected);
            var apply = Button(bar,L("Apply name","名前を適用","应用名称","이름 적용"),()=>{
                var item = Selected;
                if (item == null) return;
                SetTabName(item,input.Text);
                input.Text = TabName(item);
            });
            input.KeyDown += (s,e)=>{
                if (e.KeyCode == Keys.Enter) { apply.PerformClick(); e.SuppressKeyPress = true; }
            };
        }

        void SetTabName(Entry item,string value)
        {
            var name = value.Trim();
            if (name.Length == 0 || name == item.Name) prefs.TabNames.Remove(item.Repo);
            else prefs.TabNames[item.Repo] = name;
            var displayName = TabName(item);
            if (active.TryGetValue(item,out var slot)) slot.Item2.Text = displayName;
            foreach (ListViewItem row in list.Items)
                if (item.Equals(row.Tag)) { row.Text = displayName; break; }
        }

        void InsertChildPage(Entry item,TabPage page)
        {
            int position = prefs.TabOrder.IndexOf(item.Repo);
            if (position >= 0)
            {
                foreach (TabPage existing in tabs.TabPages)
                {
                    var other = existing.Tag as Entry;
                    if (other == null) continue;
                    int index = prefs.TabOrder.IndexOf(other.Repo);
                    if (index >= 0 && index < position) continue;
                    page.Tag = item;
                    tabs.InsertContentPage(tabs.TabPages.IndexOf(existing),page);
                    return;
                }
            }
            page.Tag = item;
            tabs.AddContentPage(page);
        }

        Entry ChildAt(Point point)
        {
            for (int i=0; i<tabs.TabCount; i++)
                if (tabs.GetTabRect(i).Contains(point)) return tabs.TabPages[i].Tag as Entry;
            return null;
        }

        void EnableTabDragging()
        {
            bool dragging = false;
            tabs.MouseDown += (s,e)=>{
                dragEntry = e.Button == MouseButtons.Left ? ChildAt(e.Location) : null;
                dragOrigin = e.Location;
                dragging = false;
            };
            tabs.MouseMove += (s,e)=>{
                if (dragEntry == null || e.Button != MouseButtons.Left) return;
                var size = SystemInformation.DragSize;
                var threshold = new Rectangle(dragOrigin.X-size.Width/2,dragOrigin.Y-size.Height/2,size.Width,size.Height);
                if (!threshold.Contains(e.Location)) { dragging = true; tabs.Capture = true; }
            };
            tabs.MouseUp += (s,e)=>{
                var source = dragEntry;
                dragEntry = null;
                var target = ChildAt(e.Location);
                if (dragging && e.Button == MouseButtons.Left && source != null && target != null && source != target)
                    MoveChild(source,target);
                dragging = false;
                tabs.Capture = false;
            };
        }

        void MoveChild(Entry source,Entry target)
        {
            if (!active.TryGetValue(source,out var from) || !active.TryGetValue(target,out var to)) return;
            foreach (TabPage page in tabs.TabPages)
                if (page.Tag is Entry item && !prefs.TabOrder.Contains(item.Repo)) prefs.TabOrder.Add(item.Repo);
            int destination = prefs.TabOrder.IndexOf(target.Repo);
            prefs.TabOrder.Remove(source.Repo);
            prefs.TabOrder.Insert(destination,source.Repo);
            var selection = tabs.SelectedTab;
            int index = tabs.TabPages.IndexOf(to.Item2);
            tabs.TabPages.Remove(from.Item2);
            tabs.InsertContentPage(index,from.Item2);
            tabs.SelectedTab = selection;
        }
    }
}
