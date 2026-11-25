using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Voltide.UI
{
    /// <summary>
    /// The tray menu, rebuilt as a compact settings card. It now owns everything that
    /// used to live in the desktop panel: refresh cadence, appearance (with a system
    /// option) plus the two actions, with the actions sharing the bottom line so the
    /// popup stays smaller than the old two-row menu. Device levels deliberately stay
    /// out — they are on the tray icon and in the hover card.
    /// </summary>
    internal class MenuPanel : Control
    {
        private const float W = 216f;
        private const float PadX = 12f;
        private const float HeaderH = 26f;
        private const float RowH = 30f;
        private const float BottomRowH = 30f;
        private const float BottomPad = 4f;
        private const float Corner = 10f;

        private const float ChipH = 22f;
        private const float ChipGap = 5f;
        private const float ChipPadX = 7f;

        private const float TrackW = 32f;
        private const float TrackH = 16f;
        private const float KnobR = 6f;

        // Bottom line: action label left, a hairline, then quit. The split keeps both
        // targets at least ~80 px wide even at 100 % DPI.
        private const float SplitX = 122f;

        private static readonly int[] RefreshOptions = { 15, 30, 90 };
        private static readonly ThemeMode[] ThemeOptions =
            { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System };

        private readonly float _scale;

        private readonly Font _markFont;
        private readonly Font _labelFont;
        private readonly Font _chipFont;
        private readonly Font _chipLatin;

        private readonly float[] _refreshChipW = new float[3];
        private readonly float[] _themeChipW = new float[3];

        private int _hot = -1;
        private int _hotChip = -1;

        private int _refreshSeconds = 90;
        private ThemeMode _theme = ThemeMode.System;
        private bool _autostartOn;
        private bool _autostartEnabled = true;

        public event EventHandler QuitRequested;
        public event EventHandler AutostartToggled;
        public event EventHandler RefreshSecondsChanged;
        public event EventHandler ThemeChanged;

        public int RefreshSeconds
        {
            get { return _refreshSeconds; }
            set { _refreshSeconds = value; Invalidate(); }
        }

        public ThemeMode Theme
        {
            get { return _theme; }
            set { _theme = value; Invalidate(); }
        }

        /// <summary>Current switch position, read back by the host when it saves.</summary>
        public bool AutostartOn
        {
            get { return _autostartOn; }
            set { _autostartOn = value; Invalidate(); }
        }

        /// <summary>False when the process is not a real Jolt.exe (in-memory dev build),
        /// where writing a Run entry would point at powershell.exe instead.</summary>
        public bool AutostartEnabled
        {
            get { return _autostartEnabled; }
            set { _autostartEnabled = value; Invalidate(); }
        }

        public MenuPanel(float scale)
        {
            _scale = scale > 0f ? scale : 1f;
            _markFont = FontManager.Get(FontKind.MontserratSemiBold, 10f * _scale);
            _labelFont = FontManager.Get(FontKind.Chinese, 12.5f * _scale);
            _chipFont = FontManager.Get(FontKind.Chinese, 11f * _scale);
            _chipLatin = FontManager.Get(FontKind.MontserratMedium, 11f * _scale);

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);

            Margin = Padding.Empty;
            Size = new Size((int)Math.Round(W * _scale),
                            (int)Math.Round(TotalHeight() * _scale));
            BackColor = FluentTheme.P.BgTop;

            MeasureChips();
        }

        private void MeasureChips()
        {
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            using (StringFormat sf = Typo())
            {
                for (int i = 0; i < RefreshOptions.Length; i++)
                {
                    _refreshChipW[i] = g.MeasureString(RefreshOptions[i] + "s", _chipLatin, 400, sf).Width
                                       + ChipPadX * 2f * _scale;
                }

                for (int i = 0; i < ThemeOptions.Length; i++)
                {
                    _themeChipW[i] = g.MeasureString(ThemeManager.NameOf(ThemeOptions[i]),
                                                     _chipFont, 400, sf).Width
                                     + ChipPadX * 2f * _scale;
                }
            }
        }

        private static StringFormat Typo()
        {
            StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
            sf.FormatFlags |= StringFormatFlags.NoWrap;
            return sf;
        }

        private static float TotalHeight()
        {
            return HeaderH + RowH * 2f + BottomRowH + BottomPad;
        }

        private float RowTop(int index)
        {
            if (index < 2) return HeaderH + index * RowH;
            return HeaderH + RowH * 2f;
        }

        private float RowHeight(int index)
        {
            return (index < 2) ? RowH : BottomRowH;
        }

        private float RowTopPx(int index) { return RowTop(index) * _scale; }

        public int ScaledCorner { get { return (int)Math.Round(Corner * _scale); } }

        // --------------------------------------------------------------- input

        private int RowAt(int y)
        {
            for (int i = 0; i < 3; i++)
            {
                float top = RowTopPx(i);
                if (y >= top && y < top + RowHeight(i) * _scale) return i;
            }
            return -1;
        }

        private int ChipAt(int x, int row)
        {
            if (row != 0 && row != 1) return -1;

            float[] widths = row == 0 ? _refreshChipW : _themeChipW;
            float total = ChipGap * _scale * (widths.Length - 1);
            foreach (float w in widths) total += w;

            float left = Width - PadX * _scale - total;
            if (x < left) return -1;

            float cursor = left;
            for (int i = 0; i < widths.Length; i++)
            {
                if (x >= cursor && x < cursor + widths[i]) return i;
                cursor += widths[i] + ChipGap * _scale;
            }

            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int row = RowAt(e.Y);
            int chip = ChipAt(e.X, row);

            if (row != _hot || chip != _hotChip)
            {
                _hot = row;
                _hotChip = chip;
                Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hot = -1;
            _hotChip = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { base.OnMouseClick(e); return; }

            int row = RowAt(e.Y);
            int chip = ChipAt(e.X, row);

            if (row == 0 && chip >= 0)
            {
                int seconds = RefreshOptions[chip];
                if (seconds != _refreshSeconds)
                {
                    _refreshSeconds = seconds;
                    Invalidate();
                    if (RefreshSecondsChanged != null) RefreshSecondsChanged(this, EventArgs.Empty);
                }
            }
            else if (row == 1 && chip >= 0)
            {
                ThemeMode mode = ThemeOptions[chip];
                if (mode != _theme)
                {
                    _theme = mode;
                    ThemeManager.Set(mode);
                    Invalidate();
                    if (ThemeChanged != null) ThemeChanged(this, EventArgs.Empty);
                }
            }
            else if (row == 2)
            {
                bool inAutostart = e.X < SplitX * _scale;

                if (inAutostart)
                {
                    if (_autostartEnabled)
                    {
                        _autostartOn = !_autostartOn;
                        Invalidate();
                        if (AutostartToggled != null) AutostartToggled(this, EventArgs.Empty);
                    }
                }
                else if (QuitRequested != null)
                {
                    QuitRequested(this, EventArgs.Empty);
                }
            }

            base.OnMouseClick(e);
        }

        // -------------------------------------------------------------- paint

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float s = _scale;
            FluentTheme.Palette p = FluentTheme.P;

            using (GraphicsPath body = FluentTheme.Round(new RectangleF(0f, 0f, Width, Height), Corner * s))
            using (LinearGradientBrush bg = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Height), p.BgTop, p.BgBottom, LinearGradientMode.Vertical))
            {
                g.FillPath(bg, body);
            }

            using (Pen pen = new Pen(p.Border, Math.Max(1f, s)))
            using (GraphicsPath outline = FluentTheme.Round(
                new RectangleF(0f, 0f, Width - 1f, Height - 1f), Corner * s))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, outline);
            }

            DrawHeader(g, s, p);
            DrawTextRow(g, s, p, 0, "刷新间隔");
            DrawTextRow(g, s, p, 1, "外观");
            DrawBottomRow(g, s, p);
            DrawChips(g, s, p, 0, _refreshChipW, false);
            DrawChips(g, s, p, 1, _themeChipW, true);
        }

        private void DrawHeader(Graphics g, float s, FluentTheme.Palette p)
        {
            using (SolidBrush brush = new SolidBrush(p.Sub))
            using (StringFormat sf = Typo())
            {
                float baseline = 18f * s;
                g.DrawString("JOLT", _markFont, brush, PadX * s,
                             baseline - FontManager.Ascent(_markFont), sf);
            }

            using (SolidBrush line = new SolidBrush(p.Hair))
            {
                g.FillRectangle(line, PadX * s, HeaderH * s,
                                Width - PadX * 2f * s, Math.Max(1f, s));
            }
        }

        /// <summary>Label row; also paints the hover wash for rows that can be clicked.</summary>
        private void DrawTextRow(Graphics g, float s, FluentTheme.Palette p, int index, string label)
        {
            float top = RowTopPx(index);
            float h = RowHeight(index) * s;

            if (_hot == index)
            {
                using (SolidBrush wash = new SolidBrush(p.Wash))
                {
                    g.FillRectangle(wash, 5f * s, top + 2f * s, Width - 10f * s, h - 4f * s);
                }
            }

            using (SolidBrush brush = new SolidBrush(p.Ink))
            using (StringFormat sf = Typo())
            {
                float y = top + (h - _labelFont.GetHeight(g)) / 2f;
                g.DrawString(label, _labelFont, brush, PadX * s, y, sf);
            }
        }

        private void DrawBottomRow(Graphics g, float s, FluentTheme.Palette p)
        {
            float top = RowTopPx(2);
            float h = BottomRowH * s;

            if (_hot == 2)
            {
                using (SolidBrush wash = new SolidBrush(p.Wash))
                {
                    g.FillRectangle(wash, 5f * s, top + 2f * s, Width - 10f * s, h - 4f * s);
                }
            }

            float cx = SplitX * s;

            using (SolidBrush line = new SolidBrush(p.Hair))
            {
                g.FillRectangle(line, cx, top + 6f * s, Math.Max(1f, s), h - 12f * s);
            }

            using (SolidBrush brush = new SolidBrush(_autostartEnabled ? p.Ink : p.Sub))
            using (StringFormat sf = Typo())
            {
                float y = top + (h - _labelFont.GetHeight(g)) / 2f;
                g.DrawString("开机启动", _labelFont, brush, PadX * s, y, sf);
            }

            float switchX = cx - 10f * s - TrackW * s;
            DrawSwitch(g, s, p, switchX, top + (h - TrackH * s) / 2f);

            // Draw the quit label with the plain point overload and centre it by hand.
            // The rectangle overload with a centred StringFormat silently produced no
            // text here, while every other label painted fine.
            using (StringFormat sf = Typo())
            {
                const string quit = "退出应用";
                float rightLeft = cx + 4f * s;
                float rightW = Width - PadX * s - rightLeft;
                float textW = g.MeasureString(quit, _labelFont, 400, sf).Width;
                float tx = rightLeft + (rightW - textW) / 2f;
                if (tx < rightLeft) tx = rightLeft;
                float y = top + (h - _labelFont.GetHeight(g)) / 2f;

                using (SolidBrush brush = new SolidBrush(p.Ink))
                {
                    g.DrawString(quit, _labelFont, brush, tx, y, sf);
                }
            }
        }

        private void DrawChips(Graphics g, float s, FluentTheme.Palette p, int row,
                               float[] widths, bool themeRow)
        {
            int selected = themeRow ? ThemeIndex() : RefreshIndex();

            float total = ChipGap * s * (widths.Length - 1);
            foreach (float w in widths) total += w;

            float x = Width - PadX * s - total;
            float y = RowTopPx(row) + (RowH * s - ChipH * s) / 2f;
            float h = ChipH * s;

            Font font = themeRow ? _chipFont : _chipLatin;

            using (StringFormat sf = Typo())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                for (int i = 0; i < widths.Length; i++)
                {
                    RectangleF rect = new RectangleF(x, y, widths[i], h);
                    bool on = i == selected;

                    using (GraphicsPath chip = FluentTheme.Round(rect, 5f * s))
                    {
                        if (on)
                        {
                            using (SolidBrush fill = new SolidBrush(p.Ink)) g.FillPath(fill, chip);
                        }
                        else
                        {
                            using (Pen pen = new Pen(p.Border, Math.Max(1f, s)))
                            {
                                pen.Alignment = PenAlignment.Inset;
                                g.DrawPath(pen, chip);
                            }
                        }
                    }

                    string text = themeRow
                        ? ThemeManager.NameOf(ThemeOptions[i])
                        : RefreshOptions[i] + "s";

                    using (SolidBrush brush = new SolidBrush(on ? p.BgTop : p.Ink))
                    {
                        g.DrawString(text, font, brush, rect, sf);
                    }

                    x += widths[i] + ChipGap * s;
                }
            }
        }

        private int RefreshIndex()
        {
            for (int i = 0; i < RefreshOptions.Length; i++)
            {
                if (RefreshOptions[i] == _refreshSeconds) return i;
            }
            return 2;
        }

        private int ThemeIndex()
        {
            for (int i = 0; i < ThemeOptions.Length; i++)
            {
                if (ThemeOptions[i] == _theme) return i;
            }
            return 2;
        }

        private void DrawSwitch(Graphics g, float s, FluentTheme.Palette p, float x, float y)
        {
            float w = TrackW * s;
            float h = TrackH * s;
            float radius = h / 2f;

            Color track = !_autostartEnabled
                ? Color.FromArgb(40, p.Sub)
                : (_autostartOn ? p.AccentGreen : Color.FromArgb(90, p.Sub));

            using (GraphicsPath path = FluentTheme.Round(new RectangleF(x, y, w, h), radius))
            using (SolidBrush brush = new SolidBrush(track))
            {
                g.FillPath(brush, path);
            }

            float knobR = KnobR * s;
            float leftX = x + knobR + 2f * s;
            float rightX = x + w - knobR - 2f * s;
            float cx = _autostartOn ? rightX : leftX;
            float cy = y + h / 2f;

            using (SolidBrush knob = new SolidBrush(_autostartEnabled ? Color.White : Color.FromArgb(180, 180, 180)))
            {
                g.FillEllipse(knob, cx - knobR, cy - knobR, knobR * 2f, knobR * 2f);
            }
        }
    }

    /// <summary>
    /// Borderless popup that hosts the compact settings card.
    ///
    /// A ContextMenuStrip was the obvious host but it lays its items out with its own
    /// padding and, when shown manually, did not always close on an outside click.
    /// Owning the window fixes both: exact client size, no system chrome, and
    /// Deactivate does the dismissing.
    /// </summary>
    internal class TrayMenuForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public MenuPanel Panel { get; set; }

        private bool _armed;
        private System.Windows.Forms.Timer _armTimer;

        public TrayMenuForm()
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

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW: keep it out of Alt+Tab
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Corners come from DWM, not SetWindowRgn. A region is a 1-bit mask, so the
            // arc is stair-stepped at every DPI; DWM rounds during composition and the
            // edge is antialiased.
            try
            {
                int preference = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */,
                                      ref preference, sizeof(int));
            }
            catch
            {
            }

            // Ignore the Deactivate that fires from the click that opened us.
            _armed = false;

            if (_armTimer == null)
            {
                _armTimer = new System.Windows.Forms.Timer();
                _armTimer.Interval = 150;
                _armTimer.Tick += delegate
                {
                    _armTimer.Stop();
                    _armed = true;
                };
            }

            _armTimer.Stop();
            _armTimer.Start();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_armed) Close();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _armTimer != null)
            {
                _armTimer.Stop();
                _armTimer.Dispose();
                _armTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}
