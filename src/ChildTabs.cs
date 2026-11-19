using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace RoxyzsActPlugin
{
    internal static class ChildTabs
    {
        static readonly HashSet<string> management = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Update","Updates","更新","업데이트","Support","支援","支持","후원","Language","言語","语言","언어" };

        internal static void HideManagementPages(Control root)
        {
            var observed=new HashSet<Control>();
            var detached=new Dictionary<TabPage,TabControl>();
            Action<Control> observe=null;
            Action<TabPage> hide=page=>{
                var owner=page.Parent as TabControl;
                if(owner==null||!management.Contains(page.Text.Trim()))return;
                detached[page]=owner;
                owner.TabPages.Remove(page);
                if(owner.TabPages.Count>0&&owner.SelectedIndex<0)owner.SelectedIndex=0;
            };
            observe=control=>{
                if(observed.Add(control))
                {
                    control.ControlAdded+=(s,e)=>observe(e.Control);
                    control.Disposed+=(s,e)=>{
                        observed.Remove(control);
                        if(control is TabPage disposed)detached.Remove(disposed);
                        if(control is TabControl owner)
                            foreach(var hidden in detached.Where(pair=>pair.Value==owner).Select(pair=>pair.Key).ToArray())
                                hidden.Dispose();
                    };
                    if(control is TabPage page)page.TextChanged+=(s,e)=>hide(page);
                }
                foreach(var child in control.Controls.Cast<Control>().ToArray())observe(child);
                if(control is TabPage candidate)hide(candidate);
            };
            // Only traverse this child's settings surface, never the ACT or portal tab strip.
            foreach(var child in root.Controls.Cast<Control>().ToArray())observe(child);
            root.ControlAdded+=(s,e)=>observe(e.Control);
            root.Disposed+=(s,e)=>{foreach(var page in detached.Keys.ToArray())page.Dispose();};
        }
    }
}
