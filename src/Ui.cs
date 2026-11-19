using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SmartCamLight
{
    // Theme, fonts, DPI scale and drawing helpers (Windows 11 look, follows dark/light mode).
    static class Ui
    {
        public static bool Dark;
        public static float S = 1f;

        public static Color Back, Card, CardBorder, Hover, Pressed, Text, Text2, Text3,
            Accent, AccentHover, OnAccent, Track, ThumbOuter, Button, ButtonBorder, Input, Menu, Divider, Red, Green;

        public static Font Body, Strong, Small, Section, PageTitle, HeroTitle, Icon, IconSmall, IconLarge;

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            Dark = ReadDarkMode();
            if (Dark)
            {
                Back = Hex("#202020"); Card = Hex("#2B2B2B"); CardBorder = Hex("#1C1C1C");
                Hover = Hex("#353535"); Pressed = Hex("#303030");
                Text = Hex("#FFFFFF"); Text2 = Hex("#C8C8C8"); Text3 = Hex("#8A8A8A");
                Accent = Hex("#FFB83D"); AccentHover = Hex("#FFC65E"); OnAccent = Hex("#1F1A12");
                Track = Hex("#5A5A5A"); ThumbOuter = Hex("#454545");
                Button = Hex("#373737"); ButtonBorder = Hex("#424242"); Input = Hex("#2F2F2F");
                Menu = Hex("#2C2C2C"); Divider = Hex("#1F1F1F");
                Red = Hex("#FF6B6B"); Green = Hex("#6CCB5F");
            }
            else
            {
                Back = Hex("#F3F3F3"); Card = Hex("#FBFBFB"); CardBorder = Hex("#E5E5E5");
                Hover = Hex("#EDEDED"); Pressed = Hex("#E6E6E6");
                Text = Hex("#1A1A1A"); Text2 = Hex("#5C5C5C"); Text3 = Hex("#8E8E8E");
                Accent = Hex("#C77700"); AccentHover = Hex("#B36B00"); OnAccent = Hex("#FFFFFF");
                Track = Hex("#C4C4C4"); ThumbOuter = Hex("#FFFFFF");
                Button = Hex("#FFFFFF"); ButtonBorder = Hex("#DCDCDC"); Input = Hex("#FFFFFF");
                Menu = Hex("#F9F9F9"); Divider = Hex("#EAEAEA");
                Red = Hex("#C42B1C"); Green = Hex("#0F7B0F");
            }

            string text = Pick("Segoe UI Variable Text", "Segoe UI");
            string strong = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            string display = Pick("Segoe UI Variable Display Semibold", "Segoe UI Semibold");
            string icons = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
            Body = new Font(text, 10f);
            Strong = new Font(strong, 10f);
            Small = new Font(text, 8.5f);
            Section = new Font(strong, 10.5f);
            PageTitle = new Font(display, 20f);
            HeroTitle = new Font(display, 15f);
            Icon = new Font(icons, 12f);
            IconSmall = new Font(icons, 9f);
            IconLarge = new Font(icons, 22f);
        }

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

        static string Pick(string preferred, string fallback)
        {
            using (var f = new Font(preferred, 9f))
                return f.Name != "Microsoft Sans Serif" ? preferred : fallback;
        }

        public static Color Hex(string h)
        {
            return ColorTranslator.FromHtml(h);
        }

        public static int Px(float v) { return (int)Math.Round(v * S); }

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
            using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width)
        {
            using (var p = Round(r, radius)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void DrawText(Graphics g, string text, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, f, r, c, flags | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        public static Size Measure(string text, Font f, int width, bool wrap)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine);
            return TextRenderer.MeasureText(text, f, new Size(width <= 0 ? int.MaxValue : width, int.MaxValue), flags);
        }

        public static void Glyph(Graphics g, string glyph, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, glyph, f, r, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        // ---- window chrome ----

        public static void ThemeWindow(IntPtr hwnd)
        {
            try
            {
                int dark = Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);              // immersive dark title bar
                int caption = ColorTranslator.ToWin32(Back);
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);           // caption colour = window colour
                int textColor = ColorTranslator.ToWin32(Text);
                DwmSetWindowAttribute(hwnd, 36, ref textColor, 4);
                int round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);             // rounded corners
            }
            catch { }
        }

        public static void ThemeScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
    }

    // Glyph code points (Segoe Fluent Icons / MDL2 Assets).
    static class G
    {
        public const string Light = "", Camera = "", Settings = "", Power = "",
            Brightness = "", Color = "", Timer = "", Video = "", Folder = "",
            Info = "", Check = "", Chevron = "", Reset = "", Startup = "",
            Block = "", Sliders = "";
    }

    // Themed renderer for context menus (tray menu, dropdowns).
    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() { RoundedEdges = false; }

        public static void Apply(ContextMenuStrip m)
        {
            m.Renderer = new MenuRenderer();
            m.BackColor = Ui.Menu;
            m.ForeColor = Ui.Text;
            m.Font = Ui.Body;
            m.ShowImageMargin = true;
            m.Padding = new Padding(Ui.Px(2), Ui.Px(4), Ui.Px(2), Ui.Px(4));
        }

        public static void Style(ToolStripItem item)
        {
            item.Padding = new Padding(Ui.Px(2), Ui.Px(5), Ui.Px(2), Ui.Px(5));
            item.ForeColor = Ui.Text;
            var mi = item as ToolStripMenuItem;
            if (mi != null)
            {
                foreach (ToolStripItem sub in mi.DropDownItems) Style(sub);
                var dd = mi.DropDown as ToolStripDropDownMenu;
                if (dd != null)
                {
                    dd.Renderer = new MenuRenderer(); dd.BackColor = Ui.Menu; dd.Font = Ui.Body;
                    dd.Padding = new Padding(Ui.Px(2), Ui.Px(4), Ui.Px(2), Ui.Px(4));
                }
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Ui.Menu)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var p = new Pen(Ui.Dark ? Ui.Hex("#3A3A3A") : Ui.Hex("#D5D5D5"))) e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(Ui.Px(4), 1, e.Item.Width - Ui.Px(8), e.Item.Height - 2);
            Ui.FillRound(e.Graphics, Ui.Hover, r, Ui.Px(4));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ui.Text : Ui.Text3;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Ui.Dark ? Ui.Hex("#3A3A3A") : Ui.Hex("#E0E0E0")))
                e.Graphics.DrawLine(p, Ui.Px(8), y, e.Item.Width - Ui.Px(8), y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Ui.Glyph(e.Graphics, G.Check, Ui.IconSmall, Ui.Accent, e.ImageRectangle);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Ui.Text2;
            base.OnRenderArrow(e);
        }
    }
}
