namespace Voltide.UI
{
    /// <summary>
    /// DPI lookup for the tray glyph and the two popups.
    ///
    /// The process is declared Per-Monitor V2 (see Program.EnableDpiAwareness), so the
    /// shell no longer resamples our bitmap when the taskbar lives on a display with a
    /// different scaling factor. What we owe it in return is a glyph rendered at the
    /// taskbar's own pixel size, which means asking the taskbar window for its DPI
    /// instead of reading the old system metric once at startup.
    ///
    /// Every native entry point here is Windows 10 1607+ or Windows 8.1+, so each call
    /// is guarded: on an older OS the next fallback simply returns a coarser answer
    /// instead of throwing.
    /// </summary>
    internal static class Dpi
    {
        private const int SM_CXSMICON = 49;
        private const int MDT_EFFECTIVE_DPI = 0;
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr FindWindow(string className, string windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(System.IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int index, uint dpi);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr MonitorFromPoint(POINT pt, int flags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(System.IntPtr monitor, int dpiType,
                                                   out uint dpiX, out uint dpiY);

        /// <summary>
        /// DPI of the display that owns the taskbar. This is the one that decides how
        /// many pixels the tray slot actually gets.
        /// </summary>
        public static int TrayDpi()
        {
            try
            {
                System.IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != System.IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(taskbar);
                    if (dpi >= 72 && dpi <= 960) return (int)dpi;
                }
            }
            catch (System.EntryPointNotFoundException) { }
            catch (System.DllNotFoundException) { }

            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 72 && dpi <= 960) return (int)dpi;
            }
            catch (System.EntryPointNotFoundException) { }
            catch (System.DllNotFoundException) { }

            return ScreenDpi();
        }

        /// <summary>DPI of the display that contains a point (popups follow the cursor).</summary>
        public static int DpiAt(System.Drawing.Point point)
        {
            POINT pt;
            pt.X = point.X;
            pt.Y = point.Y;

            try
            {
                System.IntPtr monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (monitor != System.IntPtr.Zero)
                {
                    uint x;
                    uint y;
                    if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out x, out y) == 0 &&
                        x >= 72 && x <= 960)
                    {
                        return (int)x;
                    }
                }
            }
            catch (System.EntryPointNotFoundException) { }
            catch (System.DllNotFoundException) { }

            return TrayDpi();
        }

        /// <summary>
        /// The shell's small-icon metric at a given DPI: 16 px at 100 %, 20 at 125 %,
        /// 24 at 150 %, 32 at 200 %.
        /// </summary>
        public static int SmallIconSize(int dpi)
        {
            if (dpi < 72) dpi = 96;

            try
            {
                int px = GetSystemMetricsForDpi(SM_CXSMICON, (uint)dpi);
                if (px >= 8 && px <= 256) return px;
            }
            catch (System.EntryPointNotFoundException) { }
            catch (System.DllNotFoundException) { }

            try
            {
                int px = GetSystemMetrics(SM_CXSMICON);
                if (px >= 8 && px <= 256) return px;
            }
            catch (System.EntryPointNotFoundException) { }
            catch (System.DllNotFoundException) { }

            return System.Math.Max(8, (int)System.Math.Round(16.0 * dpi / 96.0));
        }

        /// <summary>Pixels per design unit for a DPI value.</summary>
        public static float Scale(int dpi)
        {
            if (dpi < 72) dpi = 96;
            return dpi / 96f;
        }

        private static int ScreenDpi()
        {
            try
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(System.IntPtr.Zero))
                {
                    int dpi = (int)System.Math.Round(g.DpiX);
                    if (dpi >= 72 && dpi <= 960) return dpi;
                }
            }
            catch
            {
            }

            return 96;
        }
    }
}
