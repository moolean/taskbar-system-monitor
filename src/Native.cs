using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TaskbarSystemMonitor
{
    internal static class Native
    {
        internal const uint NewBar = 0, RemoveBar = 1, QueryPos = 2, SetPos = 3, ActivateBar = 6, WindowPosChanged = 9;
        internal const uint LeftEdge = 0, TopEdge = 1, RightEdge = 2, BottomEdge = 3;
        internal static readonly IntPtr Topmost = new IntPtr(-1), Bottom = new IntPtr(1);
        internal const uint NoActivate = 0x10, NoMove = 2, NoSize = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left, Top, Right, Bottom;
            internal Rect(Rectangle r) { Left = r.Left; Top = r.Top; Right = r.Right; Bottom = r.Bottom; }
            internal Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct AppBarData
        {
            internal uint Size;
            internal IntPtr Window;
            internal uint Callback, Edge;
            internal Rect Bounds;
            internal IntPtr Parameter;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo
        {
            internal int Size;
            internal Rect Monitor, Work;
            internal uint Flags;
        }
        internal static MonitorInfo PrimaryMonitor()
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromPoint(new Point(0, 0), 1), ref info))
                throw new System.ComponentModel.Win32Exception();
            return info;
        }
        internal static int Dpi(IntPtr window)
        {
            try { return Math.Max(96, (int)GetDpiForWindow(window)); }
            catch (EntryPointNotFoundException) { return 96; }
        }
        internal static AppBarData BarData(IntPtr window)
        {
            return new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)), Window = window };
        }
        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetColorizationColor(out uint color, [MarshalAs(UnmanagedType.Bool)] out bool opaque);
    }
}
