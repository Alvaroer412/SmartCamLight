using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SmartCamLight
{
    class Settings
    {
        public bool SmartActivation = true;
        public int OffDelaySec = 2;
        public bool LightOn = false;
        public int BrightnessPct = 50;
        public int TemperatureK = 4500;
        public string IgnoreApps = "";
        public string CameraId = "";
        public Dictionary<string, string> CamValues = new Dictionary<string, string>();
        public bool FirstRun;

        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartCamLight");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public static Settings Load()
        {
            var s = new Settings();
            MigrateFromOldName();
            if (!File.Exists(FilePath)) { s.FirstRun = true; return s; }
            try
            {
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "SmartActivation": s.SmartActivation = v == "1"; break;
                        case "OffDelaySec": if (int.TryParse(v, out n)) s.OffDelaySec = Math.Max(0, Math.Min(60, n)); break;
                        case "LightOn": s.LightOn = v == "1"; break;
                        case "BrightnessPct": if (int.TryParse(v, out n)) s.BrightnessPct = Math.Max(0, Math.Min(100, n)); break;
                        case "TemperatureK": if (int.TryParse(v, out n)) s.TemperatureK = Math.Max(2700, Math.Min(6500, n)); break;
                        case "IgnoreApps": s.IgnoreApps = v; break;
                        case "CameraId": s.CameraId = v; break;
                        default:
                            if (k.StartsWith("cam.")) s.CamValues[k.Substring(4)] = v;
                            break;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Settings load failed: " + ex.Message); }
            return s;
        }

        // Early builds were called "CamLight": carry their settings and autostart entry over once.
        static void MigrateFromOldName()
        {
            try
            {
                string oldFile = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CamLight"), "settings.ini");
                if (!File.Exists(FilePath) && File.Exists(oldFile))
                {
                    Directory.CreateDirectory(Dir);
                    File.Copy(oldFile, FilePath);
                }
                using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (run != null && run.GetValue("CamLight") != null)
                    {
                        run.DeleteValue("CamLight", false);
                        run.SetValue("SmartCamLight", "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --tray");
                    }
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("SmartActivation=" + (SmartActivation ? "1" : "0"));
                sb.AppendLine("OffDelaySec=" + OffDelaySec);
                sb.AppendLine("LightOn=" + (LightOn ? "1" : "0"));
                sb.AppendLine("BrightnessPct=" + BrightnessPct);
                sb.AppendLine("TemperatureK=" + TemperatureK);
                sb.AppendLine("IgnoreApps=" + IgnoreApps.Replace("\r", "").Replace("\n", ","));
                sb.AppendLine("CameraId=" + CameraId);
                foreach (var kv in CamValues) sb.AppendLine("cam." + kv.Key + "=" + kv.Value);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
                FirstRun = false;
            }
            catch (Exception ex) { Log.Write("Settings save failed: " + ex.Message); }
        }

        public HashSet<string> IgnoreSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in IgnoreApps.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
                if (t.Length > 0) set.Add(t);
            }
            return set;
        }
    }

    static class Log
    {
        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                string path = Path.Combine(Settings.Dir, "smartcamlight.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
