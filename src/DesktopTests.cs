using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    // Explicit opt-in, interactive desktop only. Never run from the CI self-test.
    internal static class DesktopTests
    {
        internal static int Run(string reportPath)
        {
            var report = new StringBuilder();
            int result = 0, step = 0;
            Rectangle baseline = Native.PrimaryMonitor().Work.Rectangle;
            Rectangle initialBounds = Rectangle.Empty;
            var settings = new Settings { FirstRun = false };
            using (var context = new ApplicationContext())
            using (var dock = new DockForm(settings))
            using (var maximized = new Form { Text = "System Monitor — layout test", Size = new Size(500, 300) })
            using (var timer = new Timer { Interval = 600 })
            {
                if (Native.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
                    throw new InvalidOperationException("Interactive Explorer taskbar is required.");
                report.AppendLine("Baseline work area: " + baseline);
                File.WriteAllText(reportPath, report + "Testing...\n");
                timer.Tick += delegate
                {
                    try
                    {
                        switch (step++)
                        {
                            case 0: dock.Show(); break;
                            case 1:
                                initialBounds = dock.Bounds;
                                VerifyReservation(dock, baseline, settings.Height);
                                Require(!Native.IsTopmost(dock.Handle), "Default bar must not be topmost");
                                report.AppendLine("PASS default non-topmost window style");
                                report.AppendLine("PASS show / reserve: " + dock.Bounds);
                                maximized.Show(); maximized.WindowState = FormWindowState.Maximized;
                                break;
                            case 2:
                                Require(maximized.PointToScreen(new Point(0, maximized.ClientSize.Height)).Y <= dock.Top + 1, "Maximized client overlaps resource bar");
                                report.AppendLine("PASS maximized window stays outside bar");
                                int changes = dock.LayoutChanges;
                                for (int i = 0; i < 50; i++) dock.PositionBar();
                                Require(dock.Bounds == initialBounds && dock.LayoutChanges == changes, "Repeated positioning drifts");
                                report.AppendLine("PASS 50 repeated positioning calls / no drift");
                                maximized.Hide(); dock.SetFullscreenState(false);
                                settings.AlwaysOnTop = true; dock.Apply(settings);
                                Require(Native.IsTopmost(dock.Handle), "Topmost switch did not enable");
                                dock.SetFullscreenState(true);
                                Require(!Native.IsTopmost(dock.Handle), "Fullscreen did not yield");
                                dock.SetFullscreenState(false);
                                Require(Native.IsTopmost(dock.Handle), "Topmost was not restored after fullscreen");
                                settings.AlwaysOnTop = false; dock.Apply(settings);
                                dock.SetFullscreenState(true); dock.SetFullscreenState(false); dock.PositionBar();
                                Require(!Native.IsTopmost(dock.Handle), "Non-topmost preference was lost after fullscreen/reposition");
                                report.AppendLine("PASS topmost toggle / fullscreen yield / non-topmost restoration");
                                maximized.Hide(); settings.Height = 32; dock.Apply(settings);
                                break;
                            case 3:
                                VerifyReservation(dock, baseline, 32);
                                report.AppendLine("PASS resize reservation: " + dock.Bounds);
                                dock.Hide(); break;
                            case 4:
                                Require(!dock.Registered && Native.PrimaryMonitor().Work.Rectangle == baseline, "Hide did not restore work area");
                                report.AppendLine("PASS hide restores work area");
                                dock.Show(); break;
                            case 5:
                                VerifyReservation(dock, baseline, 32);
                                report.AppendLine("PASS show again reserves once");
                                dock.Dispose(); break;
                            case 6:
                                Require(Native.PrimaryMonitor().Work.Rectangle == baseline, "Dispose did not restore work area");
                                report.AppendLine("PASS exit restores work area");
                                timer.Stop(); context.ExitThread(); break;
                        }
                    }
                    catch (Exception error)
                    {
                        result = 20; report.AppendLine("FAIL " + error); timer.Stop(); context.ExitThread();
                    }
                };
                timer.Start();
                Application.Run(context);
            }
            report.AppendLine("Final work area: " + Native.PrimaryMonitor().Work.Rectangle);
            File.WriteAllText(reportPath, report.ToString());
            return result;
        }
        private static void VerifyReservation(DockForm dock, Rectangle baseline, int logicalHeight)
        {
            int height = BarRenderer.Scale(logicalHeight, Native.Dpi(dock.Handle));
            Rectangle work = Native.PrimaryMonitor().Work.Rectangle;
            Require(dock.Registered, "Bar is not registered");
            Require(dock.Height == height && dock.Bottom == baseline.Bottom, "Bar has incorrect position or thickness");
            Require(work == Rectangle.FromLTRB(baseline.Left, baseline.Top, baseline.Right, baseline.Bottom - height), "Unexpected working-area reservation: " + work);
            Native.Rect taskbar;
            if (Native.GetWindowRect(Native.FindWindow("Shell_TrayWnd", null), out taskbar))
                Require(!dock.Bounds.IntersectsWith(taskbar.Rectangle), "Resource bar overlaps taskbar");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
