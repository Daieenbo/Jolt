using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Voltide.Core;

namespace Voltide.UI
{
    /// <summary>
    /// The tray process: a NotifyIcon, the compact settings menu and the hover card.
    /// There is no desktop window any more — the tray icon is the whole UI surface,
    /// so a left click and a right click both open the same menu.
    /// </summary>
    public class TrayApp : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly DeviceManager _manager;
        private readonly NotifyIcon _tray;
        private readonly System.Windows.Forms.Timer _timer;

        // The only window in the process. It is never shown; it exists so worker
        // thread results can be marshalled back onto the UI thread.
        private readonly Control _marshal;

        private bool _refreshing;
        private bool _refreshQueued;

        // A second click replaces the popup instead of stacking another copy.
        private TrayMenuForm _menuForm;

        // Tray face: one or two battery glyphs that mirror the current levels.
        private Icon _trayIcon;
        private List<DeviceSnapshot> _snapshot = new List<DeviceSnapshot>();

        // Last real percentage per device, so a sleeping mouse keeps its tray slot
        // and its last known level instead of reading as an empty battery.
        private readonly Dictionary<string, int> _lastPercent = new Dictionary<string, int>();

        private int _refreshSeconds;

        // Cached from the taskbar window: the tray slot and both popups scale from it.
        private int _dpi = 96;
        private float _dpiScale = 1f;

        // Hover card. The delay keeps it from flashing while the pointer merely
        // travels across the taskbar on its way somewhere else.
        private FluentTipForm _tip;
        private readonly System.Windows.Forms.Timer _hoverDelay;
        private readonly System.Windows.Forms.Timer _tipPoll;
        private Point _tipAnchor;

        public TrayApp()
        {
            _manager = new DeviceManager();

            _marshal = new Control();
            _marshal.CreateControl();

            RefreshDpi();

            _tray = new NotifyIcon();
            _tray.Icon = IconFactory.ToIcon(Dpi.SmallIconSize(_dpi));
            // No shell tooltip: the level list is drawn by FluentTipForm instead, so
            // there is nothing useful for the single-line system tooltip to say.
            _tray.Text = "";
            _tray.Visible = true;

            _hoverDelay = new System.Windows.Forms.Timer();
            _hoverDelay.Interval = 320;
            _hoverDelay.Tick += delegate { _hoverDelay.Stop(); ShowTip(); };

            _tipPoll = new System.Windows.Forms.Timer();
            _tipPoll.Interval = 150;
            _tipPoll.Tick += PollTip;

            _tray.MouseMove += delegate
            {
                if (_tipPoll.Enabled) return;
                _tipAnchor = Cursor.Position;
                if (!_hoverDelay.Enabled) _hoverDelay.Start();
            };

            _tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                HideTip();
                ShowMenu();
            };

            ThemeManager.Changed += OnThemeChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _refreshSeconds = LoadRefreshSeconds();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = _refreshSeconds * 1000;
            _timer.Tick += delegate
            {
                // Keep the footprint small: the receiver shares its input pipe with
                // G HUB, and every request we send is broadcast to it too. It also
                // stops answering for a while if it is polled too hard, which showed
                // up as cards flapping between "97%" and "asleep".
                RefreshNow();
            };
            _timer.Start();

            RefreshNow();
        }

        /// <summary>
        /// The cadence the user picked in the menu. No hidden-vs-visible split any
        /// more: with no panel there is nothing to slow down for.
        /// </summary>
        private void ApplyInterval()
        {
            int ms = _refreshSeconds * 1000;
            if (_timer.Interval != ms) _timer.Interval = ms;
        }

        // ------------------------------------------------------- refresh rate

        private const string SettingsKey = @"Software\Jolt";

        private static int LoadRefreshSeconds()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (key == null) return 90;
                    object raw = key.GetValue("RefreshSeconds");
                    if (raw == null) return 90;

                    int seconds = Convert.ToInt32(raw);
                    return (seconds == 15 || seconds == 30 || seconds == 90) ? seconds : 90;
                }
            }
            catch
            {
                return 90;
            }
        }

        private static void SaveRefreshSeconds(int seconds)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (key != null) key.SetValue("RefreshSeconds", seconds);
                }
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------------- menu

        /// <summary>
        /// Opens the menu above the cursor, in a borderless window of our own so the
        /// size, the corners and the dismissal are all under our control.
        /// </summary>
        private void ShowMenu()
        {
            HideTip();
            SyncDpi();

            // One popup at a time. Without this, each click built a new window and the
            // old one stayed alive until it lost focus — repeated clicks piled up.
            if (_menuForm != null && !_menuForm.IsDisposed)
            {
                try { _menuForm.Close(); } catch { }
                _menuForm = null;
            }

            Point cursor = Cursor.Position;
            float menuScale = ScaleAt(cursor);

            MenuPanel panel = new MenuPanel(menuScale);
            panel.RefreshSeconds = _refreshSeconds;
            panel.Theme = ThemeManager.Mode;
            panel.AutostartEnabled = Autostart.IsSupported();
            panel.AutostartOn = Autostart.IsSupported() && Autostart.IsEnabled();

            panel.QuitRequested += delegate { Application.Exit(); };

            panel.AutostartToggled += delegate
            {
                Autostart.Set(panel.AutostartOn);
            };

            panel.RefreshSecondsChanged += delegate
            {
                _refreshSeconds = panel.RefreshSeconds;
                SaveRefreshSeconds(_refreshSeconds);
                ApplyInterval();
            };

            panel.ThemeChanged += delegate
            {
                // The panel already persisted the choice and repainted itself; the
                // hover card and the menu chrome follow the same palette.
                if (_tip != null && !_tip.IsDisposed) _tip.Invalidate();
                if (_menuForm != null && !_menuForm.IsDisposed)
                {
                    _menuForm.BackColor = FluentTheme.P.BgTop;
                    _menuForm.Invalidate(true);
                }
            };

            TrayMenuForm form = new TrayMenuForm();
            form.Panel = panel;
            form.ClientSize = panel.Size;

            panel.Dock = DockStyle.Fill;
            form.Controls.Add(panel);

            Point p = cursor;
            Rectangle screen = Screen.FromPoint(p).WorkingArea;

            int x = p.X + 4;
            int y = p.Y - panel.Height - 4;

            if (x + panel.Width > screen.Right) x = p.X - panel.Width - 4;
            if (y < screen.Top) y = p.Y + 4;

            form.Location = new Point(x, y);
            _menuForm = form;
            form.Show();
            form.Activate();
            panel.Focus();
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            RedrawTray();
            if (_tip != null && !_tip.IsDisposed) _tip.Invalidate();
            if (_menuForm != null && !_menuForm.IsDisposed)
            {
                _menuForm.BackColor = FluentTheme.P.BgTop;
                _menuForm.Invalidate(true);
            }
        }

        /// <summary>
        /// The tray glyph is tinted from the system light/dark setting (that is what
        /// the shell does), so a system theme flip has to redraw it even when the app
        /// is pinned to one appearance.
        /// </summary>
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            try
            {
                _marshal.BeginInvoke(new MethodInvoker(delegate
                {
                    RedrawTray();
                    if (_tip != null && !_tip.IsDisposed) _tip.Invalidate();
                    if (_menuForm != null && !_menuForm.IsDisposed) _menuForm.Invalidate(true);
                }));
            }
            catch
            {
            }
        }

        /// <summary>
        /// A display swap, a resolution change or a scaling change can all move the
        /// taskbar onto a display with a different DPI. Re-render the glyph at the new
        /// slot size and rescale the hover card, otherwise the shell upscales a glyph
        /// we drew for the old monitor.
        /// </summary>
        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                _marshal.BeginInvoke(new MethodInvoker(delegate
                {
                    int previous = _dpi;
                    RefreshDpi();

                    RedrawTray();

                    if (_tip != null && !_tip.IsDisposed)
                    {
                        _tip.SetRows(BuildTipRows(), ScaleAt(_tipAnchor));
                        if (_tip.Visible) PositionTip(_tipAnchor);
                        _tip.Invalidate();
                    }

                    // The menu is already open at the old scale; closing beats
                    // relaying it out under the pointer.
                    if (previous != _dpi && _menuForm != null && !_menuForm.IsDisposed)
                    {
                        try { _menuForm.Close(); } catch { }
                        _menuForm = null;
                    }
                }));
            }
            catch
            {
            }
        }

        /// <summary>Live readings first, then sleeping or unsupported devices.</summary>
        private static List<DeviceSnapshot> Ordered(List<DeviceSnapshot> input)
        {
            List<DeviceSnapshot> live = new List<DeviceSnapshot>();
            List<DeviceSnapshot> asleep = new List<DeviceSnapshot>();

            if (input != null)
            {
                foreach (DeviceSnapshot s in input)
                {
                    if (s.Reading == null) { asleep.Add(s); continue; }

                    if (s.Reading.Online && s.Reading.Percent.HasValue) live.Add(s);
                    else asleep.Add(s);
                }
            }

            live.AddRange(asleep);
            return live;
        }

        private static string ShortLabel(DeviceSnapshot s)
        {
            if (s == null) return "";
            if (!string.IsNullOrEmpty(s.Info.ShortModel)) return s.Info.ShortModel;
            if (!string.IsNullOrEmpty(s.Info.Model)) return s.Info.Model;
            return s.Info.Brand;
        }

        private static int? LivePercent(DeviceSnapshot s)
        {
            if (s == null || s.Reading == null) return null;
            if (!s.Reading.Online || !s.Reading.Percent.HasValue) return null;
            return s.Reading.Percent.Value;
        }

        private int? LastPercent(string id)
        {
            int pct;
            if (id != null && _lastPercent.TryGetValue(id, out pct)) return pct;
            return null;
        }

        // ------------------------------------------------------------- refresh

        private void RefreshNow()
        {
            if (_refreshing)
            {
                // A read is already running. Queue one more instead of dropping the
                // request — dropping it made a refresh appear to do nothing at all.
                _refreshQueued = true;
                return;
            }

            _refreshing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _manager.Refresh();
                }
                catch
                {
                }
                finally
                {
                    _refreshing = false;
                }

                try
                {
                    _marshal.BeginInvoke(new MethodInvoker(delegate
                    {
                        UpdateTray(_manager.Snapshot());
                    }));
                }
                catch
                {
                }

                if (_refreshQueued)
                {
                    _refreshQueued = false;
                    RefreshNow();
                }
            });
        }

        // ------------------------------------------------------------ tray face

        /// <summary>
        /// Redraws the tray glyphs: one battery per device, at most two. A sleeping
        /// device keeps its slot with its last known level so the count still tracks
        /// the fleet.
        /// </summary>
        private void UpdateTray(List<DeviceSnapshot> snaps)
        {
            _snapshot = snaps ?? new List<DeviceSnapshot>();

            foreach (DeviceSnapshot s in _snapshot)
            {
                int? live = LivePercent(s);
                if (live.HasValue) _lastPercent[s.Info.Id] = live.Value;
            }

            RedrawTray();

            if (_tip != null && !_tip.IsDisposed && _tip.Visible)
            {
                _tip.SetRows(BuildTipRows(), ScaleAt(_tipAnchor));
                PositionTip(_tipAnchor);
            }
        }

        private void RedrawTray()
        {
            List<DeviceSnapshot> ordered = Ordered(_snapshot);
            int count = Math.Min(2, ordered.Count);

            if (count <= 0)
            {
                SetTrayIcon(IconFactory.Render(Dpi.SmallIconSize(_dpi)));
                return;
            }

            int?[] levels = new int?[count];
            for (int i = 0; i < count; i++)
            {
                int? pct = LivePercent(ordered[i]);
                if (!pct.HasValue) pct = LastPercent(ordered[i].Info.Id);
                levels[i] = pct;
            }

            SetTrayIcon(IconFactory.RenderTray(Dpi.SmallIconSize(_dpi), levels));
        }

        private void SetTrayIcon(Bitmap bmp)
        {
            if (bmp == null) return;

            using (bmp)
            {
                try
                {
                    Icon icon = IconFactory.ToIcon(bmp);
                    Icon old = _trayIcon;
                    _trayIcon = icon;
                    _tray.Icon = icon;
                    if (old != null) { try { old.Dispose(); } catch { } }
                }
                catch
                {
                }
            }
        }

        private List<TipRow> BuildTipRows()
        {
            List<TipRow> rows = new List<TipRow>();

            foreach (DeviceSnapshot s in Ordered(_snapshot))
            {
                TipRow row = new TipRow();
                row.Name = ShortLabel(s);
                row.Channel = (s != null && s.Reading != null) ? FluentTheme.ChannelOf(s.Reading.Detail) : "";

                int? pct = LivePercent(s);
                if (pct.HasValue)
                {
                    row.Percent = pct.Value;
                    row.Online = true;
                }
                else
                {
                    row.Percent = LastPercent(s.Info.Id);
                    row.Online = false;
                }

                if (!row.Percent.HasValue) row.Value = "休眠";
                else if (row.Percent.Value >= 100) row.Value = "Full";
                else row.Value = row.Percent.Value + "%";

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// Re-reads the taskbar DPI. Called at startup and whenever Windows reports a
        /// display or scaling change, so the glyph is always rendered at the pixel size
        /// the shell is about to ask for instead of being resampled into it.
        /// </summary>
        private void RefreshDpi()
        {
            _dpi = Dpi.TrayDpi();
            _dpiScale = Dpi.Scale(_dpi);
        }

        private static float ScaleAt(Point point)
        {
            return Dpi.Scale(Dpi.DpiAt(point));
        }

        /// <summary>
        /// Cheap re-check on the paths that rebuild UI anyway. DisplaySettingsChanged
        /// covers scaling changes, but dragging the taskbar to another monitor is not a
        /// display setting change, so the glyph also re-syncs the next time the user
        /// opens the menu or hovers the icon.
        /// </summary>
        private void SyncDpi()
        {
            int dpi = Dpi.TrayDpi();
            if (dpi == _dpi) return;

            _dpi = dpi;
            _dpiScale = Dpi.Scale(dpi);
            RedrawTray();
        }

        // ----------------------------------------------------------- hover card

        private void ShowTip()
        {
            // A click or its popup already owns the interaction.
            if (_menuForm != null && !_menuForm.IsDisposed) return;

            SyncDpi();

            if (_tip == null || _tip.IsDisposed)
            {
                _tip = new FluentTipForm();
            }

            _tip.SetRows(BuildTipRows(), ScaleAt(Cursor.Position));
            _tipAnchor = Cursor.Position;
            PositionTip(_tipAnchor);

            try
            {
                _tip.Show();
            }
            catch
            {
                return;
            }

            _tipPoll.Start();
        }

        private void PositionTip(Point anchor)
        {
            if (_tip == null || _tip.IsDisposed) return;

            Rectangle work = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X - _tip.Width / 2;
            int y = work.Bottom - _tip.Height - 8;

            if (x + _tip.Width > work.Right - 4) x = work.Right - _tip.Width - 4;
            if (x < work.Left + 4) x = work.Left + 4;
            if (y < work.Top + 4) y = work.Top + 4;

            _tip.Location = new Point(x, y);
        }

        private void PollTip(object sender, EventArgs e)
        {
            if (_tip == null || _tip.IsDisposed || !_tip.Visible)
            {
                _tipPoll.Stop();
                return;
            }

            Point c = Cursor.Position;
            Screen screen = Screen.FromPoint(c);

            bool inTip = _tip.Bounds.Contains(c);
            bool nearTray = c.Y >= screen.WorkingArea.Bottom - 6 &&
                            Math.Abs(c.X - _tipAnchor.X) <= 150;

            if (!inTip && !nearTray) HideTip();
        }

        private void HideTip()
        {
            _hoverDelay.Stop();
            _tipPoll.Stop();

            if (_tip != null && !_tip.IsDisposed && _tip.Visible) _tip.Hide();
        }

        public void Dispose()
        {
            ThemeManager.Changed -= OnThemeChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            // The popup is a separate top-level window: closing the tray app must not
            // leave it on screen.
            if (_menuForm != null && !_menuForm.IsDisposed)
            {
                try { _menuForm.Close(); } catch { }
                _menuForm = null;
            }

            HideTip();

            if (_tip != null && !_tip.IsDisposed)
            {
                try { _tip.Close(); } catch { }
                try { _tip.Dispose(); } catch { }
                _tip = null;
            }

            if (_hoverDelay != null)
            {
                _hoverDelay.Stop();
                _hoverDelay.Dispose();
            }

            if (_tipPoll != null)
            {
                _tipPoll.Stop();
                _tipPoll.Dispose();
            }

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            if (_trayIcon != null)
            {
                try { _trayIcon.Dispose(); } catch { }
                _trayIcon = null;
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }

            if (_marshal != null) _marshal.Dispose();
        }
    }
}
