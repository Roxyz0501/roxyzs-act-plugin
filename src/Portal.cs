using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RoxyzsActPlugin
{
    public class Preferences
    {
        public int Language { get; set; } = Array.IndexOf(new[]{"en","ja","zh","ko"}, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        public bool Check { get; set; } = true;
        public string CatalogJson { get; set; }
        public string CatalogETag { get; set; }
        public Dictionary<string,string> Versions { get; set; } = new Dictionary<string,string>();
        public List<string> Enabled { get; set; } = new List<string>();
    }

    public sealed class Portal : IActPluginV1
    {
        const string Title = "Roxyzs ACT Plugin";
        readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Advanced Combat Tracker","Config","RoxyzsActPlugin");
        readonly Dictionary<Entry,Tuple<IActPluginV1,TabPage,Label>> active = new Dictionary<Entry,Tuple<IActPluginV1,TabPage,Label>>();
        readonly Dictionary<Entry,Release> checkedVersions = new Dictionary<Entry,Release>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        Preferences prefs;
        Packages packages;
        string saved;
        Panel surface;
        Roxyz.Act.UI.PluginTabs tabs;
        bool selfScheduled;
        TabPage config,updates,support;
        ListView list;
        Label status,detail,feedback;
        ComboBox target;
        System.Windows.Forms.Timer saveTimer,completion;
        Task<Action> worker;
        Entry[] entries;
        CancellationTokenSource cancel;
        string ConfigPath => Path.Combine(root,"settings.json");
        string L(params string[] values) => values[Math.Max(0, Math.Min(3,prefs.Language))];
        string UpdateText => L("Update","更新","更新","업데이트");
        string SupportText => L("Support","支援","支持","후원");
        Entry Selected => list.SelectedItems.Count == 0 ? null : (Entry)list.SelectedItems[0].Tag;
        Entry UpdateTarget => target.SelectedIndex <= 0 ? Entry.Self : entries[target.SelectedIndex-1];
        string VersionOf(Entry e) => e == Entry.Self ? typeof(Portal).Assembly.GetName().Version.ToString(3) : prefs.Versions.TryGetValue(e.Repo,out var v) ? v : null;

        public void InitPlugin(TabPage page, Label label)
        {
            status=label;
            prefs = new Preferences();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    prefs=json.Deserialize<Preferences>(File.ReadAllText(ConfigPath)) ?? new Preferences();
                    if (prefs.Versions==null) prefs.Versions=new Dictionary<string,string>();
                    if (prefs.Enabled==null) prefs.Enabled=new List<string>();
                    saved=Snapshot();
                }
            }
            catch (Exception ex) { status.Text=ex.Message; }
            prefs.Language=Math.Max(0,Math.Min(3,prefs.Language));
            packages=new Packages(Path.Combine(root,"packages"));
            entries=Entry.All;
            if(prefs.CatalogJson!=null)
                Try(()=>entries=Catalog.Parse(prefs.CatalogJson));
            page.Text=Title;
            surface=new Panel { Dock=DockStyle.Fill, BackColor=Color.FromArgb(241,244,248), Padding=new Padding(8) };
            page.Controls.Add(surface);
            config=new TabPage("Config"); updates=new TabPage(); support=new TabPage();
            tabs=new Roxyz.Act.UI.PluginTabs(support){ShowToolTips=true};
            surface.Controls.Add(tabs);
            var line=new Panel { Dock=DockStyle.Top, Height=2, BackColor=Color.Goldenrod };
            surface.Controls.Add(line);
            var header=new Label { Text=Title, Dock=DockStyle.Top, Height=44, Padding=new Padding(12,0,0,0), BackColor=Color.White, ForeColor=Color.FromArgb(36,52,71), TextAlign=ContentAlignment.MiddleLeft };
            surface.Controls.Add(header);

            feedback=new Label { Dock=DockStyle.Bottom, Height=25, TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true };
            surface.Controls.Add(feedback);
            status.TextChanged+=StatusChanged;
            tabs.TabPages.AddRange(new[]{config,updates,support});
            Build();
            foreach(var item in entries.Where(i=>prefs.Enabled.Contains(i.Repo)))
                Try(()=>Start(item,VersionOf(item)));
            RefreshList();
            saveTimer=new System.Windows.Forms.Timer { Interval=60000 };
            saveTimer.Tick+=(s,e)=>Try(Save); saveTimer.Start();
            completion=new System.Windows.Forms.Timer { Interval=100 };
            completion.Tick+=(s,e)=>Finish();
            if(prefs.Check) RefreshCatalog(true);
        }
        string Snapshot() => json.Serialize(new SortedDictionary<string,object>(StringComparer.Ordinal) {
            {"Language",prefs.Language},{"Check",prefs.Check},{"CatalogJson",prefs.CatalogJson},{"CatalogETag",prefs.CatalogETag},
            {"Versions",new SortedDictionary<string,string>(prefs.Versions,StringComparer.Ordinal)},{"Enabled",prefs.Enabled.OrderBy(k=>k,StringComparer.Ordinal).ToArray()}
        });
        void Save()
        {
            string data=Snapshot();
            if(data==saved) return;
            Directory.CreateDirectory(root);
            string tmp=ConfigPath+".tmp";
            File.WriteAllText(tmp,data);
            if(File.Exists(ConfigPath)) File.Replace(tmp,ConfigPath,ConfigPath+".bak"); else File.Move(tmp,ConfigPath);
            saved=data;
        }
        string LimitMessage=>L("GitHub request limit. Use the public release.","GitHubのアクセス制限です。公開Releaseから取得できます。","GitHub 请求受限。可使用公开发行版。","GitHub 요청 제한입니다. 공개 릴리스를 사용할 수 있습니다.");
        void Try(Action work)
        {
            try { work(); }
            catch(RateLimitException) {status.Text=LimitMessage;}
            catch(System.Net.Http.HttpRequestException) {status.Text=L("Connection failed.","接続に失敗しました。","连接失败。","연결에 실패했습니다.");}
            catch(OperationCanceledException) {status.Text=L("Request timed out.","通信がタイムアウトしました。","请求超时。","요청 시간이 초과되었습니다.");}
            catch(Exception ex) { status.Text=L("Error","エラー","错误","오류")+" · "+ex.GetBaseException().Message; }
        }
        void StatusChanged(object sender,EventArgs e) { feedback.Text=status.Text; }
        static FlowLayoutPanel Bar() => new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(4),WrapContents=true };
        Button Button(Control parent,string text,Action click)
        {
            var b=new Button { Text=text,AutoSize=true,MinimumSize=new Size(80,30) };
            b.Click+=(s,e)=>Try(click); parent.Controls.Add(b); return b;
        }
        void Open(string url) { Process.Start(new ProcessStartInfo(url){ UseShellExecute=true }); }
        void Build()
        {
            foreach(var tab in new[]{config,updates,support}) while(tab.Controls.Count>0) tab.Controls[0].Dispose();
            tabs.SetLanguageFont(new[]{"Segoe UI","Meiryo UI","Microsoft YaHei UI","Malgun Gothic"}[prefs.Language]);
            config.Text=L("Config","設定","设置","설정");
            updates.Text=UpdateText; support.Text=SupportText;
            list=new ListView { Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HideSelection=false,MultiSelect=false };
            list.Columns.Add(L("Plugin","プラグイン","插件","플러그인"),155);
            list.Columns.Add(L("State","状態","状态","상태"),65);
            list.Columns.Add(L("Version","バージョン","版本","버전"),75);
            list.Columns.Add(L("Description","機能","功能","기능"),420);
            config.Controls.Add(list);
            var bar=Bar(); config.Controls.Add(bar);
            var language=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=100 };
            language.Items.AddRange(new[]{"English","日本語","简体中文","한국어"}); language.SelectedIndex=prefs.Language;
            language.SelectedIndexChanged+=(s,e)=>{prefs.Language=language.SelectedIndex; Build();};
            bar.Controls.Add(language);
            Button(bar,L("Enable","オン","启用","켜기"),()=>{var i=Selected;if(i!=null) Enable(i);});
            Button(bar,L("Disable","オフ","停用","끄기"),()=>{var i=Selected;if(i!=null){Stop(i);prefs.Enabled.Remove(i.Repo);RefreshList(i);}});
            Button(bar,UpdateText,()=>{var i=Selected;if(i!=null){target.SelectedIndex=Array.IndexOf(entries,i)+1;tabs.SelectedTab=updates;Check(i);}});
            Button(bar,L("Refresh list","一覧を更新","刷新列表","목록 새로 고침"),()=>RefreshCatalog(false));
            Button(bar,"GitHub",()=>{if(Selected!=null) Open(Selected.Url);});
            detail=new Label { Dock=DockStyle.Fill,AutoEllipsis=true,Padding=new Padding(10),BackColor=Color.White };
            updates.Controls.Add(detail);
            var actions=Bar(); updates.Controls.Add(actions);
            target=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=166 };
            target.Items.Add(Title);target.Items.AddRange(entries.Select(i=>i.Name).ToArray());target.SelectedIndex=0;
            target.SelectedIndexChanged+=(s,e)=>ShowRelease();
            actions.Controls.Add(target);
            var link=new LinkLabel { Text="GitHub",AutoSize=true,Margin=new Padding(6,9,6,3) };
            link.LinkClicked+=(s,e)=>Try(()=>Open(UpdateTarget.Url));actions.Controls.Add(link);
            Button(actions,L("Check now","今すぐ確認","立即检查","지금 확인"),()=>Check(UpdateTarget));
            Button(actions,L("Apply update","更新を適用","应用更新","업데이트 적용"),()=>Apply(UpdateTarget));
            Button(actions,L("Later","後で","稍后","나중에"),()=>tabs.SelectedTab=config);
            var auto=new CheckBox { Text=L("Check portal at startup","起動時にポータルの更新を確認","启动时检查门户更新","시작 시 포털 업데이트 확인"),Checked=prefs.Check,AutoSize=true };
            auto.CheckedChanged+=(s,e)=>prefs.Check=auto.Checked; actions.Controls.Add(auto);
            var card=Bar();card.BackColor=Color.FromArgb(255,244,218);support.Controls.Add(card);
            card.Controls.Add(new Label { AutoSize=true,Text=L("Support is optional. All features are available to everyone.","支援は任意です。すべての機能をどなたでも利用できます。","支持完全自愿，所有功能均可免费使用。","후원은 선택 사항이며 모든 기능을 이용할 수 있습니다.") });
            Button(card,L("Support Roxyz0501","Roxyz0501の開発を支援","支持 Roxyz0501","Roxyz0501 후원"),()=>Open("https://ko-fi.com/roxyz0501")).BackColor=Color.Gold;
            RefreshList(); ShowRelease();
        }
        void RefreshList(Entry selected=null)
        {
            selected=selected??Selected??entries.FirstOrDefault();list.BeginUpdate();list.Items.Clear();
            foreach(var i in entries)
            {
                var row=new ListViewItem(new[]{i.Name,active.ContainsKey(i)?"ON":"OFF",VersionOf(i)??"—",i.Description[prefs.Language]}){Tag=i};
                list.Items.Add(row);row.Selected=i==selected;
            }
            list.EndUpdate();
        }
        void ShowRelease()
        {
            var i=UpdateTarget; checkedVersions.TryGetValue(i,out var r);
            detail.Text=i.Name+Environment.NewLine+L("Author","作者","作者","제작자")+"  Roxyz0501"+Environment.NewLine+L("Current","現在版","当前版本","현재 버전")+"  "+(VersionOf(i)??"—")+Environment.NewLine+
                L("Latest","最新版","最新版本","최신 버전")+"  "+(r?.tag_name??"—")+Environment.NewLine+Environment.NewLine+(r?.Limited==true?LimitMessage: r?.body??"");
        }
        void Run(Func<CancellationToken,Task<Action>> job)
        {
            if(worker!=null) return;
            cancel=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var token=cancel.Token;
            worker=Task.Run(()=>job(token),token);
            config.Enabled=updates.Enabled=false;status.Text=L("Working…","処理中…","处理中…","처리 중…");
            completion.Start();
        }
        void Finish()
        {
            if(worker==null||!worker.IsCompleted)return;
            completion.Stop();
            var done=worker;worker=null;cancel.Dispose();cancel=null;
            config.Enabled=updates.Enabled=true;
            Try(()=>{status.Text=Title;done.GetAwaiter().GetResult()();});
        }
        void Check(Entry i) => Run(async token=>{
            var r=await packages.Latest(i,token,VersionOf(i));
            return ()=>{checkedVersions[i]=r;ShowRelease();if(i==Entry.Self&&r.Version>Version.Parse(VersionOf(i)))tabs.SelectedTab=updates;};
        });
        void RefreshCatalog(bool checkPortal)
        {
            var etag=prefs.CatalogJson==null?null:prefs.CatalogETag;
            Run(async token=>{
                Tuple<string,string> data;
                try { data=await packages.FetchCatalog(etag,token); }
                catch(Exception ex) when(checkPortal && !token.IsCancellationRequested)
                {
                    return ()=>{Try(()=>{throw ex;});Check(Entry.Self);};
                }
                return ()=>{
                    if(data!=null) ApplyCatalog(data.Item1,data.Item2);
                    if(checkPortal)Check(Entry.Self);
                };
            });
        }
        void ApplyCatalog(string raw,string etag)
        {
            var next=Catalog.Parse(raw);
            foreach(var removed in entries.Except(next))
            {
                Stop(removed);
                prefs.Enabled.RemoveAll(repo=>repo==removed.Repo);
                prefs.Versions.Remove(removed.Repo);
                checkedVersions.Remove(removed);
            }
            foreach(var added in next.Except(entries))
            {
                prefs.Enabled.RemoveAll(repo=>repo==added.Repo);
                prefs.Versions.Remove(added.Repo);
            }
            entries=next;
            prefs.CatalogJson=raw;
            prefs.CatalogETag=etag;
            Build();
        }
        void Enable(Entry i)
        {
            if(active.ContainsKey(i))return;
            var v=VersionOf(i);
            if(v!=null&&File.Exists(packages.Local(i,v))){Activate(i,v);return;}
            var registered=ActRegistry.Current().FirstOrDefault(p=>
                string.Equals(p.File?.Name,i.Dll+".dll",StringComparison.OrdinalIgnoreCase));
            if(registered?.File?.Exists==true)
            {
                if(registered.Enabled) throw Duplicate(i);
                var source=registered.File.FullName;
                var name=AssemblyName.GetAssemblyName(source);
                if(name.Name!=i.Dll)throw new InvalidDataException(i.Name);
                var version=name.Version.ToString(3);
                var path=packages.Local(i,version);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if(!File.Exists(path))
                {
                    string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try {File.Copy(source,temporary);File.Move(temporary,path);}
                    finally {if(File.Exists(temporary))File.Delete(temporary);}
                }
                Activate(i,version);return;
            }
            Run(async token=>{
                var r=await packages.Latest(i,token);
                await packages.Install(i,r,token);
                return ()=>{checkedVersions[i]=r;Activate(i,r.Version.ToString());};
            });
        }
        void Activate(Entry i,string v)
        {
            Start(i,v);prefs.Versions[i.Repo]=v;
            if(!prefs.Enabled.Contains(i.Repo))prefs.Enabled.Add(i.Repo);
            RefreshList(i);
        }
        void Apply(Entry i)
        {
            if(!checkedVersions.TryGetValue(i,out var r)) {Check(i);return;}
            string old=VersionOf(i);
            if(old!=null && r.Version<=Packages.ParseVersion(old)){status.Text=L("Up to date","最新版です","已是最新版本","최신 버전입니다");return;}
            string registered=null,original=null;
            if(i==Entry.Self)
            {
                if(selfScheduled)return;
                var registration=ActRegistry.Current().FirstOrDefault(p=>ReferenceEquals(p.Plugin,this));
                registered=SelfUpdate.RegisteredPath(registration?.File?.FullName);
                original=SelfUpdate.Hash(registered);
            }
            Run(async token=>{
                if(i==Entry.Self)
                {
                    var prepared=await packages.PrepareSelf(r,token);
                    return ()=>{
                        using(var act=Process.GetCurrentProcess())
                        using(prepared.Schedule(registered,original,act.Id)){}
                        selfScheduled=true;
                        detail.Text=status.Text=L("Exit ACT to install, then restart ACT.","ACTを終了すると自動更新されます。その後ACTを起動してください。","退出 ACT 以自动安装，然后重新启动 ACT。","ACT를 종료하면 자동 설치됩니다. 이후 ACT를 다시 시작하세요.");
                    };
                }
                await packages.Install(i,r,token);
                return ()=>{
                    if(active.ContainsKey(i))
                    {
                        Stop(i);
                        try { Start(i,r.Version.ToString()); }
                        catch { Start(i,old);throw; }
                    }
                    prefs.Versions[i.Repo]=r.Version.ToString();RefreshList(i);ShowRelease();
                };
            });
        }
        void Start(Entry i,string v)
        {
            if(v==null) throw new FileNotFoundException(i.Name);
            if(ActRegistry.Current().Any(p=>p.Enabled &&
                (string.Equals(p.File?.Name,i.Dll+".dll",StringComparison.OrdinalIgnoreCase)||p.Plugin?.GetType().Assembly.GetName().Name==i.Dll)))
                throw Duplicate(i);
            var path=packages.Local(i,v);
            var identity=AssemblyName.GetAssemblyName(path);
            if(identity.Name!=i.Dll || identity.Version.ToString(3)!=v)throw new InvalidDataException(i.Name);
            var assembly=Assembly.LoadFile(path);
            var type=assembly.GetExportedTypes().Single(t=>!t.IsAbstract && typeof(IActPluginV1).IsAssignableFrom(t));
            var plugin=(IActPluginV1)Activator.CreateInstance(type);
            var page=new TabPage(i.Name);
            var label=new Label { Dock=DockStyle.Bottom,Height=22,AutoEllipsis=true };
            tabs.TabPages.Add(page);
            try
            {
                var guideDirectory=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)),"GuideData");
                if(i.Dll=="BahamutGuide")
                {
                    var standalone=ActRegistry.Current().FirstOrDefault(p=>string.Equals(p.File?.Name,"BahamutGuide.dll",StringComparison.OrdinalIgnoreCase));
                    if(standalone?.File?.DirectoryName!=null)
                    {
                        var existing=Path.Combine(standalone.File.DirectoryName,"GuideData");
                        if(Directory.Exists(existing))guideDirectory=existing;
                    }
                }
                ManagedPluginHost.Initialize(plugin,page,label,path,Path.GetDirectoryName(root),guideDirectory);
                page.Controls.Add(label);page.Text=i.Name;page.ToolTipText=i.Name;
                active.Add(i,Tuple.Create(plugin,page,label));
            }
            catch { try {plugin.DeInitPlugin();} finally {page.Dispose();label.Dispose();} throw; }
        }
        void Stop(Entry i)
        {
            if(!active.TryGetValue(i,out var slot))return;
            slot.Item1.DeInitPlugin();
            active.Remove(i);slot.Item2.Dispose();slot.Item3.Dispose();
        }
        Exception Duplicate(Entry i) => new InvalidOperationException(L("Disable the standalone copy in ACT first.","先にACTの単体版をオフにしてください。","请先停用独立插件。","먼저 ACT의 개별 플러그인을 끄세요.")+" "+i.Name);
        public void DeInitPlugin()
        {
            saveTimer?.Dispose();completion?.Dispose();
            if(worker!=null)
            {
                cancel.Cancel();
                try{worker.GetAwaiter().GetResult();}catch(Exception ex){Debug.WriteLine(ex.Message);}
                worker=null;cancel.Dispose();cancel=null;
            }
            foreach(var i in active.Keys.ToArray())Try(()=>Stop(i));
            Try(Save); status.TextChanged-=StatusChanged; packages?.Dispose();surface?.Dispose();checkedVersions.Clear();
        }
    }
}
