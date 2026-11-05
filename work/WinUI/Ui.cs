using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ScreenCurtain
{
    internal static class Ui
    {
        // Brushes are immutable in this UI; bound the cache so imported custom colors cannot grow it indefinitely.
        private static readonly Dictionary<uint, SolidColorBrush> palette = new Dictionary<uint, SolidColorBrush>();
        internal static SolidColorBrush Brush(uint rgb)
        {
            if (palette.TryGetValue(rgb, out var brush)) return brush;
            brush = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            if (palette.Count < 64) palette.Add(rgb, brush);
            return brush;
        }
        internal static TextBlock Text(string text, double size = 14, uint color = 0x192331)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }
        internal static FontIcon Icon(string glyph, double size = 16)
        {
            return new FontIcon { Glyph = glyph, FontSize = size };
        }
        internal static Canvas ShapeIcon(Zone zone)
        {
            var icon = new Canvas { Width = 20, Height = 20, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            Microsoft.UI.Xaml.Shapes.Shape outline;
            if (zone.Curve == CurveShape.Ellipse)
            {
                bool circle = zone.Bounds.Width == zone.Bounds.Height;
                outline = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = circle ? 16 : 18, Height = circle ? 16 : 12 };
                Canvas.SetLeft(outline, circle ? 2 : 1); Canvas.SetTop(outline, circle ? 2 : 4);
            }
            else if (zone.Polygon)
            {
                var polygon = new Microsoft.UI.Xaml.Shapes.Polygon();
                var points = zone.Points.Count == 3 ? new[] { new Windows.Foundation.Point(10, 2), new Windows.Foundation.Point(19, 18), new Windows.Foundation.Point(1, 18) } :
                    new[] { new Windows.Foundation.Point(2, 7), new Windows.Foundation.Point(9, 2), new Windows.Foundation.Point(18, 5),
                        new Windows.Foundation.Point(17, 15), new Windows.Foundation.Point(8, 18), new Windows.Foundation.Point(1, 14) };
                foreach (var point in points) polygon.Points.Add(point);
                outline = polygon;
            }
            else
            {
                double radius = zone.Curve == CurveShape.RoundedRectangle ? 4 : 0;
                outline = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 18, Height = 14, RadiusX = radius, RadiusY = radius };
                Canvas.SetLeft(outline, 1); Canvas.SetTop(outline, 3);
            }
            outline.Stroke = Brush(0x44536a); outline.StrokeThickness = 1.5; outline.StrokeLineJoin = PenLineJoin.Round;
            icon.Children.Add(outline);
            ToolTipService.SetToolTip(icon, ShapeGeometry.Label(zone));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            return icon;
        }
        internal static Button Button(string label, Action click, bool accent = false, string glyph = null)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (glyph != null) content.Children.Add(Icon(glyph));
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 10, 14, 10) };
            if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            button.Click += (s, e) => click();
            return button;
        }
        internal static Grid Columns(params GridLength[] columns)
        {
            var grid = new Grid { ColumnSpacing = 10 };
            foreach (var column in columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column });
            return grid;
        }
        internal static void Put(Grid grid, FrameworkElement child, int row, int column = 0, int span = 1)
        {
            Grid.SetRow(child, row); Grid.SetColumn(child, column); Grid.SetColumnSpan(child, span); grid.Children.Add(child);
        }
        internal static GridLength Star { get { return new GridLength(1, GridUnitType.Star); } }
        internal static GridLength Auto { get { return GridLength.Auto; } }
    }
}
