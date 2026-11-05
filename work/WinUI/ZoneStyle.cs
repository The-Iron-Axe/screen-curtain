namespace ScreenCurtain
{
    // Values only: a copied style never keeps the source zone or its geometry alive.
    internal readonly struct ZoneStyle
    {
        internal int ColorRgb { get; }
        internal int OpacityPercent { get; }
        internal BorderSettings Border { get; }
        private ZoneStyle(Zone zone)
        {
            ColorRgb = Appearance.ValidRgb(zone.ColorRgb);
            OpacityPercent = Appearance.ValidOpacity(zone.OpacityPercent);
            Border = BorderSettings.FromZone(zone);
        }
        internal static ZoneStyle FromZone(Zone zone) { return new ZoneStyle(zone); }
        internal void ApplyTo(Zone zone)
        {
            zone.ColorRgb = ColorRgb; zone.OpacityPercent = OpacityPercent; Border.ApplyTo(zone);
        }
    }
}
