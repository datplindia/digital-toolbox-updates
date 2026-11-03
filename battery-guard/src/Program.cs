using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DigitalArts.BatteryGuard
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, @"Local\DigitalArts.BatteryGuard.SingleInstance", out bool isFirst))
            {
                if (!isFirst)
                {
                    // ऐप पहले से चल रहा है – उसी की विंडो खोल दें
                    NativeMethods.SafeBroadcastShowMe();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool minimized = Array.Exists(args, a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
                Application.Run(new MainForm(minimized));
                GC.KeepAlive(mutex);
            }
        }
    }

    static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xffff;
        public static readonly int WM_SHOWME = SafeRegister("DigitalArts_BatteryGuard_ShowMe");

        static int SafeRegister(string name)
        {
            try { int id = RegisterWindowMessage(name); return id != 0 ? id : -1; }
            catch { return -1; }
        }

        public static void SafeDestroyIcon(IntPtr h) { try { DestroyIcon(h); } catch { } }
        public static void SafeBroadcastShowMe() { try { if (WM_SHOWME > 0) PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero); } catch { } }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
