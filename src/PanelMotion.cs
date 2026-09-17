using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal static class PanelMotion
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        internal static bool Enabled
        {
            get { int enabled; return !SystemInformation.HighContrast && !SystemInformation.TerminalServerSession && SystemParametersInfo(0x1042, 0, out enabled, 0) && enabled != 0; }
        }
        internal static double Ease(double progress) { double t = Math.Max(0, Math.Min(1, progress)); return 1 - Math.Pow(1 - t, 3); }
        internal static Rectangle Blend(Rectangle from, Rectangle to, double progress)
        {
            double t = Ease(progress);
            return new Rectangle((int)Math.Round(from.X + (to.X - from.X) * t), (int)Math.Round(from.Y + (to.Y - from.Y) * t),
                (int)Math.Round(from.Width + (to.Width - from.Width) * t), (int)Math.Round(from.Height + (to.Height - from.Height) * t));
        }
        internal static Rectangle Place(Point anchor, Size size, Rectangle area)
        {
            size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
            return new Rectangle(Math.Max(area.Left, Math.Min(anchor.X - size.Width / 2, area.Right - size.Width)),
                Math.Max(area.Top, Math.Min(anchor.Y - size.Height, area.Bottom - size.Height)), size.Width, size.Height);
        }
    }
}
