using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Voltide.UI
{
    /// <summary>
    /// The hover card: a borderless, non-activating light window shown above the
    /// taskbar while the pointer rests on the tray icon. It never takes focus, so
    /// clicking through it still reaches the shell.
    /// </summary>
    internal class FluentTipForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private List<TipRow> _rows = new List<TipRow>();
        private float _scale = 1f;

        public FluentTipForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            // Every dimension in here is computed from our own DPI-derived scale, so
            // let WinForms stay out of it; automatic scaling would apply it twice.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = FluentTheme.P.BgTop;
            Padding = Padding.Empty;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;      // CS_DROPSHADOW
                cp.ExStyle |= 0x00000080;      // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000;      // WS_EX_NOACTIVATE
                return cp;
            }
        }

        public void SetRows(List<TipRow> rows, float scale)
        {
            _rows = rows != null ? rows : new List<TipRow>();
            _scale = scale > 0f ? scale : 1f;

            Size size = FluentTheme.TipSize(_rows, _scale);
            if (ClientSize != size) ClientSize = size;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            try
            {
                int pref = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
            }
            catch
            {
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            FluentTheme.DrawTip(e.Graphics, ClientSize.Width, ClientSize.Height, _scale, _rows);
        }
    }
}
