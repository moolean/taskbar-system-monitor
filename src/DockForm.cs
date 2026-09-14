using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarSystemMonitor
{
    internal sealed class DockForm : Form
    {
        private readonly int callback, taskbarCreated;
        private bool registered, reservationSet, positioning, queued, disposing;
        private bool fullscreen;
        private Settings settings;
        private Snapshot snapshot;
        private Palette palette;
        private readonly ToolTip tooltip = new ToolTip();
        internal event EventHandler DetailsRequested, SettingsRequested, ExplorerRestarted;
        internal bool Registered { get { return registered; } }
        internal int LayoutChanges { get; private set; }

        internal DockForm(Settings configuration)
        {
            settings = configuration;
            palette = Palette.Current(settings.Theme);
            Text = "Taskbar System Monitor AppBar";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            MinimumSize = Size.Empty;
            Size = new Size(1, 1);
            callback = (int)Native.RegisterWindowMessage("moolean.TaskbarSystemMonitor.AppBar.2");
            taskbarCreated = (int)Native.RegisterWindowMessage("TaskbarCreated");
            SystemEvents.UserPreferenceChanged += ThemeChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x80 | 0x08000000; return p; }
        }
        internal void Apply(Settings value)
        {
            settings = value; palette = Palette.Current(settings.Theme);
            if (Visible) PositionBar();
            Invalidate();
        }
        internal void UpdateSnapshot(Snapshot value)
        {
            snapshot = value;
            string text = value == null ? "采样暂不可用，正在重试" : "CPU " + value.CpuText + "  内存 " + value.MemoryText + "\n" + value.MemoryDetail + "\n" + value.NetworkName + "  ↓" + Snapshot.Speed(value.RxKbps) + " ↑" + Snapshot.Speed(value.TxKbps);
            tooltip.SetToolTip(this, text + "\n右键或点击 ··· 打开设置");
            Invalidate();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (disposing || !IsHandleCreated) return;
            if (Visible) { Register(); PositionBar(); } else Unregister();
        }
        private void Register()
        {
            if (registered) return;
            var bar = Native.BarData(Handle); bar.Callback = (uint)callback;
            if (Native.SHAppBarMessage(Native.NewBar, ref bar) == UIntPtr.Zero)
                throw new InvalidOperationException("Windows 未能为资源栏分配空间。");
            registered = true;
            reservationSet = false;
        }
        private void Unregister()
        {
            if (!registered) return;
            var bar = Native.BarData(Handle);
            Native.SHAppBarMessage(Native.RemoveBar, ref bar);
            registered = false;
            reservationSet = false;
        }

        internal void PositionBar()
        {
            if (!registered || positioning || disposing) return;
            positioning = true;
            try
            {
                Rectangle monitor = Native.PrimaryMonitor().Monitor.Rectangle;
                var bar = Native.BarData(Handle);
                // Query from the monitor bounds, NOT its working area (which includes this appbar).
                // Reusing the working area would eat another row on each notification.
                bar.Edge = Native.BottomEdge;
                bar.Bounds = new Native.Rect(monitor);
                Native.SHAppBarMessage(Native.QueryPos, ref bar);
                int height = BarRenderer.Scale(settings.Height, Native.Dpi(Handle));
                bar.Bounds.Top = bar.Bounds.Bottom - height;
                Rectangle target = bar.Bounds.Rectangle;
                // A hidden-then-shown bar keeps its old Bounds but loses its Shell
                // reservation. Only skip SETPOS while that reservation still exists.
                if (reservationSet && target == Bounds) return;
                Native.SHAppBarMessage(Native.SetPos, ref bar);
                reservationSet = true;
                target = bar.Bounds.Rectangle;
                if (target.Width <= 0 || target.Height <= 0) throw new InvalidOperationException("Windows 返回了无效的资源栏位置。");
                Bounds = target;
                Native.SetWindowPos(Handle, fullscreen ? Native.Bottom : Native.Topmost, Left, Top, Width, Height, Native.NoActivate);
                LayoutChanges++;
            }
            finally { positioning = false; }
        }
        private void QueuePosition()
        {
            if (queued || positioning || disposing || !IsHandleCreated) return;
            queued = true;
            BeginInvoke(new MethodInvoker(delegate { queued = false; if (!disposing) PositionBar(); }));
        }
        private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (!IsHandleCreated || disposing) return;
            BeginInvoke(new MethodInvoker(delegate { if (!disposing) { palette = Palette.Current(settings.Theme); Invalidate(); } }));
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // Never steal focus on hover/click.
            if (m.Msg == callback)
            {
                if (m.WParam.ToInt32() == 1) QueuePosition();
                if (m.WParam.ToInt32() == 2)
                {
                    fullscreen = m.LParam != IntPtr.Zero;
                    Native.SetWindowPos(Handle, fullscreen ? Native.Bottom : Native.Topmost, 0, 0, 0, 0, Native.NoActivate | Native.NoMove | Native.NoSize);
                }
            }
            if (m.Msg == taskbarCreated && Visible)
            {
                registered = false;
                Register();
                Bounds = new Rectangle(0, 0, 1, 1);
                QueuePosition();
                if (ExplorerRestarted != null) ExplorerRestarted(this, EventArgs.Empty);
            }
            if (m.Msg == 0x7E || m.Msg == 0x2E0) QueuePosition();
            if (m.Msg == 0x2E0) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
            if (registered && !positioning && !disposing && (m.Msg == 0x47 || m.Msg == 6))
            {
                var bar = Native.BarData(Handle);
                Native.SHAppBarMessage(m.Msg == 6 ? Native.ActivateBar : Native.WindowPosChanged, ref bar);
            }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Right || e.X >= Width - BarRenderer.Scale(30, Native.Dpi(Handle)))
            { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); }
            else if (e.Button == MouseButtons.Left && DetailsRequested != null) DetailsRequested(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e) { BarRenderer.Draw(e.Graphics, ClientRectangle, settings, snapshot, palette, Native.Dpi(Handle)); }
        protected override void OnHandleDestroyed(EventArgs e) { Unregister(); base.OnHandleDestroyed(e); }
        protected override void Dispose(bool value)
        {
            if (value && !disposing)
            {
                disposing = true;
                SystemEvents.UserPreferenceChanged -= ThemeChanged;
                Unregister();
                tooltip.Dispose();
            }
            base.Dispose(value);
        }
    }
}
