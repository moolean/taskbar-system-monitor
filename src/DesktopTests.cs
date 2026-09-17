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
            int result = 0, step = 0, compactHeight = 0, dismissWrites = 0;
            Rectangle baseline = Native.PrimaryMonitor().Work.Rectangle;
            Rectangle initialBounds = Rectangle.Empty;
            var settings = new Settings { FirstRun = false };
            using (var context = new ApplicationContext())
            using (var dock = new DockForm(settings))
            using (var maximized = new Form { Text = "System Monitor — layout test", Size = new Size(500, 300) })
            using (var work = new WorkForm(WorkTests.Demo(), delegate { return true; }))
            using (var transient = new WorkForm(WorkTests.Demo(), delegate { dismissWrites++; return true; }))
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
                                VerifyReadOnlyText(dock, settings);
                                report.AppendLine("PASS Codex/IP passive click and cursor / CPU and keyword actions / right-click menu without settings / explicit Settings choice (sample data only)");
                                VerifyWorkDrag(dock, settings);
                                report.AppendLine("PASS work drag threshold / forward and backward reorder / outside cancellation / frozen sampling / stale keyword cancellation / no accidental click (sample data only)");
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
                                work.KeepOpen = true; work.Show(); work.OpenItem(1); break;
                            case 7:
                                Require(work.SelectedIndex == 1 && WorkTests.Find<TextBox>(work, "关键词笔记").Focused, "Direct keyword entry lost its selection or notes focus after Shown");
                                Require(!Native.IsTopmost(work.Handle), "Work window must not be topmost");
                                Require(!work.Animating && work.Width == BarRenderer.Scale(420, Native.Dpi(work.Handle)) && work.Height < BarRenderer.Scale(300, Native.Dpi(work.Handle)), "Compact panel size or animation did not settle");
                                compactHeight = work.Height;
                                var deleteButton = WorkTests.Find<UiButton>(work, "删除当前关键词");
                                Require(deleteButton.Visible && deleteButton.Enabled && work.ClientRectangle.Contains(deleteButton.Bounds) && deleteButton.Width == BarRenderer.Scale(52, Native.Dpi(work.Handle)), "Direct delete action must be visible inside the compact footer");
                                WorkTests.Find<UiButton>(work, "展开或收起链接").PerformClick(); break;
                            case 8:
                                Require(!work.Animating && work.Height > compactHeight && WorkTests.Find<TextBox>(work, "关键词链接").Focused, "Link expansion animation or focus failed");
                                WorkTests.Find<UiButton>(work, "切换关键词列表").PerformClick(); break;
                            case 9:
                                Require(!work.Animating && work.Browsing && WorkTests.Find<TextBox>(work, "搜索或新建关键词").Focused, "List transition failed");
                                work.Dismiss(); break;
                            case 10:
                                Require(work.IsDisposed, "Panel did not finish its close transition");
                                report.AppendLine("PASS compact panel / direct notes focus / animated link, list and close transitions / non-topmost (sample data only)");
                                transient.Show(); transient.OpenItem(0); break;
                            case 11:
                                WorkTests.Find<TextBox>(transient, "关键词笔记").Text = "Synthetic draft before outside click";
                                maximized.WindowState = FormWindowState.Normal; maximized.Show(); maximized.Activate(); break;
                            case 12:
                                Require(transient.IsDisposed && dismissWrites == 1, "Outside dismissal must flush the pending draft exactly once before closing");
                                report.AppendLine("PASS unpinned outside dismissal / pending draft flush (sample data only)");
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
        private static void VerifyReadOnlyText(DockForm dock, Settings settings)
        {
            var sample = InfoTests.Demo(); dock.UpdateSnapshot(sample);
            int modules = 0, details = 0, work = 0, configuration = 0;
            Action<string> moduleAction = delegate { modules++; };
            Action<int> workAction = delegate { work++; };
            EventHandler detailsAction = delegate { details++; }, settingsAction = delegate { configuration++; };
            dock.ModuleRequested += moduleAction; dock.WorkRequested += workAction;
            dock.DetailsRequested += detailsAction; dock.SettingsRequested += settingsAction;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var click = typeof(DockForm).GetMethod("OnMouseClick", flags);
            var move = typeof(DockForm).GetMethod("OnMouseMove", flags);
            try
            {
                using (var graphics = dock.CreateGraphics())
                {
                    int hidden, dpi = Native.Dpi(dock.Handle);
                    var cells = BarRenderer.Layout(graphics, dock.ClientRectangle, settings, sample, dpi, out hidden);
                    foreach (var cell in cells)
                    {
                        if (cell.Id != "Codex" && cell.Id != "Ip") continue;
                        var point = new Point(cell.Bounds.Left + cell.Bounds.Width / 2, cell.Bounds.Height / 2);
                        move.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0) });
                        Require(dock.Cursor == Cursors.Default, "Read-only text still uses a hand cursor");
                        click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
                        Require(modules == 0 && details == 0 && work == 0 && configuration == 0, "Read-only click unexpectedly opens a window");
                    }
                    foreach (var cell in cells)
                    {
                        if (cell.Id == "Cpu") click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Left, 1, cell.Bounds.Left + 8, 12, 0) });
                        if (cell.Id == "Tracks")
                        {
                            var target = BarRenderer.WorkTargets(graphics, cell, sample, settings, dpi)[0];
                            click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Left, 1, target.Bounds.Left + target.Bounds.Width / 2, target.Bounds.Top + target.Bounds.Height / 2, 0) });
                        }
                        if (cell.Id == "Ip") click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Right, 1, cell.Bounds.Left + 8, 12, 0) });
                    }
                    Require(modules == 1 && work == 1 && configuration == 0 && details == 0 && dock.BarMenu.Visible, "Right-click must only open the menu, never settings");
                    dock.BarMenu.Close();
                    foreach (int x in new[] { 4, dock.Width - 4 })
                    {
                        click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Right, 1, x, 12, 0) });
                        Require(dock.BarMenu.Visible && configuration == 0, "Right-click on either end must only open the menu");
                        dock.BarMenu.Close();
                    }
                    click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 4, 12, 0) });
                    dock.BarMenu.Items["settings"].PerformClick(); dock.BarMenu.Close();
                    Require(configuration == 1 && modules == 1 && work == 1 && details == 0, "Settings requires an explicit menu choice");
                    click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Middle, 1, dock.Width - 4, 12, 0) });
                    Require(configuration == 1, "Middle-click must not open settings");
                    click.Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.Left, 1, dock.Width - 4, 12, 0) });
                    Require(configuration == 2, "Left-click ellipsis retains its settings shortcut");
                }
            }
            finally
            {
                dock.ModuleRequested -= moduleAction; dock.WorkRequested -= workAction;
                dock.DetailsRequested -= detailsAction; dock.SettingsRequested -= settingsAction;
            }
        }
        private static void VerifyWorkDrag(DockForm dock, Settings settings)
        {
            var sample = InfoTests.Demo(); sample.Briefing.WorkKeywords.Add("发布计划"); dock.UpdateSnapshot(sample);
            int moves = 0, clicks = 0, from = -1, to = -1;
            Action<int, int> reordered = delegate(int source, int target) { moves++; from = source; to = target; };
            Action<int> opened = delegate { clicks++; };
            dock.WorkReorderRequested += reordered; dock.WorkRequested += opened;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Action<string, MouseButtons, Point> mouse = delegate(string name, MouseButtons button, Point point)
            { typeof(DockForm).GetMethod(name, flags).Invoke(dock, new object[] { new MouseEventArgs(button, 1, point.X, point.Y, 0) }); };
            try
            {
                using (var g = dock.CreateGraphics())
                {
                    int hidden, dpi = Native.Dpi(dock.Handle);
                    var cell = BarRenderer.Layout(g, dock.ClientRectangle, settings, sample, dpi, out hidden).Find(x => x.Id == "Tracks");
                    var targets = BarRenderer.WorkTargets(g, cell, sample, settings, dpi);
                    Point first = new Point(targets[0].Bounds.Left + targets[0].Bounds.Width / 2, dock.Height / 2);
                    Point last = new Point(targets[2].Bounds.Right - 2, first.Y);
                    mouse("OnMouseDown", MouseButtons.Left, first); mouse("OnMouseMove", MouseButtons.Left, first);
                    Require(!dock.WorkDragActive, "Stationary click must not become a drag");
                    mouse("OnMouseUp", MouseButtons.Left, first); mouse("OnMouseClick", MouseButtons.Left, first);
                    Require(clicks == 1 && moves == 0, "Regular keyword click still opens notes once");
                    mouse("OnMouseDown", MouseButtons.Left, first); mouse("OnMouseMove", MouseButtons.Left, last);
                    Require(dock.WorkDragActive && dock.WorkDropPosition == 3, "Drag previews the final insertion gap");
                    var refresh = sample.FrozenCopy(); refresh.Cpu = 99; dock.UpdateSnapshot(refresh);
                    Require(dock.WorkDragActive && dock.WorkDropPosition == 3, "Sampling cannot reset a drag");
                    mouse("OnMouseClick", MouseButtons.Left, last); mouse("OnMouseUp", MouseButtons.Left, last);
                    Require(moves == 1 && from == 0 && to == 2 && clicks == 1 && !dock.Capture, "Drop reorders once without opening notes");
                    mouse("OnMouseDown", MouseButtons.Left, last); var beforeFirst = new Point(targets[0].Bounds.Left + 1, first.Y);
                    mouse("OnMouseMove", MouseButtons.Left, beforeFirst); mouse("OnMouseUp", MouseButtons.Left, beforeFirst);
                    Require(moves == 2 && from == 2 && to == 0, "Backward drag uses insertion semantics");
                    mouse("OnMouseDown", MouseButtons.Left, first); var outside = new Point(first.X, -BarRenderer.Scale(50, dpi));
                    mouse("OnMouseMove", MouseButtons.Left, outside); mouse("OnMouseUp", MouseButtons.Left, outside);
                    Require(moves == 2 && !dock.WorkDragActive && !dock.Capture, "Outside release cancels and releases capture");
                    mouse("OnMouseDown", MouseButtons.Left, first); mouse("OnMouseMove", MouseButtons.Left, last);
                    var changed = sample.FrozenCopy(); changed.Briefing.WorkKeywords.Reverse(); dock.UpdateSnapshot(changed);
                    mouse("OnMouseUp", MouseButtons.Left, last);
                    Require(moves == 2 && clicks == 1, "Concurrent keyword changes cancel stale-index reorders");
                }
            }
            finally { dock.WorkReorderRequested -= reordered; dock.WorkRequested -= opened; }
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
