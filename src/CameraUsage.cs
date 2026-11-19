using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SmartCamLight
{
    // Detects "camera is on" the same way Windows' privacy indicator does:
    // an app currently using the webcam has LastUsedTimeStop == 0 in the consent store.
    static class CameraUsage
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

        public static List<string> GetActiveApps()
        {
            // Primary: Media Foundation sensor activity (works on current Windows 11 builds).
            var result = SensorMonitor.ActiveApps();
            // Fallback: privacy consent store (older Windows builds).
            Scan(Registry.CurrentUser, result);
            Scan(Registry.LocalMachine, result);
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

        // Returns a friendly app name, or null if the entry is stale (process no longer running).
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
            // Store app: "Microsoft.WindowsCamera_8wekyb3d8bbwe"
            int u = keyName.IndexOf('_');
            string pkg = u > 0 ? keyName.Substring(0, u) : keyName;
            int dot = pkg.LastIndexOf('.');
            return dot >= 0 && dot < pkg.Length - 1 ? pkg.Substring(dot + 1) : pkg;
        }
    }
}
