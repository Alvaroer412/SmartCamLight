using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SmartCamLight
{
    // Answers "which apps are using a camera right now?".
    static class CameraUsage
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

        public static List<string> GetActiveApps()
        {
            // Primary: Media Foundation sensor activity (works on current Windows 11 builds).
            var result = SensorMonitor.ActiveApps();

            // Fallback for Windows builds without the monitor: the privacy consent store, where an
            // app that is using the webcam has LastUsedTimeStop == 0. It is only consulted when the
            // monitor is unavailable, because current builds leave stale entries behind there (an
            // app that never logged its stop looks "in use" for months), which kept the light on.
            if (!SensorMonitor.Running)
            {
                Scan(Registry.CurrentUser, result);
                Scan(Registry.LocalMachine, result);
            }
            return result;
        }

        static void Scan(RegistryKey hive, List<string> result)
        {
            try
            {
                using (var k = hive.OpenSubKey(KeyPath))
                    if (k != null) Walk(k, result, 0);
            }
            catch { }
        }

        static void Walk(RegistryKey key, List<string> result, int depth)
        {
            foreach (string name in key.GetSubKeyNames())
            {
                try
                {
                    using (var sub = key.OpenSubKey(name))
                    {
                        if (sub == null) continue;
                        object start = sub.GetValue("LastUsedTimeStart");
                        object stop = sub.GetValue("LastUsedTimeStop");
                        if (start != null && stop != null && Convert.ToInt64(stop) == 0 && Convert.ToInt64(start) != 0)
                        {
                            string app = Describe(name);
                            if (app != null && !result.Contains(app)) result.Add(app);
                        }
                        if (depth < 2) Walk(sub, result, depth + 1);
                    }
                }
                catch { }
            }
        }

        // Returns a friendly app name, or null if the entry is stale (the app is no longer running).
        static string Describe(string keyName)
        {
            if (keyName.Contains("#"))
            {
                // Desktop app: "C:#Program Files#Zoom#Zoom.exe"
                string exe = keyName.Substring(keyName.LastIndexOf('#') + 1);
                string proc = Path.GetFileNameWithoutExtension(exe);
                try
                {
                    Process[] ps = Process.GetProcessesByName(proc);
                    bool running = ps.Length > 0;
                    foreach (var p in ps) p.Dispose();
                    if (!running) return null;
                }
                catch { }
                return proc;
            }

            // Store app: the key is its package family name, "Microsoft.WindowsCamera_8wekyb3d8bbwe".
            if (!PackageRunning(keyName)) return null;
            int u = keyName.IndexOf('_');
            string pkg = u > 0 ? keyName.Substring(0, u) : keyName;
            int dot = pkg.LastIndexOf('.');
            return dot >= 0 && dot < pkg.Length - 1 ? pkg.Substring(dot + 1) : pkg;
        }

        // True when some running process belongs to the given Store package.
        static bool PackageRunning(string familyName)
        {
            try
            {
                bool found = false;
                foreach (var p in Process.GetProcesses())
                {
                    using (p)
                    {
                        if (found) continue;
                        IntPtr h = OpenProcess(0x1000, false, p.Id);   // PROCESS_QUERY_LIMITED_INFORMATION
                        if (h == IntPtr.Zero) continue;
                        try
                        {
                            int length = 128;
                            var name = new StringBuilder(length);
                            if (GetPackageFamilyName(h, ref length, name) == 0 &&
                                string.Equals(name.ToString(), familyName, StringComparison.OrdinalIgnoreCase)) found = true;
                        }
                        finally { CloseHandle(h); }
                    }
                }
                return found;
            }
            catch { return true; }   // cannot tell: keep the entry
        }

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackageFamilyName(IntPtr process, ref int length, StringBuilder name);
    }
}
