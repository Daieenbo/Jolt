using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Voltide.Core;
using Voltide.Interop;
using Voltide.UI;

namespace Voltide
{
    public static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 / _PER_MONITOR_AWARE. V2 tells the
        // shell (and WinForms) that we handle WM_DPICHANGED ourselves, which is the only
        // way a tray HICON can stay sharp when the taskbar sits on a 150 % display.
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);
        private static readonly IntPtr PerMonitorAware = new IntPtr(-3);

        /// <summary>
        /// Entry point usable from an in-memory compiled assembly
        /// (Add-Type ... then [Voltide.Program]::Run(@())).
        /// </summary>
        public static void Run(string[] args)
        {
            Main(args != null ? args : new string[0]);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (args != null && args.Length > 0 && args[0] == "--test")
                {
                    RunTest();
                    return;
                }

                bool createdNew;
                using (Mutex mutex = new Mutex(true, "Jolt-SingleInstance", out createdNew))
                {
                    if (!createdNew)
                    {
                        return;
                    }

                    // A Low-IL token cannot register a Shell_NotifyIcon: the shell denies
                    // it and WinForms drops the failure, so the process would run with no
                    // way to open its UI. That is what happens when Jolt.exe is started
                    // from a sandboxed workspace folder. Say so instead of appearing dead.
                    if (ProcessIntegrity.IsLow())
                    {
                        Log("Low integrity level: the shell will reject the tray icon.");
                        MessageBox.Show(
                            "Jolt 当前以低完整性级别(Low IL)运行，Windows 不允许这类进程显示托盘图标，所以程序无法正常工作。\n\n" +
                            "原因：程序位于受沙箱保护的目录中（例如 D:\\dshDesktop\\DSH-WorkSpace）。从这种目录启动的程序会被降为低完整性级别。\n\n" +
                            "解决办法：把 Jolt.exe 复制到沙箱目录之外的普通文件夹（例如 D:\\Jolt\\），再从那里运行。",
                            "Jolt 无法显示托盘图标",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    EnableDpiAwareness();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                    {
                        Log("UI thread: " + e.Exception);
                    };

                    AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                    {
                        Log("Domain: " + e.ExceptionObject);
                    };

                    using (TrayApp app = new TrayApp())
                    {
                        Application.Run();
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Main: " + ex);
                throw;
            }
        }

        /// <summary>
        /// Per-Monitor V2 first, then plain Per-Monitor, then the old system-wide flag.
        /// Each step needs a newer OS than the previous one, and every step must run
        /// before the first window exists or Windows ignores it.
        /// </summary>
        private static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(PerMonitorAwareV2)) return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                if (SetProcessDpiAwarenessContext(PerMonitorAware)) return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        private static void Log(string msg)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "Jolt-error.txt");
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void RunTest()
        {
            DeviceManager m = new DeviceManager();
            m.Refresh();

            List<DeviceSnapshot> snaps = m.Snapshot();
            Console.WriteLine("devices: " + snaps.Count);

            foreach (DeviceSnapshot s in snaps)
            {
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("name    : " + s.Info.Name);
                Console.WriteLine("brand   : " + s.Info.Brand);
                Console.WriteLine("model   : " + s.Info.Model);
                Console.WriteLine("vid/pid : " + s.Info.VendorId.ToString("X4") + ":" + s.Info.ProductId.ToString("X4"));
                Console.WriteLine("path    : " + s.Info.HidPath);
                Console.WriteLine("online  : " + s.Reading.Online);
                Console.WriteLine("percent : " + (s.Reading.Percent.HasValue ? s.Reading.Percent.Value.ToString() + "%" : "null"));
                Console.WriteLine("charging: " + s.Reading.Charging);
                Console.WriteLine("detail  : " + s.Reading.Detail);
                Console.WriteLine("raw     : " + s.Reading.Raw);
                Console.WriteLine("error   : " + s.Reading.Error);
            }
        }
    }
}
