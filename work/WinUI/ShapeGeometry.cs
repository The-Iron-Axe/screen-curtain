using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ScreenCurtain
{
    public enum CurveShape { None, Ellipse, RoundedRectangle }
    internal enum SelectionShape { Rectangle, Polygon, Ellipse, Triangle, RoundedRectangle }

    internal static class ShapeGeometry
    {
        internal static string Label(Zone zone)
        {
            if (zone.Curve == CurveShape.Ellipse) return zone.Bounds.Width == zone.Bounds.Height ? "圆形" : "椭圆";
            if (zone.Curve == CurveShape.RoundedRectangle) return "圆角矩形";
            return zone.Polygon ? (zone.Points.Count == 3 ? "三角形" : "多边形") : "矩形";
        }
        internal static Zone FromDrag(SelectionShape shape, Point start, Point end, Point origin, bool square = false)
        {
            if (square && shape == SelectionShape.Ellipse)
            {
                int size = Math.Min(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
                end = new Point(start.X + Math.Sign(end.X - start.X) * size, start.Y + Math.Sign(end.Y - start.Y) * size);
            }
            var zone = Geometry.RectangleZone(start, end, origin);
            if (shape == SelectionShape.Ellipse) zone.Curve = CurveShape.Ellipse;
            if (shape == SelectionShape.RoundedRectangle) zone.Curve = CurveShape.RoundedRectangle;
            if (shape == SelectionShape.Triangle)
            {
                var r = zone.Bounds;
                zone.Polygon = true;
                zone.Points = new List<Point> { new Point(r.Left + (r.Width - 1) / 2, r.Top),
                    new Point(r.Right - 1, r.Bottom - 1), new Point(r.Left, r.Bottom - 1) };
            }
            return zone;
        }
        internal static GraphicsPath Path(Zone zone, Point origin)
        {
            var path = new GraphicsPath(FillMode.Alternate);
            if (zone.Polygon) path.AddPolygon(zone.Points.Select(p => new Point(p.X - origin.X, p.Y - origin.Y)).ToArray());
            else
            {
                Rectangle bounds = zone.Bounds; bounds.Offset(-origin.X, -origin.Y);
                if (zone.Curve == CurveShape.Ellipse) path.AddEllipse(bounds);
                else if (zone.Curve == CurveShape.RoundedRectangle && zone.CornerPercent > 0)
                {
                    float d = Math.Min(bounds.Width, bounds.Height) * Math.Clamp(zone.CornerPercent, 0, 50) / 50f;
                    path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
                    path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
                    path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                    path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                }
                else path.AddRectangle(bounds);
            }
            return path;
        }
        internal static List<Point> ExportPoints(Zone zone)
        {
            if (zone.Curve == CurveShape.None) return zone.Points.ToList();
            var r = zone.Bounds;
            double width = r.Width - 1, height = r.Height - 1;
            var points = new List<Point>();
            void Add(double x, double y) { points.Add(new Point((int)Math.Round(x, MidpointRounding.AwayFromZero), (int)Math.Round(y, MidpointRounding.AwayFromZero))); }
            if (zone.Curve == CurveShape.Ellipse)
            {
                for (int i = 0; i < 256; i++)
                {
                    double angle = i * Math.PI * 2 / 256;
                    Add(r.Left + width * (1 + Math.Cos(angle)) / 2, r.Top + height * (1 + Math.Sin(angle)) / 2);
                }
            }
            else
            {
                double radius = Math.Min(width, height) * Math.Clamp(zone.CornerPercent, 0, 50) / 100.0;
                if (radius == 0) return zone.Points.ToList();
                var centers = new[] { (r.Left + radius, r.Top + radius), (r.Left + width - radius, r.Top + radius),
                    (r.Left + width - radius, r.Top + height - radius), (r.Left + radius, r.Top + height - radius) };
                for (int corner = 0; corner < 4; corner++)
                    for (int i = 0; i <= 32; i++)
                    {
                        double angle = (180 + corner * 90 + i * 90.0 / 32) * Math.PI / 180;
                        Add(centers[corner].Item1 + radius * Math.Cos(angle), centers[corner].Item2 + radius * Math.Sin(angle));
                    }
            }
            return points.Distinct().ToList();
        }
    }
}
