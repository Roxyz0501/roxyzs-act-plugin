namespace RoxyzsActPlugin
{
    internal sealed class Entry
    {
        public readonly string Name, Repo, Dll, Prefix;
        public readonly string[] Description;
        internal Entry(string name, string repo, string dll, string prefix, params string[] description)
        {
            Name = name; Repo = repo; Dll = dll; Prefix = prefix; Description = description;
        }
        public string Url => "https://github.com/Roxyz0501/" + Repo;
        public override bool Equals(object obj) => obj is Entry other && Repo==other.Repo && Dll==other.Dll;
        public override int GetHashCode() => Repo.GetHashCode() ^ Dll.GetHashCode();
        public static readonly Entry Self = new Entry("Roxyzs ACT Plugin","roxyzs-act-plugin","RoxyzsActPlugin","RoxyzsActPlugin");
        public static Entry[] All
        {
            get
            {
                using (var stream=typeof(Entry).Assembly.GetManifestResourceStream("RoxyzsActPlugin.catalog.json"))
                using (var reader=new System.IO.StreamReader(stream)) return Catalog.Parse(reader.ReadToEnd());
            }
        }
    }
}
