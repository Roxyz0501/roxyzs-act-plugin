using Advanced_Combat_Tracker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RoxyzsActPlugin
{
    internal sealed class ActRegistration
    {
        public FileInfo File;
        public object Plugin;
        public bool Enabled;
    }

    internal static class ActRegistry
    {
        // ACT builds expose the same registration data as fields or properties.
        static object Read(Type type, object instance, string name)
        {
            var flags = BindingFlags.Public | (instance == null ? BindingFlags.Static : BindingFlags.Instance);
            var property = type.GetProperty(name, flags);
            if (property != null) return property.GetValue(instance);
            var field = type.GetField(name, flags);
            if (field != null) return field.GetValue(instance);
            throw new MissingMemberException(type.FullName, name);
        }

        internal static IEnumerable<ActRegistration> Current() =>
            From(Read(typeof(ActGlobals), null, "oFormActMain"));

        internal static IEnumerable<ActRegistration> From(object host)
        {
            if (host == null) yield break;
            var entries = Read(host.GetType(), host, "ActPlugins") as IEnumerable;
            if (entries == null) throw new InvalidDataException("ACT plugin registry");
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var type = entry.GetType();
                var file = Read(type, entry, "pluginFile");
                var plugin = Read(type, entry, "pluginObj");
                var enabled = Read(type, entry, "cbEnabled");
                yield return new ActRegistration
                {
                    File = file as FileInfo ?? (file is string path && path.Length > 0 ? new FileInfo(path) : null),
                    Plugin = plugin,
                    Enabled = enabled is CheckBox check ? check.Checked : enabled is bool value ? value : plugin != null
                };
            }
        }
    }
}
