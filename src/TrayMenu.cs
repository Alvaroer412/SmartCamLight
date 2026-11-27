using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SmartCamLight
{
    // A slider row for the tray menu, in the style of the Windows 11 quick settings: label and
    // value on one line, the slider below. It is a real child control hosted in the menu, so a
    // drag keeps the mouse even when it leaves the row, and a click never closes the menu.
    class MenuSlider : Control
    {
        readonly string label;
        readonly int min, max, step;
        int val;
        float s = 1f;
        bool hover, drag, keyFocus;
        Font font;

        public Func<int, string> Format = v => v.ToString();
        public Color[] Gradient;
        public event EventHandler ValueChanged;   // user changes only

        public MenuSlider(string label, int minimum, int maximum, int stepSize)
        {
            this.label = label;
            min = minimum; max = maximum; step = stepSize; val = minimum;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleName = label;
            AccessibleRole = AccessibleRole.Slider;
        }

        public int Value
        {
            get { return val; }
            set { int v = Clamp(value); if (v == val) return; val = v; AccessibleDescription = Format(val); Invalidate(); }
        }

        // Sizes for the monitor the menu opens on.
        public void SetMetrics(float scale, Font textFont)
        {
            s = scale; font = textFont;
            Invalidate();
        }

        int Px(float v) { return (int)Math.Round(v * s); }

        // The menu's minimum width for the row; MenuSliderItem stretches it to the menu's full width.
        public override Size GetPreferredSize(Size proposed) { return new Size(Px(240), Px(62)); }

        // ---- layout (control coordinates) ----

        MenuRenderer Renderer { get { var ts = Parent as ToolStrip; return ts == null ? null : ts.Renderer as MenuRenderer; } }

        // Left edge of the menu items' text, so the label lines up with "Light on" and friends.
        // (The menu already places the row in its text column, so this is normally 0.)
        int TextX
        {
            get
            {
                var r = Renderer;
                return r != null && r.TextLeft > 0 ? Math.Max(0, r.TextLeft - Left) : 0;
            }
        }

        int LineTop { get { return Px(6); } }
        int LineHeight { get { return Px(22); } }
        int RightPad { get { return Px(14); } }
        float Radius { get { return Px(10); } }
        float TrackY { get { return LineTop + LineHeight + Px(16); } }
        float L { get { return TextX + Radius; } }
        float R { get { return Width - RightPad - Radius; } }
        float Frac { get { return (val - min) / (float)(max - min); } }

        int Clamp(int v) { return Math.Max(min, Math.Min(max, v)); }

        void SetValueUser(int v)
        {
            v = Clamp(min + (int)Math.Round((v - min) / (double)step) * step);
            if (v == val) return;
            Value = v;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        void SetFromX(int x)
        {
            float f = Math.Max(0, Math.Min(1, (x - L) / Math.Max(1, R - L)));
            SetValueUser(min + (int)Math.Round(f * (max - min)));
        }

        // ---- input ----

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            keyFocus = false;
            if (e.Y < LineTop + LineHeight - Px(2)) { Invalidate(); return; }   // the label line does not jump the value
            drag = true; Capture = true;
            SetFromX(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!hover) { hover = true; Invalidate(); }
            if (drag) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = false; Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int notch = Math.Max(step, (max - min) / 20);
            SetValueUser(val + Math.Sign(e.Delta) * notch);
        }

        // Left and Right move the slider; Up and Down still move between the menu items.
        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
            }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            keyFocus = true;
            int big = Math.Max(step, (max - min) / 10);
            switch (e.KeyCode)
            {
                case Keys.Left: SetValueUser(val - step); break;
                case Keys.Right: SetValueUser(val + step); break;
                case Keys.PageDown: SetValueUser(val - big); break;
                case Keys.PageUp: SetValueUser(val + big); break;
                case Keys.Home: SetValueUser(min); break;
                case Keys.End: SetValueUser(max); break;
            }
            Invalidate();
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            keyFocus = MouseButtons == MouseButtons.None;   // arrived with the arrow keys
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); keyFocus = false; Invalidate(); }

        // ---- drawing ----

        Color GradientAt(float f)
        {
            if (Gradient == null || Gradient.Length == 0) return Ui.Accent;
            if (Gradient.Length == 1) return Gradient[0];
            float p = f * (Gradient.Length - 1);
            int i = Math.Min(Gradient.Length - 2, (int)p);
            return Ui.Blend(Gradient[i], Gradient[i + 1], p - i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.Menu);
            if (font == null) return;

            const TextFormatFlags one = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            int tx = TextX;
            var line = new Rectangle(tx, LineTop, Width - tx - RightPad, LineHeight);
            string value = Format(val);
            int vw = TextRenderer.MeasureText(g, value, font, Size.Empty, one).Width;
            // The label keeps GDI's usual side padding, as the menu's own item text does, so they line up.
            TextRenderer.DrawText(g, label, font, new Rectangle(line.X, line.Y, Math.Max(0, line.Width - vw - Px(12)), line.Height), Ui.Text,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, value, font, line, Ui.Text2, one | TextFormatFlags.Right);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float cy = TrackY, x = L + Frac * (R - L), hair = Math.Max(1f, (float)Math.Round(s));

            if (Gradient != null)
            {
                float th = Px(6);
                var tr = new RectangleF(L - th / 2, cy - th / 2, R - L + th, th);
                using (var lb = new LinearGradientBrush(new PointF(tr.Left - 1, 0), new PointF(tr.Right + 1, 0), Color.Black, Color.White))
                {
                    var blend = new ColorBlend(Gradient.Length);
                    for (int i = 0; i < Gradient.Length; i++)
                    {
                        blend.Colors[i] = Gradient[i];
                        blend.Positions[i] = i / (float)(Gradient.Length - 1);
                    }
                    lb.InterpolationColors = blend;
                    using (var p = Ui.Round(tr, th / 2)) g.FillPath(lb, p);
                }
                Ui.DrawRound(g, Ui.Stroke, tr, th / 2, hair);
            }
            else
            {
                float th = Px(4);
                Ui.FillRound(g, Ui.Rail, new RectangleF(x, cy - th / 2, R - x + th / 2, th), th / 2);
                Ui.FillRound(g, Ui.Accent, new RectangleF(L - th / 2, cy - th / 2, x - L + th / 2, th), th / 2);
            }

            float rad = Radius;
            using (var b = new SolidBrush(Ui.ThumbOuter)) g.FillEllipse(b, x - rad, cy - rad, rad * 2, rad * 2);
            using (var p = new Pen(Ui.ThumbStroke, hair)) g.DrawEllipse(p, x - rad, cy - rad, rad * 2, rad * 2);
            float ir = (Px(12) + (drag ? -Px(2) : hover ? Px(2) : 0)) / 2f;
            Color inner = Gradient != null ? GradientAt(Frac) : Ui.Accent;
            using (var b = new SolidBrush(inner)) g.FillEllipse(b, x - ir, cy - ir, ir * 2, ir * 2);
            if (Gradient != null)
                using (var p = new Pen(Ui.ThumbStroke, hair)) g.DrawEllipse(p, x - ir, cy - ir, ir * 2, ir * 2);

            if (Focused && keyFocus)
            {
                float fr = rad + Px(3), w = Math.Max(2f, (float)Math.Round(2 * s));
                var ring = new RectangleF(x - fr + w / 2, cy - fr + w / 2, fr * 2 - w, fr * 2 - w);
                Ui.DrawRound(g, Ui.Dark ? Color.White : Color.Black, ring, fr - w / 2, w);
            }
        }
    }

    // Hosts a MenuSlider in a ContextMenuStrip. The menu places the row in its text column at the
    // row's preferred width; the slider control itself is then stretched to the menu's right edge.
    // The item keeps its size, so the stretch never feeds back into the menu's own width.
    class MenuSliderItem : ToolStripControlHost
    {
        ToolStrip hooked;

        public MenuSliderItem(MenuSlider slider) : base(slider)
        {
            AutoSize = false;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
        }

        public MenuSlider Slider { get { return (MenuSlider)Control; } }

        public void Restyle(float scale, Font font)
        {
            Slider.SetMetrics(scale, font);
            Size = Slider.GetPreferredSize(Size.Empty);
        }

        // The control's size follows the item, never the other way round.
        protected override void OnHostedControlResize(EventArgs e) { }

        protected override void OnBoundsChanged() { base.OnBoundsChanged(); Stretch(); }

        protected override void OnOwnerChanged(EventArgs e)
        {
            base.OnOwnerChanged(e);
            if (hooked != null) hooked.Resize -= OwnerResized;
            hooked = Owner;
            if (hooked != null) hooked.Resize += OwnerResized;
        }

        void OwnerResized(object sender, EventArgs e) { Stretch(); }

        void Stretch()
        {
            if (Owner == null || Control == null) return;
            Rectangle b = Bounds;
            Control.Bounds = new Rectangle(b.X, b.Y, Math.Max(b.Width, Owner.DisplayRectangle.Right - b.X), b.Height);
        }
    }
}
