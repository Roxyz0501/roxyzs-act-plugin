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
    static void CheckChildTabs()
    {
        using(var root=new TabPage())
        using(var strip=new TabControl())
        {
            root.Controls.Add(strip);
            var normal=new TabPage("Display");
            var hidden=new TabPage("Updates");
            strip.TabPages.AddRange(new[]{normal,hidden,new TabPage("支援")});
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
                for(int v=1;v<=2;v++)
                {
                    var path=packages.Local(item,v+".0.0"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var compiler=new CSharpCodeProvider())
                    {
                        var options=new CompilerParameters(new[]{"System.dll","System.Windows.Forms.dll",typeof(IActPluginV1).Assembly.Location},path);
                        var output=compiler.CompileAssemblyFromSource(options,"using System.Windows.Forms;using Advanced_Combat_Tracker;[assembly:System.Reflection.AssemblyVersion(\""+v+".0.0.0\")] public class TestPlugin:IActPluginV1{public void InitPlugin(TabPage p,Label s){s.Text=\""+v+"\";} public void DeInitPlugin(){}}");
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
                    var p=new Portal(); typeof(Portal).GetField("root",Private).SetValue(p,root);p.InitPlugin(page,label);
                    var tabs=(TabControl)Field(p,"tabs");
                    Assert(tabs.TabCount==3&&page.Text=="Roxyzs ACT Plugin","initial tabs");
                    var snapshot=File.ReadAllText(cfg);Call(p,"Save");Assert(snapshot==File.ReadAllText(cfg) && !File.Exists(cfg+".bak"),"unchanged write");
                    Call(p,"Activate",item,"1.0.0");Assert(tabs.TabCount==4,"enable");
                    Call(p,"Stop",item);Assert(tabs.TabCount==3,"stop");
                    Call(p,"Activate",item,"1.0.0");
                    var net=new ChildUpdateTests(item,packages.Local(item,"2.0.0"));
                    ((Packages)Field(p,"packages")).Dispose();
                    typeof(Portal).GetField("packages",Private).SetValue(p,new Packages(Path.Combine(root,"packages"),net));
                    var currentTab=tabs.SelectedTab;
                    Call(p,"UpdateChildren",(object)new[]{Entry.All[1],item});
                    var timeout=Stopwatch.StartNew();
                    while(Field(p,"worker")!=null&&timeout.ElapsedMilliseconds<10000){Call(p,"Finish");Thread.Sleep(5);}
                    Assert(Field(p,"worker")==null&&net.AssetRequests==2&&net.HeadRequests==2,"batch continued after a failed item");
                    Assert(tabs.SelectedTab==currentTab&&tabs.TabPages[3].Controls[0].Text=="2","hot update in Config: "+label.Text);
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
                    Assert(!prefs.Enabled.Contains(added.Repo)&&prefs.Enabled.Contains(item.Repo)&&tabs.TabCount==4,"new off, existing on");
                    p.DeInitPlugin();Assert(File.ReadAllText(cfg).Contains("2.0.0"),"final save");
                    p.InitPlugin(page,label);Assert(((TabControl)Field(p,"tabs")).TabCount==4,"restore enabled");
                    Assert(!((Preferences)Field(p,"prefs")).Enabled.Contains(added.Repo),"new remains off after restart");
                    p.DeInitPlugin();
                }
            }
            Console.WriteLine("Portal lifecycle and catalog tests passed.");
        }
        catch(Exception ex){for(;ex!=null;ex=ex.InnerException)Console.Error.WriteLine(ex.GetType().Name+" "+ex.Message+" "+ex.StackTrace);Environment.ExitCode=1;}
    }
}
