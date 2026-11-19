using Advanced_Combat_Tracker;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace RoxyzsActPlugin
{
    internal static class ManagedPluginHost
    {
        internal static void Initialize(IActPluginV1 plugin, TabPage page, Label status, string path,
            string configDirectory, string guideDirectory)
        {
            var type = plugin.GetType();
            var hosted = type.GetMethod("InitHostedPlugin", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(TabPage), typeof(Label), typeof(string), typeof(string) }, null);
            if (hosted != null && hosted.ReturnType == typeof(void))
            {
                Invoke(hosted,plugin,new object[] {page,status,path,guideDirectory});
                return;
            }
            if (type.FullName != "BahamutGuide.BahamutGuidePlugin" || type.Assembly.GetName().Name != "BahamutGuide")
            {
                plugin.InitPlugin(page, status);
                return;
            }
            // Bahamut's standalone entry point searches ACT for its own instance.
            // Its configuration overload accepts the host's actual data directory.
            var service = type.Assembly.GetType("BahamutGuide.UpdateService", true);
            var signature = new[] { typeof(TabPage), typeof(Label), typeof(string), service, typeof(string), typeof(Func<string>) };
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var init = type.GetMethod("InitPlugin", flags, null, signature, null);
            var args = new object[] { page, status, Path.Combine(configDirectory, "BahamutGuide.xml"),
                Activator.CreateInstance(service, true), guideDirectory, (Func<string>)(() => path) };
            if (init == null)
            {
                Array.Resize(ref signature, 5);
                Array.Resize(ref args, 5);
                init = type.GetMethod("InitPlugin", flags, null, signature, null);
            }
            if (init == null) { plugin.InitPlugin(page, status); return; }
            Invoke(init,plugin,args);
        }

        static void Invoke(MethodInfo method,IActPluginV1 plugin,object[] args)
        {
            try { method.Invoke(plugin, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }
    }
}
