using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace RoxyzsActPlugin
{
    internal sealed class SelfUpdate
    {
        readonly string dll,helper,dllHash,helperHash,version;
        SelfUpdate(string dir,string version)
        {
            dll=Path.Combine(dir,Id+".dll");helper=Path.Combine(dir,Id+".Updater.exe");
            this.version=version;dllHash=Hash(dll);helperHash=Hash(helper);
        }
        const string Id="RoxyzsActPlugin";
        internal static string RegisteredPath(string value)
        {
            if(string.IsNullOrWhiteSpace(value))throw new InvalidDataException("ACT plugin registration");
            if(value.StartsWith("file:",StringComparison.OrdinalIgnoreCase))value=new Uri(value).LocalPath;
            if(!Path.IsPathRooted(value))throw new InvalidDataException("ACT plugin registration");
            string path=Path.GetFullPath(value);
            if(!File.Exists(path)||AssemblyName.GetAssemblyName(path).Name!=Id)throw new InvalidDataException("ACT plugin registration");
            return path;
        }
        internal static string Hash(string path)
        {
            using(var input=File.OpenRead(path))
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
        }
        internal static SelfUpdate Stage(byte[] bytes,string root,string version,CancellationToken token)
        {
            string dir=Path.Combine(root,"pending",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using(var zip=new ZipArchive(new MemoryStream(bytes)))
                {
                    var names=new[]{Id+".dll",Id+".Updater.exe","README.md"};
                    if(zip.Entries.Count!=names.Length || names.Any(n=>zip.Entries.Count(e=>e.FullName==n)!=1))
                        throw new InvalidDataException("Portal ZIP");
                    foreach(var entry in zip.Entries.Where(e=>e.FullName!="README.md"))
                    {
                        if(entry.Length>32*1024*1024)throw new InvalidDataException("Portal ZIP size");
                        string file=Path.Combine(dir,entry.FullName);
                        using(var input=entry.Open())
                        using(var output=File.Create(file))
                        {
                            byte[] block=new byte[16384];
                            int count;
                            while((count=input.Read(block,0,block.Length))!=0)
                            {
                                token.ThrowIfCancellationRequested();
                                if(output.Length+count>entry.Length)throw new InvalidDataException("Portal ZIP size");
                                output.Write(block,0,count);
                            }
                            if(output.Length!=entry.Length)throw new InvalidDataException("Portal ZIP size");
                        }
                        var identity=AssemblyName.GetAssemblyName(file);
                        if(identity.Name!=Path.GetFileNameWithoutExtension(file)||identity.Version.ToString(3)!=version||
                            FileVersionInfo.GetVersionInfo(file).CompanyName!="Roxyz0501")throw new InvalidDataException("Portal package identity");
                    }
                }
                return new SelfUpdate(dir,version);
            }
            catch {Directory.Delete(dir,true);throw;}
        }
        internal Process Schedule(string target,string original,int pid)
        {
            target=RegisteredPath(target);
            using(var locked=File.Open(helper,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(Hash(helper)!=helperHash || Hash(dll)!=dllHash || Hash(target)!=original)throw new InvalidDataException("Update files changed");
                return Process.Start(new ProcessStartInfo(helper,
                "--wait-pid "+pid+" --source \""+dll+"\" --target \""+target+"\" --sha256 "+dllHash+
                " --original "+original+" --version "+version){UseShellExecute=false,CreateNoWindow=true});
            }
        }
    }
}
