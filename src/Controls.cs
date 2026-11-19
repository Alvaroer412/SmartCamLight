using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SmartCamLight
{
    interface IAutoHeight { int MeasureHeight(int width); }

    abstract class UiControl : Control
    {
        protected bool hover, pressed;

        protected UiControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected static Graphics Smooth(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return e.Graphics;
        }
    }

    // ---------------- Toggle switch ----------------
    class Toggle : UiControl
    {
        bool on;
        float pos;
        readonly Timer anim = new Timer { Interval = 15 };
        public event EventHandler Toggled;

        public Toggle()
        {
            Size = new Size(Ui.Px(40), Ui.Px(20));
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            anim.Tick += delegate
            {
                float target = on ? 1 : 0;
                pos += (target - pos) * 0.35f;
                if (Math.Abs(target - pos) < 0.02f) { pos = target; anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; anim.Start(); }
        }

        public void SetInstant(bool value) { on = value; pos = value ? 1 : 0; anim.Stop(); Invalidate(); }

        void Flip()
        {
            if (!Enabled) return;
            Checked = !on;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e) { Flip(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Flip(); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float rad = r.Height / 2;
            Color knob;
            if (on)
            {
                Ui.FillRound(g, !Enabled ? Ui.Text3 : (hover ? Ui.AccentHover : Ui.Accent), r, rad);
                knob = Ui.OnAccent;
            }
            else
            {
                Ui.FillRound(g, hover && Enabled ? Ui.Hover : BackColor, r, rad);
                Ui.DrawRound(g, Enabled ? Ui.Text2 : Ui.Text3, r, rad, Math.Max(1f, Ui.S));
                knob = Enabled ? Ui.Text2 : Ui.Text3;
            }
            float kr = r.Height * (hover && Enabled ? 0.31f : 0.26f);
            float cx = r.X + rad + (r.Width - 2 * rad) * pos;
            float cy = r.Y + r.Height / 2;
            using (var b = new SolidBrush(knob)) g.FillEllipse(b, cx - kr, cy - kr, kr * 2, kr * 2);
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    // ---------------- Slider ----------------
    class ModernSlider : UiControl
    {
        int min, max = 100, val, step = 1;
        bool drag;
        public Color[] Gradient;
        public event EventHandler ValueChanged;   // user changes only

        public ModernSlider()
        {
            Height = Ui.Px(28);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void SetRange(int minimum, int maximum, int stepSize)
        {
            min = minimum; max = Math.Max(minimum + 1, maximum); step = Math.Max(1, stepSize);
            val = Clamp(val); Invalidate();
        }

        public int Value { get { return val; } set { val = Clamp(value); Invalidate(); } }
        public int Minimum { get { return min; } }
        public int Maximum { get { return max; } }

        int Clamp(int v) { return Math.Max(min, Math.Min(max, v)); }
        float Radius { get { return Ui.Px(10); } }
        float L { get { return Radius + 1; } }
        float R { get { return Width - Radius - 1; } }
        float Frac { get { return (val - min) / (float)(max - min); } }

        void SetValueUser(int v)
        {
            v = Clamp(min + (int)Math.Round((v - min) / (double)step) * step);
            if (v == val) return;
            val = v; Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        void SetFromX(int x)
        {
            float f = Math.Max(0, Math.Min(1, (x - L) / Math.Max(1, R - L)));
            SetValueUser(min + (int)Math.Round(f * (max - min)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus(); drag = true; Capture = true; SetFromX(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; Capture = false; Invalidate(); }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int big = Math.Max(step, (max - min) / 10);
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: SetValueUser(val - step); break;
                case Keys.Right: case Keys.Up: SetValueUser(val + step); break;
                case Keys.PageDown: SetValueUser(val - big); break;
                case Keys.PageUp: SetValueUser(val + big); break;
                case Keys.Home: SetValueUser(min); break;
                case Keys.End: SetValueUser(max); break;
            }
            base.OnKeyDown(e);
        }

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
            var g = Smooth(e);
            float cy = Height / 2f;
            float x = L + Frac * (R - L);

            if (Gradient != null)
            {
                float th = Ui.Px(6);
                var tr = new RectangleF(L - th / 2, cy - th / 2, R - L + th, th);
                using (var lb = new LinearGradientBrush(new PointF(tr.Left - 1, 0), new PointF(tr.Right + 1, 0), Color.Black, Color.White))
                {
                    var blend = new ColorBlend(Gradient.Length);
                    for (int i = 0; i < Gradient.Length; i++)
                    {
                        blend.Colors[i] = Enabled ? Gradient[i] : Ui.Blend(Gradient[i], Ui.Track, 0.7f);
                        blend.Positions[i] = i / (float)(Gradient.Length - 1);
                    }
                    lb.InterpolationColors = blend;
                    using (var p = Ui.Round(tr, th / 2)) g.FillPath(lb, p);
                }
            }
            else
            {
                float th = Ui.Px(4);
                Ui.FillRound(g, Ui.Track, new RectangleF(L - th / 2, cy - th / 2, R - L + th, th), th / 2);
                Ui.FillRound(g, Enabled ? Ui.Accent : Ui.Text3, new RectangleF(L - th / 2, cy - th / 2, x - L + th, th), th / 2);
            }

            float rad = Radius;
            using (var b = new SolidBrush(Ui.ThumbOuter)) g.FillEllipse(b, x - rad, cy - rad, rad * 2, rad * 2);
            using (var p = new Pen(Ui.Dark ? Ui.Hex("#505050") : Ui.Hex("#D6D6D6"), Math.Max(1f, Ui.S))) g.DrawEllipse(p, x - rad, cy - rad, rad * 2, rad * 2);
            float ir = rad * (drag ? 0.45f : (hover ? 0.62f : 0.52f));
            Color inner = !Enabled ? Ui.Text3 : (Gradient != null ? GradientAt(Frac) : Ui.Accent);
            using (var b = new SolidBrush(inner)) g.FillEllipse(b, x - ir, cy - ir, ir * 2, ir * 2);
        }
    }

    // ---------------- Buttons ----------------
    class FlatButton : UiControl
    {
        readonly string glyph;
        readonly bool primary;
        bool selected;

        public FlatButton(string text, string glyph, bool primary)
        {
            Text = text; this.glyph = glyph; this.primary = primary;
            Cursor = Cursors.Hand;
            Fit();
        }

        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public void Fit()
        {
            int w = Ui.Measure(Text, Ui.Body, 0, false).Width + Ui.Px(28);
            if (glyph != null) w += Ui.Px(24);
            Size = new Size(w, Ui.Px(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, fg;
            if (primary)
            {
                fill = !Enabled ? Ui.Text3 : (pressed ? Ui.Accent : hover ? Ui.AccentHover : Ui.Accent);
                fg = Ui.OnAccent;
            }
            else
            {
                fill = pressed ? Ui.Pressed : hover ? Ui.Hover : Ui.Button;
                fg = Enabled ? Ui.Text : Ui.Text3;
            }
            Ui.FillRound(g, fill, r, Ui.Px(5));
            if (!primary) Ui.DrawRound(g, selected ? Ui.Accent : Ui.ButtonBorder, r, Ui.Px(5), selected ? Math.Max(1.5f, 1.5f * Ui.S) : 1f);
            if (selected && !primary) fg = Ui.Accent;

            int tw = Ui.Measure(Text, Ui.Body, 0, false).Width;
            int total = tw + (glyph != null ? Ui.Px(24) : 0);
            int x = (Width - total) / 2;
            if (glyph != null)
            {
                Ui.Glyph(g, glyph, Ui.IconSmall, fg, new Rectangle(x, 0, Ui.Px(16), Height));
                x += Ui.Px(24);
            }
            Ui.DrawText(g, Text, Ui.Body, fg, new Rectangle(x, 0, tw + 2, Height), TextFormatFlags.VerticalCenter);
        }
    }

    class DropDown : UiControl
    {
        readonly List<object> items = new List<object>();
        int sel = -1;
        public event EventHandler SelectionChanged;

        public DropDown() { Size = new Size(Ui.Px(300), Ui.Px(32)); Cursor = Cursors.Hand; }

        public void SetItems(IEnumerable<object> list, object selected)
        {
            items.Clear(); items.AddRange(list);
            sel = selected == null ? -1 : items.IndexOf(selected);
            Invalidate();
        }

        public object SelectedItem { get { return sel >= 0 && sel < items.Count ? items[sel] : null; } }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var menu = new ContextMenuStrip();
            MenuRenderer.Apply(menu);
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var mi = new ToolStripMenuItem(items[i].ToString()) { Checked = i == sel };
                mi.Click += delegate
                {
                    if (idx == sel) return;
                    sel = idx; Invalidate();
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                };
                MenuRenderer.Style(mi);
                menu.Items.Add(mi);
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += delegate { BeginInvoke((MethodInvoker)menu.Dispose); };
            menu.Show(this, new Point(0, Height + Ui.Px(2)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, pressed ? Ui.Pressed : hover ? Ui.Hover : Ui.Button, r, Ui.Px(5));
            Ui.DrawRound(g, Ui.ButtonBorder, r, Ui.Px(5), 1f);
            string text = SelectedItem != null ? SelectedItem.ToString() : "No camera";
            Ui.DrawText(g, text, Ui.Body, Ui.Text, new Rectangle(Ui.Px(12), 0, Width - Ui.Px(44), Height), TextFormatFlags.VerticalCenter);
            Ui.Glyph(g, G.Chevron, Ui.IconSmall, Ui.Text2, new Rectangle(Width - Ui.Px(32), 0, Ui.Px(20), Height));
        }
    }

    class InputBox : UiControl
    {
        public readonly TextBox Box;

        public InputBox()
        {
            Height = Ui.Px(34);
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Ui.Input, ForeColor = Ui.Text, Font = Ui.Body };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
            Cursor = Cursors.IBeam;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Box != null) Box.SetBounds(Ui.Px(11), (Height - Box.Height) / 2, Width - Ui.Px(22), Box.Height);
            base.OnLayout(e);
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, Ui.Input, r, Ui.Px(5));
            Ui.DrawRound(g, Ui.ButtonBorder, r, Ui.Px(5), 1f);
            bool focus = Box.Focused;
            float lh = focus ? Ui.Px(2) : 1;
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(focus ? Ui.Accent : Ui.Text3))
                g.FillRectangle(b, Ui.Px(3), Height - lh - 1, Width - Ui.Px(6), lh);
        }
    }

    // ---------------- Layout containers ----------------
    class ScrollPage : Panel
    {
        readonly List<Control> items = new List<Control>();
        readonly Dictionary<Control, int> gaps = new Dictionary<Control, int>();

        public ScrollPage()
        {
            AutoScroll = true; BackColor = Ui.Back; Dock = DockStyle.Fill; DoubleBuffered = true;
        }

        public T Add<T>(T c, int gapBefore) where T : Control
        {
            items.Add(c); gaps[c] = Ui.Px(gapBefore);
            Controls.Add(c);
            return c;
        }

        public void ClearItems(int keep)
        {
            SuspendLayout();
            while (items.Count > keep)
            {
                Control c = items[items.Count - 1];
                items.RemoveAt(items.Count - 1); gaps.Remove(c);
                Controls.Remove(c); c.Dispose();
            }
            ResumeLayout();
        }

        public void Relayout()
        {
            int padL = Ui.Px(12), padR = Ui.Px(28);
            int w = Math.Min(ClientSize.Width - padL - padR, Ui.Px(860));
            if (w <= 0) return;
            int y = Ui.Px(14);
            SuspendLayout();
            foreach (var c in items)
            {
                y += gaps[c];
                var ah = c as IAutoHeight;
                int h = ah != null ? ah.MeasureHeight(w) : c.Height;
                c.SetBounds(padL, y + AutoScrollPosition.Y, w, h);
                y += h;
            }
            AutoScrollMinSize = new Size(0, y + Ui.Px(28));
            ResumeLayout();
            foreach (var c in items) c.PerformLayout();
        }

        protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); Relayout(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.ThemeScrollbars(this); }
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
    }

    class Card : Panel, IAutoHeight
    {
        readonly List<Control> rows = new List<Control>();
        readonly List<int> dividers = new List<int>();

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Back;
        }

        public T Add<T>(T row) where T : Control
        {
            row.BackColor = Ui.Card;
            rows.Add(row); Controls.Add(row);
            return row;
        }

        int PadX { get { return Ui.Px(16); } }

        static int RowHeight(Control c, int w)
        {
            var ah = c as IAutoHeight;
            return ah != null ? ah.MeasureHeight(w) : c.Height;
        }

        public int MeasureHeight(int width)
        {
            int inner = width - 2 * PadX, h = Ui.Px(2) * 2;
            for (int i = 0; i < rows.Count; i++) h += RowHeight(rows[i], inner) + (i > 0 ? 1 : 0);
            return h;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int w = Width - 2 * PadX, y = Ui.Px(2);
            dividers.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) { dividers.Add(y); y += 1; }
                int h = RowHeight(rows[i], w);
                rows[i].SetBounds(PadX, y, w, h);
                y += h;
            }
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Ui.Back)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, Ui.Card, r, Ui.Px(8));
            Ui.DrawRound(g, Ui.CardBorder, r, Ui.Px(8), 1f);
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Ui.Divider))
                foreach (int y in dividers) g.DrawLine(p, 1, y, Width - 2, y);
        }
    }

    // ---------------- Rows ----------------
    class TextBlock : UiControl, IAutoHeight
    {
        readonly Font font; Color color; readonly int padTop, padBottom;

        public TextBlock(string text, Font font, Color color, int padTop, int padBottom)
        {
            Text = text; this.font = font; this.color = color; this.padTop = Ui.Px(padTop); this.padBottom = Ui.Px(padBottom);
        }

        public int MeasureHeight(int width) { return padTop + Ui.Measure(Text, font, width, true).Height + padBottom; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.DrawText(e.Graphics, Text, font, color, new Rectangle(0, padTop, Width, Height - padTop), TextFormatFlags.WordBreak);
        }
    }

    class SettingRow : UiControl, IAutoHeight
    {
        string glyph, title, desc;
        readonly Control accessory;

        public SettingRow(string glyph, string title, string desc, Control accessory)
        {
            this.glyph = glyph; this.title = title; this.desc = desc; this.accessory = accessory;
            if (accessory != null) Controls.Add(accessory);
        }

        public void SetText(string newTitle, string newDesc) { title = newTitle; desc = newDesc; Invalidate(); }

        int TextLeft { get { return glyph != null ? Ui.Px(40) : 0; } }
        int TextWidth(int w) { return w - TextLeft - (accessory != null ? accessory.Width + Ui.Px(20) : 0); }

        public int MeasureHeight(int width)
        {
            int th = Ui.Measure(title, Ui.Strong, 0, false).Height;
            int dh = string.IsNullOrEmpty(desc) ? 0 : Ui.Measure(desc, Ui.Small, TextWidth(width), true).Height + Ui.Px(3);
            int h = th + dh + Ui.Px(28);
            if (accessory != null) h = Math.Max(h, accessory.Height + Ui.Px(24));
            return Math.Max(h, Ui.Px(string.IsNullOrEmpty(desc) ? 52 : 62));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (accessory != null) accessory.Location = new Point(Width - accessory.Width, (Height - accessory.Height) / 2);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (glyph != null) Ui.Glyph(g, glyph, Ui.Icon, Ui.Text, new Rectangle(0, 0, Ui.Px(24), Height));
            int th = Ui.Measure(title, Ui.Strong, 0, false).Height;
            int tw = TextWidth(Width);
            int dh = string.IsNullOrEmpty(desc) ? 0 : Ui.Measure(desc, Ui.Small, tw, true).Height + Ui.Px(3);
            int y = (Height - th - dh) / 2;
            Ui.DrawText(g, title, Ui.Strong, Ui.Text, new Rectangle(TextLeft, y, tw, th + 2), TextFormatFlags.Default);
            if (dh > 0)
                Ui.DrawText(g, desc, Ui.Small, Ui.Text2, new Rectangle(TextLeft, y + th + Ui.Px(3), tw, dh + 2), TextFormatFlags.WordBreak);
        }
    }

    class SliderRow : UiControl, IAutoHeight
    {
        readonly string glyph, title;
        public readonly ModernSlider Slider = new ModernSlider();
        public readonly Toggle Auto;
        public Func<int, string> Format = v => v.ToString();
        public object Tag2;

        public SliderRow(string glyph, string title, int min, int max, int step, bool withAuto)
        {
            this.glyph = glyph; this.title = title;
            Slider.SetRange(min, max, step);
            Slider.ValueChanged += delegate { Invalidate(); };
            Controls.Add(Slider);
            if (withAuto)
            {
                Auto = new Toggle();
                Auto.Toggled += delegate { Slider.Enabled = !Auto.Checked; Invalidate(); };
                Controls.Add(Auto);
            }
        }

        int TextLeft { get { return glyph != null ? Ui.Px(40) : 0; } }
        int TopLine { get { return Ui.Px(14); } }
        int LineH { get { return Ui.Px(22); } }

        public int MeasureHeight(int width) { return Ui.Px(80); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int x = Math.Max(0, TextLeft - Ui.Px(10));
            Slider.SetBounds(x, TopLine + LineH + Ui.Px(8), Width - x, Ui.Px(28));
            if (Auto != null) Auto.Location = new Point(Width - Auto.Width, TopLine + (LineH - Auto.Height) / 2);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (glyph != null) Ui.Glyph(g, glyph, Ui.Icon, Ui.Text, new Rectangle(0, TopLine, Ui.Px(24), LineH));
            Ui.DrawText(g, title, Ui.Strong, Ui.Text, new Rectangle(TextLeft, TopLine, Width / 2, LineH), TextFormatFlags.VerticalCenter);

            int right = Width;
            if (Auto != null)
            {
                right = Auto.Left - Ui.Px(8);
                int aw = Ui.Measure("Auto", Ui.Small, 0, false).Width;
                Ui.DrawText(g, "Auto", Ui.Small, Ui.Text2, new Rectangle(right - aw, TopLine, aw + 2, LineH), TextFormatFlags.VerticalCenter);
                right -= aw + Ui.Px(18);
            }
            string val = Format(Slider.Value);
            Color vc = Auto != null && Auto.Checked ? Ui.Text3 : Ui.Text2;
            Ui.DrawText(g, val, Ui.Body, vc, new Rectangle(right - Ui.Px(160), TopLine, Ui.Px(160), LineH),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    class ChipsRow : UiControl, IAutoHeight
    {
        readonly string label;
        public readonly List<FlatButton> Chips = new List<FlatButton>();

        public ChipsRow(string label) { this.label = label; }

        public FlatButton AddChip(string text)
        {
            var b = new FlatButton(text, null, false);
            b.Height = Ui.Px(28);
            Chips.Add(b); Controls.Add(b);
            return b;
        }

        public int MeasureHeight(int width) { return Ui.Px(54); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int x = Ui.Px(40) + Ui.Measure(label, Ui.Small, 0, false).Width + Ui.Px(16);
            foreach (var c in Chips)
            {
                c.Location = new Point(x, (Height - c.Height) / 2);
                x += c.Width + Ui.Px(8);
            }
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.DrawText(e.Graphics, label, Ui.Small, Ui.Text2, new Rectangle(Ui.Px(40), 0, Ui.Px(120), Height), TextFormatFlags.VerticalCenter);
        }
    }

    class InputRow : UiControl, IAutoHeight
    {
        readonly string glyph, title, desc;
        public readonly InputBox Input = new InputBox();

        public InputRow(string glyph, string title, string desc)
        {
            this.glyph = glyph; this.title = title; this.desc = desc;
            Controls.Add(Input);
        }

        int TextLeft { get { return glyph != null ? Ui.Px(40) : 0; } }

        int TextBlockHeight(int width)
        {
            return Ui.Measure(title, Ui.Strong, 0, false).Height + Ui.Px(3) + Ui.Measure(desc, Ui.Small, width - TextLeft, true).Height;
        }

        public int MeasureHeight(int width) { return Ui.Px(14) + TextBlockHeight(width) + Ui.Px(12) + Input.Height + Ui.Px(16); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Input.SetBounds(TextLeft, Ui.Px(14) + TextBlockHeight(Width) + Ui.Px(12), Width - TextLeft, Input.Height);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int y = Ui.Px(14);
            int th = Ui.Measure(title, Ui.Strong, 0, false).Height;
            if (glyph != null) Ui.Glyph(g, glyph, Ui.Icon, Ui.Text, new Rectangle(0, y, Ui.Px(24), th));
            Ui.DrawText(g, title, Ui.Strong, Ui.Text, new Rectangle(TextLeft, y, Width - TextLeft, th + 2), TextFormatFlags.Default);
            Ui.DrawText(g, desc, Ui.Small, Ui.Text2, new Rectangle(TextLeft, y + th + Ui.Px(3), Width - TextLeft, Ui.Px(60)), TextFormatFlags.WordBreak);
        }
    }

    // ---------------- Light hero ----------------
    class PowerButton : UiControl
    {
        public bool On, Available = true;
        public int Kelvin = 4500, Brightness = 50;

        public PowerButton() { Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            float d = Math.Min(Width, Height);
            float cx = Width / 2f, cy = Height / 2f;
            float r = d * 0.34f;

            if (On && Available)
            {
                Color lc = Ui.Kelvin(Kelvin);
                float hr = d * 0.5f;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx - hr, cy - hr, hr * 2, hr * 2);
                    using (var pg = new PathGradientBrush(path))
                    {
                        int a = 70 + Brightness * 110 / 100;
                        pg.CenterColor = Color.FromArgb(a, lc);
                        pg.SurroundColors = new[] { Color.FromArgb(0, lc) };
                        pg.FocusScales = new PointF(0.55f, 0.55f);
                        g.FillPath(pg, path);
                    }
                }
                Color fill = Ui.Blend(lc, Color.White, hover ? 0.35f : 0.2f);
                using (var b = new SolidBrush(fill)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                Ui.Glyph(g, G.Power, Ui.IconLarge, Ui.Hex("#2A2418"), new Rectangle((int)(cx - r), (int)(cy - r), (int)(r * 2), (int)(r * 2)));
            }
            else
            {
                using (var b = new SolidBrush(hover && Available ? Ui.Hover : Ui.Button)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                using (var p = new Pen(Ui.ButtonBorder, Math.Max(1f, 1.5f * Ui.S))) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
                Ui.Glyph(g, G.Power, Ui.IconLarge, Available ? Ui.Text2 : Ui.Text3, new Rectangle((int)(cx - r), (int)(cy - r), (int)(r * 2), (int)(r * 2)));
            }
        }
    }

    class HeroRow : UiControl, IAutoHeight
    {
        public readonly PowerButton Power = new PowerButton();
        string title = "", sub = "", status = "";
        Color dot;

        public HeroRow() { Controls.Add(Power); }

        public void SetInfo(string t, string s, string st, Color dotColor)
        {
            title = t; sub = s; status = st; dot = dotColor; Invalidate();
        }

        public int MeasureHeight(int width) { return Ui.Px(156); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int size = Ui.Px(140);
            Power.SetBounds(-Ui.Px(10), (Height - size) / 2, size, size);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int x = Ui.Px(140);
            int th = Ui.Measure(title, Ui.HeroTitle, 0, false).Height;
            int sh = Ui.Measure(sub, Ui.Body, 0, false).Height;
            int lh = Ui.Measure(status, Ui.Small, 0, false).Height;
            int y = (Height - (th + Ui.Px(4) + sh + Ui.Px(14) + lh)) / 2;
            Ui.DrawText(g, title, Ui.HeroTitle, Ui.Text, new Rectangle(x, y, Width - x, th + 2), TextFormatFlags.Default);
            y += th + Ui.Px(4);
            Ui.DrawText(g, sub, Ui.Body, Ui.Text2, new Rectangle(x, y, Width - x, sh + 2), TextFormatFlags.Default);
            y += sh + Ui.Px(14);

            // status pill
            int pw = Ui.Measure(status, Ui.Small, 0, false).Width + Ui.Px(30);
            int ph = lh + Ui.Px(10);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.FillRound(g, Ui.Dark ? Ui.Hex("#333333") : Ui.Hex("#EFEFEF"), new RectangleF(x, y - Ui.Px(5), pw, ph), ph / 2f);
            float dr = Ui.Px(3.5f);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, x + Ui.Px(10), y - Ui.Px(5) + ph / 2f - dr, dr * 2, dr * 2);
            Ui.DrawText(g, status, Ui.Small, Ui.Text2, new Rectangle(x + Ui.Px(22), y - Ui.Px(5), pw, ph), TextFormatFlags.VerticalCenter);
        }
    }

    // ---------------- Sidebar ----------------
    class NavButton : UiControl
    {
        readonly string glyph;
        bool selected;

        public NavButton(string glyph, string text)
        {
            this.glyph = glyph; Text = text;
            Height = Ui.Px(38);
            Cursor = Cursors.Hand;
        }

        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(Ui.Px(4), 1, Width - Ui.Px(8), Height - 2);
            if (selected || hover)
                Ui.FillRound(g, selected ? (Ui.Dark ? Ui.Hex("#2D2D2D") : Ui.Hex("#EAEAEA")) : (Ui.Dark ? Ui.Hex("#292929") : Ui.Hex("#EDEDED")), r, Ui.Px(5));
            if (selected)
                Ui.FillRound(g, Ui.Accent, new RectangleF(Ui.Px(4), Height / 2f - Ui.Px(8), Ui.Px(3), Ui.Px(16)), Ui.Px(1.5f));
            Ui.Glyph(g, glyph, Ui.Icon, Ui.Text, new Rectangle(Ui.Px(16), 0, Ui.Px(22), Height));
            Ui.DrawText(g, Text, Ui.Body, Ui.Text, new Rectangle(Ui.Px(50), 0, Width - Ui.Px(54), Height), TextFormatFlags.VerticalCenter);
        }
    }

    class NavHeader : UiControl
    {
        public NavHeader() { Height = Ui.Px(64); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            int size = Ui.Px(30);
            var state = g.Save();
            g.TranslateTransform(Ui.Px(14), (Height - size) / 2f);
            IconArt.Draw(g, size, true, false, false);
            g.Restore(state);
            Ui.DrawText(g, "SmartCamLight", Ui.Section, Ui.Text, new Rectangle(Ui.Px(54), 0, Width - Ui.Px(54), Height), TextFormatFlags.VerticalCenter);
        }
    }

    class NavStatus : UiControl
    {
        string camText = "", lightText = "";
        Color camDot, lightDot;

        public NavStatus() { Height = Ui.Px(70); }

        public void Set(string cam, Color camColor, string light, Color lightColor)
        {
            camText = cam; camDot = camColor; lightText = light; lightDot = lightColor; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            Line(g, Ui.Px(12), camText, camDot);
            Line(g, Ui.Px(36), lightText, lightDot);
        }

        void Line(Graphics g, int y, string text, Color dot)
        {
            float r = Ui.Px(4);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, Ui.Px(22), y + Ui.Px(10) - r, r * 2, r * 2);
            Ui.DrawText(g, text, Ui.Small, Ui.Text2, new Rectangle(Ui.Px(40), y, Width - Ui.Px(44), Ui.Px(20)), TextFormatFlags.VerticalCenter);
        }
    }
}
