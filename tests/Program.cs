using System.Collections.Generic;
using RoxyzsActPlugin;
using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;
using Advanced_Combat_Tracker;

class Program
{
    sealed class FieldRegistry { public object[] ActPlugins; }
    sealed class FieldRegistration
    {
        public FileInfo pluginFile;
        public object pluginObj;
        public CheckBox cbEnabled;
    }
    static void RegistryCompatibility(string root)
    {
        var file=new FileInfo(Path.Combine(root,"登録 プラグイン.dll"));
        var plugin=new object();
        using(var check=new CheckBox { Checked=true })
        {
            var fieldHost=new FieldRegistry { ActPlugins=new object[]{null,new FieldRegistration {pluginFile=file,pluginObj=plugin,cbEnabled=check}} };
            var field=ActRegistry.From(fieldHost).Single();
            Assert(field.Enabled&&field.File.FullName==file.FullName&&ReferenceEquals(field.Plugin,plugin),"ACT field registration");
            var propertyHost=new {ActPlugins=new[]{new {pluginFile=file,pluginObj=plugin,cbEnabled=false}}};
            var property=ActRegistry.From(propertyHost).Single();
            Assert(!property.Enabled&&property.File.FullName==file.FullName&&ReferenceEquals(property.Plugin,plugin),"ACT property registration");
        }
    }
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Field(Portal p,string name)=>typeof(Portal).GetField(name,Private).GetValue(p);
    static void Call(Portal p,string method,params object[] args)=>typeof(Portal).GetMethod(method,Private).Invoke(p,args);
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static void MeasureUpdateControls(Control control,System.Drawing.Graphics graphics)
    {
        if(!string.IsNullOrEmpty(control.Text))graphics.MeasureString(control.Text,control.Font);
        foreach(Control child in control.Controls)MeasureUpdateControls(child,graphics);
    }
    static void CheckPortalRendering(Portal portal)
    {
        var pref=(Preferences)Field(portal,"prefs");
        int original=pref.Language;
        using(var bmp=new System.Drawing.Bitmap(720,440))
        using(var g=System.Drawing.Graphics.FromImage(bmp))
        {
            foreach(var language in new[]{original,original,0,2,3,1,original})
            {
                pref.Language=language;
                Call(portal,"Build");
                var page=(Roxyz.Act.UI.PluginUpdatePage)Field(portal,"updates");
                page.SetLanguage(new[]{ "en", "ja", "zh", "ko" }[language]);
                MeasureUpdateControls(page,g);
                page.Size=bmp.Size;
                page.DrawToBitmap(bmp,page.ClientRectangle);
            }
        }
    }
    static void CheckChildTabs()
    {
        using(var root=new TabPage())
        using(var strip=new TabControl())
        {
            root.Controls.Add(strip);
            var normal=new TabPage("Display");
            var hidden=new TabPage("Updates");
            strip.TabPages.AddRange(new[]{normal,hidden,new TabPage("支援"),new TabPage("언어")});
            ChildTabs.HideManagementPages(root);
            Assert(strip.TabCount==1&&strip.TabPages[0]==normal&&!hidden.IsDisposed,"hide child management pages");
            var late=new TabPage();strip.TabPages.Add(late);late.Text="후원";
            Assert(strip.TabCount==1,"late localized management tab");
            strip.Dispose();Assert(hidden.IsDisposed&&late.IsDisposed,"hidden pages released");
        }
    }

    static void VerifyGuide(string dll,string root)
    {
        var type=Assembly.LoadFile(dll).GetType("BahamutGuide.BahamutGuidePlugin",true);
        var plugin=(IActPluginV1)Activator.CreateInstance(type);
        using(var page=new TabPage())using(var label=new Label())
        {
            try
            {
                ManagedPluginHost.Initialize(plugin,page,label,dll,root,Path.Combine(root,"GuideData"));
                Assert(page.Controls.Count>0,"Bahamut Guide settings did not open");
            }
            finally{plugin.DeInitPlugin();}
        }
        Console.WriteLine("Hosted Bahamut Guide "+AssemblyName.GetAssemblyName(dll).Version);
    }
    [STAThread] static void Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--host")
        {
            using(var signal=EventWaitHandle.OpenExisting(args[1]))signal.WaitOne(TimeSpan.FromSeconds(30));
            return;
        }
        if (!args.Contains("--child"))
        {
            var dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"portal-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                var info=new ProcessStartInfo(typeof(Program).Assembly.Location,"--child \""+dir+"\" "+string.Join(" ",args)){UseShellExecute=false};
                using(var child=Process.Start(info)){child.WaitForExit(); Environment.ExitCode=child.ExitCode;}
            }
            finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
            return;
        }
        var root=args[1];
        Directory.CreateDirectory(root);
        try
        {
            using(var packages=new Packages(Path.Combine(root,"packages")))
            {
                CheckChildTabs();
                RegistryCompatibility(root);
                UpdateTests.Run(root);
                if(args.Contains("--verify-self"))
                {
                    var release=packages.Latest(Entry.Self,CancellationToken.None,"1.1.0").GetAwaiter().GetResult();
                    packages.PrepareSelf(release,CancellationToken.None).GetAwaiter().GetResult();
                    Console.WriteLine("Verified published portal package "+release.tag_name);
                }
                foreach(var i in Entry.All)
                {
                    Assert(i.Description.Length==4,"translations");
                    try{packages.Local(i,"../1");throw new Exception("path traversal");}catch(InvalidDataException){}
                    if(Array.IndexOf(args,"--online")>=0)
                    using(var cts=new CancellationTokenSource(TimeSpan.FromSeconds(90)))
                    {
                        var r=packages.Latest(i,cts.Token).GetAwaiter().GetResult();
                        var dll=packages.Install(i,r,cts.Token).GetAwaiter().GetResult();
                        Assert(File.Exists(dll),"missing DLL");
                        if(i.Dll=="BahamutGuide")
                        {
                            VerifyGuide(dll,root);
                            var legacy=Environment.GetEnvironmentVariable("PORTAL_TEST_LEGACY_GUIDE");
                            if(!string.IsNullOrEmpty(legacy))VerifyGuide(legacy,root);
                        }
                        Console.WriteLine(i.Name+" "+r.tag_name);
                    }
                }
                var item=Entry.All[0];
                foreach (var fixture in Entry.All.Take(2))
                for(int v=1;v<=2;v++)
                {
                    var path=packages.Local(fixture,v+".0.0"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var compiler=new CSharpCodeProvider())
                    {
                        var options=new CompilerParameters(new[]{"System.dll","System.Windows.Forms.dll",typeof(IActPluginV1).Assembly.Location},path);
                        var output=compiler.CompileAssemblyFromSource(options,"using System.Windows.Forms;using Advanced_Combat_Tracker;[assembly:System.Reflection.AssemblyVersion(\""+v+".0.0.0\")] public class TestPlugin:IActPluginV1{TabPage page;public void SetHostedLanguage(string code){page.AccessibleDescription=code;} public void InitPlugin(TabPage p,Label s){page=p;s.Text=\""+v+"\";} public void DeInitPlugin(){}}");
                        Assert(!output.Errors.HasErrors,"fixture compiler");
                    }
                }
                var cfg=Path.Combine(root,"settings.json");File.WriteAllText(cfg,"{\"Language\":1,\"Check\":false,\"Enabled\":[],\"Versions\":{}}");
                if(args.Contains("--discovery"))
                {
                    var test="discovery/test_discovery.py";
                    using(var child=Process.Start(new ProcessStartInfo("python",test+" \""+packages.Local(item,"1.0.0")+"\""){UseShellExecute=false}))
                    {
                        child.WaitForExit();Assert(child.ExitCode==0,"automatic discovery");
                    }
                }
                using(var page=new TabPage())using(var label=new Label())
                {
                    page.Size=new System.Drawing.Size(900,600);
                    var p=new Portal(); typeof(Portal).GetField("root",Private).SetValue(p,root);p.InitPlugin(page,label);
                    CheckPortalRendering(p);
                    var tabs=(TabControl)Field(p,"tabs");
                    Assert(tabs.TabCount==4&&page.Text=="Roxyzs ACT Plugin","initial tabs");
                    var snapshot=File.ReadAllText(cfg);Call(p,"Save");Assert(snapshot==File.ReadAllText(cfg) && !File.Exists(cfg+".bak"),"unchanged write");
                    Call(p,"Activate",item,"1.0.0");Assert(tabs.TabCount==5,"enable");
                    Func<ListViewItem> row = () => ((ListView)Field(p,"list")).Items.Cast<ListViewItem>().Single(r=>item.Equals(r.Tag));
                    Call(p,"SetTabName",item,"試験名");
                    Assert(row().Text=="試験名","rename updates list immediately");
                    Call(p,"SetTabName",item," ");
                    Assert(row().Text==item.Name && tabs.TabPages[3].Text==item.Name,"blank restores catalog name");
                    Call(p,"SetTabName",item,"Custom");
                    Assert(tabs.TabPages[3].Text=="Custom" && tabs.TabPages[4]==Field(p,"support"),"custom name and support last");
                    Call(p,"Stop",item);Assert(tabs.TabCount==4,"stop");
                    Call(p,"Activate",item,"1.0.0");
                    var other=Entry.All[1];
                    Call(p,"Activate",other,"1.0.0");
                    var first=tabs.GetTabRect(4);
                    var second=tabs.GetTabRect(3);
                    foreach(var evt in new[]{"OnMouseDown","OnMouseMove","OnMouseUp"})
                    {
                        var rect=evt=="OnMouseDown" ? first : second;
                        typeof(Control).GetMethod(evt,Private).Invoke(tabs,new object[]{new MouseEventArgs(
                            MouseButtons.Left,1,rect.Left+rect.Width/2,rect.Top+rect.Height/2,0)});
                    }
                    Assert(tabs.TabPages[3].Text==other.Name && tabs.TabPages[4].Text=="Custom","drag order");
                    Call(p,"Stop",other);
                    Call(p,"Activate",other,"1.0.0");
                    Assert(tabs.TabPages[3].Text==other.Name && tabs.TabPages[5]==Field(p,"support"),"restore position on enable");
                    Call(p,"Stop",other);
                    ((Preferences)Field(p,"prefs")).Enabled.Remove(other.Repo);
                    ((Preferences)Field(p,"prefs")).Versions.Remove(other.Repo);
                    var languagePage=(TabPage)Field(p,"languagePage");
                    tabs.SelectedTab=languagePage;
                    var selector=(ComboBox)languagePage.Controls[0].Controls[0];
                    foreach(var index in new[]{2,0,3,1})
                    {
                        selector.SelectedIndex=index;
                        Assert(((Preferences)Field(p,"prefs")).Language==index && tabs.TabPages[3].AccessibleDescription==Roxyz.Act.UI.PluginLanguagePage.Codes[index],"child follows language page");
                        using(var image=new System.Drawing.Bitmap(500,300)){
                            languagePage.Size=image.Size;
                            languagePage.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,image.Size));
                        }
                    }
                    tabs.SelectedTab=(TabPage)Field(p,"config");
                    var net=new ChildUpdateTests(item,packages.Local(item,"2.0.0"));
                    ((Packages)Field(p,"packages")).Dispose();
                    typeof(Portal).GetField("packages",Private).SetValue(p,new Packages(Path.Combine(root,"packages"),net));
                    var currentTab=tabs.SelectedTab;
                    Call(p,"UpdateChildren",(object)new[]{Entry.All[1],item});
                    var timeout=Stopwatch.StartNew();
                    while(Field(p,"worker")!=null&&timeout.ElapsedMilliseconds<10000){Call(p,"Finish");Thread.Sleep(5);}
                    Assert(Field(p,"worker")==null&&net.AssetRequests==2&&net.HeadRequests==2,"batch continued after a failed item");
                    Assert(tabs.SelectedTab==currentTab&&tabs.TabPages[3].Controls[0].Text=="2","hot update in Config: "+label.Text);
                    Assert(((HashSet<string>)Field(p,"updatedRepos")).SetEquals(new[]{item.Repo}),"only successfully updated rows highlighted");
                    Call(p,"ClearHighlights");
                    Assert(((HashSet<string>)Field(p,"updatedRepos")).Count==0,"clear highlights");
                    Call(p,"UpdateChildren",(object)new[]{item});
                    while(Field(p,"worker")!=null&&timeout.ElapsedMilliseconds<10000){Call(p,"Finish");Thread.Sleep(5);}
                    Assert(net.AssetRequests==2&&net.HeadRequests==2,"up to date individual update reuses release check");
                    var added=new Catalog.Row{Name="New Tool",Repo="new.tool",Dll="NewTool",Prefix="NewTool",Description=new[]{"a","b","c","d"}};
                    var rows=Entry.All.Select(i=>new Catalog.Row{Name=i.Name,Repo=i.Repo,Dll=i.Dll,Prefix=i.Prefix,Description=i.Description}).Concat(new[]{added});
                    var prefs=(Preferences)Field(p,"prefs");
                    prefs.Enabled.Add(added.Repo);
                    var raw=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(rows.ToArray());
                    Call(p,"ApplyCatalog",raw,"test-etag");
                    Assert(((Entry[])Field(p,"entries")).Any(i=>i.Repo==added.Repo),"new row");
                    Assert(!prefs.Enabled.Contains(added.Repo)&&prefs.Enabled.Contains(item.Repo)&&tabs.TabCount==5,"new off, existing on");
                    p.DeInitPlugin();Assert(File.ReadAllText(cfg).Contains("2.0.0"),"final save");
                    p.InitPlugin(page,label);Assert(((TabControl)Field(p,"tabs")).TabCount==5,"restore enabled");
                    Assert(((TabControl)Field(p,"tabs")).TabPages[3].Text=="Custom" && row().Text=="Custom","restore tab and list names");
                    Assert(!((Preferences)Field(p,"prefs")).Enabled.Contains(added.Repo),"new remains off after restart");
                    p.DeInitPlugin();
                }
            }
            Console.WriteLine("Portal lifecycle and catalog tests passed.");
        }
        catch(Exception ex){for(;ex!=null;ex=ex.InnerException)Console.Error.WriteLine(ex.GetType().Name+" "+ex.Message+" "+ex.StackTrace);Environment.ExitCode=1;}
    }
}
