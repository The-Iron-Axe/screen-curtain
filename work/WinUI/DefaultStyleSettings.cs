namespace ScreenCurtain
{
    public sealed class DefaultStyleSettings
    {
        public int ColorRgb { get; set; }
        public int OpacityPercent { get; set; } = 100;
        public bool BorderEnabled { get; set; }
        public int BorderWidth { get; set; } = 2;
        public int BorderColorRgb { get; set; } = BorderSettings.DefaultColor;
        public int BorderStyle { get; set; }

        internal Zone ToZone()
        {
            return new Zone { ColorRgb = ColorRgb, OpacityPercent = OpacityPercent, BorderEnabled = BorderEnabled,
                BorderWidth = BorderWidth, BorderColorRgb = BorderColorRgb, BorderStyle = BorderStyle };
        }
        internal static DefaultStyleSettings FromStyle(ZoneStyle style)
        {
            return new DefaultStyleSettings { ColorRgb = style.ColorRgb, OpacityPercent = style.OpacityPercent,
                BorderEnabled = style.Border.Enabled, BorderWidth = style.Border.Width, BorderColorRgb = style.Border.ColorRgb, BorderStyle = style.Border.Style };
        }
    }
}
