using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenCurtain
{
    internal sealed class SpotlightForm : Form
    {
        private readonly Rectangle screen;
        private Zone opening;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT); return p; }
        }
        internal SpotlightForm(Rectangle bounds, Zone hole)
        {
            screen = bounds; opening = hole; AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; BackColor = Color.Black;
            Opacity = 0.70; Bounds = screen;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateOpening(opening);
            Native.Place(Handle, screen);
            Native.SetStyle(Handle, Native.GetStyle(Handle) | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE);
        }
        internal void UpdateOpening(Zone value)
        {
            opening = value;
            if (!IsHandleCreated) return;
            using (var path = ShapeGeometry.Path(opening, screen.Location))
            {
                var area = new Region(new Rectangle(Point.Empty, screen.Size));
                area.Exclude(path); var old = Region; Region = area; old?.Dispose();
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
    }
}
