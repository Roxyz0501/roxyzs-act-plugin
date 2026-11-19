using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RoxyzsActPlugin
{
    internal sealed class RateLimitException : HttpRequestException { }
    internal sealed partial class Packages
    {
        readonly Dictionary<string,Tuple<DateTime,Release>> recent = new Dictionary<string,Tuple<DateTime,Release>>();
        static bool Limited(HttpStatusCode status) => (int)status==403 || (int)status==429;
        static void Ensure(HttpResponseMessage response)
        {
            if(Limited(response.StatusCode))throw new RateLimitException();
            response.EnsureSuccessStatusCode();
        }
        internal static string ReleaseTag(Entry item,Uri uri)
        {
            var prefix=new Uri(item.Url+"/releases/tag/");
            if(uri==null || uri.Scheme!="https"||uri.Host!="github.com"||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||
               !uri.AbsolutePath.StartsWith(prefix.AbsolutePath,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release redirect");
            var tag=uri.AbsolutePath.Substring(prefix.AbsolutePath.Length);
            ParseVersion(tag.StartsWith("v",StringComparison.Ordinal)?tag.Substring(1):tag);
            return tag;
        }
        internal async Task<Release> Latest(Entry item,CancellationToken token,string installed=null)
        {
            recent.TryGetValue(item.Repo,out var memo);
            if(memo!=null && DateTime.UtcNow-memo.Item1<TimeSpan.FromSeconds(30) &&
               (memo.Item2.assets!=null || memo.Item2.Limited || memo.Item2.Version.ToString()==installed))return memo.Item2;
            var latestUrl=new Uri(item.Url+"/releases/latest");
            using(var request=new HttpRequestMessage(HttpMethod.Head,latestUrl))
            using(var head=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
            {
                if((int)head.StatusCode/100!=3){Ensure(head);throw new InvalidDataException("Release redirect");}
                var tag=ReleaseTag(item,head.Headers.Location==null?null:new Uri(latestUrl,head.Headers.Location));
                var result=new Release{tag_name=tag};
                if(installed==null || result.Version>ParseVersion(installed))
                {
                    using(var api=await client.GetAsync("https://api.github.com/repos/Roxyz0501/"+item.Repo+"/releases/tags/"+tag,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                    {
                        if(Limited(api.StatusCode)) result.Limited=true;
                        else
                        {
                            Ensure(api);
                            var json=Encoding.UTF8.GetString(await Read(api,256*1024,token).ConfigureAwait(false));
                            result=new JavaScriptSerializer().Deserialize<Release>(json);
                            if(result==null || result.tag_name!=tag || result.draft || result.prerelease || result.assets==null)
                                throw new InvalidDataException("Release");
                        }
                    }
                }
                recent[item.Repo]=Tuple.Create(DateTime.UtcNow,result);
                return result;
            }
        }
        async Task<string[]> AssetNames(Entry item,Release release,CancellationToken token)
        {
            if(release.assets!=null)return release.assets.Select(a=>a.name).ToArray();
            using(var response=await client.GetAsync(item.Url+"/releases/expanded_assets/"+release.tag_name,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
            {
                Ensure(response);
                var html=Encoding.UTF8.GetString(await Read(response,512*1024,token).ConfigureAwait(false));
                var path=new Uri(item.Url).AbsolutePath+"/releases/download/"+release.tag_name+"/";
                return Regex.Matches(html,"href=\""+Regex.Escape(path)+"([A-Za-z0-9_.-]+)\"",RegexOptions.IgnoreCase)
                    .Cast<Match>().Select(m=>m.Groups[1].Value).Distinct().ToArray();
            }
        }
    }
}
