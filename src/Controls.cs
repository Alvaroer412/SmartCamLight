using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SmartCamLight
{
    interface IAutoHeight { int MeasureHeight(int width); }

    // Eases a value towards a target on the UI thread and repaints its owner while it moves.
    class Tween
    {
        readonly Timer timer = new Timer { Interval = 15 };
        readonly Control owner;
        public float Value, Target;

        public Tween(Control owner, float rate)
        {
            this.owner = owner;
            timer.Tick += delegate
            {
                Value += (Target - Value) * rate;
                if (Math.Abs(Target - Value) < 0.01f) { Value = Target; timer.Stop(); }
                this.owner.Invalidate();
            };
        }

        public void To(float target)
        {
            Target = target;
            if (Value != Target) timer.Start();
        }

        public void Jump(float value)
        {
            Value = Target = value;
            timer.Stop();
            owner.Invalidate();
        }

        public void Dispose() { timer.Dispose(); }
    }

    // Container that shows the bare window backdrop (Mica, or the solid fallback colour).
    class Backdrop : Control
    {
        public Backdrop()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e) { Ui.Erase(e); }
    }

    // Base for everything that draws itself. Subclasses draw in Draw(); the result keeps its alpha
    // channel all the way to the screen, so anything left unpainted shows the backdrop.
    abstract class UiControl : Control
    {
        protected bool hover, pressed;
        bool onCard;
        List<Tween> tweens;

        protected UiControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        // True when the control sits on a card: it then paints the card's translucent fill under itself.
        public bool OnCard
        {
            get { return onCard; }
            set
            {
                onCard = value;
                foreach (Control c in Controls)
                {
                    var u = c as UiControl;
                    if (u != null) u.OnCard = value;
                }
                Invalidate();
            }
        }

        protected void MakeFocusable()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected bool ShowFocus { get { return Focused && Ui.KeyFocus; } }

        protected Tween NewTween(float rate)
        {
            if (tweens == null) tweens = new List<Tween>();
            var t = new Tween(this, rate);
            tweens.Add(t);
            return t;
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            var u = e.Control as UiControl;
            if (u != null) u.OnCard = onCard;
            base.OnControlAdded(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true; Invalidate();
                if (GetStyle(ControlStyles.Selectable) && CanFocus) Focus();
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e) { Ui.Paint(this, e, onCard, Draw); }

        protected abstract void Draw(Graphics g);

        protected override void Dispose(bool disposing)
        {
            if (disposing && tweens != null) foreach (var t in tweens) t.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---------------- Toggle switch ----------------
    class Toggle : UiControl
    {
        bool on;
        readonly bool showState;
        readonly Tween pos, grow;
        public event EventHandler Toggled;

        public Toggle() : this(false) { }

        // showState adds the "On" / "Off" label beside the switch, as in Windows Settings.
        public Toggle(bool showState)
        {
            this.showState = showState;
            pos = NewTween(0.35f);
            grow = NewTween(0.4f);
            int label = 0;
            if (showState)
                label = Math.Max(Ui.Measure("On", Ui.Body, 0, false).Width, Ui.Measure("Off", Ui.Body, 0, false).Width) + Ui.Px(12);
            Size = new Size(label + Ui.Px(48), Ui.Px(28));
            Cursor = Cursors.Hand;
            MakeFocusable();
        }

        // Gap between the switch and the control's edge; it leaves room for the focus ring.
        public static int Inset { get { return Ui.Px(4); } }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; pos.To(on ? 1 : 0); Invalidate(); }
        }

        public void SetInstant(bool value) { on = value; pos.Jump(value ? 1 : 0); }

        void Flip()
        {
            if (!Enabled) return;
            Checked = !on;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e) { Flip(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Flip(); base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); grow.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); grow.To(0); }

        protected override void Draw(Graphics g)
        {
            float w = Ui.Px(40), h = Ui.Px(20);
            var r = new RectangleF(Width - Inset - w, (Height - h) / 2f, w, h);
            float rad = h / 2;
            Color knob;
            if (on)
            {
                Ui.FillRound(g, !Enabled ? Ui.AccentDisabled : pressed ? Ui.AccentPressed : hover ? Ui.AccentHover : Ui.Accent, r, rad);
                knob = Enabled ? Ui.OnAccent : Ui.OnAccentDisabled;
            }
            else
            {
                float t = Ui.Hair;
                var o = new RectangleF(r.X + t / 2, r.Y + t / 2, r.Width - t, r.Height - t);
                Ui.FillRound(g, hover && Enabled ? Ui.SwitchOffHover : Ui.SwitchOff, o, rad);
                Ui.DrawRound(g, Enabled ? Ui.Strong : Ui.StrongDisabled, o, rad, t);
                knob = Enabled ? Ui.Text2 : Ui.TextDisabled;
            }

            // The knob grows a little on hover and stretches while pressed.
            float kh = Ui.Px(12) + (Enabled ? grow.Value : 0) * Ui.Px(2);
            float stretch = pressed && Enabled ? Ui.Px(3) : 0;
            float cx = r.X + h / 2 + (w - h) * pos.Value;
            float left = cx - kh / 2 - stretch * pos.Value;
            Ui.FillRound(g, knob, new RectangleF(left, r.Y + (h - kh) / 2, kh + stretch, kh), kh / 2);

            if (showState)
                Ui.DrawText(g, on ? "On" : "Off", Ui.Body, Enabled ? Ui.Text : Ui.TextDisabled,
                    new Rectangle(0, 0, (int)r.X - Ui.Px(12), Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            if (ShowFocus) Ui.FocusRing(g, RectangleF.Inflate(r, Ui.Px(3), Ui.Px(3)), rad + Ui.Px(3));
        }
    }

    // ---------------- Slider ----------------
    class ModernSlider : UiControl
    {
        int min, max = 100, val, step = 1;
        bool drag;
        readonly Tween dot;   // inner dot size: -1 pressed, 0 rest, 1 hover
        public Color[] Gradient;
        public event EventHandler ValueChanged;   // user changes only

        public ModernSlider()
        {
            Height = Ui.Px(32);
            Cursor = Cursors.Hand;
            dot = NewTween(0.4f);
            MakeFocusable();
        }

        public void SetRange(int minimum, int maximum, int stepSize)
        {
            min = minimum; max = Math.Max(minimum + 1, maximum); step = Math.Max(1, stepSize);
            val = Clamp(val); Invalidate();
        }

        public int Value { get { return val; } set { val = Clamp(value); Invalidate(); } }
        public int Minimum { get { return min; } }
        public int Maximum { get { return max; } }

        // Distance from the control's edge to the end of the track (thumb radius plus focus ring).
        public static int Inset { get { return Ui.Px(13); } }

        int Clamp(int v) { return Math.Max(min, Math.Min(max, v)); }
        float L { get { return Inset; } }
        float R { get { return Width - Inset; } }
        float Frac { get { return (val - min) / (float)(max - min); } }

        void Aim() { dot.To(!Enabled ? 0 : drag ? -1 : hover ? 1 : 0); }

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

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Aim(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Aim(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Aim(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            drag = true; Capture = true; Aim(); SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; Capture = false; Aim(); }

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

        protected override void Draw(Graphics g)
        {
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
                        blend.Colors[i] = Enabled ? Gradient[i] : Color.FromArgb(70, Gradient[i]);
                        blend.Positions[i] = i / (float)(Gradient.Length - 1);
                    }
                    lb.InterpolationColors = blend;
                    using (var p = Ui.Round(tr, th / 2)) g.FillPath(lb, p);
                }
                Ui.DrawRound(g, Ui.Stroke, tr, th / 2, Ui.Hair);
            }
            else
            {
                // Rail to the right of the thumb, accent fill to the left; the thumb hides the join.
                float th = Ui.Px(4);
                Ui.FillRound(g, Enabled ? Ui.Rail : Ui.RailDisabled, new RectangleF(x, cy - th / 2, R - x + th / 2, th), th / 2);
                Ui.FillRound(g, Enabled ? Ui.Accent : Ui.AccentDisabled, new RectangleF(L - th / 2, cy - th / 2, x - L + th / 2, th), th / 2);
            }

            float rad = Ui.Px(10);
            using (var b = new SolidBrush(Ui.ThumbOuter)) g.FillEllipse(b, x - rad, cy - rad, rad * 2, rad * 2);
            using (var p = new Pen(Ui.ThumbStroke, Ui.Hair)) g.DrawEllipse(p, x - rad, cy - rad, rad * 2, rad * 2);
            float ir = (Ui.Px(12) + dot.Value * Ui.Px(2)) / 2f;
            Color inner = !Enabled ? Ui.AccentDisabled : Gradient != null ? GradientAt(Frac) : Ui.Accent;
            using (var b = new SolidBrush(inner)) g.FillEllipse(b, x - ir, cy - ir, ir * 2, ir * 2);
            if (Gradient != null && Enabled)
                using (var p = new Pen(Ui.ThumbStroke, Ui.Hair)) g.DrawEllipse(p, x - ir, cy - ir, ir * 2, ir * 2);

            if (ShowFocus)
            {
                float fr = rad + Ui.Px(3);
                Ui.FocusRing(g, new RectangleF(x - fr, cy - fr, fr * 2, fr * 2), fr);
            }
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
            MakeFocusable();
            Fit();
        }

        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public void Fit()
        {
            int w = Ui.Measure(Text, Ui.Body, 0, false).Width + Ui.Px(24);
            if (glyph != null) w += Ui.Px(22);
            Size = new Size(Math.Max(w, Ui.Px(56)), Ui.Px(32));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void Draw(Graphics g)
        {
            float rad = Ui.Px(4);
            Color fg;
            if (primary || selected)
            {
                // Accent button: also the "on" look of a toggle button.
                Ui.FillRound(g, !Enabled ? Ui.AccentDisabled : pressed ? Ui.AccentPressed : hover ? Ui.AccentHover : Ui.Accent,
                    new RectangleF(0, 0, Width, Height), rad);
                fg = Enabled ? Ui.OnAccent : Ui.OnAccentDisabled;
            }
            else
            {
                var r = Ui.Edge(Width, Height);
                Ui.FillRound(g, !Enabled ? Ui.FillDisabled : pressed ? Ui.FillPressed : hover ? Ui.FillHover : Ui.Fill, r, rad);
                Ui.Elevation(g, r, rad);
                fg = !Enabled ? Ui.TextDisabled : pressed ? Ui.Text2 : Ui.Text;
            }

            int tw = Ui.Measure(Text, Ui.Body, 0, false).Width;
            int total = tw + (glyph != null ? Ui.Px(22) : 0);
            int x = (Width - total) / 2;
            if (glyph != null)
            {
                Ui.Glyph(g, glyph, Ui.IconSmall, fg, new Rectangle(x, 0, Ui.Px(14), Height));
                x += Ui.Px(22);
            }
            Ui.DrawText(g, Text, Ui.Body, fg, new Rectangle(x, 0, tw + 2, Height - Ui.Px(1)), TextFormatFlags.VerticalCenter);
            if (ShowFocus) Ui.FocusRing(g, new RectangleF(0, 0, Width, Height), rad);
        }
    }

    class DropDown : UiControl
    {
        readonly List<object> items = new List<object>();
        int sel = -1;
        public string Placeholder = "";
        public event EventHandler SelectionChanged;

        public DropDown()
        {
            Size = new Size(Ui.Px(280), Ui.Px(32));
            Cursor = Cursors.Hand;
            MakeFocusable();
        }

        public void SetItems(IEnumerable<object> list, object selected)
        {
            items.Clear(); items.AddRange(list);
            sel = selected == null ? -1 : items.IndexOf(selected);
            Invalidate();
        }

        public object SelectedItem { get { return sel >= 0 && sel < items.Count ? items[sel] : null; } }

        void Open()
        {
            if (items.Count == 0) return;
            var menu = new ContextMenuStrip();
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
                menu.Items.Add(mi);
            }
            MenuRenderer.Apply(menu, Ui.S);
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += delegate { BeginInvoke((MethodInvoker)menu.Dispose); };
            menu.Show(this, new Point(0, Height + Ui.Px(4)));
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Open(); }

        protected override bool IsInputKey(Keys k) { return k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down) Open();
            base.OnKeyDown(e);
        }

        protected override void Draw(Graphics g)
        {
            var r = Ui.Edge(Width, Height);
            float rad = Ui.Px(4);
            Ui.FillRound(g, pressed ? Ui.FillPressed : hover ? Ui.FillHover : Ui.Fill, r, rad);
            Ui.Elevation(g, r, rad);
            object item = SelectedItem;
            Ui.DrawText(g, item != null ? item.ToString() : Placeholder, Ui.Body, item != null ? Ui.Text : Ui.Text2,
                new Rectangle(Ui.Px(12), 0, Width - Ui.Px(44), Height - Ui.Px(1)), TextFormatFlags.VerticalCenter);
            Ui.Glyph(g, G.Chevron, Ui.IconSmall, Ui.Text2, new Rectangle(Width - Ui.Px(32), 0, Ui.Px(20), Height));
            if (ShowFocus) Ui.FocusRing(g, new RectangleF(0, 0, Width, Height), rad);
        }
    }

    // A native edit control draws without an alpha channel, so on a translucent backdrop it is
    // given its own opaque layer.
    class OpaqueTextBox : TextBox
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (Ui.Glass) cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Ui.Glass) Ui.MakeOpaque(Handle);
        }
    }

    class InputBox : UiControl
    {
        public readonly TextBox Box;

        public InputBox()
        {
            Height = Ui.Px(32);
            Box = new OpaqueTextBox { BorderStyle = BorderStyle.None, BackColor = Ui.Input, ForeColor = Ui.Text, Font = Ui.Body };
            Box.GotFocus += delegate { Box.BackColor = Ui.InputFocus; Invalidate(); };
            Box.LostFocus += delegate { Box.BackColor = Ui.Input; Invalidate(); };
            Controls.Add(Box);
            Cursor = Cursors.IBeam;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Box != null) Box.SetBounds(Ui.Px(11), (Height - Box.Height) / 2, Width - Ui.Px(22), Box.Height);
            base.OnLayout(e);
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void Draw(Graphics g)
        {
            var r = Ui.Edge(Width, Height);
            float rad = Ui.Px(4);
            bool focus = Box.Focused;
            using (var path = Ui.Round(r, rad))
            {
                using (var b = new SolidBrush(focus ? Ui.InputFocus : Ui.Input)) g.FillPath(b, path);
                using (var pen = new Pen(Ui.InputStroke, Ui.Hair)) g.DrawPath(pen, path);
                // Underline: a hairline at rest, a two-pixel accent line when focused.
                var state = g.Save();
                g.SetClip(new RectangleF(0, 0, Width, Height));
                using (var outer = Ui.Round(new RectangleF(0, 0, Width, Height), rad)) g.SetClip(outer, CombineMode.Intersect);
                float lh = focus ? Ui.Px(2) : Ui.Hair;
                using (var b = new SolidBrush(focus ? Ui.Accent : Ui.InputLine)) g.FillRectangle(b, 0, Height - lh, Width, lh);
                g.Restore(state);
            }
        }
    }

    // ---------------- Scrolling ----------------

    // Thin overlay scroll indicator that widens under the pointer.
    class ScrollIndicator : UiControl
    {
        float pos;
        int range, view, grab;
        bool drag;
        readonly Tween wide;
        public event EventHandler Scrolled;

        public ScrollIndicator() { wide = NewTween(0.35f); }

        public float Position { get { return pos; } }

        public void Set(float position, int scrollRange, int viewport)
        {
            if (position == pos && scrollRange == range && viewport == view) return;
            pos = position; range = scrollRange; view = viewport;
            Invalidate();
        }

        float Track { get { return Height - Ui.Px(8); } }
        float Length { get { return Math.Max(Ui.Px(32), Track * view / Math.Max(1f, view + range)); } }

        RectangleF Thumb()
        {
            float len = Length;
            float y = Ui.Px(4) + (Track - len) * (range > 0 ? pos / range : 0);
            float w = Ui.Px(2) + wide.Value * Ui.Px(4);
            return new RectangleF(Width - Ui.Px(4) - w, y, w, len);
        }

        void MoveTo(float position)
        {
            pos = Math.Max(0, Math.Min(range, position));
            Invalidate();
            if (Scrolled != null) Scrolled(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); wide.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (!drag) wide.To(0); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || range <= 0) return;
            RectangleF t = Thumb();
            if (e.Y < t.Top) MoveTo(pos - view * 0.9f);
            else if (e.Y > t.Bottom) MoveTo(pos + view * 0.9f);
            else { drag = true; grab = e.Y - (int)t.Top; Capture = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!drag) return;
            float free = Track - Length;
            if (free > 0) MoveTo((e.Y - grab - Ui.Px(4)) / free * range);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = false; Capture = false;
            if (!ClientRectangle.Contains(e.Location)) wide.To(0);
        }

        protected override void Draw(Graphics g)
        {
            if (range <= 0) return;
            RectangleF t = Thumb();
            Ui.FillRound(g, Ui.Rail, t, t.Width / 2);
        }
    }

    // A vertically scrolling page of full-width items.
    class ScrollPage : Backdrop
    {
        readonly Backdrop content = new Backdrop();
        readonly ScrollIndicator bar = new ScrollIndicator();
        readonly List<Control> items = new List<Control>();
        readonly Dictionary<Control, int> gaps = new Dictionary<Control, int>();
        readonly Timer anim = new Timer { Interval = 15 };
        float pos, target, enter;
        int total;

        public ScrollPage()
        {
            Dock = DockStyle.Fill;
            Controls.Add(bar);       // first child = topmost
            Controls.Add(content);
            bar.Scrolled += delegate { pos = target = bar.Position; Place(); };
            anim.Tick += delegate
            {
                pos += (target - pos) * 0.3f;
                if (Math.Abs(target - pos) < 0.5f) pos = target;
                enter *= 0.72f;
                if (enter < 0.5f) enter = 0;
                if (pos == target && enter == 0) anim.Stop();
                Place();
            };
        }

        int Range { get { return Math.Max(0, total - ClientSize.Height); } }

        public T Add<T>(T c, int gapBefore) where T : Control
        {
            items.Add(c); gaps[c] = Ui.Px(gapBefore);
            content.Controls.Add(c);
            return c;
        }

        public void ClearItems(int keep)
        {
            content.SuspendLayout();
            while (items.Count > keep)
            {
                Control c = items[items.Count - 1];
                items.RemoveAt(items.Count - 1); gaps.Remove(c);
                content.Controls.Remove(c); c.Dispose();
            }
            content.ResumeLayout();
        }

        public void Relayout()
        {
            int padL = Ui.Px(20), padR = Ui.Px(32);
            int w = Math.Min(ClientSize.Width - padL - padR, Ui.Px(1000));
            if (w <= 0) return;
            int y = Ui.Px(4);
            content.SuspendLayout();
            foreach (var c in items)
            {
                y += gaps[c];
                var ah = c as IAutoHeight;
                int h = ah != null ? ah.MeasureHeight(w) : c.Height;
                c.SetBounds(padL, y, w, h);
                y += h;
            }
            total = y + Ui.Px(24);
            content.ResumeLayout(false);
            target = Math.Max(0, Math.Min(Range, target));
            pos = Math.Max(0, Math.Min(Range, pos));
            Place();
            foreach (var c in items) c.PerformLayout();
        }

        void Place()
        {
            content.SetBounds(0, (int)Math.Round(enter - pos), ClientSize.Width, Math.Max(1, total));
            bar.SetBounds(ClientSize.Width - Ui.Px(14), 0, Ui.Px(14), ClientSize.Height);
            bar.Visible = Range > 0;
            bar.Set(pos, Range, ClientSize.Height);
        }

        public void Wheel(int delta)
        {
            if (Range <= 0) return;
            target = Math.Max(0, Math.Min(Range, target - delta / 120f * Ui.Px(110)));
            anim.Start();
        }

        // Slides the content up into place, like a page transition in Windows 11 apps.
        public void PlayEntrance()
        {
            pos = target = 0;
            enter = Ui.Px(36);
            Place();
            anim.Start();
        }

        public void EnsureVisible(Control c)
        {
            if (c == null || !content.Contains(c)) return;
            int top = content.PointToClient(c.PointToScreen(Point.Empty)).Y;
            int margin = Ui.Px(24);
            float t = target;
            if (top - margin < t || c.Height + 2 * margin > ClientSize.Height) t = top - margin;   // too tall: show its top
            else if (top + c.Height + margin > t + ClientSize.Height) t = top + c.Height + margin - ClientSize.Height;
            t = Math.Max(0, Math.Min(Range, t));
            if (t != target) { target = t; anim.Start(); }
        }

        protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); Relayout(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---------------- Cards and rows ----------------

    // A translucent layer holding one or more rows, separated by hairlines.
    class Card : UiControl, IAutoHeight
    {
        readonly List<Control> rows = new List<Control>();
        readonly List<int> dividers = new List<int>();

        // Horizontal padding rows keep from the card's edge (rows span the card so that
        // right-aligned controls have room for their focus ring).
        public static int RowPad { get { return Ui.Px(15); } }

        public T Add<T>(T row) where T : Control
        {
            rows.Add(row); Controls.Add(row);
            var u = row as UiControl;
            if (u != null) u.OnCard = true;
            return row;
        }

        static int Line { get { return (int)Ui.Hair; } }
        static int PadY { get { return Ui.Px(4); } }   // keeps rows clear of the rounded corners

        static int RowHeight(Control c, int w)
        {
            var ah = c as IAutoHeight;
            return ah != null ? ah.MeasureHeight(w) : c.Height;
        }

        // A row that measures zero high is collapsed: it takes no space and gets no divider.
        public int MeasureHeight(int width)
        {
            int inner = width - 2 * Line, h = 2 * PadY, shown = 0;
            foreach (Control row in rows)
            {
                int rh = RowHeight(row, inner);
                if (rh <= 0) continue;
                h += rh + (shown > 0 ? Line : 0);
                shown++;
            }
            return h;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int w = Width - 2 * Line, y = PadY, shown = 0;
            dividers.Clear();
            foreach (Control row in rows)
            {
                int h = RowHeight(row, w);
                if (h <= 0) { row.SetBounds(Line, y, w, 0); continue; }
                if (shown > 0) { dividers.Add(y); y += Line; }
                row.SetBounds(Line, y, w, h);
                y += h;
                shown++;
            }
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            var r = Ui.Edge(Width, Height);
            float rad = Ui.Px(4);
            Ui.FillRound(g, Ui.Card, r, rad);
            Ui.DrawRound(g, Ui.CardStroke, r, rad, Ui.Hair);
            using (var b = new SolidBrush(Ui.Divider))
                foreach (int y in dividers) g.FillRectangle(b, Line, y, Width - 2 * Line, Line);
        }
    }

    class TextBlock : UiControl, IAutoHeight
    {
        readonly Font font; readonly Color color; readonly int padTop, padBottom;

        public TextBlock(string text, Font font, Color color, int padTop, int padBottom)
        {
            Text = text; this.font = font; this.color = color; this.padTop = Ui.Px(padTop); this.padBottom = Ui.Px(padBottom);
        }

        public int MeasureHeight(int width) { return padTop + Ui.Measure(Text, font, width, true).Height + padBottom; }

        protected override void Draw(Graphics g)
        {
            Ui.DrawText(g, Text, font, color, new Rectangle(0, padTop, Width, Height - padTop), TextFormatFlags.WordBreak);
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

        static int Pad { get { return Card.RowPad; } }
        int TextLeft { get { return Pad + (glyph != null ? Ui.Px(40) : 0); } }
        int TextWidth(int w) { return w - TextLeft - Pad - (accessory != null ? accessory.Width + Ui.Px(16) : 0); }

        // A dropdown gives way in a narrow window so the label keeps a readable width.
        void FitAccessory(int width)
        {
            if (!(accessory is DropDown)) return;
            int room = width - TextLeft - Pad - Ui.Px(16) - Ui.Px(150);
            accessory.Width = Math.Max(Ui.Px(120), Math.Min(Ui.Px(280), room));
        }

        public int MeasureHeight(int width)
        {
            FitAccessory(width);
            int th = Ui.Measure(title, Ui.Body, 0, false).Height;
            int dh = string.IsNullOrEmpty(desc) ? 0 : Ui.Measure(desc, Ui.Small, TextWidth(width), true).Height + Ui.Px(2);
            int h = th + dh + Ui.Px(24);
            if (accessory != null) h = Math.Max(h, accessory.Height + Ui.Px(20));
            return Math.Max(h, Ui.Px(60));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (accessory != null)
            {
                FitAccessory(Width);
                int inset = accessory is Toggle ? Toggle.Inset : 0;
                accessory.Location = new Point(Width - Pad - accessory.Width + inset, (Height - accessory.Height) / 2);
            }
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            if (glyph != null) Ui.Glyph(g, glyph, Ui.Icon, Ui.Text, new Rectangle(Pad, 0, Ui.Px(20), Height));
            int th = Ui.Measure(title, Ui.Body, 0, false).Height;
            int tw = TextWidth(Width);
            int dh = string.IsNullOrEmpty(desc) ? 0 : Ui.Measure(desc, Ui.Small, tw, true).Height + Ui.Px(2);
            int y = (Height - th - dh) / 2;
            Ui.DrawText(g, title, Ui.Body, Ui.Text, new Rectangle(TextLeft, y, tw, th + 2), TextFormatFlags.Default);
            if (dh > 0)
                Ui.DrawText(g, desc, Ui.Small, Ui.Text2, new Rectangle(TextLeft, y + th + Ui.Px(2), tw, dh + 2), TextFormatFlags.WordBreak);
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

        static int Pad { get { return Card.RowPad; } }
        int TextLeft { get { return Pad + (glyph != null ? Ui.Px(40) : 0); } }
        static int TopLine { get { return Ui.Px(10); } }
        static int LineH { get { return Ui.Px(24); } }

        public int MeasureHeight(int width) { return Ui.Px(76); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            // The track starts under the title and ends at the row's right padding.
            int x = TextLeft - ModernSlider.Inset;
            Slider.SetBounds(x, TopLine + LineH + Ui.Px(2), Width - Pad - x + ModernSlider.Inset, Slider.Height);
            if (Auto != null) Auto.Location = new Point(Width - Pad - Auto.Width + Toggle.Inset, TopLine + (LineH - Auto.Height) / 2);
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            if (glyph != null) Ui.Glyph(g, glyph, Ui.Icon, Enabled ? Ui.Text : Ui.TextDisabled, new Rectangle(Pad, TopLine, Ui.Px(20), LineH));
            Ui.DrawText(g, title, Ui.Body, Enabled ? Ui.Text : Ui.TextDisabled, new Rectangle(TextLeft, TopLine, Width / 2, LineH), TextFormatFlags.VerticalCenter);

            int right = Width - Pad;
            if (Auto != null)
            {
                right = Auto.Left + Toggle.Inset - Ui.Px(10);
                int aw = Ui.Measure("Auto", Ui.Small, 0, false).Width;
                Ui.DrawText(g, "Auto", Ui.Small, Ui.Text2, new Rectangle(right - aw, TopLine, aw + 2, LineH), TextFormatFlags.VerticalCenter);
                right -= aw + Ui.Px(16);
            }
            string val = Format(Slider.Value);
            Color vc = !Enabled || (Auto != null && Auto.Checked) ? Ui.Text3 : Ui.Text2;
            Ui.DrawText(g, val, Ui.Body, vc, new Rectangle(right - Ui.Px(180), TopLine, Ui.Px(180), LineH),
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

        static int Left0 { get { return Card.RowPad + Ui.Px(40); } }

        static int LineH { get { return Ui.Px(28); } }
        static int PadY { get { return Ui.Px(12); } }

        // Chips flow onto further lines when the row is too narrow for all of them.
        int Flow(int width, bool place)
        {
            int x0 = Left0 + Ui.Measure(label, Ui.Small, 0, false).Width + Ui.Px(16);
            int x = x0, line = 0, gap = Ui.Px(8);
            foreach (var c in Chips)
            {
                if (x > x0 && x + c.Width > width - Card.RowPad) { x = x0; line++; }
                if (place) c.Location = new Point(x, PadY + line * (LineH + gap));
                x += c.Width + gap;
            }
            return PadY + (line + 1) * LineH + line * gap + PadY;
        }

        public int MeasureHeight(int width) { return Flow(width, false); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Flow(Width, true);
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            Ui.DrawText(g, label, Ui.Small, Ui.Text2, new Rectangle(Left0, PadY, Ui.Px(120), LineH), TextFormatFlags.VerticalCenter);
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

        static int Pad { get { return Card.RowPad; } }
        int TextLeft { get { return Pad + (glyph != null ? Ui.Px(40) : 0); } }

        int TextBlockHeight(int width)
        {
            return Ui.Measure(title, Ui.Body, 0, false).Height + Ui.Px(2) + Ui.Measure(desc, Ui.Small, width - TextLeft - Pad, true).Height;
        }

        public int MeasureHeight(int width) { return Ui.Px(12) + TextBlockHeight(width) + Ui.Px(12) + Input.Height + Ui.Px(14); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Input.SetBounds(TextLeft, Ui.Px(12) + TextBlockHeight(Width) + Ui.Px(12), Width - TextLeft - Pad, Input.Height);
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            int y = Ui.Px(12);
            int th = Ui.Measure(title, Ui.Body, 0, false).Height;
            int w = Width - TextLeft - Pad;
            if (glyph != null) Ui.Glyph(g, glyph, Ui.Icon, Ui.Text, new Rectangle(Pad, y, Ui.Px(20), th));
            Ui.DrawText(g, title, Ui.Body, Ui.Text, new Rectangle(TextLeft, y, w, th + 2), TextFormatFlags.Default);
            Ui.DrawText(g, desc, Ui.Small, Ui.Text2, new Rectangle(TextLeft, y + th + Ui.Px(2), w, Ui.Px(80)), TextFormatFlags.WordBreak);
        }
    }

    // The live camera picture as a row in a card. It has no height while the preview is off.
    class PreviewPane : UiControl, IAutoHeight
    {
        CameraFeed feed;
        string notice;
        bool open;

        public void Set(CameraFeed newFeed, string newNotice, bool isOpen)
        {
            feed = newFeed; notice = newNotice; open = isOpen;
            Invalidate();
        }

        // Width, in pixels, of a 16:9 picture in the pane.
        public static int ShownWidth { get { return Ui.Px(240) * 16 / 9; } }

        // The picture keeps the camera's own proportions (16:9 until the first frame arrives).
        Size VideoSize(int width)
        {
            Size f = feed != null ? feed.FrameSize : Size.Empty;
            float aspect = f.IsEmpty ? 16f / 9f : f.Width / (float)f.Height;
            int h = Ui.Px(240), w = (int)Math.Round(h * aspect), max = Math.Max(1, width - 2 * Card.RowPad);
            if (w > max) { w = max; h = (int)Math.Round(w / aspect); }
            return new Size(w, h);
        }

        public int MeasureHeight(int width) { return open ? Ui.Px(8) + VideoSize(width).Height + Ui.Px(16) : 0; }

        protected override void Draw(Graphics g)
        {
            if (!open) return;
            Size v = VideoSize(Width);
            var r = new RectangleF((Width - v.Width) / 2, Ui.Px(8), v.Width, v.Height);
            float rad = Ui.Px(6);
            Ui.FillRound(g, Color.FromArgb(16, 16, 16), r, rad);

            string message = notice;
            if (feed != null)
            {
                if (feed.Error != null) message = feed.Error;
                else if (!feed.Draw(g, r, rad)) message = "Starting the camera…";
            }
            if (message == null) return;
            Rectangle box = Rectangle.Round(RectangleF.Inflate(r, -Ui.Px(24), -Ui.Px(12)));
            int th = Math.Min(box.Height, Ui.Measure(message, Ui.Body, box.Width, true).Height);
            Ui.DrawText(g, message, Ui.Body, Color.FromArgb(208, 208, 208),
                new Rectangle(box.X, box.Y + (box.Height - th) / 2, box.Width, th + 2), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }
    }

    // ---------------- Light hero ----------------

    // The light's status with its power button. While the light is on, the card carries a wash
    // of the light's own colour, stronger the brighter the light is set.
    class HeroCard : UiControl, IAutoHeight
    {
        bool on, available = true, overButton, downOnButton;
        int kelvin = 4500, brightness = 50;
        string title = "", sub = "", status = "";
        Color dot;
        readonly Tween glow;
        public event EventHandler PowerClicked;

        public HeroCard()
        {
            glow = NewTween(0.18f);
            MakeFocusable();
        }

        public void SetInfo(string t, string s, string st, Color dotColor)
        {
            title = t; sub = s; status = st; dot = dotColor; Invalidate();
        }

        public void SetState(bool lightOn, bool lightAvailable, int lightKelvin, int lightBrightness)
        {
            on = lightOn; available = lightAvailable; kelvin = lightKelvin; brightness = lightBrightness;
            glow.To(on && available ? 1 : 0);
            Invalidate();
        }

        public int MeasureHeight(int width) { return Ui.Px(132); }

        RectangleF ButtonRect
        {
            get { float d = Ui.Px(72); return new RectangleF(Ui.Px(28), (Height - d) / 2f, d, d); }
        }

        bool Hit(Point p)
        {
            RectangleF b = ButtonRect;
            float dx = p.X - (b.X + b.Width / 2), dy = p.Y - (b.Y + b.Height / 2), r = b.Width / 2;
            return dx * dx + dy * dy <= r * r;
        }

        void RaisePower()
        {
            if (available && PowerClicked != null) PowerClicked(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = available && Hit(e.Location);
            if (over == overButton) return;
            overButton = over;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { overButton = false; downOnButton = false; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            downOnButton = e.Button == MouseButtons.Left && available && Hit(e.Location);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool click = downOnButton && Hit(e.Location);
            downOnButton = false;
            base.OnMouseUp(e);
            if (click) RaisePower();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) RaisePower();
            base.OnKeyDown(e);
        }

        protected override void Draw(Graphics g)
        {
            RectangleF b = ButtonRect;
            float cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2, rad = Ui.Px(8);

            using (var path = Ui.Round(Ui.Edge(Width, Height), rad))
            {
                using (var fill = new SolidBrush(Ui.Card)) g.FillPath(fill, path);
                if (glow.Value > 0.01f)
                {
                    var state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    float reach = Math.Max(Ui.Px(240), Width * 0.62f);
                    float strength = glow.Value * (0.35f + 0.65f * brightness / 100f);
                    Color tint = Ui.Tint(kelvin);
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(cx - reach, cy - reach, reach * 2, reach * 2);
                        using (var pg = new PathGradientBrush(gp))
                        {
                            pg.CenterPoint = new PointF(cx, cy);
                            pg.CenterColor = Color.FromArgb((int)((Ui.Dark ? 120 : 170) * strength), tint);
                            pg.SurroundColors = new[] { Color.FromArgb(0, tint) };
                            g.FillPath(pg, gp);
                        }
                    }
                    g.Restore(state);
                }
                using (var pen = new Pen(Ui.CardStroke, Ui.Hair)) g.DrawPath(pen, path);
            }

            // Power button: accent-filled while the light is on, like a toggled quick setting.
            bool down = downOnButton && overButton;
            Color glyph;
            if (on && available)
            {
                using (var br = new SolidBrush(down ? Ui.AccentPressed : overButton ? Ui.AccentHover : Ui.Accent)) g.FillEllipse(br, b);
                glyph = Ui.OnAccent;
            }
            else
            {
                var e = new RectangleF(b.X + Ui.Hair / 2, b.Y + Ui.Hair / 2, b.Width - Ui.Hair, b.Height - Ui.Hair);
                using (var br = new SolidBrush(!available ? Ui.FillDisabled : down ? Ui.FillPressed : overButton ? Ui.FillHover : Ui.Fill)) g.FillEllipse(br, e);
                using (var pen = new Pen(Ui.Dark ? Ui.StrokeEdge : Ui.Stroke, Ui.Hair)) g.DrawEllipse(pen, e);
                glyph = available ? Ui.Text : Ui.TextDisabled;
            }
            Ui.Glyph(g, G.Power, Ui.IconLarge, glyph, Rectangle.Round(b));
            if (ShowFocus) Ui.FocusRing(g, RectangleF.Inflate(b, Ui.Px(4), Ui.Px(4)), b.Width / 2 + Ui.Px(4));

            int x = (int)b.Right + Ui.Px(24);
            int th = Ui.Measure(title, Ui.HeroTitle, 0, false).Height;
            int sh = Ui.Measure(sub, Ui.Body, 0, false).Height;
            int lh = Ui.Measure(status, Ui.Small, 0, false).Height;
            int ph = lh + Ui.Px(10);
            int y = (Height - (th + Ui.Px(2) + sh + Ui.Px(12) + ph)) / 2;
            int w = Width - x - Ui.Px(20);
            Ui.DrawText(g, title, Ui.HeroTitle, Ui.Text, new Rectangle(x, y, w, th + 2), TextFormatFlags.Default);
            y += th + Ui.Px(2);
            Ui.DrawText(g, sub, Ui.Body, Ui.Text2, new Rectangle(x, y, w, sh + 2), TextFormatFlags.Default);
            y += sh + Ui.Px(12);

            // Status pill
            int tw = Math.Min(Ui.Measure(status, Ui.Small, 0, false).Width, w - Ui.Px(34));
            int pw = tw + Ui.Px(34);
            Ui.FillRound(g, Ui.Pill, new RectangleF(x, y, pw, ph), ph / 2f);
            float dr = Ui.Px(3.5f);
            using (var br = new SolidBrush(dot)) g.FillEllipse(br, x + Ui.Px(11), y + ph / 2f - dr, dr * 2, dr * 2);
            Ui.DrawText(g, status, Ui.Small, Ui.Text2, new Rectangle(x + Ui.Px(24), y, tw + 2, ph - Ui.Px(1)), TextFormatFlags.VerticalCenter);
        }
    }

    // ---------------- Navigation pane ----------------

    // Left-hand navigation in the style of a Windows 11 NavigationView: the selection pill slides
    // between items. The foot of the pane shows the live camera and light status.
    class NavPane : UiControl
    {
        readonly List<string> glyphs = new List<string>(), texts = new List<string>();
        int selected = -1, hoverItem = -1, downItem = -1;
        readonly Tween slide;
        string camText = "", lightText = "";
        Color camDot, lightDot;
        public event EventHandler SelectedChanged;

        public NavPane()
        {
            Dock = DockStyle.Left;
            Width = Ui.Px(228);
            slide = NewTween(0.28f);
            MakeFocusable();
        }

        public void AddItem(string glyph, string text) { glyphs.Add(glyph); texts.Add(text); Invalidate(); }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected || value < 0 || value >= texts.Count) return;
                bool first = selected < 0;
                selected = value;
                if (first) slide.Jump(value); else slide.To(value);
                Invalidate();
            }
        }

        public void SetStatus(string cam, Color camColor, string light, Color lightColor)
        {
            camText = cam; camDot = camColor; lightText = light; lightDot = lightColor; Invalidate();
        }

        static int ItemH { get { return Ui.Px(36); } }
        static int Pitch { get { return Ui.Px(40); } }
        static int Top0 { get { return Ui.Px(8); } }

        Rectangle ItemRect(int i) { return new Rectangle(Ui.Px(8), Top0 + i * Pitch, Width - Ui.Px(12), ItemH); }

        int ItemAt(Point p)
        {
            for (int i = 0; i < texts.Count; i++) if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        void Select(int i)
        {
            if (i == selected || i < 0 || i >= texts.Count) return;
            SelectedIndex = i;
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = ItemAt(e.Location);
            if (i == hoverItem) return;
            hoverItem = i;
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { hoverItem = -1; downItem = -1; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { downItem = e.Button == MouseButtons.Left ? ItemAt(e.Location) : -1; base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = downItem;
            downItem = -1;
            base.OnMouseUp(e);
            if (i >= 0 && i == ItemAt(e.Location)) Select(i);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down) Select(selected + 1);
            else if (e.KeyCode == Keys.Up) Select(selected - 1);
            base.OnKeyDown(e);
        }

        protected override void Draw(Graphics g)
        {
            for (int i = 0; i < texts.Count; i++)
            {
                Rectangle r = ItemRect(i);
                if (i == selected || i == hoverItem)
                    Ui.FillRound(g, i == downItem ? Ui.SubtlePressed : Ui.Subtle, r, Ui.Px(4));
                Ui.Glyph(g, glyphs[i], Ui.Icon, Ui.Text, new Rectangle(r.X + Ui.Px(10), r.Y, Ui.Px(24), r.Height));
                Ui.DrawText(g, texts[i], Ui.Body, Ui.Text, new Rectangle(r.X + Ui.Px(46), r.Y, r.Width - Ui.Px(52), r.Height - Ui.Px(1)), TextFormatFlags.VerticalCenter);
            }

            if (selected >= 0)
            {
                // Selection pill: it stretches while it travels to the new item.
                float travel = Math.Min(1f, Math.Abs(slide.Target - slide.Value));
                float ph = Ui.Px(16) + travel * Ui.Px(14);
                float py = Top0 + slide.Value * Pitch + (ItemH - ph) / 2f;
                Ui.FillRound(g, Ui.Accent, new RectangleF(Ui.Px(8), py, Ui.Px(3), ph), Ui.Px(1.5f));
                if (ShowFocus) Ui.FocusRing(g, ItemRect(selected), Ui.Px(4));
            }

            int y = Height - Ui.Px(66);
            using (var b = new SolidBrush(Ui.Divider)) g.FillRectangle(b, Ui.Px(16), y - Ui.Px(10), Width - Ui.Px(28), Ui.Hair);
            StatusLine(g, y, camText, camDot);
            StatusLine(g, y + Ui.Px(26), lightText, lightDot);
        }

        void StatusLine(Graphics g, int y, string text, Color dot)
        {
            float r = Ui.Px(4);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, Ui.Px(30) - r, y + Ui.Px(10) - r, r * 2, r * 2);
            Ui.DrawText(g, text, Ui.Small, Ui.Text2, new Rectangle(Ui.Px(54), y, Width - Ui.Px(62), Ui.Px(20)), TextFormatFlags.VerticalCenter);
        }
    }
}
