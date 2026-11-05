using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;


namespace ScreenCurtain
{
    internal static class Native
    {
        internal const int WM_HOTKEY = 0x0312;
        internal const int GWL_EXSTYLE = -20;
        internal const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;
        internal const long WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        internal static readonly IntPtr Topmost = new IntPtr(-1);
        internal static readonly uint ShowMessage = RegisterWindowMessage("ScreenCurtain.ShowController.v1");

        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetLastActivePopup(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetLong64(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetLong64(IntPtr h, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetLong32(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetLong32(IntPtr h, int index, int value);
        [DllImport("user32.dll")] internal static extern int GetWindowRgn(IntPtr h, IntPtr region);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] internal static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }

        internal static int PixelsAt(Point screenPoint, int logicalPixels, int fallbackDpi)
        {
            uint dpi = (uint)Math.Max(96, fallbackDpi);
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(screenPoint, 2), 0, out x, out y) == 0 && x > 0) dpi = x;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return Math.Max(1, (int)Math.Round(logicalPixels * dpi / 96.0));
        }

        internal static long GetStyle(IntPtr h) { return IntPtr.Size == 8 ? GetLong64(h, GWL_EXSTYLE).ToInt64() : GetLong32(h, GWL_EXSTYLE); }
        internal static void SetStyle(IntPtr h, long value)
        {
            if (IntPtr.Size == 8) SetLong64(h, GWL_EXSTYLE, new IntPtr(value));
            else SetLong32(h, GWL_EXSTYLE, (int)value);
        }
        internal static void Raise(IntPtr h) { SetWindowPos(h, Topmost, 0, 0, 0, 0, 0x0013); }
        internal static void Place(IntPtr h, Rectangle r) { SetWindowPos(h, Topmost, r.X, r.Y, r.Width, r.Height, 0x0010); }
        internal static void EnableDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }
    }

    public class Zone
    {
        public int Number { get; set; }
        public string Name { get; set; } = "";
        [XmlIgnore] public string DisplayName { get { return string.IsNullOrWhiteSpace(Name) ? "区域 " + Number : Name; } }
        public bool Polygon { get; set; }
        public CurveShape Curve { get; set; }
        public int CornerPercent { get; set; } = 15;
        public bool Enabled { get; set; }
        public int ColorRgb { get; set; }
        public int OpacityPercent { get; set; }
        public bool BorderEnabled { get; set; }
        public int BorderWidth { get; set; }
        public int BorderColorRgb { get; set; }
        public int BorderStyle { get; set; }
        public List<Point> Points { get; set; }
        public Zone() { Enabled = true; OpacityPercent = 100; BorderWidth = 2; BorderColorRgb = BorderSettings.DefaultColor; Points = new List<Point>(); }
        [XmlIgnore] public Rectangle Bounds { get { return Geometry.Bounds(Points); } }
    }

    internal static class Appearance
    {
        internal static int ValidRgb(int rgb) { return rgb >= 0 && rgb <= 0xffffff ? rgb : 0; }
        internal static int ValidOpacity(int percent) { return Math.Max(0, Math.Min(100, percent)); }
        internal static Color ToColor(int rgb) { return Color.FromArgb(unchecked((int)0xff000000) | ValidRgb(rgb)); }
        internal static byte Alpha(int percent) { return (byte)((ValidOpacity(percent) * 255 + 50) / 100); }
    }

    public class Settings
    {
        public bool ClickThrough { get; set; }
        public DefaultStyleSettings DefaultStyle { get; set; } = new DefaultStyleSettings();
        public List<Zone> Zones { get; set; }
        public Settings() { Zones = new List<Zone>(); }
    }

    internal static class Geometry
    {
        internal static Rectangle Bounds(IList<Point> p)
        {
            if (p == null || p.Count == 0) return Rectangle.Empty;
            int l = p.Min(q => q.X), t = p.Min(q => q.Y), r = p.Max(q => q.X), b = p.Max(q => q.Y);
            return new Rectangle(l, t, r - l + 1, b - t + 1);
        }
        internal static Zone RectangleZone(Point a, Point b, Point origin)
        {
            int l = Math.Min(a.X, b.X) + origin.X, t = Math.Min(a.Y, b.Y) + origin.Y;
            int r = Math.Max(a.X, b.X) + origin.X, d = Math.Max(a.Y, b.Y) + origin.Y;
            return new Zone { Points = new List<Point> { new Point(l, t), new Point(r, t), new Point(r, d), new Point(l, d) } };
        }
        internal static bool Valid(Zone z)
        {
            if (z == null || z.Points == null || z.Points.Count < 3 || z.Points.Count > 256) return false;
            if (!z.Polygon && z.Points.Count != 4) return false;
            if (!Enum.IsDefined(typeof(CurveShape), z.Curve) || (z.Polygon && z.Curve != CurveShape.None)) return false;
            if (z.Points.Any(p => Math.Abs((long)p.X) > 100000 || Math.Abs((long)p.Y) > 100000)) return false;
            Rectangle r = z.Bounds;
            if (r.Width < 3 || r.Height < 3 || r.Width > 50000 || r.Height > 50000) return false;
            using (GraphicsPath path = ShapeGeometry.Path(z, Point.Empty))
            {
                using (Region region = new Region(path))
                using (Matrix identity = new Matrix())
                    return region.GetRegionScans(identity).Any(scan => scan.Width >= 1 && scan.Height >= 1);
            }
        }
    }

    internal sealed class MaskForm : Form
    {
        private readonly Zone zone;
        private readonly ContextMenuStrip popup;
        private bool clickThrough, requestedVisible;
        private int colorRgb, opacityPercent;
        private BorderSettings border;
        private byte? appliedAlpha;
        internal BorderSettings CurrentBorder { get { return border; } }
        internal int CurrentColorRgb { get { return colorRgb; } }
        internal int CurrentOpacityPercent { get { return opacityPercent; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED);
                if (clickThrough) p.ExStyle |= (int)Native.WS_EX_TRANSPARENT;
                return p;
            }
        }
        internal MaskForm(Zone value, bool pass, Action hideZone, Action showPanel, Action exit)
        {
            zone = value;
            clickThrough = pass;
            colorRgb = Appearance.ValidRgb(zone.ColorRgb);
            opacityPercent = Appearance.ValidOpacity(zone.OpacityPercent);
            border = BorderSettings.FromZone(zone);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Appearance.ToColor(colorRgb);
            Bounds = zone.Bounds;
            popup = new ContextMenuStrip();
            popup.Items.Add("隐藏这个区域", null, delegate { hideZone(); });
            popup.Items.Add("打开控制面板", null, delegate { showPanel(); });
            popup.Items.Add(new ToolStripSeparator());
            popup.Items.Add("退出屏幕遮罩", null, delegate { exit(); });
            ContextMenuStrip = popup;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            appliedAlpha = null;
            ApplyGeometry();
        }
        internal void ApplyGeometry()
        {
            Rectangle r = zone.Bounds;
            Bounds = r;
            if (!IsHandleCreated) return;
            Region previous = Region;
            using (GraphicsPath path = ShapeGeometry.Path(zone, r.Location)) Region = new Region(path);
            previous?.Dispose();
            Native.Place(Handle, r);
            ApplyWindowStyles();
        }
        internal void SetClickThrough(bool value)
        {
            clickThrough = value;
            ApplyWindowStyles();
        }
        internal void ApplyAppearance(int rgb, int percent)
        {
            colorRgb = Appearance.ValidRgb(rgb);
            opacityPercent = Appearance.ValidOpacity(percent);
            BackColor = Appearance.ToColor(colorRgb);
            ApplyWindowStyles();
            UpdateVisibility();
        }
        internal void ApplyBorder(BorderSettings value)
        {
            if (border.Equals(value)) return;
            border = value; Invalidate(); Update();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            MaskBorderRenderer.Draw(e.Graphics, zone, zone.Bounds.Location, border);
        }
        internal void SetMaskVisible(bool value)
        {
            requestedVisible = value;
            UpdateVisibility();
        }
        private void UpdateVisibility()
        {
            bool show = requestedVisible && opacityPercent > 0;
            if (show && !Visible) { Show(); ApplyWindowStyles(); }
            else if (!show && Visible) Hide();
        }
        private void ApplyWindowStyles()
        {
            if (!IsHandleCreated) return;
            long previous = Native.GetStyle(Handle);
            long style = previous | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            style = clickThrough ? style | Native.WS_EX_TRANSPARENT : style & ~Native.WS_EX_TRANSPARENT;
            if (style != previous) Native.SetStyle(Handle, style);
            byte alpha = Appearance.Alpha(opacityPercent);
            if (!appliedAlpha.HasValue || appliedAlpha.Value != alpha || (previous & Native.WS_EX_LAYERED) == 0)
            {
                Native.SetLayeredWindowAttributes(Handle, 0, alpha, 2); appliedAlpha = alpha;
            }
        }
        protected override void WndProc(ref Message m)
        {
            // Overlay coordinates are physical desktop pixels, including across monitors.
            if (m.Msg == 0x02E0) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) popup.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class SelectionForm : Form
    {
        private readonly bool polygon;
        private readonly SelectionShape shape;
        private readonly Rectangle desktop;
        private readonly List<Point> points = new List<Point>();
        private readonly Font hintFont = new Font("Microsoft YaHei UI", 12f);
        private readonly Font smallFont = new Font("Microsoft YaHei UI", 10f);
        private Point? start;
        private Point cursor;
        private bool polygonCanClose;
        private Zone preciseTemplate;
        private string error = "";
        internal Zone Result { get; private set; }
        internal bool SnappedToStart { get; private set; }
        internal int StartSnapRadius { get; private set; } = 12;
        internal Point PreviewCursor { get { return SnappedToStart ? points[0] : cursor; } }

        internal SelectionForm(bool isPolygon, Rectangle screenBounds) : this(isPolygon ? SelectionShape.Polygon : SelectionShape.Rectangle, screenBounds) { }
        internal SelectionForm(SelectionShape selectedShape, Rectangle screenBounds)
        {
            shape = selectedShape; polygon = shape == SelectionShape.Polygon;
            desktop = screenBounds;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = desktop;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.48;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= (int)Native.WS_EX_TOOLWINDOW; return p; }
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.Place(Handle, desktop);
            Activate();
            Focus();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { Cancel(); return; }
            if (e.Button != MouseButtons.Left) return;
            error = "";
            cursor = ClampCursor(e.Location);
            if (preciseTemplate != null)
            {
                var placed = PreciseShape.Place(preciseTemplate, PreciseLocation());
                placed.Points = placed.Points.Select(p => new Point(p.X + desktop.X, p.Y + desktop.Y)).ToList();
                Finish(placed); return;
            }
            if (polygon)
            {
                UpdateSnap();
                // Closure is confirmed by a click, never by hovering or by adding a duplicate vertex.
                if (SnappedToStart) { FinishPolygon(); return; }
                if (e.Clicks > 1) { FinishPolygon(); return; }
                if (points.Count < 256 && (points.Count == 0 || points[points.Count - 1] != cursor)) points.Add(cursor);
                UpdateSnap(true);
            }
            else { start = cursor; Capture = true; }
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            cursor = ClampCursor(e.Location);
            UpdateSnap();
            Invalidate();
        }
        private Point ClampCursor(Point point)
        {
            return new Point(Math.Max(0, Math.Min(ClientSize.Width - 1, point.X)), Math.Max(0, Math.Min(ClientSize.Height - 1, point.Y)));
        }
        private Point PreciseLocation()
        {
            return new Point(Math.Clamp(cursor.X, 0, Math.Max(0, ClientSize.Width - preciseTemplate.Bounds.Width)),
                Math.Clamp(cursor.Y, 0, Math.Max(0, ClientSize.Height - preciseTemplate.Bounds.Height)));
        }
        private void UpdateSnap(bool verticesChanged = false)
        {
            if (!polygon) return;
            if (verticesChanged)
            {
                // Validate only when vertices change; mouse moves only need a distance check.
                polygonCanClose = points.Count >= 3 && Geometry.Valid(new Zone { Polygon = true, Points = points });
                if (points.Count > 0)
                    StartSnapRadius = Native.PixelsAt(new Point(points[0].X + desktop.X, points[0].Y + desktop.Y), 12, DeviceDpi);
            }
            long dx = points.Count > 0 ? (long)cursor.X - points[0].X : 0;
            long dy = points.Count > 0 ? (long)cursor.Y - points[0].Y : 0;
            SnappedToStart = polygonCanClose && dx * dx + dy * dy <= (long)StartSnapRadius * StartSnapRadius;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (polygon || !start.HasValue || e.Button != MouseButtons.Left) return;
            Point end = new Point(Math.Max(0, Math.Min(ClientSize.Width - 1, e.X)), Math.Max(0, Math.Min(ClientSize.Height - 1, e.Y)));
            Zone z = ShapeGeometry.FromDrag(shape, start.Value, end, desktop.Location, (ModifierKeys & Keys.Shift) != 0);
            Capture = false;
            start = null;
            if (Geometry.Valid(z)) Finish(z);
            else { error = "区域太小，请重新拖拽。"; Invalidate(); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Cancel(); return true; }
            if (!polygon && keyData == Keys.S && !start.HasValue)
            {
                var selected = PreciseShape.ShowDialog(this, shape, ClientSize);
                if (selected != null) { preciseTemplate = selected; error = ""; }
                Activate(); Focus(); Invalidate(); return true;
            }
            if (polygon && keyData == Keys.Enter) { FinishPolygon(); return true; }
            if (polygon && keyData == Keys.Back)
            {
                if (points.Count > 0) points.RemoveAt(points.Count - 1);
                error = "";
                UpdateSnap(true);
                Invalidate();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.ShiftKey) Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.ShiftKey) Invalidate(); }
        private void FinishPolygon()
        {
            Zone z = new Zone { Polygon = true, Points = points.Select(p => new Point(p.X + desktop.X, p.Y + desktop.Y)).ToList() };
            if (Geometry.Valid(z)) Finish(z);
            else { error = "请选取至少三个不共线的点，再按 Enter。"; Invalidate(); }
        }
        private void Finish(Zone z) { Result = z; Capture = false; DialogResult = DialogResult.OK; Close(); }
        private void Cancel() { Result = null; Capture = false; DialogResult = DialogResult.Cancel; Close(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen line = new Pen(Color.FromArgb(66, 255, 218), 3))
            using (Brush fill = new SolidBrush(Color.FromArgb(70, 66, 255, 218)))
            {
                if (polygon && points.Count > 0)
                {
                    Point preview = PreviewCursor;
                    if (points.Count > 2) g.FillPolygon(fill, points.ToArray(), FillMode.Alternate);
                    if (points.Count > 1) g.DrawLines(line, points.ToArray());
                    g.DrawLine(line, points[points.Count - 1], preview);
                    using (Pen dashed = new Pen(Color.White, 2))
                    {
                        dashed.DashStyle = DashStyle.Dash;
                        if (points.Count > 1 && !SnappedToStart) g.DrawLine(dashed, preview, points[0]);
                    }
                    foreach (Point p in points) g.FillEllipse(Brushes.White, p.X - 4, p.Y - 4, 8, 8);
                    Point first = points[0];
                    int radius = polygonCanClose ? StartSnapRadius : 7;
                    Rectangle target = new Rectangle(first.X - radius, first.Y - radius, radius * 2, radius * 2);
                    using (Brush halo = new SolidBrush(Color.FromArgb(SnappedToStart ? 150 : 45, 66, 255, 218)))
                    using (Pen ring = new Pen(SnappedToStart ? Color.White : line.Color, SnappedToStart ? 3 : 2))
                    {
                        g.FillEllipse(halo, target);
                        g.DrawEllipse(ring, target);
                        g.FillEllipse(Brushes.White, first.X - 4, first.Y - 4, 8, 8);
                    }
                }
                if (!polygon && start.HasValue)
                {
                    var preview = ShapeGeometry.FromDrag(shape, start.Value, cursor, Point.Empty, (ModifierKeys & Keys.Shift) != 0);
                    Rectangle r = preview.Bounds;
                    using (var path = ShapeGeometry.Path(preview, Point.Empty)) { g.FillPath(fill, path); g.DrawPath(line, path); }
                    TextRenderer.DrawText(g, r.Width + " × " + r.Height + " px", smallFont,
                        new Point(r.X + 8, Math.Max(5, r.Y - 28)), Color.White, Color.Black);
                }
                if (preciseTemplate != null)
                {
                    var preview = PreciseShape.Place(preciseTemplate, PreciseLocation());
                    using (var path = ShapeGeometry.Path(preview, Point.Empty)) { g.FillPath(fill, path); g.DrawPath(line, path); }
                }
            }
            Rectangle monitor = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            int width = Math.Min(720, monitor.Width - 40);
            Rectangle hint = new Rectangle(monitor.X - desktop.X + (monitor.Width - width) / 2,
                monitor.Y - desktop.Y + 22, width, 78);
            using (Brush b = new SolidBrush(Color.FromArgb(20, 26, 33))) g.FillRectangle(b, hint);
            string title = polygon ? (SnappedToStart ? "已吸附到起点：单击闭合选区" : "多边形：单击添加顶点，靠近起点后单击闭合") : "矩形：按住鼠标左键，拖拽选择区域";
            if (shape == SelectionShape.Ellipse) title = "椭圆：拖拽选择区域，按住 Shift 画正圆";
            if (shape == SelectionShape.Triangle) title = "三角形：拖拽生成，完成后可编辑三个顶点";
            if (shape == SelectionShape.RoundedRectangle) title = "圆角矩形：拖拽选择区域，完成后可编辑圆角大小";
            TextRenderer.DrawText(g, title, hintFont, new Rectangle(hint.X + 18, hint.Y + 12, hint.Width - 36, 28), Color.White);
            string detail = error.Length > 0 ? error : (polygon ? "Enter / 双击完成 · Backspace 撤销 · Esc / 右键取消" :
                preciseTemplate != null ? "移动鼠标预览 · 单击放置 · S 修改尺寸 · Esc 取消" : "拖拽框选 · S 输入精确尺寸 · Esc / 右键取消");
            TextRenderer.DrawText(g, detail, smallFont, new Rectangle(hint.X + 18, hint.Y + 44, hint.Width - 36, 24),
                error.Length > 0 ? Color.FromArgb(255, 207, 105) : Color.FromArgb(186, 213, 210));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { hintFont.Dispose(); smallFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

}
