using Advanced_Combat_Tracker;
using Roxyz.Act.UI;
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

    public sealed partial class Portal : IActPluginV1
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
        UpdatePageState selfState;
        Func<string> selfMessage=()=>"";

        TabPage config,support;
        PluginUpdatePage updates;
        string updateLanguage;
        ListView list;
        Label status,feedback;
        Action<Exception> onFailure;
        System.Windows.Forms.Timer saveTimer,completion;
        Task<Action> worker;
        Entry[] entries;
        CancellationTokenSource cancel;
        string ConfigPath => Path.Combine(root,"settings.json");
        string L(params string[] values) => values[Math.Max(0, Math.Min(3,prefs.Language))];
        string UpdateText => L("Update","更新","更新","업데이트");
        string SupportText => L("Support","支援","支持","후원");
        Entry Selected => list.SelectedItems.Count == 0 ? null : (Entry)list.SelectedItems[0].Tag;
        string LanguageCode => new[]{"en","ja","zh","ko"}[prefs.Language];
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
            config=new TabPage("Config"); support=new TabPage {AutoScroll=true,BackColor=Color.White};
            updateLanguage=LanguageCode;
            updates=new PluginUpdatePage(Entry.Self.Repo,VersionOf(Entry.Self),prefs.Check,updateLanguage);
            updates.CheckRequested+=(s,e)=>CheckSelf();
            updates.InstallRequested+=(s,e)=>Try(PrepareSelf);
            updates.LaterRequested+=(s,e)=>{SetSelfState(UpdatePageState.Ready,()=>"");tabs.SelectedTab=config;};
            updates.StartupChanged+=(s,e)=>prefs.Check=updates.CheckOnStartup;
            selfScheduled=false;
            SetSelfState(UpdatePageState.Ready,()=>"");
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
            catch(Exception ex){status.Text=ErrorText(ex);}
        }
        string ErrorText(Exception ex)
        {
            if(ex is RateLimitException)return LimitMessage;
            if(ex is System.Net.Http.HttpRequestException)return L("Connection failed.","接続に失敗しました。","连接失败。","연결에 실패했습니다.");
            if(ex is OperationCanceledException)return L("Request timed out.","通信がタイムアウトしました。","请求超时。","요청 시간이 초과되었습니다.");
            return L("Error","エラー","错误","오류")+" · "+ex.GetBaseException().Message;
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
            foreach(var tab in new[]{config,support}) while(tab.Controls.Count>0) tab.Controls[0].Dispose();
            tabs.SetLanguageFont(new[]{"Segoe UI","Meiryo UI","Microsoft YaHei UI","Malgun Gothic"}[prefs.Language]);
            config.Text=L("Config","設定","设置","설정");
            // WinForms can retain an equal Font instance. Reapplying the same language
            // would let the shared page dispose a font still used for measurement.
            if(updateLanguage!=LanguageCode)
            {
                updates.SetLanguage(LanguageCode);
                updateLanguage=LanguageCode;
            }
            updates.SetCheckOnStartup(prefs.Check);
            updates.ShowState(selfState,selfMessage());
            support.Text=SupportText;
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
            Button(bar,L("Update selected","個別更新","更新所选","선택 업데이트"),()=>{var i=Selected;if(i!=null)UpdateChildren(new[]{i});});
            Button(bar,L("Update all installed","一括更新","更新全部已安装插件","설치된 항목 모두 업데이트"),()=>UpdateChildren(entries.Where(i=>VersionOf(i)!=null).ToArray()));
            Button(bar,L("Refresh list","一覧を更新","刷新列表","목록 새로 고침"),()=>RefreshCatalog(false));
            Button(bar,"GitHub",()=>{if(Selected!=null) Open(Selected.Url);});
            support.Controls.Add(BuildSupportLayout());
            RefreshList();
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
        void SetSelfState(UpdatePageState state,Func<string> message)
        {
            selfState=state;selfMessage=message;
            updates.ShowState(state,message());
        }
        void Run(Func<CancellationToken,Task<Action>> job,Action<Exception> failed=null)
        {
            if(worker!=null) return;
            onFailure=failed;
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
            var failed=onFailure;onFailure=null;
            try {status.Text=Title;done.GetAwaiter().GetResult()();}
            catch(Exception ex){Try(()=>{throw ex;});failed?.Invoke(ex);}
        }
        void CheckSelf()
        {
            if(worker!=null||selfScheduled)return;
            SetSelfState(UpdatePageState.Checking,()=>L("Checking…","確認中…","检查中…","확인 중…"));
            Run(async token=>{
                var r=await packages.Latest(Entry.Self,token,VersionOf(Entry.Self));
                return ()=>{
                    checkedVersions[Entry.Self]=r;
                    bool available=r.Version>Version.Parse(VersionOf(Entry.Self));
                    Func<string> message=()=>VersionOf(Entry.Self)+" → "+L("Latest","最新版","最新版本","최신 버전")+"  "+r.tag_name+Environment.NewLine+
                        (available?(r.Limited?LimitMessage:r.body):L("Up to date","最新版です","已是最新版本","최신 버전입니다"));
                    SetSelfState(available?UpdatePageState.Available:UpdatePageState.Ready,message);
                    if(available)tabs.SelectedTab=updates;
                };
            },ex=>SetSelfState(UpdatePageState.Ready,()=>ErrorText(ex)));
        }
        void RefreshCatalog(bool checkPortal)
        {
            var etag=prefs.CatalogJson==null?null:prefs.CatalogETag;
            Run(async token=>{
                Tuple<string,string> data;
                try { data=await packages.FetchCatalog(etag,token); }
                catch(Exception ex) when(checkPortal && !token.IsCancellationRequested)
                {
                    return ()=>{Try(()=>{throw ex;});CheckSelf();};
                }
                return ()=>{
                    if(data!=null) ApplyCatalog(data.Item1,data.Item2);
                    if(checkPortal)CheckSelf();
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
        void PrepareSelf()
        {
            if(selfScheduled||worker!=null||!checkedVersions.TryGetValue(Entry.Self,out var release))return;
            if(release.Version<=Version.Parse(VersionOf(Entry.Self)))return;
            var registration=ActRegistry.Current().FirstOrDefault(p=>ReferenceEquals(p.Plugin,this));
            var registered=SelfUpdate.RegisteredPath(registration?.File?.FullName);
            var original=SelfUpdate.Hash(registered);
            SetSelfState(UpdatePageState.Preparing,()=>L("Preparing…","更新を準備中…","准备中…","준비 중…"));
            Run(async token=>{
                var prepared=await packages.PrepareSelf(release,token);
                return ()=>{
                    using(var act=Process.GetCurrentProcess())
                    using(prepared.Schedule(registered,original,act.Id)){}
                    selfScheduled=true;
                    SetSelfState(UpdatePageState.Prepared,()=>L("Exit ACT to install, then restart ACT.","ACTを終了すると自動更新されます。その後ACTを起動してください。","退出 ACT 以自动安装，然后重新启动 ACT。","ACT를 종료하면 자동 설치됩니다. 이후 ACT를 다시 시작하세요."));
                };
            },ex=>SetSelfState(UpdatePageState.PrepareFailed,()=>ErrorText(ex)));
        }
        void UpdateChildren(Entry[] items)
        {
            if(items.Length==0){status.Text=L("No installed plugins","導入済みのプラグインはありません","没有已安装的插件","설치된 플러그인이 없습니다");return;}
            var problems=new List<string>();
            int updated=0,unchanged=0;
            Action<int> next=null;
            next=index=>{
                if(index==items.Length)
                {
                    status.Text=string.Format(L("Updated {0}, current {1}, failed {2}","更新 {0}件、最新版 {1}件、失敗 {2}件","已更新 {0}，已是最新 {1}，失败 {2}","업데이트 {0}, 최신 {1}, 실패 {2}"),updated,unchanged,problems.Count)
                        +(problems.Count==0?"":" · "+string.Join(" / ",problems));
                    return;
                }
                var item=items[index];
                var old=VersionOf(item);
                Run(async token=>{
                    var release=await packages.Latest(item,token,old);
                    bool upgrade=old==null||release.Version>Packages.ParseVersion(old);
                    if(upgrade)await packages.Install(item,release,token);
                    return ()=>{
                        if(upgrade)
                        {
                            if(active.ContainsKey(item))
                            {
                                Stop(item);
                                try{Start(item,release.Version.ToString());}catch{Start(item,old);throw;}
                            }
                            prefs.Versions[item.Repo]=release.Version.ToString();
                            updated++;RefreshList(item);
                        }
                        else unchanged++;
                        next(index+1);
                    };
                },ex=>{problems.Add(item.Name+" · "+status.Text);next(index+1);});
                status.Text=string.Format("{0}/{1}  {2}",index+1,items.Length,item.Name);
            };
            next(0);
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
                ChildTabs.HideManagementPages(page);
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
