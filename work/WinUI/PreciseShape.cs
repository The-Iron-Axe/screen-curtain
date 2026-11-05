using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ScreenCurtain
{
    internal static class PreciseShape
    {
        internal static Zone Create(SelectionShape shape, int a, int b, int c)
        {
            if (a < 1 || b < 1 || c < 1) throw new ArgumentException("尺寸必须大于零。");
            if (shape == SelectionShape.Ellipse)
            {
                if (a < 2) throw new ArgumentException("半径至少为 2 像素。");
                var circle = Geometry.RectangleZone(Point.Empty, new Point(2 * a - 1, 2 * a - 1), Point.Empty);
                circle.Curve = CurveShape.Ellipse; return circle;
            }
            if (shape == SelectionShape.Rectangle || shape == SelectionShape.RoundedRectangle)
            {
                var rectangle = Geometry.RectangleZone(Point.Empty, new Point(a - 1, b - 1), Point.Empty);
                if (shape == SelectionShape.RoundedRectangle) rectangle.Curve = CurveShape.RoundedRectangle;
                if (!Geometry.Valid(rectangle)) throw new ArgumentException("宽和高至少为 3 像素。");
                return rectangle;
            }
            if (shape != SelectionShape.Triangle) throw new ArgumentException("此形状不支持精确尺寸。");
            if ((long)b + c <= a || (long)a + c <= b || (long)a + b <= c) throw new ArgumentException("三条边必须满足任意两边之和大于第三边。");
            // a is the horizontal base; b and c are the left and right sides.
            double x = ((double)b * b + (double)a * a - (double)c * c) / (2 * a);
            double y = Math.Sqrt(Math.Max(0, (double)b * b - x * x));
            int apexX = (int)Math.Round(x, MidpointRounding.AwayFromZero), height = (int)Math.Round(y, MidpointRounding.AwayFromZero);
            int left = Math.Min(0, apexX);
            var triangle = new Zone { Polygon = true, Points = new List<Point> { new Point(apexX - left, 0), new Point(a - left, height), new Point(-left, height) } };
            if (!Geometry.Valid(triangle)) throw new ArgumentException("像素取整后形状过小，请增大边长。");
            return triangle;
        }
        internal static Zone ShowDialog(IWin32Window owner, SelectionShape shape, Size available)
        {
            using (var form = new Form { Text = "输入精确尺寸", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(380, 260), MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, TopMost = true,
                Font = new Font("Microsoft YaHei UI", 9f), AutoScaleMode = AutoScaleMode.Font })
            {
                string[] labels = shape == SelectionShape.Ellipse ? new[] { "圆形半径（像素）" } : shape == SelectionShape.Triangle ?
                    new[] { "底边长度（像素）", "左边长度（像素）", "右边长度（像素）" } : new[] { "宽度（像素）", "高度（像素）" };
                var inputs = new List<NumericUpDown>();
                for (int i = 0; i < labels.Length; i++)
                {
                    form.Controls.Add(new Label { Text = labels[i], Location = new Point(20, 20 + i * 44), Size = new Size(180, 30) });
                    var input = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = 100, Location = new Point(210, 20 + i * 44), Width = 130 };
                    inputs.Add(input); form.Controls.Add(input);
                }
                var note = new Label { Text = shape == SelectionShape.Triangle ? "三角形底边水平，顶点按像素取整。" : "输入后移动鼠标预览，单击放置。", Location = new Point(20, 157), AutoSize = true };
                var ok = new Button { Text = "确定", Location = new Point(174, 205), Size = new Size(80, 30) };
                var cancel = new Button { Text = "取消", Location = new Point(264, 205), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
                Zone result = null;
                ok.Click += (s, e) => {
                    try {
                        result = Create(shape, (int)inputs[0].Value, inputs.Count > 1 ? (int)inputs[1].Value : 1, inputs.Count > 2 ? (int)inputs[2].Value : 1);
                        if (result.Bounds.Width > available.Width || result.Bounds.Height > available.Height) throw new ArgumentException("尺寸超过整个桌面的范围，请缩小尺寸。");
                        form.DialogResult = DialogResult.OK; form.Close();
                    } catch (ArgumentException ex) { MessageBox.Show(form, ex.Message, "尺寸无效", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                };
                form.Controls.Add(note); form.Controls.Add(ok); form.Controls.Add(cancel); form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog(owner) == DialogResult.OK ? result : null;
            }
        }
        internal static Zone Place(Zone template, Point location)
        {
            return new Zone { Polygon = template.Polygon, Curve = template.Curve, CornerPercent = template.CornerPercent,
                Points = template.Points.Select(p => new Point(p.X + location.X, p.Y + location.Y)).ToList() };
        }
    }
}
