using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyDescription("A lightweight Windows tray CPU and memory monitor")]
[assembly: System.Reflection.AssemblyCompany("moolean")]
[assembly: System.Reflection.AssemblyProduct("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © moolean")]
[assembly: System.Reflection.AssemblyVersion("1.2.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.2.0")]

namespace TaskbarSystemMonitor
{
    internal static class Program
    {
        private const string MutexName = "Local\\TaskbarSystemMonitor.moolean";

        [STAThread]
        private static int Main(string[] args)
        {
            if (HasArgument(args, "--self-test"))
            {
                return RunSelfTest();
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                TryEnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool startedByWindows = HasArgument(args, "--startup");
                Application.Run(new TrayMonitorContext(startedByWindows));
                return 0;
            }
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string argument in args)
            {
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int RunSelfTest()
        {
            try
            {
                var sampler = new SystemStatsSampler();
                sampler.Sample();
                Thread.Sleep(300);
                SystemSnapshot snapshot = sampler.Sample();

                if (snapshot.CpuPercent < 0 || snapshot.CpuPercent > 100 ||
                    snapshot.MemoryPercent < 0 || snapshot.MemoryPercent > 100 ||
                    snapshot.TotalMemoryBytes == 0)
                {
                    return 2;
                }

                return 0;
            }
            catch
            {
                return 3;
            }
        }

        private static void TryEnableDpiAwareness()
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Older systems may not expose this API. The app can safely continue.
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }

    internal sealed class TrayMonitorContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly SystemStatsSampler sampler;
        private readonly ToolStripMenuItem cpuItem;
        private readonly ToolStripMenuItem memoryItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem widgetItem;
        private readonly ToolStripMenuItem transparentBackgroundItem;
        private readonly ToolStripMenuItem systemBackgroundItem;
        private readonly StartupManager startupManager;
        private readonly TaskbarWidgetForm taskbarWidget;
        private MonitorForm monitorForm;
        private Icon currentIcon;
        private SystemSnapshot latestSnapshot;
        private bool isExiting;

        public TrayMonitorContext(bool startedByWindows)
        {
            sampler = new SystemStatsSampler();
            startupManager = new StartupManager();

            cpuItem = new ToolStripMenuItem("CPU：正在采样…");
            cpuItem.Enabled = false;
            memoryItem = new ToolStripMenuItem("内存：正在采样…");
            memoryItem.Enabled = false;

            startupItem = new ToolStripMenuItem("开机自动启动");
            startupItem.CheckOnClick = true;
            startupItem.Checked = startupManager.IsEnabled();
            startupItem.Click += ToggleStartup;

            widgetItem = new ToolStripMenuItem("在任务栏直接显示数值");
            widgetItem.CheckOnClick = true;
            widgetItem.Checked = true;
            widgetItem.Click += ToggleTaskbarWidget;

            transparentBackgroundItem = new ToolStripMenuItem("透明背景");
            transparentBackgroundItem.Click += delegate
            {
                SetWidgetBackgroundMode(WidgetBackgroundMode.Transparent);
            };

            systemBackgroundItem = new ToolStripMenuItem("跟随系统深浅色");
            systemBackgroundItem.Click += delegate
            {
                SetWidgetBackgroundMode(WidgetBackgroundMode.System);
            };

            var backgroundMenuItem = new ToolStripMenuItem("任务栏背景");
            backgroundMenuItem.DropDownItems.Add(transparentBackgroundItem);
            backgroundMenuItem.DropDownItems.Add(systemBackgroundItem);

            var openItem = new ToolStripMenuItem("打开监控面板");
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            openItem.Click += delegate { ShowMonitorWindow(); };

            var taskManagerItem = new ToolStripMenuItem("打开任务管理器");
            taskManagerItem.Click += delegate { StartSystemTool("taskmgr.exe"); };

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApplication(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(cpuItem);
            menu.Items.Add(memoryItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(taskManagerItem);
            menu.Items.Add(widgetItem);
            menu.Items.Add(backgroundMenuItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Text = "CPU / RAM 监控";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { ShowMonitorWindow(); };

            taskbarWidget = new TaskbarWidgetForm();
            taskbarWidget.ContextMenuStrip = menu;
            taskbarWidget.DetailsRequested += delegate { ShowMonitorWindow(); };
            SetWidgetBackgroundMode(WidgetPreferences.LoadBackgroundMode(), false);
            taskbarWidget.Show();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += delegate { UpdateStats(); };

            sampler.Sample();
            UpdateStats();
            timer.Start();

            if (!startupItem.Checked)
            {
                if (startupManager.SetEnabled(true))
                {
                    startupItem.Checked = true;
                }
            }

            if (!startedByWindows)
            {
                trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                trayIcon.BalloonTipTitle = "系统监控已启动";
                trayIcon.BalloonTipText = "CPU 与内存状态会显示在任务栏通知区。双击图标可打开面板。";
                trayIcon.ShowBalloonTip(2500);
            }
        }

        private void UpdateStats()
        {
            latestSnapshot = sampler.Sample();

            cpuItem.Text = string.Format("CPU：{0:0}%", latestSnapshot.CpuPercent);
            memoryItem.Text = string.Format(
                "内存：{0:0}%（{1:0.0} / {2:0.0} GB）",
                latestSnapshot.MemoryPercent,
                latestSnapshot.UsedMemoryGigabytes,
                latestSnapshot.TotalMemoryGigabytes);

            trayIcon.Text = TruncateTooltip(string.Format(
                "CPU {0:0}% | RAM {1:0}% ({2:0.0}/{3:0.0} GB)",
                latestSnapshot.CpuPercent,
                latestSnapshot.MemoryPercent,
                latestSnapshot.UsedMemoryGigabytes,
                latestSnapshot.TotalMemoryGigabytes));

            Icon nextIcon = TrayIconRenderer.Create(
                latestSnapshot.CpuPercent,
                latestSnapshot.MemoryPercent);
            trayIcon.Icon = nextIcon;

            if (currentIcon != null)
            {
                currentIcon.Dispose();
            }
            currentIcon = nextIcon;

            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                monitorForm.UpdateSnapshot(latestSnapshot);
            }

            if (widgetItem.Checked)
            {
                taskbarWidget.UpdateSnapshot(latestSnapshot);
                taskbarWidget.EnsureTaskbarPosition();
            }
        }

        private static string TruncateTooltip(string value)
        {
            return value.Length <= 63 ? value : value.Substring(0, 63);
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            bool requested = startupItem.Checked;
            if (!startupManager.SetEnabled(requested))
            {
                startupItem.Checked = !requested;
                MessageBox.Show(
                    "无法更新开机启动设置。请确认当前用户可以写入注册表启动项。",
                    "Taskbar System Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ToggleTaskbarWidget(object sender, EventArgs e)
        {
            if (widgetItem.Checked)
            {
                taskbarWidget.Show();
                taskbarWidget.UpdateSnapshot(latestSnapshot);
                taskbarWidget.EnsureTaskbarPosition();
            }
            else
            {
                taskbarWidget.Hide();
            }
        }

        private void SetWidgetBackgroundMode(WidgetBackgroundMode mode)
        {
            SetWidgetBackgroundMode(mode, true);
        }

        private void SetWidgetBackgroundMode(WidgetBackgroundMode mode, bool save)
        {
            taskbarWidget.BackgroundMode = mode;
            transparentBackgroundItem.Checked = mode == WidgetBackgroundMode.Transparent;
            systemBackgroundItem.Checked = mode == WidgetBackgroundMode.System;

            if (save)
            {
                WidgetPreferences.SaveBackgroundMode(mode);
            }
        }

        private void ShowMonitorWindow()
        {
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                monitorForm = new MonitorForm();
                monitorForm.FormClosed += delegate { monitorForm = null; };
            }

            monitorForm.UpdateSnapshot(latestSnapshot);
            monitorForm.Show();
            monitorForm.WindowState = FormWindowState.Normal;
            monitorForm.Activate();
        }

        private static void StartSystemTool(string fileName)
        {
            try
            {
                Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "无法打开系统工具：" + ex.Message,
                    "Taskbar System Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ExitApplication()
        {
            if (isExiting)
            {
                return;
            }

            isExiting = true;
            timer.Stop();
            trayIcon.Visible = false;

            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                monitorForm.Close();
            }

            taskbarWidget.Close();
            trayIcon.Dispose();
            if (currentIcon != null)
            {
                currentIcon.Dispose();
            }

            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !isExiting)
            {
                ExitApplication();
            }

            base.Dispose(disposing);
        }
    }

    internal enum WidgetBackgroundMode
    {
        Transparent,
        System
    }

    internal sealed class TaskbarWidgetForm : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly Color TransparentKeyColor = Color.FromArgb(1, 2, 3);

        private readonly Font labelFont;
        private readonly Font valueFont;
        private SystemSnapshot snapshot;
        private WidgetBackgroundMode backgroundMode;
        private bool systemUsesLightTheme;

        public TaskbarWidgetForm()
        {
            Text = "Taskbar System Monitor";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ForeColor = Color.White;
            TopMost = true;
            DoubleBuffered = true;
            backgroundMode = WidgetBackgroundMode.Transparent;
            systemUsesLightTheme = ReadSystemLightTheme();

            labelFont = new Font(
                "Segoe UI Variable Text",
                9F,
                FontStyle.Regular,
                GraphicsUnit.Point);
            valueFont = new Font(
                "Segoe UI Variable Text Semibold",
                10F,
                FontStyle.Regular,
                GraphicsUnit.Point);

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            MouseClick += HandleMouseClick;
            SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
            ApplyBackgroundMode();
        }

        public event EventHandler DetailsRequested;

        public WidgetBackgroundMode BackgroundMode
        {
            get { return backgroundMode; }
            set
            {
                if (backgroundMode != value)
                {
                    backgroundMode = value;
                    ApplyBackgroundMode();
                }
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }

        public void UpdateSnapshot(SystemSnapshot nextSnapshot)
        {
            snapshot = nextSnapshot;
            Invalidate();
        }

        public void EnsureTaskbarPosition()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero)
            {
                return;
            }

            RECT taskbarBounds;
            if (!GetWindowRect(taskbar, out taskbarBounds))
            {
                return;
            }

            int taskbarWidth = taskbarBounds.Right - taskbarBounds.Left;
            int taskbarHeight = taskbarBounds.Bottom - taskbarBounds.Top;
            if (taskbarWidth <= 4 || taskbarHeight <= 4)
            {
                return;
            }

            bool horizontal = taskbarWidth >= taskbarHeight;
            int x;
            int y;
            int width;
            int height;

            if (horizontal)
            {
                width = Math.Min(216, Math.Max(156, taskbarWidth / 3));
                height = Math.Min(34, Math.Max(28, taskbarHeight - 6));
                y = taskbarBounds.Top + Math.Max(2, (taskbarHeight - height) / 2);

                int notificationLeft = FindNotificationAreaLeft(taskbar, taskbarBounds);
                x = notificationLeft - width - 8;
                x = Math.Max(taskbarBounds.Left + 4, x);
                x = Math.Min(x, taskbarBounds.Right - width - 4);
            }
            else
            {
                width = Math.Max(34, taskbarWidth - 6);
                height = 70;
                x = taskbarBounds.Left + Math.Max(2, (taskbarWidth - width) / 2);

                int notificationTop = FindNotificationAreaTop(taskbar, taskbarBounds);
                y = notificationTop - height - 8;
                y = Math.Max(taskbarBounds.Top + 4, y);
                y = Math.Min(y, taskbarBounds.Bottom - height - 4);
            }

            if (Bounds.X != x || Bounds.Y != y || Bounds.Width != width || Bounds.Height != height)
            {
                Bounds = new Rectangle(x, y, width, height);
            }

            SetWindowPos(
                Handle,
                HwndTopmost,
                x,
                y,
                width,
                height,
                SwpNoActivate | SwpShowWindow);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = backgroundMode == WidgetBackgroundMode.Transparent
                ? SmoothingMode.None
                : SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = backgroundMode == WidgetBackgroundMode.Transparent
                ? System.Drawing.Text.TextRenderingHint.AntiAliasGridFit
                : System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(background, ClientRectangle);
            }

            int gap = 8;
            int padding = 2;
            int itemWidth = Math.Max(1, (ClientSize.Width - gap - padding * 2) / 2);
            Rectangle cpuBounds = new Rectangle(padding, padding, itemWidth, ClientSize.Height - padding * 2);
            Rectangle memoryBounds = new Rectangle(
                padding + itemWidth + gap,
                padding,
                itemWidth,
                ClientSize.Height - padding * 2);

            double cpu = snapshot == null ? 0 : snapshot.CpuPercent;
            double memory = snapshot == null ? 0 : snapshot.MemoryPercent;

            DrawMetric(e.Graphics, cpuBounds, "CPU", cpu, GetCpuColor(cpu));
            DrawMetric(e.Graphics, memoryBounds, "RAM", memory, GetMemoryColor(memory));
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmMouseActivate)
            {
                message.Result = new IntPtr(MaNoActivate);
                return;
            }

            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
                labelFont.Dispose();
                valueFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private void HandleMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                EventHandler handler = DetailsRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void HandleUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(ApplyBackgroundMode));
                return;
            }

            ApplyBackgroundMode();
        }

        private void ApplyBackgroundMode()
        {
            systemUsesLightTheme = ReadSystemLightTheme();

            if (backgroundMode == WidgetBackgroundMode.Transparent)
            {
                BackColor = TransparentKeyColor;
                TransparencyKey = TransparentKeyColor;
                Opacity = 1.0;
            }
            else
            {
                TransparencyKey = Color.Empty;
                BackColor = systemUsesLightTheme
                    ? Color.FromArgb(243, 243, 243)
                    : Color.FromArgb(32, 32, 32);
                Opacity = 0.96;
            }

            Invalidate();
        }

        private void DrawMetric(
            Graphics graphics,
            Rectangle bounds,
            string label,
            double percent,
            Color accent)
        {
            Color labelColor = systemUsesLightTheme
                ? Color.FromArgb(73, 78, 88)
                : Color.FromArgb(205, 211, 222);
            Color valueColor = systemUsesLightTheme
                ? Color.FromArgb(20, 23, 29)
                : Color.White;
            using (var accentBrush = new SolidBrush(accent))
            using (var labelBrush = new SolidBrush(labelColor))
            using (var valueBrush = new SolidBrush(valueColor))
            {
                graphics.FillRectangle(
                    accentBrush,
                    bounds.Left,
                    bounds.Top + Math.Max(2, (bounds.Height - 16) / 2),
                    3,
                    Math.Min(16, bounds.Height - 4));

                int centerY = bounds.Top + bounds.Height / 2;
                float labelX = bounds.Left + 9;
                float labelY = centerY - labelFont.Height / 2;
                graphics.DrawString(label, labelFont, labelBrush, labelX, labelY);

                string value = string.Format("{0:0}%", percent);
                SizeF labelSize = graphics.MeasureString(label, labelFont);
                SizeF valueSize = graphics.MeasureString(value, valueFont);
                float valueX = labelX + labelSize.Width + 6;
                if (valueX + valueSize.Width > bounds.Right - 2)
                {
                    valueX = bounds.Right - valueSize.Width - 2;
                }
                float valueY = centerY - valueFont.Height / 2 - 1;

                graphics.DrawString(
                    value,
                    valueFont,
                    valueBrush,
                    valueX,
                    valueY);
            }
        }

        private static bool ReadSystemLightTheme()
        {
            const string themePath =
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(themePath, false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("SystemUsesLightTheme");
                        if (value == null)
                        {
                            value = key.GetValue("AppsUseLightTheme");
                        }

                        if (value != null)
                        {
                            return Convert.ToInt32(value) != 0;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to the current system color brightness.
            }

            return SystemColors.Window.GetBrightness() >= 0.5F;
        }

        private static Color GetCpuColor(double percent)
        {
            if (percent >= 85)
            {
                return Color.FromArgb(255, 92, 92);
            }
            if (percent >= 65)
            {
                return Color.FromArgb(255, 184, 77);
            }
            return Color.FromArgb(44, 207, 255);
        }

        private static Color GetMemoryColor(double percent)
        {
            if (percent >= 85)
            {
                return Color.FromArgb(255, 92, 92);
            }
            if (percent >= 70)
            {
                return Color.FromArgb(255, 184, 77);
            }
            return Color.FromArgb(190, 94, 255);
        }

        private static int FindNotificationAreaLeft(IntPtr taskbar, RECT fallback)
        {
            IntPtr notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT bounds;
            if (notificationArea != IntPtr.Zero && GetWindowRect(notificationArea, out bounds))
            {
                return bounds.Left;
            }

            return fallback.Right - Math.Min(240, Math.Max(120, (fallback.Right - fallback.Left) / 6));
        }

        private static int FindNotificationAreaTop(IntPtr taskbar, RECT fallback)
        {
            IntPtr notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT bounds;
            if (notificationArea != IntPtr.Zero && GetWindowRect(notificationArea, out bounds))
            {
                return bounds.Top;
            }

            return fallback.Bottom - Math.Min(160, Math.Max(90, (fallback.Bottom - fallback.Top) / 4));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(
            IntPtr parentHandle,
            IntPtr childAfter,
            string className,
            string windowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr windowHandle, out RECT bounds);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }

    internal static class WidgetPreferences
    {
        private const string RegistryPath = @"Software\moolean\TaskbarSystemMonitor";
        private const string BackgroundModeValue = "BackgroundMode";

        public static WidgetBackgroundMode LoadBackgroundMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null)
                    {
                        return WidgetBackgroundMode.Transparent;
                    }

                    string value = key.GetValue(BackgroundModeValue) as string;
                    WidgetBackgroundMode mode;
                    if (Enum.TryParse(value, true, out mode))
                    {
                        return mode;
                    }
                }
            }
            catch
            {
                // Use the transparent default when preferences cannot be read.
            }

            return WidgetBackgroundMode.Transparent;
        }

        public static void SaveBackgroundMode(WidgetBackgroundMode mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        key.SetValue(
                            BackgroundModeValue,
                            mode.ToString(),
                            RegistryValueKind.String);
                    }
                }
            }
            catch
            {
                // Visual preferences are best-effort and must not stop monitoring.
            }
        }
    }

    internal sealed class MonitorForm : Form
    {
        private readonly Label cpuValue;
        private readonly Label memoryValue;
        private readonly Label memoryDetail;
        private readonly SmoothProgressBar cpuBar;
        private readonly SmoothProgressBar memoryBar;
        private readonly Label statusLabel;

        public MonitorForm()
        {
            Text = "CPU 与内存监控";
            ClientSize = new Size(420, 255);
            MinimumSize = new Size(380, 250);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(18, 22, 30);
            ForeColor = Color.FromArgb(235, 240, 248);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = true;

            var title = new Label();
            title.Text = "系统资源";
            title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(24, 20);
            Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "每秒刷新 · 托盘常驻";
            subtitle.AutoSize = true;
            subtitle.ForeColor = Color.FromArgb(135, 148, 166);
            subtitle.Location = new Point(27, 58);
            Controls.Add(subtitle);

            var cpuLabel = CreateMetricLabel("CPU", 25, 92);
            Controls.Add(cpuLabel);

            cpuValue = CreateValueLabel(322, 87);
            Controls.Add(cpuValue);

            cpuBar = new SmoothProgressBar();
            cpuBar.Location = new Point(25, 116);
            cpuBar.Size = new Size(370, 12);
            cpuBar.BarColor = Color.FromArgb(44, 207, 255);
            Controls.Add(cpuBar);

            var ramLabel = CreateMetricLabel("内存", 25, 148);
            Controls.Add(ramLabel);

            memoryValue = CreateValueLabel(322, 143);
            Controls.Add(memoryValue);

            memoryBar = new SmoothProgressBar();
            memoryBar.Location = new Point(25, 172);
            memoryBar.Size = new Size(370, 12);
            memoryBar.BarColor = Color.FromArgb(190, 94, 255);
            Controls.Add(memoryBar);

            memoryDetail = new Label();
            memoryDetail.AutoSize = true;
            memoryDetail.ForeColor = Color.FromArgb(160, 171, 188);
            memoryDetail.Location = new Point(25, 194);
            Controls.Add(memoryDetail);

            statusLabel = new Label();
            statusLabel.Text = "● 正在监控";
            statusLabel.AutoSize = true;
            statusLabel.ForeColor = Color.FromArgb(83, 218, 150);
            statusLabel.Location = new Point(305, 220);
            Controls.Add(statusLabel);

            FormClosing += HandleFormClosing;
        }

        public void UpdateSnapshot(SystemSnapshot snapshot)
        {
            if (IsDisposed)
            {
                return;
            }

            int cpu = ClampToPercent(snapshot.CpuPercent);
            int memory = ClampToPercent(snapshot.MemoryPercent);

            cpuValue.Text = cpu + "%";
            memoryValue.Text = memory + "%";
            cpuBar.Value = cpu;
            memoryBar.Value = memory;
            memoryDetail.Text = string.Format(
                "已使用 {0:0.0} GB，共 {1:0.0} GB",
                snapshot.UsedMemoryGigabytes,
                snapshot.TotalMemoryGigabytes);
        }

        private static int ClampToPercent(double value)
        {
            return Math.Max(0, Math.Min(100, (int)Math.Round(value)));
        }

        private Label CreateMetricLabel(string text, int x, int y)
        {
            var label = new Label();
            label.Text = text;
            label.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            label.AutoSize = true;
            label.Location = new Point(x, y);
            return label;
        }

        private Label CreateValueLabel(int x, int y)
        {
            var label = new Label();
            label.Text = "0%";
            label.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            label.ForeColor = Color.White;
            label.TextAlign = ContentAlignment.MiddleRight;
            label.Size = new Size(73, 28);
            label.Location = new Point(x, y);
            return label;
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }
    }

    internal sealed class SmoothProgressBar : Control
    {
        private int value;
        private Color barColor;

        public SmoothProgressBar()
        {
            DoubleBuffered = true;
            value = 0;
            barColor = Color.DodgerBlue;
            BackColor = Color.FromArgb(42, 49, 61);
        }

        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (this.value != next)
                {
                    this.value = next;
                    Invalidate();
                }
            }
        }

        public Color BarColor
        {
            get { return barColor; }
            set
            {
                barColor = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var backgroundPath = RoundedRectangle(bounds, Height / 2))
            using (var backgroundBrush = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(backgroundBrush, backgroundPath);
            }

            int fillWidth = (int)Math.Round((Width - 1) * (value / 100.0));
            if (fillWidth > 0)
            {
                Rectangle fillBounds = new Rectangle(0, 0, Math.Max(1, fillWidth), Math.Max(1, Height - 1));
                using (var fillPath = RoundedRectangle(fillBounds, Height / 2))
                using (var fillBrush = new SolidBrush(barColor))
                {
                    e.Graphics.FillPath(fillBrush, fillPath);
                }
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class TrayIconRenderer
    {
        public static Icon Create(double cpuPercent, double memoryPercent)
        {
            using (var bitmap = new Bitmap(16, 16))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(23, 28, 37));
                graphics.SmoothingMode = SmoothingMode.None;

                DrawMeter(graphics, 2, cpuPercent, Color.FromArgb(44, 207, 255));
                DrawMeter(graphics, 9, memoryPercent, Color.FromArgb(190, 94, 255));

                IntPtr iconHandle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(iconHandle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(iconHandle);
                }
            }
        }

        private static void DrawMeter(Graphics graphics, int x, double percent, Color color)
        {
            const int meterWidth = 5;
            const int meterHeight = 12;
            const int top = 2;

            using (var borderPen = new Pen(Color.FromArgb(90, 104, 122)))
            {
                graphics.DrawRectangle(borderPen, x, top, meterWidth, meterHeight);
            }

            int innerHeight = meterHeight - 2;
            int fillHeight = (int)Math.Round(innerHeight * Math.Max(0, Math.Min(100, percent)) / 100.0);
            if (fillHeight > 0)
            {
                using (var brush = new SolidBrush(color))
                {
                    graphics.FillRectangle(
                        brush,
                        x + 1,
                        top + meterHeight - fillHeight,
                        meterWidth - 1,
                        fillHeight);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }

    internal sealed class StartupManager
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TaskbarSystemMonitor";

        public bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    string configured = key.GetValue(ValueName) as string;
                    string expected = BuildCommand();
                    return string.Equals(configured, expected, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildCommand()
        {
            return "\"" + Application.ExecutablePath + "\" --startup";
        }
    }

    internal sealed class SystemStatsSampler
    {
        private ulong previousIdle;
        private ulong previousKernel;
        private ulong previousUser;
        private bool hasPreviousSample;

        public SystemSnapshot Sample()
        {
            FILETIME idleTime;
            FILETIME kernelTime;
            FILETIME userTime;

            if (!GetSystemTimes(out idleTime, out kernelTime, out userTime))
            {
                throw new InvalidOperationException("GetSystemTimes failed.");
            }

            ulong idle = ToUInt64(idleTime);
            ulong kernel = ToUInt64(kernelTime);
            ulong user = ToUInt64(userTime);
            double cpu = 0;

            if (hasPreviousSample)
            {
                ulong idleDelta = idle - previousIdle;
                ulong kernelDelta = kernel - previousKernel;
                ulong userDelta = user - previousUser;
                ulong totalDelta = kernelDelta + userDelta;

                if (totalDelta > 0)
                {
                    cpu = (totalDelta - idleDelta) * 100.0 / totalDelta;
                }
            }

            previousIdle = idle;
            previousKernel = kernel;
            previousUser = user;
            hasPreviousSample = true;

            var memory = new MEMORYSTATUSEX();
            memory.Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref memory))
            {
                throw new InvalidOperationException("GlobalMemoryStatusEx failed.");
            }

            ulong usedBytes = memory.TotalPhysical - memory.AvailablePhysical;
            double memoryPercent = memory.TotalPhysical == 0
                ? 0
                : usedBytes * 100.0 / memory.TotalPhysical;

            return new SystemSnapshot(
                Math.Max(0, Math.Min(100, cpu)),
                Math.Max(0, Math.Min(100, memoryPercent)),
                usedBytes,
                memory.TotalPhysical);
        }

        private static ulong ToUInt64(FILETIME time)
        {
            return ((ulong)time.HighDateTime << 32) | time.LowDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint LowDateTime;
            public uint HighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out FILETIME idleTime,
            out FILETIME kernelTime,
            out FILETIME userTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    }

    internal sealed class SystemSnapshot
    {
        private const double BytesPerGigabyte = 1024.0 * 1024.0 * 1024.0;

        public SystemSnapshot(
            double cpuPercent,
            double memoryPercent,
            ulong usedMemoryBytes,
            ulong totalMemoryBytes)
        {
            CpuPercent = cpuPercent;
            MemoryPercent = memoryPercent;
            UsedMemoryBytes = usedMemoryBytes;
            TotalMemoryBytes = totalMemoryBytes;
        }

        public double CpuPercent { get; private set; }
        public double MemoryPercent { get; private set; }
        public ulong UsedMemoryBytes { get; private set; }
        public ulong TotalMemoryBytes { get; private set; }

        public double UsedMemoryGigabytes
        {
            get { return UsedMemoryBytes / BytesPerGigabyte; }
        }

        public double TotalMemoryGigabytes
        {
            get { return TotalMemoryBytes / BytesPerGigabyte; }
        }
    }
}
