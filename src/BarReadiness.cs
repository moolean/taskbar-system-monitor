using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TaskbarSystemMonitor
{
    // A live process and a Running scheduled task do not prove an AppBar exists.
    internal static class BarReadiness
    {
        internal const string Caption = "Taskbar System Monitor AppBar";
        private delegate bool WindowCallback(IntPtr window, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        internal static bool Visible(int pid)
        {
            if (pid <= 0) return false;
            Rectangle screen = Native.PrimaryMonitor().Monitor.Rectangle;
            bool found = false;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                uint owner; GetWindowThreadProcessId(window, out owner);
                if (owner != pid || !IsWindowVisible(window)) return true;
                var title = new StringBuilder(Caption.Length + 2);
                if (GetWindowText(window, title, title.Capacity) != Caption.Length || title.ToString() != Caption) return true;
                Native.Rect rect;
                if (Native.GetWindowRect(window, out rect))
                {
                    Rectangle r = rect.Rectangle;
                    found = r.Height > 1 && r.Width >= screen.Width - 2 && screen.Contains(r) && r.Top >= screen.Top + screen.Height / 2;
                }
                return !found;
            }, IntPtr.Zero);
            return found;
        }
        internal static bool Wait(int pid, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            do { if (Visible(pid)) return true; Thread.Sleep(200); } while (clock.ElapsedMilliseconds < timeoutMs);
            return false;
        }
    }
}
