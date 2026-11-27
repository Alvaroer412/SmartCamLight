using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SmartCamLight
{
    // Theme, fonts, DPI scale and drawing helpers.
    //
    // The window follows the Windows 11 design language: it sits on a Mica backdrop, cards are
    // translucent layers on top of it, and colours follow the system accent and dark/light mode.
    // To get that out of plain WinForms, every control renders into a 32-bit premultiplied ARGB
    // surface (GDI+ for shapes, composited theme text for labels and glyphs) and blits it to the
    // window, so the alpha channel reaches the desktop compositor intact.
    static class Ui
    {
        public static bool Dark;
        public static bool Glass;            // the window sits on a Mica backdrop (Windows 11 22H2+)
        public static bool KeyFocus;         // focus was last moved with the keyboard: show focus rings
        public static float S = 1f;
        public static int ThemeOverride;     // 0 = follow Windows, 1 = light, 2 = dark
        public static bool NoBackdrop;       // force the solid fallback background

        public static Color Back, Card, CardStroke, Divider, Subtle, SubtlePressed,
            Fill, FillHover, FillPressed, FillDisabled, Stroke, StrokeEdge,
            SwitchOff, SwitchOffHover, Strong, StrongDisabled, Rail, RailDisabled, ThumbOuter, ThumbStroke,
            Input, InputFocus, InputStroke, InputLine,
            Text, Text2, Text3, TextDisabled,
            Accent, AccentHover, AccentPressed, AccentDisabled, OnAccent, OnAccentDisabled,
            Menu, MenuBorder, MenuHover, Pill, Red, Green;

        public static Font Body, Semibold, Small, PageTitle, HeroTitle, Icon, IconSmall, IconLarge;
        public static string TextFamily, StrongFamily, DisplayFamily, SmallFamily, IconFamily;

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static readonly Dictionary<Font, IntPtr> hfonts = new Dictionary<Font, IntPtr>();

        public static void Init()
        {
            GdiSetBatchLimit(1);   // GDI text and GDI+ shapes share one surface: keep them in order
            TextFamily = Pick("Segoe UI Variable Text", "Segoe UI");
            StrongFamily = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            DisplayFamily = Pick("Segoe UI Variable Display Semibold", "Segoe UI Semibold");
            SmallFamily = Pick("Segoe UI Variable Small", "Segoe UI");
            IconFamily = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) SetScale(g.DpiX / 96f);
            LoadTheme();
        }

        // Sizes follow the Windows 11 type ramp (caption 12, body 14, subtitle 20, title 28).
        public static void SetScale(float scale)
        {
            S = scale;
            Body = FontAt(TextFamily, 14 * S);
            Semibold = FontAt(StrongFamily, 14 * S);
            Small = FontAt(SmallFamily, 12 * S);
            PageTitle = FontAt(DisplayFamily, 28 * S);
            HeroTitle = FontAt(DisplayFamily, 20 * S);
            Icon = FontAt(IconFamily, 16 * S);
            IconSmall = FontAt(IconFamily, 12 * S);
            IconLarge = FontAt(IconFamily, 24 * S);
        }

        // Fonts are cached for the life of the process (a handful per DPI scale).
        public static Font FontAt(string family, float pixels)
        {
            string key = family + "|" + (int)Math.Round(pixels * 4);
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(family, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        static IntPtr Hfont(Font f)
        {
            IntPtr h;
            if (!hfonts.TryGetValue(f, out h)) { h = f.ToHfont(); hfonts[f] = h; }
            return h;
        }

        static string Pick(string preferred, string fallback)
        {
            using (var f = new Font(preferred, 9f))
                return f.Name != "Microsoft Sans Serif" ? preferred : fallback;
        }

        // Reads dark/light mode and the accent colour. Returns true when anything changed.
        public static bool LoadTheme()
        {
            bool dark = ThemeOverride == 0 ? ReadDarkMode() : ThemeOverride == 2;
            bool glass = !NoBackdrop && Environment.OSVersion.Version.Build >= 22621 && !SystemInformation.HighContrast;
            Color accent = ReadAccent(dark);
            if (!Back.IsEmpty && dark == Dark && glass == Glass && accent.ToArgb() == Accent.ToArgb()) return false;

            Dark = dark; Glass = glass; Accent = accent;
            AccentHover = Color.FromArgb(230, accent);
            AccentPressed = Color.FromArgb(204, accent);
            if (dark)
            {
                Back = Hex("#202020");
                Card = W(13); CardStroke = K(25); Divider = W(21);
                Subtle = W(15); SubtlePressed = W(10);
                Fill = W(15); FillHover = W(21); FillPressed = W(8); FillDisabled = W(11);
                Stroke = W(18); StrokeEdge = W(28);
                SwitchOff = K(25); SwitchOffHover = W(11);
                Strong = W(139); StrongDisabled = W(40); Rail = W(139); RailDisabled = W(63);
                ThumbOuter = Hex("#454545"); ThumbStroke = W(31);
                Input = Hex("#343434"); InputFocus = Hex("#1F1F1F"); InputStroke = Hex("#414141"); InputLine = Hex("#9A9A9A");
                Text = Hex("#FFFFFF"); Text2 = Hex("#D0D0D0"); Text3 = Hex("#9E9E9E"); TextDisabled = Hex("#787878");
                AccentDisabled = W(40); OnAccent = Hex("#000000"); OnAccentDisabled = W(135);
                Menu = Hex("#2C2C2C"); MenuBorder = Hex("#454545"); MenuHover = W(15); Pill = W(15);
                Red = Hex("#FF6B6B"); Green = Hex("#6CCB5F");
            }
            else
            {
                Back = Hex("#F3F3F3");
                Card = W(179); CardStroke = K(15); Divider = K(15);
                Subtle = K(9); SubtlePressed = K(6);
                Fill = W(179); FillHover = Color.FromArgb(128, 249, 249, 249); FillPressed = Color.FromArgb(77, 249, 249, 249);
                FillDisabled = Color.FromArgb(77, 249, 249, 249);
                Stroke = K(15); StrokeEdge = K(41);
                SwitchOff = K(6); SwitchOffHover = K(15);
                Strong = K(114); StrongDisabled = K(55); Rail = K(114); RailDisabled = K(81);
                ThumbOuter = Hex("#FFFFFF"); ThumbStroke = K(36);
                Input = Hex("#FBFBFB"); InputFocus = Hex("#FFFFFF"); InputStroke = Hex("#E5E5E5"); InputLine = Hex("#868686");
                Text = Hex("#1A1A1A"); Text2 = Hex("#5F5F5F"); Text3 = Hex("#8A8A8A"); TextDisabled = Hex("#9F9F9F");
                AccentDisabled = K(55); OnAccent = Hex("#FFFFFF"); OnAccentDisabled = Hex("#FFFFFF");
                Menu = Hex("#F9F9F9"); MenuBorder = Hex("#D6D6D6"); MenuHover = K(9); Pill = K(9);
                Red = Hex("#C42B1C"); Green = Hex("#0F7B0F");
            }
            return true;
        }

        static Color W(int alpha) { return Color.FromArgb(alpha, 255, 255, 255); }
        static Color K(int alpha) { return Color.FromArgb(alpha, 0, 0, 0); }

        static bool ReadDarkMode()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v != null && Convert.ToInt32(v) == 0;
                }
            }
            catch { return false; }
        }

        // The accent shade Windows itself uses for controls: "light 2" on dark, "dark 1" on light.
        static Color ReadAccent(bool dark)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var pal = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (pal != null && pal.Length >= 28)
                    {
                        int i = dark ? 4 : 16;
                        return Color.FromArgb(pal[i], pal[i + 1], pal[i + 2]);
                    }
                }
            }
            catch { }
            return dark ? Hex("#60CDFF") : Hex("#005FB8");
        }

        public static Color Hex(string h)
        {
            return ColorTranslator.FromHtml(h);
        }

        public static int Px(float v) { return (int)Math.Round(v * S); }

        // One effective pixel, snapped to whole device pixels (for hairline strokes).
        public static float Hair { get { return Math.Max(1f, (float)Math.Round(S)); } }

        // Rectangle whose hairline outline lies exactly on the outermost pixels of a w x h area.
        public static RectangleF Edge(float w, float h)
        {
            float t = Hair;
            return new RectangleF(t / 2, t / 2, w - t, h - t);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Approximate colour of a black-body light at the given temperature.
        public static Color Kelvin(int k)
        {
            double t = k / 100.0, r, g, b;
            if (t <= 66) { r = 255; g = 99.4708025861 * Math.Log(t) - 161.1195681661; }
            else { r = 329.698727446 * Math.Pow(t - 60, -0.1332047592); g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492); }
            if (t >= 66) b = 255; else if (t <= 19) b = 0; else b = 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            return Color.FromArgb(C(r), C(g), C(b));
        }

        static int C(double v) { return (int)Math.Max(0, Math.Min(255, v)); }

        // Colour used to show a temperature in the interface: the same warm-to-cool ramp, stretched
        // so the cool end reads as cool (a real 6500 K is close to plain white).
        public static Color Tint(int kelvin)
        {
            double f = Math.Max(0, Math.Min(1, (kelvin - 2700) / 3800.0));
            return Kelvin((int)(2200 + f * 6000));
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var p = Round(r, radius)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        // Control outline with the Windows 11 "elevation" edge: a lighter top edge in dark mode,
        // a darker bottom edge in light mode.
        public static void Elevation(Graphics g, RectangleF r, float radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            float t = Math.Min(0.45f, 3f * S / r.Height);
            using (var p = Round(r, radius))
            using (var lb = new LinearGradientBrush(new PointF(0, r.Top - 1), new PointF(0, r.Bottom + 1), Stroke, Stroke))
            {
                var cb = new ColorBlend(3);
                if (Dark) { cb.Colors = new[] { StrokeEdge, Stroke, Stroke }; cb.Positions = new[] { 0f, t, 1f }; }
                else { cb.Colors = new[] { Stroke, Stroke, StrokeEdge }; cb.Positions = new[] { 0f, 1f - t, 1f }; }
                lb.InterpolationColors = cb;
                using (var pen = new Pen(lb, Hair)) g.DrawPath(pen, p);
            }
        }

        // Keyboard focus ring; its outer edge lies on r.
        public static void FocusRing(Graphics g, RectangleF r, float radius)
        {
            float w = Math.Max(2f, (float)Math.Round(2 * S));
            r.Inflate(-w / 2, -w / 2);
            DrawRound(g, Dark ? Color.White : Color.Black, r, Math.Max(0, radius - w / 2), w);
        }

        // ---- off-screen surface ----

        static IntPtr sDc, sDib, sOld, sBits, theme;
        static int sW, sH, ox, oy;
        static Bitmap sBmp;
        static bool painting, themeTried;

        static void EnsureSurface(int w, int h)
        {
            if (sDc != IntPtr.Zero && w <= sW && h <= sH) return;
            if (sBmp != null) { sBmp.Dispose(); sBmp = null; }
            if (sDc != IntPtr.Zero)
            {
                SelectObject(sDc, sOld);
                DeleteObject(sDib);
                DeleteDC(sDc);
                sDc = IntPtr.Zero;
            }
            sW = (Math.Max(w, sW) + 255) & ~255;
            sH = (Math.Max(h, sH) + 255) & ~255;
            var bi = new BITMAPINFOHEADER();
            bi.biSize = 40; bi.biWidth = sW; bi.biHeight = -sH;   // top-down, as composited text requires
            bi.biPlanes = 1; bi.biBitCount = 32;
            sDc = CreateCompatibleDC(IntPtr.Zero);
            sDib = CreateDIBSection(sDc, ref bi, 0, out sBits, IntPtr.Zero, 0);
            sOld = SelectObject(sDc, sDib);
            sBmp = new Bitmap(sW, sH, sW * 4, PixelFormat.Format32bppPArgb, sBits);
        }

        // Frees the off-screen surface (it is recreated on demand). The tray app outlives the
        // settings window, so the memory is handed back when that window closes.
        public static void ReleaseSurface()
        {
            if (painting || sDc == IntPtr.Zero) return;
            if (sBmp != null) { sBmp.Dispose(); sBmp = null; }
            SelectObject(sDc, sOld);
            DeleteObject(sDib);
            DeleteDC(sDc);
            sDc = IntPtr.Zero; sW = sH = 0;
        }

        // Renders a control through the shared ARGB surface. Only the invalid part is drawn.
        public static void Paint(Control c, PaintEventArgs e, bool onCard, Action<Graphics> draw)
        {
            Rectangle clip = Rectangle.Intersect(e.ClipRectangle, c.ClientRectangle);
            if (clip.Width <= 0 || clip.Height <= 0) return;
            if (painting) { c.BeginInvoke((MethodInvoker)c.Invalidate); return; }
            painting = true;
            try
            {
                EnsureSurface(clip.Width, clip.Height);
                ox = clip.X; oy = clip.Y;
                using (var g = Graphics.FromImage(sBmp))
                {
                    g.SetClip(new Rectangle(0, 0, clip.Width, clip.Height));
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (var b = new SolidBrush(Glass ? Color.FromArgb(0, 0, 0, 0) : Back)) g.FillRectangle(b, 0, 0, clip.Width, clip.Height);
                    g.CompositingMode = CompositingMode.SourceOver;
                    if (onCard) using (var b = new SolidBrush(Card)) g.FillRectangle(b, 0, 0, clip.Width, clip.Height);
                    g.TranslateTransform(-ox, -oy);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    draw(g);
                }
                GdiFlush();
                IntPtr hdc = e.Graphics.GetHdc();
                try { BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, sDc, 0, 0, 0x00CC0020); }
                finally { e.Graphics.ReleaseHdc(hdc); }
            }
            finally { painting = false; }
        }

        // Paints the bare backdrop: fully transparent over Mica, or the solid fallback colour.
        public static void Erase(PaintEventArgs e)
        {
            Rectangle r = e.ClipRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            IntPtr hdc = e.Graphics.GetHdc();
            try
            {
                if (Glass) PatBlt(hdc, r.X, r.Y, r.Width, r.Height, 0x00000042);   // BLACKNESS: zero colour, zero alpha
                else
                {
                    var rc = new RECT(r.Left, r.Top, r.Right, r.Bottom);
                    IntPtr brush = CreateSolidBrush(ColorTranslator.ToWin32(Back));
                    FillRect(hdc, ref rc, brush);
                    DeleteObject(brush);
                }
            }
            finally { e.Graphics.ReleaseHdc(hdc); }
        }

        // ---- text ----

        const int DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_WORDBREAK = 0x10, DT_SINGLELINE = 0x20, DT_NOCLIP = 0x100,
            DT_CALCRECT = 0x400, DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000;

        // Draws text on the control being painted. Only valid inside a Paint callback.
        public static void DrawText(Graphics g, string text, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            int dt = ((int)flags & 0xFFFF) | DT_NOPREFIX | DT_END_ELLIPSIS;
            if ((dt & DT_WORDBREAK) == 0) dt |= DT_SINGLELINE;
            RawText(g, text, f, c, r, dt);
        }

        public static void Glyph(Graphics g, string glyph, Font f, Color c, Rectangle r)
        {
            RawText(g, glyph, f, c, r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOCLIP | DT_NOPREFIX);
        }

        static void RawText(Graphics g, string text, Font f, Color c, Rectangle r, int dt)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 0 || r.Height <= 0 || !painting) return;
            g.Flush();
            if (!themeTried) { themeTried = true; theme = OpenThemeData(IntPtr.Zero, "CompositedWindow::Window"); }
            var rc = new RECT(r.Left - ox, r.Top - oy, r.Right - ox, r.Bottom - oy);
            IntPtr old = SelectObject(sDc, Hfont(f));
            if (theme != IntPtr.Zero)
            {
                // Composited text writes correct alpha, so it stays readable over the translucent backdrop.
                var o = new DTTOPTS();
                o.dwSize = Marshal.SizeOf(typeof(DTTOPTS));
                o.dwFlags = 0x2000 | 0x1;   // DTT_COMPOSITED | DTT_TEXTCOLOR
                o.crText = ColorTranslator.ToWin32(c);
                DrawThemeTextEx(theme, sDc, 0, 0, text, text.Length, dt, ref rc, ref o);
            }
            else
            {
                SetBkMode(sDc, 1);
                SetTextColor(sDc, ColorTranslator.ToWin32(c));
                DrawTextW(sDc, text, text.Length, ref rc, dt);
            }
            SelectObject(sDc, old);
        }

        public static Size Measure(string text, Font f, int width, bool wrap)
        {
            bool empty = string.IsNullOrEmpty(text);
            if (empty) text = " ";
            EnsureSurface(1, 1);
            var rc = new RECT(0, 0, width <= 0 ? 0 : width, 0);
            IntPtr old = SelectObject(sDc, Hfont(f));
            DrawTextW(sDc, text, text.Length, ref rc, DT_CALCRECT | DT_NOPREFIX | (wrap && width > 0 ? DT_WORDBREAK : DT_SINGLELINE));
            SelectObject(sDc, old);
            return new Size(empty ? 0 : rc.Right - rc.Left, rc.Bottom - rc.Top);
        }

        // ---- window chrome ----

        public static void ThemeWindow(IntPtr hwnd)
        {
            try
            {
                int v = Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref v, 4);               // immersive dark title bar
                if (Glass)
                {
                    v = 2;
                    DwmSetWindowAttribute(hwnd, 38, ref v, 4);           // system backdrop: Mica
                    var m = new MARGINS(); m.Left = m.Right = m.Top = m.Bottom = -1;
                    DwmExtendFrameIntoClientArea(hwnd, ref m);            // ...across the whole client area
                }
                else
                {
                    v = ColorTranslator.ToWin32(Back);
                    DwmSetWindowAttribute(hwnd, 35, ref v, 4);           // caption colour = window colour
                    v = ColorTranslator.ToWin32(Text);
                    DwmSetWindowAttribute(hwnd, 36, ref v, 4);
                }
            }
            catch { }
        }

        // Rounded corners and a hairline border for popup menus, as on Windows 11.
        public static void RoundPopup(IntPtr hwnd)
        {
            try
            {
                int v = 2;
                DwmSetWindowAttribute(hwnd, 33, ref v, 4);
                v = ColorTranslator.ToWin32(MenuBorder);
                DwmSetWindowAttribute(hwnd, 34, ref v, 4);
                v = Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref v, 4);
            }
            catch { }
        }

        public static bool RoundedPopups { get { return Environment.OSVersion.Version.Build >= 22000; } }

        // Makes a child window opaque to the compositor (its GDI drawing carries no alpha of its own).
        public static void MakeOpaque(IntPtr hwnd)
        {
            try { SetLayeredWindowAttributes(hwnd, 0, 255, 2); } catch { }
        }

        // DPI scale of the monitor under a screen point (the system scale when not per-monitor aware).
        public static float ScaleAt(Point p)
        {
            try
            {
                var pt = new POINT(); pt.X = p.X; pt.Y = p.Y;
                uint dx, dy;
                if (GetDpiForMonitor(MonitorFromPoint(pt, 2), 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        // DPI scale a window is actually rendered at.
        public static float ScaleOf(IntPtr hwnd)
        {
            try { int dpi = GetDpiForWindow(hwnd); if (dpi > 0) return dpi / 96f; } catch { }
            return S;
        }

        // Windows created between these two calls are scaled by the system instead of per monitor
        // (for third-party dialogs that don't handle per-monitor DPI themselves).
        public static IntPtr BeginSystemDpi()
        {
            try { return SetThreadDpiAwarenessContext(new IntPtr(-2)); } catch { return IntPtr.Zero; }
        }

        public static void EndSystemDpi(IntPtr previous)
        {
            try { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); } catch { }
        }

        public static Control FocusedControl()
        {
            IntPtr h = GetFocus();
            return h == IntPtr.Zero ? null : Control.FromHandle(h);
        }

        public static bool IsInWindow(IntPtr child, IntPtr window)
        {
            return child == window || GetAncestor(child, 2) == window;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DTTOPTS
        {
            public int dwSize, dwFlags, crText, crBorder, crShadow, iTextShadowType;
            public POINT ptShadowOffset;
            public int iBorderSize, iFontPropId, iColorPropId, iStateId, fApplyOverlay, iGlowSize;
            public IntPtr pfnDrawTextCallback, lParam;
        }

        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, int rop);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
        [DllImport("gdi32.dll")] static extern int GdiSetBatchLimit(int limit);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int color);
        [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr hdc, string text, int len, ref RECT r, int flags);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, int key, byte alpha, int flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern IntPtr GetFocus();
        [DllImport("user32.dll")] static extern int GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, int flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenThemeData(IntPtr hwnd, string classList);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int DrawThemeTextEx(IntPtr theme, IntPtr hdc, int part, int state, string text, int len, int flags, ref RECT r, ref DTTOPTS options);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    }

    // Glyph code points (Segoe Fluent Icons / MDL2 Assets).
    static class G
    {
        public const string Light = "", Camera = "", Settings = "", Power = "",
            Brightness = "", Color = "", Timer = "", Video = "", Folder = "",
            Info = "", Check = "", Chevron = "", Reset = "", Startup = "",
            Block = "", Sliders = "", Webcam = "";
    }

    // Themed renderer for context menus (tray menu, dropdowns). Each menu is styled for the DPI
    // scale of the monitor it opens on, which can differ from the settings window's.
    class MenuRenderer : ToolStripProfessionalRenderer
    {
        readonly float s;
        ToolStrip root;

        // Where the top-level menu draws item text (menu coordinates), noted while painting so the
        // slider rows can line up with the ordinary items.
        public int TextLeft = -1;

        MenuRenderer(float scale)
        {
            s = scale;
            RoundedEdges = false;
        }

        int Px(float v) { return (int)Math.Round(v * s); }

        public static void Apply(ContextMenuStrip menu, float scale)
        {
            var r = new MenuRenderer(scale) { root = menu };
            Font font = Ui.FontAt(Ui.TextFamily, 14 * scale);
            r.Style(menu, font);
            foreach (ToolStripItem item in menu.Items) r.Style(item, font);
        }

        void Note(ToolStrip owner, ref int field, int value)
        {
            if (owner != root || field == value) return;
            field = value;
            foreach (ToolStripItem item in owner.Items)
                if (item is MenuSliderItem) ((MenuSliderItem)item).Control.Invalidate();
        }

        void Style(ToolStripDropDown dd, Font font)
        {
            dd.Renderer = this;
            dd.BackColor = Ui.Menu;
            dd.ForeColor = Ui.Text;
            dd.Font = font;
            dd.Padding = new Padding(Px(3), Px(5), Px(3), Px(5));
            var ddm = dd as ToolStripDropDownMenu;
            if (ddm != null) ddm.ShowImageMargin = true;
            if (dd.Tag == null)
            {
                dd.Tag = "themed";
                dd.HandleCreated += delegate { Ui.RoundPopup(dd.Handle); };
            }
            if (dd.IsHandleCreated) Ui.RoundPopup(dd.Handle);
        }

        void Style(ToolStripItem item, Font font)
        {
            var slider = item as MenuSliderItem;
            if (slider != null) { slider.Restyle(s, font); return; }
            item.Padding = new Padding(Px(2), Px(6), Px(2), Px(6));
            item.ForeColor = Ui.Text;
            var mi = item as ToolStripMenuItem;
            if (mi != null && mi.HasDropDownItems)
            {
                Style(mi.DropDown, font);
                foreach (ToolStripItem sub in mi.DropDownItems) Style(sub, font);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Ui.Menu)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (Ui.RoundedPopups) return;   // the compositor draws the rounded border
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var p = new Pen(Ui.MenuBorder)) e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var r = new RectangleF(Px(2), 1, e.Item.Width - Px(4), e.Item.Height - 2);
            Ui.FillRound(e.Graphics, Ui.MenuHover, r, Px(4));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Right-aligned text is the shortcut column, used for secondary details such as "2700 K".
            bool detail = (e.TextFormat & TextFormatFlags.Right) != 0;
            e.TextColor = !e.Item.Enabled ? Ui.Text3 : detail ? Ui.Text2 : Ui.Text;
            if (!detail && e.Item is ToolStripMenuItem) Note(e.ToolStrip, ref TextLeft, e.Item.Bounds.X + e.TextRectangle.X);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Ui.Divider))
                e.Graphics.DrawLine(p, 0, y, e.Item.Width, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Drawn as a path rather than a font glyph, so it stays free of ClearType colour fringes.
            Rectangle r = e.ImageRectangle;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var pen = new Pen(Ui.Text, Math.Max(1f, 1.3f * s)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(cx - 4.5f * s, cy + 0.3f * s), new PointF(cx - 1.5f * s, cy + 3.3f * s), new PointF(cx + 4.8f * s, cy - 3.3f * s)
                });
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Ui.Text2;
            base.OnRenderArrow(e);
        }
    }
}
