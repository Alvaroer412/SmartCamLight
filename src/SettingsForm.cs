using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SmartCamLight
{
    class SettingsForm : Form
    {
        readonly TrayApp app;
        bool loading;
        int current = -1;

        readonly NavButton[] navs;
        readonly ScrollPage[] pages;
        readonly NavStatus navStatus;

        // Light page
        HeroRow hero;
        SliderRow rowBright, rowTemp, rowDelay;
        ChipsRow presets;
        Toggle tSmart;
        static readonly int[] PresetK = { 2700, 3500, 4500, 5600, 6500 };

        // Camera page
        ScrollPage camPage;
        DropDown ddCam;
        bool camLoaded;
        UvcCamera cam;
        readonly List<SliderRow> camRows = new List<SliderRow>();
        const int CamFixedItems = 2;

        // General page
        Toggle tStartup;

        public SettingsForm(TrayApp app)
        {
            this.app = app;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Body;
            BackColor = Ui.Back;
            ForeColor = Ui.Text;
            DoubleBuffered = true;
            Text = "SmartCamLight";
            Icon = IconArt.MakeIcon(32, true, false, false);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.Px(880), Ui.Px(660));
            MinimumSize = SizeFromClientSize(new Size(Ui.Px(720), Ui.Px(480)));

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Back };
            camPage = BuildCameraPage();
            pages = new[] { BuildLightPage(), camPage, BuildGeneralPage() };
            foreach (var p in pages) { p.Visible = false; content.Controls.Add(p); }

            var nav = new Panel { Dock = DockStyle.Left, Width = Ui.Px(236), BackColor = Ui.Back };
            var header = new NavHeader { BackColor = Ui.Back };
            header.SetBounds(Ui.Px(4), Ui.Px(4), Ui.Px(228), header.Height);
            nav.Controls.Add(header);
            navs = new[]
            {
                new NavButton(G.Light, "Light"),
                new NavButton(G.Camera, "Camera"),
                new NavButton(G.Settings, "General"),
            };
            for (int i = 0; i < navs.Length; i++)
            {
                int idx = i;
                navs[i].BackColor = Ui.Back;
                navs[i].SetBounds(Ui.Px(8), Ui.Px(76) + i * Ui.Px(42), Ui.Px(220), navs[i].Height);
                navs[i].Click += delegate { SelectPage(idx); };
                nav.Controls.Add(navs[i]);
            }
            navStatus = new NavStatus { Dock = DockStyle.Bottom, BackColor = Ui.Back };
            nav.Controls.Add(navStatus);

            Controls.Add(content);
            Controls.Add(nav);

            FormClosed += delegate { CloseCamera(); };
            ResumeLayout(false);

            ReloadLight();
            RefreshStatus();
            SelectPage(0);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ThemeWindow(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (current >= 0) pages[current].Relayout();
        }

        void SelectPage(int i)
        {
            if (i == current) return;
            current = i;
            for (int j = 0; j < pages.Length; j++) { navs[j].Selected = j == i; pages[j].Visible = j == i; }
            if (i == 1 && !camLoaded) LoadCameras();
            pages[i].Relayout();
        }

        static TextBlock Title(string t) { return new TextBlock(t, Ui.PageTitle, Ui.Text, 8, 4); }
        static TextBlock Section(string t) { return new TextBlock(t, Ui.Section, Ui.Text, 0, 0); }

        // ================= Light =================

        ScrollPage BuildLightPage()
        {
            var p = new ScrollPage();
            p.Add(Title("Light"), 0);

            var heroCard = p.Add(new Card(), 12);
            hero = heroCard.Add(new HeroRow());
            hero.Power.Click += delegate { if (app.LightConnected) app.SetLightManual(!app.LightOn); };

            p.Add(Section("Adjust"), 26);
            var adj = p.Add(new Card(), 8);
            rowBright = adj.Add(new SliderRow(G.Brightness, "Brightness", 0, 100, 1, false));
            rowBright.Format = v => v + "%";
            rowBright.Slider.ValueChanged += delegate
            {
                if (loading) return;
                app.SetBrightness(rowBright.Slider.Value);
                UpdateHero();
            };

            rowTemp = adj.Add(new SliderRow(G.Color, "Colour temperature", 2700, 6500, 100, false));
            rowTemp.Format = v => v + " K";
            rowTemp.Slider.Gradient = new[] { Ui.Kelvin(2700), Ui.Kelvin(3600), Ui.Kelvin(4800), Ui.Kelvin(6500) };
            rowTemp.Slider.ValueChanged += delegate
            {
                if (loading) return;
                app.SetTemperature(rowTemp.Slider.Value);
                UpdatePresets(); UpdateHero();
            };

            presets = adj.Add(new ChipsRow("Presets"));
            string[] names = { "Warm", "Soft", "Neutral", "Daylight", "Cool" };
            for (int i = 0; i < PresetK.Length; i++)
            {
                int k = PresetK[i];
                var chip = presets.AddChip(names[i]);
                chip.Click += delegate
                {
                    app.SetTemperature(k);
                    loading = true; rowTemp.Slider.Value = k; loading = false;
                    rowTemp.Invalidate(); UpdatePresets(); UpdateHero();
                };
            }

            p.Add(Section("Smart activation"), 26);
            var smart = p.Add(new Card(), 8);
            tSmart = new Toggle();
            tSmart.Toggled += delegate
            {
                app.SetSmart(tSmart.Checked);
                rowDelay.Enabled = tSmart.Checked;
            };
            smart.Add(new SettingRow(G.Video, "Turn on with camera",
                "The light switches on when any app starts using a camera, and off again when it stops.", tSmart));
            rowDelay = smart.Add(new SliderRow(G.Timer, "Turn off delay", 0, 30, 1, false));
            rowDelay.Format = v => v == 0 ? "Instantly" : v + " s after camera stops";
            rowDelay.Slider.ValueChanged += delegate { if (!loading) app.SetOffDelay(rowDelay.Slider.Value); };
            return p;
        }

        public void ReloadLight()
        {
            loading = true;
            rowBright.Slider.Value = app.S.BrightnessPct;
            rowTemp.Slider.Value = app.S.TemperatureK;
            rowDelay.Slider.Value = app.S.OffDelaySec;
            tSmart.SetInstant(app.S.SmartActivation);
            rowDelay.Enabled = app.S.SmartActivation;
            loading = false;
            rowBright.Invalidate(); rowTemp.Invalidate(); rowDelay.Invalidate();
            UpdatePresets(); UpdateHero();
        }

        void UpdatePresets()
        {
            for (int i = 0; i < presets.Chips.Count; i++) presets.Chips[i].Selected = PresetK[i] == app.S.TemperatureK;
        }

        void UpdateHero()
        {
            bool ok = app.LightConnected;
            string model = app.LightDescription.Replace(" connected", "");
            string title = !ok ? "No light connected" : app.LightOn ? "Light is on" : "Light is off";
            string sub = ok ? model + "  ·  " + app.S.BrightnessPct + "%  ·  " + app.S.TemperatureK + " K" : "Plug in your Litra light to control it";
            string status = app.CameraInUse
                ? "Camera in use" + (app.CameraApps.Count > 0 ? " by " + string.Join(", ", app.CameraApps) : "")
                : "Camera not in use";
            if (!app.S.SmartActivation) status += "  ·  smart activation off";
            hero.SetInfo(title, sub, status, app.CameraInUse ? Ui.Red : Ui.Text3);
            hero.Power.On = app.LightOn;
            hero.Power.Available = ok;
            hero.Power.Kelvin = app.S.TemperatureK;
            hero.Power.Brightness = app.S.BrightnessPct;
            hero.Power.Invalidate();
        }

        public void RefreshStatus()
        {
            UpdateHero();
            bool ok = app.LightConnected;
            rowBright.Slider.Enabled = rowTemp.Slider.Enabled = ok;
            navStatus.Set(
                app.CameraInUse ? "Camera on" : "Camera off", app.CameraInUse ? Ui.Red : Ui.Text3,
                !ok ? "No light found" : app.LightOn ? "Light on" : "Light off", ok && app.LightOn ? Ui.Accent : Ui.Text3);
        }

        // ================= Camera =================

        ScrollPage BuildCameraPage()
        {
            var p = new ScrollPage();
            p.Add(Title("Camera"), 0);
            var devCard = p.Add(new Card(), 12);
            ddCam = new DropDown();
            ddCam.SelectionChanged += delegate
            {
                var d = ddCam.SelectedItem as CamDevice;
                if (d == null) return;
                if (app.S.CameraId != d.Id) { app.S.CameraId = d.Id; app.S.CamValues.Clear(); app.S.Save(); }
                OpenCamera(d);
            };
            devCard.Add(new SettingRow(G.Camera, "Device", "The webcam whose picture you want to adjust", ddCam));
            return p;
        }

        void LoadCameras()
        {
            camLoaded = true;
            try
            {
                var devices = UvcCamera.ListDevices();
                CamDevice pick = UvcCamera.Pick(devices, app.S.CameraId);
                ddCam.SetItems(devices.ConvertAll(d => (object)d), pick);
                if (pick != null)
                {
                    if (string.IsNullOrEmpty(app.S.CameraId)) { app.S.CameraId = pick.Id; app.S.Save(); }
                    OpenCamera(pick);
                }
                else ShowCamMessage("No camera found. Plug in your webcam and it will show up here.");
            }
            catch (Exception ex)
            {
                ShowCamMessage("Could not list cameras: " + ex.Message);
                Log.Write("LoadCameras: " + ex);
            }
        }

        public void CameraListChanged() { if (camLoaded) LoadCameras(); }

        void CloseCamera() { if (cam != null) { cam.Dispose(); cam = null; } }

        void ShowCamMessage(string msg)
        {
            CloseCamera();
            camRows.Clear();
            camPage.ClearItems(CamFixedItems);
            camPage.Add(new TextBlock(msg, Ui.Body, Ui.Text2, 0, 0), 20);
            camPage.Relayout();
        }

        static readonly string[][] Groups =
        {
            new[] { "Picture", "VPA.0", "VPA.1", "VPA.3", "VPA.2", "VPA.4", "VPA.5" },
            new[] { "Exposure & colour", "VPA.7", "CC.4", "VPA.9", "VPA.8" },
            new[] { "Lens & framing", "CC.3", "CC.6", "CC.0", "CC.1", "CC.2", "CC.5" },
        };

        static Func<int, string> FormatFor(CamProp p)
        {
            if (p.Key == "VPA.7") return v => v + " K";
            if (p.Key == "CC.4") return v => v <= 0 ? "1/" + (1 << Math.Min(30, -v)) + " s" : (1 << Math.Min(30, v)) + " s";
            if (p.Key == "CC.0" || p.Key == "CC.1" || p.Key == "CC.2") return v => v + "°";
            if (p.Key == "CC.3") return v => v == 0 ? "None" : v.ToString();
            return v => v.ToString();
        }

        void OpenCamera(CamDevice dev)
        {
            CloseCamera();
            camRows.Clear();
            try { cam = UvcCamera.Open(dev); }
            catch (Exception ex) { Log.Write("OpenCamera: " + ex); }
            if (cam == null) { ShowCamMessage("Could not open " + dev.Name + "."); return; }

            var props = new Dictionary<string, CamProp>();
            foreach (var pr in cam.GetProps()) props[pr.Key] = pr;
            if (props.Count == 0) { ShowCamMessage("This camera has no adjustable settings."); return; }

            camPage.SuspendLayout();
            camPage.ClearItems(CamFixedItems);
            foreach (var group in Groups)
            {
                Card card = null;
                for (int i = 1; i < group.Length; i++)
                {
                    CamProp p;
                    if (!props.TryGetValue(group[i], out p)) continue;
                    if (card == null)
                    {
                        camPage.Add(Section(group[0]), 26);
                        card = camPage.Add(new Card(), 8);
                    }
                    card.Add(MakeCamRow(p));
                }
            }

            camPage.Add(Section("Tools"), 26);
            var tools = camPage.Add(new Card(), 8);
            var btnReset = new FlatButton("Reset", null, false);
            btnReset.Click += delegate { ResetCamera(); };
            tools.Add(new SettingRow(G.Reset, "Reset to defaults", "Restore the camera's factory picture settings", btnReset));
            var btnDriver = new FlatButton("Open", null, false);
            btnDriver.Click += delegate { if (cam != null) { cam.ShowDriverDialog(Handle); ReadCameraValues(); } };
            tools.Add(new SettingRow(G.Sliders, "Driver settings", "Open the camera driver's own settings dialog", btnDriver));

            camPage.Add(new TextBlock("Your changes are saved and re-applied whenever the camera reconnects. " +
                "AI tracking, framing, HDR and gestures are OBSBOT features; use OBSBOT Center for those.",
                Ui.Small, Ui.Text3, 0, 0), 16);
            camPage.ResumeLayout();
            ReadCameraValues();
            camPage.Relayout();
        }

        SliderRow MakeCamRow(CamProp p)
        {
            var row = new SliderRow(null, p.Name, p.Min, p.Max, p.Step, p.CanAuto);
            row.Format = FormatFor(p);
            row.Tag2 = p;
            row.Slider.ValueChanged += delegate
            {
                if (loading || cam == null) return;
                int v = row.Slider.Value;
                cam.Set(p, v, false);
                app.S.CamValues[p.Key] = v + ",0";
                app.SaveSoon();
            };
            if (row.Auto != null)
            {
                row.Auto.Toggled += delegate
                {
                    if (loading || cam == null) return;
                    int v = row.Slider.Value;
                    cam.Set(p, v, row.Auto.Checked);
                    app.S.CamValues[p.Key] = v + (row.Auto.Checked ? ",1" : ",0");
                    app.SaveSoon();
                };
            }
            camRows.Add(row);
            return row;
        }

        void ReadCameraValues()
        {
            if (cam == null) return;
            loading = true;
            foreach (var r in camRows)
            {
                var p = (CamProp)r.Tag2;
                int v; bool auto;
                if (cam.Get(p, out v, out auto)) r.Slider.Value = v;
                if (r.Auto != null) r.Auto.SetInstant(auto);
                r.Slider.Enabled = !(r.Auto != null && auto);
                r.Invalidate();
            }
            loading = false;
        }

        void ResetCamera()
        {
            if (cam == null) return;
            foreach (var r in camRows)
            {
                var p = (CamProp)r.Tag2;
                cam.Set(p, p.Default, p.CanAuto);
            }
            app.S.CamValues.Clear();
            app.S.Save();
            ReadCameraValues();
        }

        // ================= General =================

        ScrollPage BuildGeneralPage()
        {
            var p = new ScrollPage();
            p.Add(Title("General"), 0);

            var c1 = p.Add(new Card(), 12);
            tStartup = new Toggle();
            try { tStartup.SetInstant(app.StartWithWindows); } catch { }
            tStartup.Toggled += delegate
            {
                try { app.StartWithWindows = tStartup.Checked; }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't change the startup setting: " + ex.Message, "SmartCamLight"); }
            };
            c1.Add(new SettingRow(G.Startup, "Start with Windows", "Run SmartCamLight quietly in the tray when you sign in", tStartup));

            p.Add(Section("Ignored apps"), 26);
            var c2 = p.Add(new Card(), 8);
            var ignore = c2.Add(new InputRow(G.Block, "Don't react to these apps",
                "Camera use by these apps won't turn the light on. Separate names with commas, for example OBSBOT_Center, NVIDIA Broadcast."));
            ignore.Input.Box.Text = app.S.IgnoreApps;
            ignore.Input.Box.TextChanged += delegate { app.SetIgnore(ignore.Input.Box.Text); };

            p.Add(Section("About"), 26);
            var c3 = p.Add(new Card(), 8);
            c3.Add(new SettingRow(G.Info, "SmartCamLight 1.1", "Controls your Litra light and webcam without Logi Tune.", null));
            var open = new FlatButton("Open folder", null, false);
            open.Click += delegate
            {
                System.IO.Directory.CreateDirectory(Settings.Dir);
                Process.Start("explorer.exe", "\"" + Settings.Dir + "\"");
            };
            c3.Add(new SettingRow(G.Folder, "Settings and log", Settings.Dir, open));
            return p;
        }
    }
}
