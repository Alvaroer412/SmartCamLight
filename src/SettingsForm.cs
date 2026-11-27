using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SmartCamLight
{
    class SettingsForm : Form, IMessageFilter
    {
        readonly TrayApp app;
        bool loading;
        int current = -1;

        NavPane nav;
        ScrollPage[] pages;

        // Light page
        HeroCard hero;
        SliderRow rowBright, rowTemp, rowDelay;
        ChipsRow presets;
        Toggle tSmart;
        public static readonly int[] PresetK = { 2700, 3500, 4500, 5600, 6500 };
        public static readonly string[] PresetNames = { "Warm", "Soft", "Neutral", "Daylight", "Cool" };

        // Camera page
        ScrollPage camPage;
        DropDown ddCam;
        bool camLoaded;
        UvcCamera cam;
        readonly List<SliderRow> camRows = new List<SliderRow>();
        const int CamFixedItems = 2;

        // General page
        Toggle tStartup;

        // Live camera preview, shared by the Light and Camera pages
        CameraFeed feed;
        string feedNotice;
        bool previewOn;
        volatile bool framePending;
        Size lastFrame;
        Toggle tPreviewLight, tPreviewCam;
        PreviewPane paneLight, paneCam;
        Card previewCardLight, previewCardCam;

        public SettingsForm(TrayApp app)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.None;
            Text = Program.Preview ? "SmartCamLight (preview)" : "SmartCamLight";
            Icon = IconArt.MakeIcon(32, true, false, false);
            StartPosition = FormStartPosition.Manual;

            // Open on the monitor the pointer is on, at that monitor's scale.
            Screen screen = Screen.FromPoint(Cursor.Position);
            Ui.SetScale(Ui.ScaleAt(Cursor.Position));
            BuildUi();

            Rectangle area = screen.WorkingArea;
            ClientSize = new Size(Math.Min(Ui.Px(920), area.Width - Ui.Px(48)), Math.Min(Ui.Px(680), area.Height - Ui.Px(72)));
            MinimumSize = MinSize();
            Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);

            Application.AddMessageFilter(this);
            FormClosed += delegate
            {
                Application.RemoveMessageFilter(this);
                StopFeed();
                this.app.SetPreviewActive(false);
                CloseCamera();
                Ui.ReleaseSurface();
            };
            SelectPage(0, false);
        }

        Size MinSize() { return SizeFromClientSize(new Size(Ui.Px(660), Ui.Px(440))); }

        void BuildUi()
        {
            SuspendLayout();
            BackColor = Ui.Glass ? Color.Black : Ui.Back;
            ForeColor = Ui.Text;
            Font = Ui.Body;

            var content = new Backdrop { Dock = DockStyle.Fill };
            camLoaded = false;
            camPage = BuildCameraPage();
            pages = new[] { BuildLightPage(), camPage, BuildGeneralPage() };
            foreach (var p in pages) { p.Visible = false; content.Controls.Add(p); }

            nav = new NavPane();
            nav.AddItem(G.Light, "Light");
            nav.AddItem(G.Camera, "Camera");
            nav.AddItem(G.Settings, "General");
            nav.SelectedChanged += delegate { SelectPage(nav.SelectedIndex, true); };

            Controls.Add(content);
            Controls.Add(nav);
            ResumeLayout(true);
            ActiveControl = nav;   // keyboard focus starts on the navigation, not on the power button

            ReloadLight();
            RefreshStatus();
            SyncPreview(true);   // a running preview carries over into the rebuilt pages
        }

        // ================= Camera preview =================

        // Turns the live preview on or off. It uses the camera chosen on the Camera page.
        void SetPreview(bool on)
        {
            StopFeed();
            feedNotice = null;
            if (on)
            {
                CamDevice device = null;
                try { device = UvcCamera.Pick(UvcCamera.ListDevices(), app.S.CameraId); }
                catch (Exception ex) { Log.Write("Preview: " + ex.Message); }
                if (device == null) feedNotice = "No camera found. Plug in your webcam and try again.";
                else
                {
                    feed = new CameraFeed(device, PreviewPane.ShownWidth);
                    feed.Changed = OnFeedChanged;
                }
            }
            previewOn = on;
            // The preview counts as camera use, so smart activation lights you the way it will in a call.
            app.SetPreviewActive(feed != null);
            SyncPreview(false);
            if (on && current >= 0) pages[current].EnsureVisible(current == 1 ? previewCardCam : previewCardLight);
        }

        public bool PreviewOn { get { return previewOn; } }

        // From the tray menu: the preview opens on the Light page, beside the light controls.
        public void SetPreviewFromTray(bool on)
        {
            if (on && current != 0 && current != 1) SelectPage(0, false);
            if (on != previewOn) SetPreview(on);
        }

        void StopFeed()
        {
            if (feed != null) { feed.Dispose(); feed = null; }
            lastFrame = Size.Empty;
        }

        // Pushes the preview state into the controls on both pages.
        void SyncPreview(bool instant)
        {
            foreach (Toggle t in new[] { tPreviewLight, tPreviewCam })
            {
                if (instant) t.SetInstant(previewOn); else t.Checked = previewOn;
            }
            paneLight.Set(feed, feedNotice, previewOn);
            paneCam.Set(feed, feedNotice, previewOn);
            if (current >= 0) pages[current].Relayout();
        }

        // Called on the camera thread for every new frame; repaints at most once per message loop turn.
        void OnFeedChanged()
        {
            if (framePending || IsDisposed || !IsHandleCreated) return;
            framePending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    framePending = false;
                    if (feed == null || current < 0) return;
                    if (feed.Error != null) app.SetPreviewActive(false);
                    Size size = feed.FrameSize;
                    if (size != lastFrame) { lastFrame = size; pages[current].Relayout(); }   // proportions now known
                    PreviewPane pane = current == 0 ? paneLight : current == 1 ? paneCam : null;
                    if (pane != null) pane.Invalidate();
                });
            }
            catch { framePending = false; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // A minimized window should not keep the camera busy.
            if (WindowState == FormWindowState.Minimized && previewOn) SetPreview(false);
        }

        // Rebuilds the interface after the theme or the display scale changed. All state lives in
        // the app and its settings, so the controls can simply be recreated.
        public void Rebuild()
        {
            int page = Math.Max(0, current);
            CloseCamera();
            camRows.Clear();
            var old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (var c in old) c.Dispose();
            current = -1;
            BuildUi();
            SelectPage(page, false);
            if (IsHandleCreated) Ui.ThemeWindow(Handle);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Safety net: the window came up on a monitor with another scale than it was built for.
            float actual = Ui.ScaleOf(Handle);
            if (Math.Abs(actual - Ui.S) > 0.01f)
            {
                MinimumSize = Size.Empty;
                Ui.SetScale(actual);
                Rebuild();
                MinimumSize = MinSize();
            }
            Ui.ThemeWindow(Handle);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Ui.Erase(e); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (current >= 0) pages[current].Relayout();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0)   // WM_DPICHANGED: the window moved to a monitor with a different scale
            {
                float scale = (int)(m.WParam.ToInt64() & 0xFFFF) / 96f;
                var r = (Ui.RECT)Marshal.PtrToStructure(m.LParam, typeof(Ui.RECT));
                MinimumSize = Size.Empty;
                if (Math.Abs(scale - Ui.S) > 0.01f) { Ui.SetScale(scale); Rebuild(); }
                Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                MinimumSize = MinSize();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // Window-wide input handling: the wheel scrolls the page wherever the focus is, and focus
        // rings appear only while the keyboard is being used.
        public bool PreFilterMessage(ref Message m)
        {
            if (!IsHandleCreated) return false;
            switch (m.Msg)
            {
                case 0x020A:   // WM_MOUSEWHEEL
                    if (current < 0 || !Ui.IsInWindow(m.HWnd, Handle)) return false;
                    pages[current].Wheel((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                    return true;

                case 0x0100:   // WM_KEYDOWN
                    int key = (int)m.WParam.ToInt64();
                    if ((key == 9 || (key >= 37 && key <= 40)) && Ui.IsInWindow(m.HWnd, Handle))
                    {
                        SetKeyFocus(true);
                        if (key == 9) BeginInvoke((MethodInvoker)RevealFocus);
                    }
                    return false;

                case 0x0201: case 0x0204: case 0x00A1:   // mouse buttons
                    SetKeyFocus(false);
                    return false;
            }
            return false;
        }

        static void SetKeyFocus(bool on)
        {
            if (Ui.KeyFocus == on) return;
            Ui.KeyFocus = on;
            Control f = Ui.FocusedControl();
            if (f != null) f.Invalidate();
        }

        void RevealFocus()
        {
            Control f = Ui.FocusedControl();
            if (f != null && current >= 0) pages[current].EnsureVisible(f);
        }

        void SelectPage(int i, bool animate)
        {
            if (i == current) return;
            current = i;
            nav.SelectedIndex = i;
            for (int j = 0; j < pages.Length; j++) pages[j].Visible = j == i;
            if (i == 1 && !camLoaded) LoadCameras();
            pages[i].Relayout();
            if (animate) pages[i].PlayEntrance();
        }

        static TextBlock Title(string t) { return new TextBlock(t, Ui.PageTitle, Ui.Text, 4, 0); }
        static TextBlock Section(string t) { return new TextBlock(t, Ui.Semibold, Ui.Text, 0, 0); }

        // ================= Light =================

        ScrollPage BuildLightPage()
        {
            var p = new ScrollPage();
            p.Add(Title("Light"), 0);

            hero = p.Add(new HeroCard(), 14);
            hero.PowerClicked += delegate { if (app.LightConnected) app.SetLightManual(!app.LightOn); };

            p.Add(Section("Adjust"), 24);
            var adj = p.Add(new Card(), 8);
            previewCardLight = adj;
            tPreviewLight = new Toggle(true);
            tPreviewLight.Toggled += delegate { SetPreview(tPreviewLight.Checked); };
            adj.Add(new SettingRow(G.Webcam, "Camera preview", "See how your lighting looks on camera while you adjust it", tPreviewLight));
            paneLight = adj.Add(new PreviewPane());
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
            rowTemp.Slider.Gradient = new[] { Ui.Tint(2700), Ui.Tint(3650), Ui.Tint(4600), Ui.Tint(5550), Ui.Tint(6500) };
            rowTemp.Slider.ValueChanged += delegate
            {
                if (loading) return;
                app.SetTemperature(rowTemp.Slider.Value);
                UpdatePresets(); UpdateHero();
            };

            presets = adj.Add(new ChipsRow("Presets"));
            for (int i = 0; i < PresetK.Length; i++)
            {
                int k = PresetK[i];
                var chip = presets.AddChip(PresetNames[i]);
                chip.Click += delegate
                {
                    app.SetTemperature(k);
                    loading = true; rowTemp.Slider.Value = k; loading = false;
                    rowTemp.Invalidate(); UpdatePresets(); UpdateHero();
                };
            }

            p.Add(Section("Smart activation"), 24);
            var smart = p.Add(new Card(), 8);
            tSmart = new Toggle(true);
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
            hero.SetState(app.LightOn, ok, app.S.TemperatureK, app.S.BrightnessPct);
        }

        public void RefreshStatus()
        {
            UpdateHero();
            bool ok = app.LightConnected;
            rowBright.Enabled = rowTemp.Enabled = ok;
            nav.SetStatus(
                app.CameraInUse ? "Camera on" : "Camera off", app.CameraInUse ? Ui.Red : Ui.Text3,
                !ok ? "No light found" : app.LightOn ? "Light on" : "Light off", ok && app.LightOn ? Ui.Accent : Ui.Text3);
        }

        // ================= Camera =================

        ScrollPage BuildCameraPage()
        {
            var p = new ScrollPage();
            p.Add(Title("Camera"), 0);
            var devCard = p.Add(new Card(), 14);
            ddCam = new DropDown { Placeholder = "No camera" };
            ddCam.SelectionChanged += delegate
            {
                var d = ddCam.SelectedItem as CamDevice;
                if (d == null) return;
                if (app.S.CameraId != d.Id) { app.S.CameraId = d.Id; app.S.CamValues.Clear(); app.S.Save(); }
                OpenCamera(d);
                if (previewOn) SetPreview(true);   // follow the newly chosen camera
            };
            devCard.Add(new SettingRow(G.Camera, "Device", "The webcam whose picture you want to adjust", ddCam));
            previewCardCam = devCard;
            tPreviewCam = new Toggle(true);
            tPreviewCam.Toggled += delegate { SetPreview(tPreviewCam.Checked); };
            devCard.Add(new SettingRow(G.Webcam, "Preview", "Watch the picture while you adjust it", tPreviewCam));
            paneCam = devCard.Add(new PreviewPane());
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
                        camPage.Add(Section(group[0]), 24);
                        card = camPage.Add(new Card(), 8);
                    }
                    card.Add(MakeCamRow(p));
                }
            }

            camPage.Add(Section("Tools"), 24);
            var tools = camPage.Add(new Card(), 8);
            var btnReset = new FlatButton("Reset", null, false);
            btnReset.Click += delegate { ResetCamera(); };
            tools.Add(new SettingRow(G.Reset, "Reset to defaults", "Restore the camera's factory picture settings", btnReset));
            var btnDriver = new FlatButton("Open", null, false);
            btnDriver.Click += delegate
            {
                if (cam == null) return;
                // Driver dialogs rarely handle per-monitor DPI: let Windows scale this one.
                IntPtr dpi = Ui.BeginSystemDpi();
                try { cam.ShowDriverDialog(Handle); }
                finally { Ui.EndSystemDpi(dpi); }
                ReadCameraValues();
            };
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

            var c1 = p.Add(new Card(), 14);
            tStartup = new Toggle(true);
            try { tStartup.SetInstant(app.StartWithWindows); } catch { }
            tStartup.Toggled += delegate
            {
                try { app.StartWithWindows = tStartup.Checked; }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't change the startup setting: " + ex.Message, "SmartCamLight"); }
            };
            c1.Add(new SettingRow(G.Startup, "Start with Windows", "Run SmartCamLight quietly in the tray when you sign in", tStartup));

            p.Add(Section("Ignored apps"), 24);
            var c2 = p.Add(new Card(), 8);
            var ignore = c2.Add(new InputRow(G.Block, "Don't react to these apps",
                "Camera use by these apps won't turn the light on. Separate names with commas, for example OBSBOT_Center, NVIDIA Broadcast."));
            ignore.Input.Box.Text = app.S.IgnoreApps;
            ignore.Input.Box.TextChanged += delegate { app.SetIgnore(ignore.Input.Box.Text); };

            p.Add(Section("About"), 24);
            var c3 = p.Add(new Card(), 8);
            string arch = RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
            c3.Add(new SettingRow(G.Info, "SmartCamLight 1.1", "Controls your Litra light and webcam without Logi Tune.  ·  " + arch + " build", null));
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
