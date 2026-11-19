using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RoxyzsActPlugin
{
    internal sealed class Release
    {
        public string tag_name { get; set; }
        public string body { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public Asset[] assets { get; set; }
        public Version Version => Packages.ParseVersion(tag_name.TrimStart('v'));
    }
    internal sealed class Asset { public string name { get; set; } }

    internal sealed class Packages : IDisposable
    {
        readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        readonly Dictionary<string, Tuple<string,Release>> cache = new Dictionary<string, Tuple<string,Release>>();
        readonly string root;
        public Packages(string root)
        {
            this.root = root;
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RoxyzsActPlugin/1.0");
        }
        internal static Version ParseVersion(string value)
        {
            if (!Regex.IsMatch(value ?? "", @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z")) throw new InvalidDataException("Version");
            return Version.Parse(value);
        }
        public string Local(Entry item, string version) =>
            Path.Combine(root, item.Repo, ParseVersion(version).ToString(), item.Dll + ".dll");
        internal async Task<Release> Latest(Entry item, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Roxyz0501/" + item.Repo + "/releases/latest"))
            {
                cache.TryGetValue(item.Repo, out var saved);
                if (saved?.Item1 != null) request.Headers.TryAddWithoutValidation("If-None-Match", saved.Item1);
                using (var response = await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotModified && saved != null) return saved.Item2;
                    response.EnsureSuccessStatusCode();
                    var json = await Read(response, 256 * 1024, token).ConfigureAwait(false);
                    var release = new JavaScriptSerializer().Deserialize<Release>(Encoding.UTF8.GetString(json));
                    if (release == null || release.draft || release.prerelease || !Regex.IsMatch(release.tag_name ?? "", @"\Av?\d+\.\d+\.\d+\z") || release.assets == null)
                        throw new InvalidDataException("Release");
                    ParseVersion(release.tag_name.TrimStart('v'));
                    cache[item.Repo] = Tuple.Create(response.Headers.ETag?.ToString(),release);
                    return release;
                }
            }
        }
        internal async Task<Tuple<string,string>> FetchCatalog(string etag,CancellationToken token)
        {
            var url="https://api.github.com/repos/Roxyz0501/roxyzs-act-plugin/contents/catalog.json?ref=main";
            using(var request=new HttpRequestMessage(HttpMethod.Get,url))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
                if(!string.IsNullOrEmpty(etag))request.Headers.TryAddWithoutValidation("If-None-Match",etag);
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                {
                    if(response.StatusCode==HttpStatusCode.NotModified)return null;
                    response.EnsureSuccessStatusCode();
                    var raw=Encoding.UTF8.GetString(await Read(response,65536,token).ConfigureAwait(false));
                    Catalog.Parse(raw);
                    return Tuple.Create(raw,response.Headers.ETag?.ToString());
                }
            }
        }
        static async Task<byte[]> Read(HttpResponseMessage response, int limit, CancellationToken token)
        {
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Size");
            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[16384];
                int n;
                while ((n = await input.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + n > limit) throw new InvalidDataException("Size");
                    output.Write(buffer,0,n);
                }
                return output.ToArray();
            }
        }
        async Task<byte[]> AssetBytes(Entry item, Release release, string name, int limit, CancellationToken token)
        {
            var uri = new Uri(item.Url + "/releases/download/" + release.tag_name + "/" + name);
            for (int i=0; i<4; i++)
            using (var response = await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
            {
                if ((int)response.StatusCode / 100 == 3)
                {
                    uri = new Uri(uri,response.Headers.Location);
                    if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                        (uri.Host != "release-assets.githubusercontent.com" && uri.Host != "objects.githubusercontent.com"))
                        throw new InvalidDataException("Redirect");
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return await Read(response,limit,token).ConfigureAwait(false);
            }
            throw new InvalidDataException("Redirect");
        }
        public async Task<string> Install(Entry item, Release release, CancellationToken token)
        {
            var version = release.Version.ToString();
            var expected = item.Prefix + (item.Dll == "SSRename" ? "-" : "-v") + version + ".zip";
            var names = release.assets.Select(a => a.name).ToArray();
            var manifest = new[]{expected + ".sha256", Path.ChangeExtension(expected,"sha256"), "SHA256SUMS.txt"}.FirstOrDefault(n => names.Contains(n));
            if (!names.Contains(expected) || manifest == null) throw new InvalidDataException("ZIP / SHA-256");
            var sums = Encoding.UTF8.GetString(await AssetBytes(item,release,manifest,16384,token).ConfigureAwait(false)).TrimStart('\ufeff');
            var match = Regex.Match(sums, @"(?m)^([A-Fa-f0-9]{64})[ \t]+\*?" + Regex.Escape(expected) + @"[ \t]*\r?$");
            if (!match.Success) throw new InvalidDataException("SHA-256 manifest");
            var bytes = await AssetBytes(item,release,expected,32*1024*1024,token).ConfigureAwait(false);
            using (var sha = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""), match.Groups[1].Value,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256");
            var path = Local(item,version);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var archive = new ZipArchive(new MemoryStream(bytes)))
                {
                    if (archive.Entries.Count > 64) throw new InvalidDataException("ZIP");
                    var dll = archive.Entries.Single(e => e.FullName == item.Dll + ".dll");
                    if (dll.Length > 32*1024*1024) throw new InvalidDataException("DLL size");
                    using (var source = dll.Open())
                    using (var output = File.Create(temp))
                    {
                        byte[] buffer = new byte[16384];
                        int n;
                        while ((n=source.Read(buffer,0,buffer.Length))>0)
                        {
                            token.ThrowIfCancellationRequested();
                            if (output.Length+n>dll.Length) throw new InvalidDataException("DLL size");
                            output.Write(buffer,0,n);
                        }
                        if (output.Length!=dll.Length) throw new InvalidDataException("DLL size");
                    }
                }
                var assembly = AssemblyName.GetAssemblyName(temp);
                if (assembly.Name != item.Dll || assembly.Version.ToString(3) != version) throw new InvalidDataException("DLL identity");
                token.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
                return path;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Dispose() { client.Dispose(); }
    }
}
