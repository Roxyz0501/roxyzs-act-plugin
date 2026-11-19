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
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Field(Portal p,string name)=>typeof(Portal).GetField(name,Private).GetValue(p);
    static void Call(Portal p,string method,params object[] args)=>typeof(Portal).GetMethod(method,Private).Invoke(p,args);
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    [STAThread] static void Main(string[] args)
    {
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
                foreach(var i in Entry.All)
                {
                    Assert(i.Description.Length==4,"translations");
                    try{packages.Local(i,"../1");throw new Exception("path traversal");}catch(InvalidDataException){}
                    if(Array.IndexOf(args,"--online")>=0)
                    using(var cts=new CancellationTokenSource(TimeSpan.FromSeconds(90)))
                    {
                        var r=packages.Latest(i,cts.Token).GetAwaiter().GetResult();
                        Assert(File.Exists(packages.Install(i,r,cts.Token).GetAwaiter().GetResult()),"missing DLL");
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
                    Call(p,"Activate",item,"2.0.0");Assert(tabs.TabPages[3].Controls[0].Text=="2","reload newer code");
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
