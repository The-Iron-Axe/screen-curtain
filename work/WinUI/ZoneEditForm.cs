using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ScreenCurtain
{
    internal sealed class ZoneEditForm : Form
    {
        private readonly Zone original;
        private readonly Rectangle desktop;
        private readonly List<Point> points;
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 12f);
        private readonly Font textFont = new Font("Microsoft YaHei UI", 10f);
        private List<Point> beforeDrag;
        private Point dragOffset, opposite;
        private Point moveStart;
        private bool moving;
        private int dragging = -1, hovered = -1;
        private string error = "";
        private Rectangle toolbar, saveButton, resetButton, cancelButton;
        private float toolbarScale;
        internal readonly NumericUpDown RadiusInput;
        private readonly Label radiusLabel;
        internal Zone Result { get; private set; }
        internal bool IsDragging { get { return dragging >= 0 || moving; } }
        internal Rectangle SaveButtonBounds { get { return saveButton; } }
        internal Rectangle ResetButtonBounds { get { return resetButton; } }
        internal Zone Draft
        {
            get
            {
                return new Zone { Number = original.Number, Name = original.Name, Polygon = original.Polygon, Enabled = original.Enabled,
                    Curve = original.Curve, CornerPercent = RadiusInput == null ? original.CornerPercent : (int)RadiusInput.Value,
                    ColorRgb = original.ColorRgb, OpacityPercent = original.OpacityPercent,
                    BorderEnabled = original.BorderEnabled, BorderWidth = original.BorderWidth, BorderColorRgb = original.BorderColorRgb, BorderStyle = original.BorderStyle,
                    Points = points.Select(p => new Point(p.X + desktop.X, p.Y + desktop.Y)).ToList() };
            }
        }

        internal ZoneEditForm(Zone zone, Rectangle screenBounds)
        {
            original = zone;
            desktop = screenBounds;
            points = zone.Points.Select(p => new Point(p.X - desktop.X, p.Y - desktop.Y)).ToList();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = desktop;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.68;
            DoubleBuffered = true;
            KeyPreview = true;
            if (zone.Curve == CurveShape.RoundedRectangle)
            {
                RadiusInput = new NumericUpDown { Minimum = 0, Maximum = 50, Value = Math.Clamp(zone.CornerPercent, 0, 50),
                    Font = textFont, AccessibleName = "圆角占短边的百分比" };
                radiusLabel = new Label { Text = "圆角 %", ForeColor = Color.White, BackColor = Color.FromArgb(24, 32, 44),
                    Font = textFont, TextAlign = ContentAlignment.MiddleLeft };
                RadiusInput.ValueChanged += (s, e) => Invalidate();
                Controls.Add(radiusLabel); Controls.Add(RadiusInput);
            }
            LayoutToolbar();
        }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= (int)Native.WS_EX_TOOLWINDOW; return p; }
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.Place(Handle, desktop);
            Activate(); Focus();
        }
        private Point Clamp(Point p)
        {
            return new Point(Math.Max(0, Math.Min(ClientSize.Width - 1, p.X)), Math.Max(0, Math.Min(ClientSize.Height - 1, p.Y)));
        }
        private int HitVertex(Point p)
        {
            int radius = Native.PixelsAt(new Point(p.X + desktop.X, p.Y + desktop.Y), 12, DeviceDpi);
            long nearest = (long)radius * radius;
            int hit = -1;
            for (int i = 0; i < points.Count; i++)
            {
                long dx = (long)p.X - points[i].X, dy = (long)p.Y - points[i].Y;
                long distance = dx * dx + dy * dy;
                if (distance <= nearest) { nearest = distance; hit = i; }
            }
            return hit;
        }
        private bool HitShape(Point p)
        {
            var shape = new Zone { Polygon = original.Polygon, Curve = original.Curve, CornerPercent = Draft.CornerPercent, Points = points };
            using (var path = ShapeGeometry.Path(shape, Point.Empty)) return path.IsVisible(p);
        }
        private void Translate(IList<Point> start, int dx, int dy)
        {
            int minX = start.Min(p => p.X), maxX = start.Max(p => p.X);
            int minY = start.Min(p => p.Y), maxY = start.Max(p => p.Y);
            dx = Math.Clamp(dx, Math.Min(0, -minX), Math.Max(0, ClientSize.Width - 1 - maxX));
            dy = Math.Clamp(dy, Math.Min(0, -minY), Math.Max(0, ClientSize.Height - 1 - maxY));
            for (int i = 0; i < points.Count; i++) points[i] = new Point(start[i].X + dx, start[i].Y + dy);
            error = ""; Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { CancelEdit(); return; }
            if (e.Button != MouseButtons.Left) return;
            int hit = HitVertex(e.Location);
            // Handles have priority even when a vertex overlaps the floating toolbar.
            if (hit >= 0)
            {
                dragging = hovered = hit;
                beforeDrag = points.ToList();
                dragOffset = new Point(e.X - points[hit].X, e.Y - points[hit].Y);
                if (!original.Polygon) opposite = points[(hit + 2) % 4];
                error = ""; Capture = true; Cursor = Cursors.SizeAll; Invalidate(); return;
            }
            if (saveButton.Contains(e.Location)) ConfirmEdit();
            else if (cancelButton.Contains(e.Location)) CancelEdit();
            else if (resetButton.Contains(e.Location)) ResetEdit();
            else if (HitShape(e.Location))
            {
                moving = true; moveStart = e.Location; beforeDrag = points.ToList();
                error = ""; Capture = true; Cursor = Cursors.SizeAll; Invalidate();
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (moving) Translate(beforeDrag, e.X - moveStart.X, e.Y - moveStart.Y);
            else if (dragging >= 0)
            {
                Point p = Clamp(new Point(e.X - dragOffset.X, e.Y - dragOffset.Y));
                points[dragging] = p;
                if (!original.Polygon)
                {
                    // Keep the opposite corner fixed and both neighboring edges axis-aligned.
                    points[(dragging + 2) % 4] = opposite;
                    points[(dragging + 1) % 4] = dragging % 2 == 0 ? new Point(opposite.X, p.Y) : new Point(p.X, opposite.Y);
                    points[(dragging + 3) % 4] = dragging % 2 == 0 ? new Point(p.X, opposite.Y) : new Point(opposite.X, p.Y);
                }
            }
            else
            {
                hovered = HitVertex(e.Location);
                bool button = saveButton.Contains(e.Location) || resetButton.Contains(e.Location) || cancelButton.Contains(e.Location);
                Cursor = hovered >= 0 ? Cursors.SizeAll : button ? Cursors.Hand : HitShape(e.Location) ? Cursors.SizeAll : Cursors.Default;
            }
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && IsDragging)
            {
                // Apply the final mouse location even if Windows coalesced the last move event.
                OnMouseMove(e);
                EndDrag();
            }
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && IsDragging) EndDrag();
        }
        private void EndDrag()
        {
            dragging = -1; moving = false; Capture = false;
            if (!Geometry.Valid(Draft))
            {
                points.Clear(); points.AddRange(beforeDrag);
                error = "调整后区域过小或没有面积，已恢复这次拖动。";
            }
            beforeDrag = null; Invalidate();
        }
        private void ResetEdit()
        {
            if (IsDragging) EndDrag();
            points.Clear(); points.AddRange(original.Points.Select(p => new Point(p.X - desktop.X, p.Y - desktop.Y)));
            if (RadiusInput != null) RadiusInput.Value = Math.Clamp(original.CornerPercent, 0, 50);
            error = ""; hovered = -1; Invalidate();
        }
        private void ConfirmEdit()
        {
            if (IsDragging) EndDrag();
            Zone draft = Draft;
            if (!Geometry.Valid(draft)) { error = "选区没有有效面积，请调整顶点后再保存。"; Invalidate(); return; }
            Result = draft; DialogResult = DialogResult.OK; Close();
        }
        private void CancelEdit()
        {
            dragging = -1; moving = false; Capture = false;
            Result = null; DialogResult = DialogResult.Cancel; Close();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter) { ConfirmEdit(); return true; }
            if (keyData == Keys.Escape) { CancelEdit(); return true; }
            if (!IsDragging && (RadiusInput == null || !RadiusInput.ContainsFocus))
            {
                int dx = keyData == Keys.Left ? -1 : keyData == Keys.Right ? 1 : 0;
                int dy = keyData == Keys.Up ? -1 : keyData == Keys.Down ? 1 : 0;
                if (dx != 0 || dy != 0) { Translate(points.ToList(), dx, dy); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }
        private void LayoutToolbar()
        {
            Rectangle monitor = Rectangle.Intersect(Screen.FromRectangle(original.Bounds).WorkingArea, desktop);
            if (monitor.Width < 200 || monitor.Height < 200) monitor = desktop;
            toolbarScale = Native.PixelsAt(monitor.Location, 96, DeviceDpi) / 96f;
            int margin = (int)(18 * toolbarScale), width = Math.Min((int)(740 * toolbarScale), monitor.Width - margin * 2);
            int height = (int)((RadiusInput == null ? 116 : 156) * toolbarScale);
            int x = monitor.X - desktop.X + (monitor.Width - width) / 2;
            var top = new Rectangle(x, monitor.Y - desktop.Y + margin, width, height);
            var bottom = new Rectangle(x, monitor.Bottom - desktop.Y - margin - height, width, height);
            int Hits(Rectangle r) { r.Inflate(margin, margin); return points.Count(p => r.Contains(p)); }
            toolbar = Hits(top) <= Hits(bottom) ? top : bottom;
            int bw = (int)(92 * toolbarScale), bh = (int)(30 * toolbarScale), gap = (int)(10 * toolbarScale);
            int y = toolbar.Bottom - bh - (int)(12 * toolbarScale);
            saveButton = new Rectangle(toolbar.X + margin, y, bw, bh);
            resetButton = new Rectangle(saveButton.Right + gap, y, bw, bh);
            cancelButton = new Rectangle(resetButton.Right + gap, y, bw, bh);
            if (RadiusInput != null)
            {
                radiusLabel.Bounds = new Rectangle(toolbar.X + margin, toolbar.Y + (int)(72 * toolbarScale), (int)(80 * toolbarScale), (int)(28 * toolbarScale));
                RadiusInput.SetBounds(radiusLabel.Right + gap, radiusLabel.Top, (int)(88 * toolbarScale), radiusLabel.Height);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var preview = new Zone { Polygon = original.Polygon, Curve = original.Curve,
                CornerPercent = Draft.CornerPercent, Points = points };
            Color accent = Color.FromArgb(66, 255, 218);
            using (var fill = new SolidBrush(Color.FromArgb(Math.Max(45, (int)Appearance.Alpha(original.OpacityPercent)), Appearance.ToColor(original.ColorRgb))))
            using (var line = new Pen(accent, 3))
            {
                using (var path = ShapeGeometry.Path(preview, Point.Empty)) { g.FillPath(fill, path); g.DrawPath(line, path); }
            }
            MaskBorderRenderer.Draw(g, preview, Point.Empty, BorderSettings.FromZone(original));
            using (var background = new SolidBrush(Color.FromArgb(24, 32, 44))) g.FillRectangle(background, toolbar);
            int inset = (int)(18 * toolbarScale);
            TextRenderer.DrawText(g, "编辑区域 " + original.Number + " · " + (original.Polygon ? "拖动顶点调整形状" : "拖动角点调整" + ShapeGeometry.Label(original)), titleFont,
                new Rectangle(toolbar.X + inset, toolbar.Y + (int)(9 * toolbarScale), toolbar.Width - inset * 2, (int)(28 * toolbarScale)), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, error.Length > 0 ? error : "拖动内部移动 · 方向键微调 · Enter 保存 · Esc 取消", textFont,
                new Rectangle(toolbar.X + inset, toolbar.Y + (int)(40 * toolbarScale), toolbar.Width - inset * 2, (int)(24 * toolbarScale)),
                error.Length > 0 ? Color.FromArgb(255, 207, 105) : Color.FromArgb(193, 210, 228),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            PaintButton(g, saveButton, "保存", true); PaintButton(g, resetButton, "恢复原状", false); PaintButton(g, cancelButton, "取消", false);
            // Paint handles last so they remain reachable when dragged across the toolbar.
            for (int i = 0; i < points.Count; i++)
            {
                Point p = points[i];
                int radius = Native.PixelsAt(new Point(p.X + desktop.X, p.Y + desktop.Y), i == hovered ? 8 : 6, DeviceDpi);
                var handle = new Rectangle(p.X - radius, p.Y - radius, radius * 2, radius * 2);
                using (var border = new Pen(i == hovered ? accent : Color.FromArgb(35, 118, 164), 3))
                {
                    g.FillEllipse(Brushes.White, handle); g.DrawEllipse(border, handle);
                }
            }
        }
        private void PaintButton(Graphics g, Rectangle bounds, string label, bool primary)
        {
            using (var fill = new SolidBrush(primary ? Color.FromArgb(33, 125, 230) : Color.FromArgb(58, 70, 87))) g.FillRectangle(fill, bounds);
            TextRenderer.DrawText(g, label, textFont, bounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { titleFont.Dispose(); textFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
