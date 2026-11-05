using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ScreenCurtain
{
    internal readonly struct BorderSettings
    {
        internal static readonly string[] StyleNames = { "实线", "虚线", "点线", "点划线", "双点划线" };
        internal const int DefaultColor = 0x176be8;
        internal bool Enabled { get; }
        internal int Width { get; }
        internal int ColorRgb { get; }
        internal int Style { get; }
        internal BorderSettings(bool enabled, int width, int colorRgb, int style)
        {
            Enabled = enabled; Width = Math.Clamp(width, 1, 50);
            ColorRgb = Appearance.ValidRgb(colorRgb); Style = style >= 0 && style < StyleNames.Length ? style : 0;
        }
        internal static BorderSettings FromZone(Zone zone) { return new BorderSettings(zone.BorderEnabled, zone.BorderWidth, zone.BorderColorRgb, zone.BorderStyle); }
        internal void ApplyTo(Zone zone)
        {
            zone.BorderEnabled = Enabled; zone.BorderWidth = Width; zone.BorderColorRgb = ColorRgb; zone.BorderStyle = Style;
        }
        internal static float[] Pattern(int style)
        {
            switch (style)
            {
                case 1: return new[] { 4f, 2f };
                case 2: return new[] { 1f, 2f };
                case 3: return new[] { 4f, 2f, 1f, 2f };
                case 4: return new[] { 4f, 2f, 1f, 2f, 1f, 2f };
                default: return null;
            }
        }
    }
    internal static class MaskBorderRenderer
    {
        internal static void Draw(Graphics g, Zone zone, Point origin, BorderSettings border)
        {
            if (!border.Enabled) return;
            using (var path = ShapeGeometry.Path(zone, origin))
            using (var pen = new Pen(Appearance.ToColor(border.ColorRgb), border.Width * 2f))
            {
                pen.LineJoin = LineJoin.Round; pen.DashCap = DashCap.Flat;
                var pattern = BorderSettings.Pattern(border.Style);
                // The outer half is clipped away; compensate dash lengths for the doubled stroke width.
                if (pattern != null) pen.DashPattern = pattern.Select(length => length / 2f).ToArray();
                GraphicsState saved = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.SetClip(path, CombineMode.Intersect);
                    g.DrawPath(pen, path);
                }
                finally { g.Restore(saved); }
            }
        }
    }
}
