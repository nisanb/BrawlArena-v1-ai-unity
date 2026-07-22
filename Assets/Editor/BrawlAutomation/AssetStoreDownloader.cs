using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BrawlAutomation
{
    /// Downloads an owned Asset Store package through the Package Manager's
    /// internal asset-store services (the entitlement is checked server-side, so
    /// this only works for assets already in My Assets). Reflection-based because
    /// every type involved is internal; DescribeApi() exists so a version drift
    /// can be diagnosed through the harness without attaching a debugger.
    public static class AssetStoreDownloader
    {
        static Assembly PmAsm => typeof(UnityEditor.PackageManager.UI.Window).Assembly;

        static object Resolve(string typeName)
        {
            var container = PmAsm.GetType("UnityEditor.PackageManager.UI.Internal.ServicesContainer");
            if (container == null) throw new Exception("ServicesContainer type not found");
            var inst = container.GetProperty("instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null);
            if (inst == null) throw new Exception("ServicesContainer.instance null");
            var target = PmAsm.GetType(typeName);
            if (target == null) throw new Exception(typeName + " type not found");
            var resolve = container.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Resolve" && m.IsGenericMethod);
            if (resolve == null) throw new Exception("Resolve<T> not found");
            return resolve.MakeGenericMethod(target).Invoke(inst, null);
        }

        public static string StartDownload(long productId)
        {
            var dm = Resolve("UnityEditor.PackageManager.UI.Internal.AssetStoreDownloadManager");
            var methods = dm.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name.Contains("Download")).ToArray();

            // try the known shapes, most specific first
            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                try
                {
                    if (m.Name == "Download" && ps.Length == 1 &&
                        typeof(System.Collections.Generic.IEnumerable<long>).IsAssignableFrom(ps[0].ParameterType))
                    {
                        m.Invoke(dm, new object[] { new[] { productId } });
                        return "started via Download(IEnumerable<long>)";
                    }
                    if (m.Name == "Download" && ps.Length == 1 && ps[0].ParameterType == typeof(long))
                    {
                        m.Invoke(dm, new object[] { productId });
                        return "started via Download(long)";
                    }
                }
                catch (Exception e)
                {
                    return "invoke failed on " + m + ": " + e.InnerException?.Message ?? e.Message;
                }
            }
            return "NO usable Download method. " + DescribeApi();
        }

        public static string DescribeApi()
        {
            var sb = new StringBuilder();
            try
            {
                var dm = Resolve("UnityEditor.PackageManager.UI.Internal.AssetStoreDownloadManager");
                sb.AppendLine("AssetStoreDownloadManager methods:");
                foreach (var m in dm.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                    if (!m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                        sb.AppendLine("  " + m);
            }
            catch (Exception e)
            {
                sb.AppendLine("resolve failed: " + e.Message);
            }
            try
            {
                var uc = typeof(EditorApplication).Assembly.GetType("UnityEditor.Connect.UnityConnect");
                var inst = uc.GetProperty("instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
                sb.AppendLine("editor loggedIn=" + uc.GetProperty("loggedIn").GetValue(inst));
                sb.AppendLine("userName=" + uc.GetProperty("userInfo")?.GetValue(inst)?.GetType()
                    .GetProperty("displayName")?.GetValue(uc.GetProperty("userInfo").GetValue(inst)));
            }
            catch (Exception e)
            {
                sb.AppendLine("connect probe failed: " + e.Message);
            }
            return sb.ToString();
        }

        public static string ImportPackage(string path)
        {
            AssetDatabase.ImportPackage(path, false);
            return "import started: " + path;
        }
    }
}
