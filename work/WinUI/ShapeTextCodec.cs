using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenCurtain
{
    internal enum CoordinateMode { Pixels, ScreenPercent }

    internal static class ShapeTextCodec
    {
        internal const int MaxShapes = 100;
        internal const int MaxTextLength = 2 * 1024 * 1024;
        private static readonly Regex Pair = new Regex(@"\G\s*\[\s*(?<x>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*(?<xp>%?)\s*,\s*(?<y>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*(?<yp>%?)\s*\]",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

        internal static List<Zone> Parse(string text, CoordinateMode mode, Rectangle screen)
        {
            if (text == null || text.Length > MaxTextLength) throw new FormatException("TXT 文件过大，请控制在 2 MB 字符以内。");
            ValidateScreen(screen);
            var result = new List<Zone>();
            using (var reader = new StringReader(text.TrimStart('\ufeff')))
            {
                string line; int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (result.Count == MaxShapes) throw Error(lineNumber, "一次最多导入 " + MaxShapes + " 个形状。");
                    if (line.Length > 32768) throw Error(lineNumber, "这一行过长，每个形状最多 256 个顶点。");
                    var points = new List<Point>();
                    int offset = 0;
                    while (offset < line.Length)
                    {
                        Match match = Pair.Match(line, offset);
                        if (!match.Success) throw Error(lineNumber, "格式应为 [x1,y1],[x2,y2],[x3,y3]，每行一个形状。");
                        points.Add(new Point(ReadCoordinate(match, "x", mode, screen.X, screen.Width, lineNumber),
                            ReadCoordinate(match, "y", mode, screen.Y, screen.Height, lineNumber)));
                        if (points.Count > 257) throw Error(lineNumber, "每个形状最多 256 个顶点。");
                        offset = match.Index + match.Length;
                        while (offset < line.Length && char.IsWhiteSpace(line[offset])) offset++;
                        if (offset == line.Length) break;
                        if (line[offset] != ',') throw Error(lineNumber, "相邻顶点之间需要英文逗号。");
                        offset++;
                        if (string.IsNullOrWhiteSpace(line.Substring(offset))) throw Error(lineNumber, "行末的逗号后缺少顶点。");
                    }
                    if (points.Count > 1 && points[points.Count - 1] == points[0]) points.RemoveAt(points.Count - 1);
                    if (points.Count > 256) throw Error(lineNumber, "每个形状最多 256 个顶点。");
                    var zone = new Zone { Polygon = true, Points = points };
                    if (!Geometry.Valid(zone)) throw Error(lineNumber, "需要至少 3 个顶点并形成有效面积；比例换算后的区域也不能过小。");
                    if (IsRectangle(points))
                    {
                        var r = zone.Bounds;
                        zone = Geometry.RectangleZone(r.Location, new Point(r.Right - 1, r.Bottom - 1), Point.Empty);
                    }
                    result.Add(zone);
                }
            }
            if (result.Count == 0) throw new FormatException("TXT 中没有形状，请每行填写至少 3 个顶点。");
            return result;
        }
        private static int ReadCoordinate(Match match, string name, CoordinateMode mode, int origin, int size, int line)
        {
            decimal value;
            if (!decimal.TryParse(match.Groups[name].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value)) throw Error(line, "坐标数值无效或过大。");
            if (mode == CoordinateMode.Pixels)
            {
                if (match.Groups[name + "p"].Length > 0) throw Error(line, "检测到百分号，请切换为“屏幕比例”模式。");
                if (decimal.Truncate(value) != value) throw Error(line, "像素坐标应为整数；百分比小数请使用“屏幕比例”模式。");
            }
            else
            {
                // Pre-bound the number so even very large decimal input cannot overflow during conversion.
                if (value < -20000000m || value > 20000000m) throw Error(line, "屏幕比例超出可用范围。");
                value = decimal.Round(origin + value * size / 100m, 0, MidpointRounding.AwayFromZero);
            }
            if (value < -100000 || value > 100000) throw Error(line, "换算后的像素坐标需在 -100000～100000 范围内。");
            return (int)value;
        }
        private static bool IsRectangle(IList<Point> points)
        {
            if (points.Count != 4 || points.Distinct().Count() != 4) return false;
            if (points.Select(p => p.X).Distinct().Count() != 2 || points.Select(p => p.Y).Distinct().Count() != 2) return false;
            return Enumerable.Range(0, 4).All(i => (points[i].X == points[(i + 1) % 4].X) != (points[i].Y == points[(i + 1) % 4].Y));
        }
        internal static List<Zone> ScalePixels(IEnumerable<Zone> zones, Size basis, Rectangle fitted)
        {
            if (basis.Width <= 0 || basis.Height <= 0) throw new FormatException("基准分辨率无效。");
            ValidateScreen(fitted);
            double scale = Math.Min((double)fitted.Width / basis.Width, (double)fitted.Height / basis.Height);
            int Convert(int value, int origin)
            {
                double converted = Math.Round(origin + value * scale, MidpointRounding.AwayFromZero);
                if (converted < -100000 || converted > 100000) throw new FormatException("换算后的像素坐标需在 -100000～100000 范围内。");
                return (int)converted;
            }
            var result = new List<Zone>();
            foreach (var zone in zones)
            {
                var mapped = new Zone { Polygon = zone.Polygon, Curve = zone.Curve, CornerPercent = zone.CornerPercent,
                    Points = zone.Points.Select(p => new Point(Convert(p.X, fitted.X), Convert(p.Y, fitted.Y))).ToList() };
                if (!Geometry.Valid(mapped)) throw new FormatException("换算后的区域过小或没有有效面积，请调整基准分辨率或坐标。");
                result.Add(mapped);
            }
            return result;
        }
        internal static string Serialize(IEnumerable<Zone> zones, CoordinateMode mode, Rectangle screen)
        {
            ValidateScreen(screen);
            var lines = new List<string>();
            foreach (var zone in zones)
            {
                if (!Geometry.Valid(zone)) throw new FormatException("存在无效选区，无法导出。");
                lines.Add(string.Join(",", ShapeGeometry.ExportPoints(zone).Select(p => "[" + FormatCoordinate(p.X, mode, screen.X, screen.Width) + "," +
                    FormatCoordinate(p.Y, mode, screen.Y, screen.Height) + "]")));
            }
            if (lines.Count == 0) throw new FormatException("还没有可以导出的选区。");
            return string.Join("\r\n", lines) + "\r\n";
        }
        private static string FormatCoordinate(int value, CoordinateMode mode, int origin, int size)
        {
            return mode == CoordinateMode.Pixels ? value.ToString(CultureInfo.InvariantCulture) :
                (((decimal)value - origin) * 100m / size).ToString("0.########", CultureInfo.InvariantCulture) + "%";
        }
        private static void ValidateScreen(Rectangle screen)
        {
            if (screen.Width <= 0 || screen.Height <= 0) throw new FormatException("基准屏幕尺寸无效。");
        }
        private static FormatException Error(int line, string reason) { return new FormatException("第 " + line + " 行：" + reason); }
        internal static string ReadFile(string path)
        {
            if (new FileInfo(path).Length > MaxTextLength * 4L) throw new FormatException("TXT 文件过大。");
            string text = File.ReadAllText(path, new UTF8Encoding(false, true));
            if (text.Length > MaxTextLength) throw new FormatException("TXT 文件过大，请控制在 2 MB 字符以内。");
            return text;
        }
        internal static void WriteFile(string path, string text)
        {
            string full = Path.GetFullPath(path);
            string temp = Path.Combine(Path.GetDirectoryName(full), ".screen-curtain-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(full)) File.Replace(temp, full, null); else File.Move(temp, full);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
