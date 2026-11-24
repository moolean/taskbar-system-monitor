using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    // Fault injection is confined to our sample AppBar. Never stop Explorer or
    // broadcast fake Shell events to other desktop applications.
    internal static class ShellRecoveryTests
    {
        internal static void Run(StringBuilder report)
        {
            Rectangle baseline = Native.PrimaryMonitor().Work.Rectangle;
            bool shellAvailable = false, failQuery = false, invalidPosition = false;
            int failures = 2, registrations = 0;
            AppBarMessage message = delegate(uint command, ref Native.AppBarData data)
            {
                if (command == Native.NewBar)
                {
                    registrations++;
                    if (failures > 0) { failures--; return UIntPtr.Zero; }
                }
                if (command == Native.QueryPos && failQuery) { failQuery = false; return UIntPtr.Zero; }
                UIntPtr result = Native.SHAppBarMessage(command, ref data);
                if (command == Native.SetPos && invalidPosition)
                {
                    invalidPosition = false; data.Bounds = new Native.Rect(Rectangle.Empty);
                }
                return result;
            };
            using (var dock = new DockForm(new Settings { FirstRun = false }, message,
                delegate { return shellAvailable ? Native.FindWindow("Shell_TrayWnd", null) : IntPtr.Zero; }))
            {
                dock.Show();
                Require(dock.WaitingForShell && registrations == 0, "Missing Shell waits without native registration");
                Require(Native.PrimaryMonitor().Work.Rectangle == baseline && dock.Right < 0, "Unregistered bar does not reserve or cover desktop space");
                shellAvailable = true;
                dock.PositionBar(); dock.PositionBar();
                Require(dock.WaitingForShell && registrations == 2, "Repeated ABM_NEW failures do not escape as exceptions");
                PumpUntil(delegate { return dock.Registered; });
                Rectangle ready = dock.Bounds;
                Require(ready.Width > 1 && ready.Bottom == baseline.Bottom && !Native.IsTopmost(dock.Handle), "Timer recovers correct non-topmost position");
                report.AppendLine("PASS Shell unavailable / repeated registration failures / automatic timer recovery / no overlap while waiting");

                failures = 1;
                NotifyTaskbarCreated(dock); NotifyTaskbarCreated(dock);
                Application.DoEvents();
                Require(dock.WaitingForShell && Native.PrimaryMonitor().Work.Rectangle == baseline, "Duplicate Shell broadcasts release our stale registration and tolerate failure; waiting=" + dock.WaitingForShell + ", registrations=" + registrations + ", work=" + Native.PrimaryMonitor().Work.Rectangle);
                PumpUntil(delegate { return dock.Registered; });
                Require(dock.Bounds == ready, "Restart recovery reserves exactly one row");
                int count = registrations;
                NotifyTaskbarCreated(dock); NotifyTaskbarCreated(dock); Application.DoEvents();
                Require(dock.Registered && registrations == count + 1 && dock.Bounds == ready, "Duplicate broadcasts coalesce and can re-register an already live Shell");
                report.AppendLine("PASS exact TaskbarCreated callback / duplicate broadcasts / failed re-registration recovery / one-row reservation");

                failQuery = true; dock.PositionBar();
                Require(dock.WaitingForShell, "Failed position query waits safely");
                invalidPosition = true; dock.PositionBar();
                Require(dock.WaitingForShell && Native.PrimaryMonitor().Work.Rectangle == baseline, "Invalid Shell position releases the reservation");
                PumpUntil(delegate { return dock.Registered; });
                Require(dock.Bounds == ready, "Position failure recovery restores original bounds");

                shellAvailable = false; dock.PositionBar();
                Require(dock.WaitingForShell, "Missing taskbar is detected without a broadcast");
                dock.Hide(); count = registrations; NotifyTaskbarCreated(dock); Application.DoEvents(); dock.PositionBar();
                Require(!dock.WaitingForShell && !dock.Registered && registrations == count, "Hidden bar cancels retries and ignores Shell broadcasts");
                shellAvailable = true; dock.Show();
                Require(dock.Registered && dock.Bounds == ready, "Showing again recovers normally");
                report.AppendLine("PASS failed query / invalid bounds / missed-broadcast recovery / hidden-state cancellation");
            }
            Require(Native.PrimaryMonitor().Work.Rectangle == baseline, "Recovery test disposal restores work area");
            report.AppendLine("PASS recovery timer disposal / work-area cleanup (isolated sample, no Explorer restart)");
        }

        private static void NotifyTaskbarCreated(DockForm dock)
        {
            var callback = typeof(DockForm).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
            var message = Message.Create(dock.Handle, (int)Native.RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
            callback.Invoke(dock, new object[] { message });
        }
        private static void PumpUntil(Func<bool> ready)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(20); }
            Require(ready(), "Recovery timer did not restore the bar within 5 seconds");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Shell recovery: " + message); }
    }
}
