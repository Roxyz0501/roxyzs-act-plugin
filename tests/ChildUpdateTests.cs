using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RoxyzsActPlugin;

internal sealed class ChildUpdateTests : HttpMessageHandler
{
    readonly Entry item;
    readonly byte[] zip;
    public int HeadRequests,AssetRequests;
    public ChildUpdateTests(Entry item,string dll)
    {
        this.item=item;
        using(var buffer=new MemoryStream())
        {
            using(var archive=new ZipArchive(buffer,ZipArchiveMode.Create,true))
            using(var output=archive.CreateEntry(item.Dll+".dll").Open())
            using(var input=File.OpenRead(dll))input.CopyTo(output);
            zip=buffer.ToArray();
        }
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        var response=new HttpResponseMessage(HttpStatusCode.OK);
        string name=item.Prefix+"-v2.0.0.zip";
        string path=request.RequestUri.AbsolutePath;
        if(request.Method==HttpMethod.Head)
        {
            HeadRequests++;
            if(path.Contains(item.Repo))
            {
                response.StatusCode=HttpStatusCode.Redirect;
                response.Headers.Location=new Uri(item.Url+"/releases/tag/v2.0.0");
            }
            else response.StatusCode=HttpStatusCode.NotFound;
        }
        else if(request.RequestUri.Host=="api.github.com")
            response.Content=new StringContent("{\"tag_name\":\"v2.0.0\",\"assets\":[{\"name\":\""+name+"\"},{\"name\":\""+name+".sha256\"}]}");
        else if(path.EndsWith(".sha256"))
        {
            using(var sha=System.Security.Cryptography.SHA256.Create())
                response.Content=new StringContent(BitConverter.ToString(sha.ComputeHash(zip)).Replace("-","")+"  "+name);
            AssetRequests++;
        }
        else {response.Content=new ByteArrayContent(zip);AssetRequests++;}
        return Task.FromResult(response);
    }
}
