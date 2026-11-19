using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RoxyzsActPlugin;

internal static class UpdateTests
{
    sealed class Transport : HttpMessageHandler
    {
        public int Heads,Api,Assets;
        public string Location;
        public int ApiStatus;
        public byte[] Zip;
        public bool Corrupt;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var result=new HttpResponseMessage(HttpStatusCode.OK);
            string name="RoxyzsActPlugin-v1.2.0.zip";
            if(request.Method==HttpMethod.Head)
            {
                Heads++; result.StatusCode=HttpStatusCode.Redirect; result.Headers.Location=new Uri(Location);
            }
            else if(request.RequestUri.Host=="api.github.com"){ Api++;result.StatusCode=(HttpStatusCode)ApiStatus; }
            else if(request.RequestUri.AbsolutePath.Contains("/expanded_assets/"))
            {
                string prefix="/Roxyz0501/roxyzs-act-plugin/releases/download/v1.2.0/";
                result.Content=new StringContent("<a href=\""+prefix+name+"\"></a><a href=\""+prefix+name+".sha256\"></a>");
            }
            else
            {
                Assets++;
                if(request.RequestUri.AbsolutePath.EndsWith(".sha256"))
                {
                    using(var sha=System.Security.Cryptography.SHA256.Create())
                        result.Content=new StringContent((Corrupt?new string('0',64):BitConverter.ToString(sha.ComputeHash(Zip)).Replace("-",""))+"  "+name);
                }
                else result.Content=new ByteArrayContent(Zip);
            }
            return Task.FromResult(result);
        }
    }
    static void Require(bool success,string message){if(!success)throw new Exception(message);}
    static byte[] Package(bool invalid=false)
    {
        using(var bytes=new MemoryStream())
        {
            using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))
            {
                foreach(var path in new[]{typeof(Portal).Assembly.Location,Path.GetFullPath("updater/bin/Release/net48/RoxyzsActPlugin.Updater.exe")})
                    using(var output=zip.CreateEntry(Path.GetFileName(path)).Open())
                    using(var input=File.OpenRead(path))input.CopyTo(output);
                zip.CreateEntry(invalid?"../outside.txt":"README.md");
            }
            return bytes.ToArray();
        }
    }
    public static void Run(string root)
    {
        var item=Entry.Self;
        var token=CancellationToken.None;
        foreach(var bad in new[]{"https://github.com/other/project/releases/tag/v1.2.0",item.Url+"/releases/tag/v1.2.0-beta",item.Url+"/releases/tag/v1.2.0?ref=x"})
        {
            try{Packages.ReleaseTag(item,new Uri(bad));throw new Exception("redirect accepted");}catch(InvalidDataException){}
        }
        var zip=Package();
        var registered=Path.Combine(root,"登録 先.dll");
        File.Copy(typeof(Portal).Assembly.Location,registered);
        using(var tail=File.Open(registered,FileMode.Append))tail.WriteByte(1);
        string original=SelfUpdate.Hash(registered);
        Require(SelfUpdate.RegisteredPath(new Uri(registered).AbsoluteUri)==registered,"registered URI");
        try{SelfUpdate.Stage(Package(true),root,"1.2.0",token);throw new Exception("unsafe ZIP accepted");}catch(InvalidDataException){}
        foreach(int code in new[]{403,429})
        {
            var transport=new Transport{Location=item.Url+"/releases/tag/v1.2.0",ApiStatus=code,Zip=zip};
            using(var client=new Packages(root,transport))
            {
                client.Latest(item,token,"1.2.0").GetAwaiter().GetResult();
                client.Latest(item,token,"1.2.0").GetAwaiter().GetResult();
                Require(transport.Heads==1&&transport.Api==0,"unchanged check");
                var release=client.Latest(item,token,"1.1.0").GetAwaiter().GetResult();
                Require(release.Limited&&transport.Api==1&&transport.Assets==0,"rate-limit detection");
                transport.Corrupt=true;
                try{client.PrepareSelf(release,token).GetAwaiter().GetResult();throw new Exception("bad hash accepted");}catch(InvalidDataException){}
                transport.Corrupt=false;
                var staged=client.PrepareSelf(release,token).GetAwaiter().GetResult();
                if(code!=429)continue;
                var stop="portal-test-"+Guid.NewGuid().ToString("N");
                using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset,stop))
                using(var host=Process.Start(new ProcessStartInfo(typeof(UpdateTests).Assembly.Location,"--host "+stop){UseShellExecute=false,CreateNoWindow=true}))
                using(var helper=staged.Schedule(registered,original,host.Id))
                {
                    try{Require(!helper.WaitForExit(450)&&SelfUpdate.Hash(registered)==original,"wait for host");}
                    finally{signal.Set();}
                    Require(helper.WaitForExit(15000)&&helper.ExitCode==0,"updater exit");
                }
                Require(SelfUpdate.Hash(registered)==SelfUpdate.Hash(typeof(Portal).Assembly.Location),"registered DLL replaced");
                Require(SelfUpdate.Hash(Directory.GetFiles(root,"*.backup-*").Single())==original,"backup verified");
            }
        }
        var worker=Assembly.Load(File.ReadAllBytes("updater/bin/Release/net48/RoxyzsActPlugin.Updater.exe"));
        var replace=worker.GetType("RoxyzsActPlugin.Updater.Program").GetMethods(BindingFlags.NonPublic|BindingFlags.Static)
            .Single(m=>m.Name=="ReplaceWithBackup"&&m.GetParameters().Length==5);
        var restoredHash=SelfUpdate.Hash(registered);
        Action<string,string> fail=(source,target)=>{File.WriteAllText(target,"partial");throw new IOException("test failure");};
        try{replace.Invoke(null,new object[]{typeof(Portal).Assembly.Location,registered,restoredHash,"1.2.0",fail});throw new Exception("expected failure");}
        catch(TargetInvocationException ex)when(ex.InnerException is IOException){}
        Require(SelfUpdate.Hash(registered)==restoredHash,"restore after partial replacement");
        Console.WriteLine("HEAD cache, zero same-version REST, 403/429 fallback, corrupt packages, and post-exit replacement passed.");
    }
}
