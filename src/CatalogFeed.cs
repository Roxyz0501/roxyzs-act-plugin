using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RoxyzsActPlugin
{
    internal static class Catalog
    {
        public sealed class Row
        {
            public string Name { get; set; }
            public string Repo { get; set; }
            public string Dll { get; set; }
            public string Prefix { get; set; }
            public string[] Description { get; set; }
        }
        internal static Entry[] Parse(string json)
        {
            var rows=new JavaScriptSerializer{MaxJsonLength=65536}.Deserialize<Row[]>(json);
            if(rows==null||rows.Length>64) throw new InvalidDataException("Catalog");
            var blocked=new[]{"inventory-ledger-act","mitigation-flytext-act","screenshotmetadata","roxyzs-act-plugin"};
            foreach(var row in rows)
            {
                if(row==null || !Regex.IsMatch(row.Repo??"",@"\A[a-z0-9][a-z0-9-]{0,79}\z") || blocked.Contains(row.Repo)
                   || !Regex.IsMatch(row.Dll??"",@"\A[A-Za-z][A-Za-z0-9]{0,63}\z")
                   || new[]{"InventoryLedger","MitigationFlytext","ScreenshotMetadata","RoxyzsActPlugin"}.Contains(row.Dll,StringComparer.OrdinalIgnoreCase)
                   || !Regex.IsMatch(row.Prefix??"",@"\A[A-Za-z][A-Za-z0-9-]{0,79}\z")
                   || string.IsNullOrWhiteSpace(row.Name) || row.Name.Length>80
                   || row.Description==null || row.Description.Length!=4 || row.Description.Any(v=>string.IsNullOrWhiteSpace(v)||v.Length>512))
                    throw new InvalidDataException("Catalog entry");
            }
            if(rows.Select(r=>r.Repo).Distinct().Count()!=rows.Length||rows.Select(r=>r.Dll).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=rows.Length)
                throw new InvalidDataException("Duplicate catalog entry");
            return rows.Select(r=>new Entry(r.Name,r.Repo,r.Dll,r.Prefix,r.Description)).ToArray();
        }
    }
}
