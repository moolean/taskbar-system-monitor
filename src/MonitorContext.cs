using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class MonitorContext : ApplicationContext
    {
        private NotifyIcon tray;
        private System.Windows.Forms.Timer timer;
        private readonly Sampler sampler = new Sampler();
        private readonly NetworkSampler network = new NetworkSampler();
        private readonly History history = new History();
        private readonly Stopwatch samplingClock = Stopwatch.StartNew();
        private DockForm dock;
        private DetailsForm details;
        private ModulePopup popup;
        private SettingsForm activeSettings;
        private readonly InfoHub info = new InfoHub();
        private Settings settings;
        private Snapshot last;
        private EventWaitHandle exitSignal, showSignal;
        private readonly System.Collections.Generic.Dictionary<string, EventWaitHandle> actions = new System.Collections.Generic.Dictionary<string, EventWaitHandle>();
        private bool stopping, settingsOpen;

        internal MonitorContext(string[] args)
        {
            try
            {
                settings = Settings.Load(Settings.DefaultPath);
                dock = new DockForm(settings);
                details = new DetailsForm();
                popup = new ModulePopup();
                details.Apply(settings);
                tray = new NotifyIcon { Text = "系统监控", Icon = TrayIcon(null, null) };
                tray.ContextMenuStrip = BuildMenu();
                tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowDetails(); };
                dock.DetailsRequested += delegate { ShowDetails(); };
                dock.SettingsRequested += delegate { ShowSettings(); };
                details.SettingsRequested += delegate { ShowSettings(); };
                dock.ModuleRequested += ShowModule;
                popup.ConfigureRequested += delegate(string page) { ShowSettings(page); };
                popup.RefreshRequested += delegate { info.Reset(); info.Pulse(settings); };
                popup.WorkEdited += delegate(int index, WorkItem item)
                {
                    var next = settings.Copy(); if (index < 0 || index >= next.WorkItems.Count) return;
                    next.WorkItems[index] = item; if (SaveSettings(next)) { settings = next; ApplySettings(); }
                };
                // Poll auto-reset IPC events on the UI thread. This also works when
                // the bar has never been shown (and therefore has no window handle).
                exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEvent);
                showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEvent);
                foreach (string action in Program.Actions) actions[action] = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ActionEvent + action);
                tray.Visible = true;
                if (settings.Dock) dock.Show();
                timer = new System.Windows.Forms.Timer { Interval = 250 };
                timer.Tick += delegate { Tick(); };
                timer.Start();
                Tick();
                string requested = Program.RequestedAction(args); if (requested != null) actions[requested].Set();
                if (settings.FirstRun)
                {
                    try { Startup.Set(true); }
                    catch (Exception error) { ReportError("无法启用开机启动，请在托盘菜单中重试。", error); }
                    settings.FirstRun = false;
                    SaveSettings(settings);
                }
            }
            catch { Cleanup(); throw; }
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("打开监控面板"); open.Click += delegate { ShowDetails(); }; menu.Items.Add(open);
            var settingsItem = new ToolStripMenuItem("设置…"); settingsItem.Click += delegate { ShowSettings(); }; menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            var cpu = new ToolStripMenuItem("CPU：—") { Enabled = false };
            var memory = new ToolStripMenuItem("内存：—") { Enabled = false };
            menu.Items.Add(cpu); menu.Items.Add(memory);
            var dockItem = new ToolStripMenuItem("任务栏上方资源栏") { CheckOnClick = true };
            dockItem.Click += delegate
            {
                var next = settings.Copy(); next.Dock = dockItem.Checked;
                if (SaveSettings(next)) { settings = next; ApplySettings(); }
                else dockItem.Checked = settings.Dock;
            };
            menu.Items.Add(dockItem);
            var startup = new ToolStripMenuItem("开机自动启动") { CheckOnClick = true };
            startup.Click += delegate
            {
                try { Startup.Set(startup.Checked); }
                catch (Exception error) { startup.Checked = Startup.Enabled; ReportError("无法更改开机启动项。", error); }
            };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            var manager = new ToolStripMenuItem("打开任务管理器");
            manager.Click += delegate { try { Process.Start("taskmgr.exe"); } catch (Exception error) { ReportError("无法打开任务管理器。", error); } };
            menu.Items.Add(manager);
            var exit = new ToolStripMenuItem("退出"); exit.Click += delegate { BeginExit(); }; menu.Items.Add(exit);
            menu.Opening += delegate
            {
                cpu.Text = "CPU：" + (last == null ? "—" : last.CpuText);
                memory.Text = "内存：" + (last == null ? "—" : last.MemoryDetail);
                dockItem.Checked = settings.Dock; startup.Checked = Startup.Enabled;
            };
            return menu;
        }

        private void ShowSettings(string page = null)
        {
            if (stopping || settingsOpen) return;
            settingsOpen = true;
            try
            {
                using (var form = new SettingsForm(settings, page))
                {
                    activeSettings = form;
                    if (form.ShowDialog() == DialogResult.OK && !stopping && SaveSettings(form.Value)) { settings = form.Value; ApplySettings(); }
                }
            }
            catch (Exception error) { ReportError("设置未能应用，请重试。", error); }
            finally { settingsOpen = false; activeSettings = null; }
        }

        private bool SaveSettings(Settings value)
        {
            try { value.Save(Settings.DefaultPath); return true; }
            catch (Exception error) { ReportError("设置未保存，请检查配置目录是否可写。", error); return false; }
        }

        private void ApplySettings()
        {
            // Hiding first releases the reservation immediately.
            if (!settings.Dock) dock.Hide();
            dock.Apply(settings); details.Apply(settings);
            info.Reset();
            if (settings.Dock && !dock.Visible) dock.Show();
            samplingClock.Restart();
        }

        private void Tick()
        {
            if (stopping) return;
            if (exitSignal.WaitOne(0)) { BeginExit(); return; }
            if (showSignal.WaitOne(0)) ShowDetails();
            foreach (var action in actions)
            {
                if (stopping) return;
                if (action.Value.WaitOne(0)) { if (action.Key == "Settings") ShowSettings(); else ShowModule(action.Key); }
            }
            if (stopping) return;
            info.Pulse(settings);
            if (last != null && samplingClock.ElapsedMilliseconds < settings.Interval) return;
            samplingClock.Restart();
            try
            {
                Snapshot value = sampler.Sample();
                // An unavailable adapter must not blank the CPU and RAM readings.
                try { network.Sample(value, settings.NetworkId); }
                catch (Exception error) { Log.Error(error); value.NetworkName = "网络暂不可用"; }
                value.Battery = NetworkSampler.Battery();
                value.Briefing = info.Snapshot(settings);
                last = value; history.Add(value); dock.UpdateSnapshot(value); details.SetData(value, history);
                popup.UpdateData(settings, value);
                tray.Text = "CPU " + value.CpuText + " · 内存 " + value.MemoryText;
                using (Icon old = tray.Icon) tray.Icon = TrayIcon(value.Cpu, value.Memory);
            }
            catch (Exception error) { Log.Error(error); dock.UpdateSnapshot(null); }
        }

        private void ShowDetails()
        {
            if (stopping) return;
            if (details.WindowState == FormWindowState.Minimized) details.WindowState = FormWindowState.Normal;
            if (!details.Visible) details.Show();
            details.Activate();
        }
        private void ShowModule(string id)
        {
            if (id == "Codex" || id == "Calendar" || id == "Ip" || id == "Tracks") popup.OpenModule(id, settings, last);
            else ShowDetails();
        }
        private void ReportError(string message, Exception error)
        {
            Log.Error(error);
            if (tray != null && !stopping) tray.ShowBalloonTip(5000, "系统监控", message, ToolTipIcon.Warning);
        }
        private void BeginExit() { if (stopping) return; Cleanup(); ExitThread(); }
        private void Cleanup()
        {
            if (stopping) return;
            stopping = true;
            if (timer != null) { timer.Stop(); timer.Dispose(); }
            info.Dispose();
            if (activeSettings != null) { activeSettings.DialogResult = DialogResult.Cancel; activeSettings.Close(); }
            // Dispose the AppBar before anything else so Explorer gets ABM_REMOVE.
            if (dock != null) dock.Dispose();
            if (details != null) details.Dispose();
            if (popup != null) popup.Dispose();
            if (tray != null)
            {
                tray.Visible = false;
                var icon = tray.Icon; var menu = tray.ContextMenuStrip;
                tray.Dispose(); if (icon != null) icon.Dispose(); if (menu != null) menu.Dispose();
            }
            if (exitSignal != null) exitSignal.Dispose();
            if (showSignal != null) showSignal.Dispose();
            foreach (var action in actions.Values) action.Dispose();
        }
        protected override void Dispose(bool disposing) { if (disposing) Cleanup(); base.Dispose(disposing); }
        private static Color Shade(double? value) { return value >= 85 ? Color.FromArgb(225,84,91) : value >= 65 ? Color.FromArgb(220,159,64) : Color.FromArgb(49,183,220); }
        private static Icon TrayIcon(double? cpu, double? memory)
        {
            using (var bitmap = new Bitmap(16,16)) using (Graphics g = Graphics.FromImage(bitmap))
            using (var cpuBrush = new SolidBrush(Shade(cpu))) using (var memoryBrush = new SolidBrush(Shade(memory)))
            {
                g.Clear(Color.Transparent);
                int c = Math.Max(3, (int)Math.Round((cpu ?? 0) * 0.12));
                int m = Math.Max(3, (int)Math.Round((memory ?? 0) * 0.12));
                g.FillRectangle(cpuBrush, 2, 14 - c, 5, c); g.FillRectangle(memoryBrush, 9, 14 - m, 5, m);
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
    }
}
