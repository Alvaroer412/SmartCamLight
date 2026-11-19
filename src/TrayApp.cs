using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SmartCamLight
{
    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "SmartCamLight";

        public readonly Settings S;
        readonly LitraManager litra = new LitraManager();
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem miStatus, miPower, miSmart;
        readonly System.Windows.Forms.Timer pollTimer, refreshTimer, debounceTimer, camApplyTimer, saveTimer;
        readonly DeviceChangeWindow devWin;
        readonly Control invoker;
        SettingsForm form;
        Icon currentIcon;
        string lastIconKey = "", lastCamList = "";

        bool lightOn, cameraInUse;
        DateTime offPendingSince = DateTime.MinValue;
        List<string> cameraApps = new List<string>();

        public bool LightOn { get { return lightOn; } }
        public bool CameraInUse { get { return cameraInUse; } }
        public List<string> CameraApps { get { return cameraApps; } }
        public string LightDescription { get { return litra.Description; } }
        public bool LightConnected { get { return litra.Count > 0; } }

        public TrayApp(EventWaitHandle showEvent, bool showSettings)
        {
            S = Settings.Load();
            invoker = new Control();
            IntPtr forceHandle = invoker.Handle;

            menu = new ContextMenuStrip();
            miStatus = new ToolStripMenuItem("") { Enabled = false };
            miPower = new ToolStripMenuItem("Light on", null, delegate { SetLightManual(!lightOn); });
            miSmart = new ToolStripMenuItem("Smart activation", null, delegate { SetSmart(!S.SmartActivation); });
            var miBright = new ToolStripMenuItem("Brightness");
            foreach (int pct in new[] { 10, 25, 50, 75, 100 })
            {
                int p = pct;
                miBright.DropDownItems.Add(new ToolStripMenuItem(p + "%", null, delegate { SetBrightness(p); if (form != null) form.ReloadLight(); }));
            }
            var miTemp = new ToolStripMenuItem("Colour temperature");
            foreach (int k in new[] { 2700, 3500, 4500, 5500, 6500 })
            {
                int kk = k;
                miTemp.DropDownItems.Add(new ToolStripMenuItem(kk + " K", null, delegate { SetTemperature(kk); if (form != null) form.ReloadLight(); }));
            }
            menu.Items.Add(miStatus);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miPower);
            menu.Items.Add(miSmart);
            menu.Items.Add(miBright);
            menu.Items.Add(miTemp);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(); }));
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Exit(); }));
            menu.Opening += delegate { UpdateUi(); };
            MenuRenderer.Apply(menu);
            foreach (ToolStripItem item in menu.Items) MenuRenderer.Style(item);
            miStatus.ForeColor = Ui.Text2;

            saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
            saveTimer.Tick += delegate { saveTimer.Stop(); S.Save(); };

            tray = new NotifyIcon { ContextMenuStrip = menu, Text = "SmartCamLight", Visible = true };
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };

            // Initial state
            SensorMonitor.Start();
            System.Threading.Thread.Sleep(300); // let the first activity report arrive
            litra.Refresh(true);
            cameraApps = FilteredApps();
            cameraInUse = cameraApps.Count > 0;
            ApplyLightLevels();
            SetPower(S.SmartActivation ? cameraInUse : S.LightOn);
            lastCamList = CameraListKey();
            ApplyCameraSettings();

            pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            pollTimer.Tick += delegate { Poll(); };
            pollTimer.Start();

            refreshTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            refreshTimer.Tick += delegate { CheckDevices(false); };
            refreshTimer.Start();

            debounceTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            debounceTimer.Tick += delegate { debounceTimer.Stop(); CheckDevices(false); };

            camApplyTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            camApplyTimer.Tick += delegate { camApplyTimer.Stop(); ApplyCameraSettings(); };

            devWin = new DeviceChangeWindow(delegate { debounceTimer.Stop(); debounceTimer.Start(); });
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            var t = new Thread(delegate ()
            {
                while (true)
                {
                    showEvent.WaitOne();
                    try { invoker.BeginInvoke((MethodInvoker)ShowSettings); } catch { return; }
                }
            }) { IsBackground = true };
            t.Start();

            UpdateUi();
            if (S.FirstRun)
            {
                S.Save();
                tray.ShowBalloonTip(4000, "SmartCamLight is running", "It lives in the system tray. Click the icon for settings.", ToolTipIcon.Info);
            }
            if (showSettings) ShowSettings();
        }

        // ---------- Smart activation ----------

        List<string> FilteredApps()
        {
            var ignore = S.IgnoreSet();
            ignore.Add("SmartCamLight");
            var list = CameraUsage.GetActiveApps();
            list.RemoveAll(a => ignore.Contains(a));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        void Poll()
        {
            List<string> apps = FilteredApps();
            bool changed = string.Join(",", apps) != string.Join(",", cameraApps);
            cameraApps = apps;

            if (apps.Count > 0)
            {
                offPendingSince = DateTime.MinValue;
                if (!cameraInUse)
                {
                    cameraInUse = true; changed = true;
                    Log.Write("Camera on (" + string.Join(", ", apps) + ")");
                    if (S.SmartActivation) SetPower(true);
                    camApplyTimer.Stop(); camApplyTimer.Start();
                }
            }
            else if (cameraInUse)
            {
                if (offPendingSince == DateTime.MinValue) offPendingSince = DateTime.UtcNow;
                if ((DateTime.UtcNow - offPendingSince).TotalSeconds >= S.OffDelaySec)
                {
                    cameraInUse = false; changed = true;
                    offPendingSince = DateTime.MinValue;
                    Log.Write("Camera off");
                    if (S.SmartActivation) SetPower(false);
                }
            }
            if (changed) UpdateUi();
        }

        // ---------- Light ----------

        void SetPower(bool on)
        {
            lightOn = on;
            litra.SetPower(on);
            UpdateUi();
        }

        void ApplyLightLevels()
        {
            litra.SetBrightnessPercent(S.BrightnessPct);
            litra.SetTemperature(S.TemperatureK);
        }

        public void SetLightManual(bool on)
        {
            SetPower(on);
            S.LightOn = on;
            S.Save();
        }

        public void SaveSoon() { saveTimer.Stop(); saveTimer.Start(); }

        public void SetBrightness(int pct)
        {
            S.BrightnessPct = pct;
            litra.SetBrightnessPercent(pct);
            SaveSoon();
        }

        public void SetTemperature(int k)
        {
            S.TemperatureK = k;
            litra.SetTemperature(k);
            SaveSoon();
        }

        public void SetSmart(bool on)
        {
            S.SmartActivation = on;
            S.Save();
            if (on) SetPower(cameraInUse);
            UpdateUi();
        }

        public void SetOffDelay(int sec) { S.OffDelaySec = sec; SaveSoon(); }

        public void SetIgnore(string text) { S.IgnoreApps = text; S.Save(); Poll(); }

        // ---------- Devices ----------

        void CheckDevices(bool force)
        {
            try
            {
                if (litra.Refresh(force) && litra.Count > 0)
                {
                    Log.Write("Light (re)connected: " + litra.Description);
                    ApplyLightLevels();
                    litra.SetPower(lightOn);
                }
                string key = CameraListKey();
                if (key != lastCamList)
                {
                    lastCamList = key;
                    Log.Write("Camera list changed: " + key);
                    ApplyCameraSettings();
                    if (form != null) form.CameraListChanged();
                }
                UpdateUi();
            }
            catch (Exception ex) { Log.Write("CheckDevices: " + ex); }
        }

        static string CameraListKey()
        {
            try
            {
                var names = new List<string>();
                foreach (var d in UvcCamera.ListDevices()) names.Add(d.Name);
                return string.Join("|", names);
            }
            catch { return ""; }
        }

        public void ApplyCameraSettings()
        {
            if (S.CamValues.Count == 0) return;
            try
            {
                CamDevice dev = UvcCamera.Pick(UvcCamera.ListDevices(), S.CameraId);
                if (dev == null) return;
                using (var cam = UvcCamera.Open(dev))
                    if (cam != null) cam.Apply(S.CamValues);
            }
            catch (Exception ex) { Log.Write("ApplyCameraSettings: " + ex.Message); }
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            invoker.BeginInvoke((MethodInvoker)delegate
            {
                var t = new System.Windows.Forms.Timer { Interval = 3000 };
                t.Tick += delegate { t.Stop(); t.Dispose(); CheckDevices(true); };
                t.Start();
            });
        }

        // ---------- Startup ----------

        public bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValue) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(RunValue, false);
                }
            }
        }

        // ---------- UI ----------

        public string StatusText()
        {
            string cam = cameraInUse
                ? "Camera on" + (cameraApps.Count > 0 ? " (" + string.Join(", ", cameraApps) + ")" : "")
                : "Camera off";
            string light = !LightConnected ? "no light found" : (lightOn ? "light on" : "light off");
            return cam + " · " + light;
        }

        void UpdateUi()
        {
            miStatus.Text = StatusText();
            miPower.Checked = lightOn;
            miPower.Enabled = LightConnected;
            miSmart.Checked = S.SmartActivation;

            string tip = "SmartCamLight – " + StatusText();
            tray.Text = tip.Length > 63 ? tip.Substring(0, 60) + "…" : tip;

            string iconKey = lightOn + "|" + cameraInUse + "|" + LightConnected;
            if (iconKey != lastIconKey)
            {
                lastIconKey = iconKey;
                Icon old = currentIcon;
                currentIcon = IconArt.MakeIcon(SystemInformation.SmallIconSize.Width, lightOn, cameraInUse, !LightConnected);
                tray.Icon = currentIcon;
                if (old != null) old.Dispose();
            }
            if (form != null && !form.IsDisposed) form.RefreshStatus();
        }

        public void ShowSettings()
        {
            if (form == null || form.IsDisposed)
            {
                form = new SettingsForm(this);
                form.FormClosed += delegate { form = null; };
                form.Show();
            }
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        void Exit()
        {
            pollTimer.Stop(); refreshTimer.Stop();
            saveTimer.Stop(); S.Save();
            SensorMonitor.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (form != null) form.Close();
            tray.Visible = false;
            tray.Dispose();
            devWin.DestroyHandle();
            litra.Dispose();
            ExitThread();
        }
    }

    // Hidden top-level window that receives WM_DEVICECHANGE broadcasts (plug/unplug).
    class DeviceChangeWindow : NativeWindow
    {
        readonly Action onChange;
        public DeviceChangeWindow(Action onChange)
        {
            this.onChange = onChange;
            CreateHandle(new CreateParams { Caption = "SmartCamLightDeviceWatcher" });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0219) onChange();
            base.WndProc(ref m);
        }
    }
}
