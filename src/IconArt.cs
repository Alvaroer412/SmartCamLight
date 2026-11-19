using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SmartCamLight
{
    static class IconArt
    {
        public static void Draw(Graphics g, int size, bool on, bool cam, bool missing)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = size / 32f;

            Color panel = on ? Color.FromArgb(255, 210, 80) : Color.FromArgb(165, 165, 170);
            Color rim = on ? Color.FromArgb(190, 130, 20) : Color.FromArgb(95, 95, 100);
            if (missing) { panel = Color.FromArgb(120, 120, 125); rim = Color.FromArgb(70, 70, 75); }

            if (on)
                using (var glow = new SolidBrush(Color.FromArgb(70, 255, 215, 110)))
                    g.FillEllipse(glow, 1 * s, -2 * s, 30 * s, 28 * s);

            using (var path = RoundRect(new RectangleF(6 * s, 2 * s, 20 * s, 19 * s), 6 * s))
            using (var b = new SolidBrush(panel))
            using (var p = new Pen(rim, Math.Max(1f, 2f * s)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(rim))
            {
                g.FillRectangle(b, 14.5f * s, 21f * s, 3f * s, 6f * s);
                using (var basePath = RoundRect(new RectangleF(9 * s, 26.5f * s, 14 * s, 4 * s), 2 * s))
                    g.FillPath(b, basePath);
            }
            if (cam)
            {
                using (var w = new SolidBrush(Color.White))
                    g.FillEllipse(w, 19 * s, 18 * s, 13 * s, 13 * s);
                using (var r = new SolidBrush(Color.FromArgb(225, 40, 40)))
                    g.FillEllipse(r, 21 * s, 20 * s, 9 * s, 9 * s);
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Icon MakeIcon(int size, bool on, bool cam, bool missing)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) Draw(g, size, on, cam, missing);
                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return icon;
            }
        }

        // Writes a multi-size PNG-compressed .ico (used at build time for the exe icon).
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 256 };
            var pngs = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (var bmp = new Bitmap(sizes[i], sizes[i], PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp)) Draw(g, sizes[i], true, false, false);
                    using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs[i] = ms.ToArray(); }
                }
            }
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((ushort)1); bw.Write((ushort)32);
                    bw.Write(pngs[i].Length); bw.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var png in pngs) bw.Write(png);
            }
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    }
}
