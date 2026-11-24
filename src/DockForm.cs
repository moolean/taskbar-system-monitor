using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarSystemMonitor
{
    internal delegate UIntPtr AppBarMessage(uint message, ref Native.AppBarData data);

    internal sealed class DockForm : Form
    {
        private readonly int callback, taskbarCreated;
        private readonly AppBarMessage appBarMessage;
        private readonly Func<IntPtr> findTaskbar;
        private readonly Timer shellRetry = new Timer { Interval = 2000 };
        private IntPtr registeredTaskbar;
        private bool shellResetPending, shellWaiting;
        private bool registered, reservationSet, positioning, queued, disposing;
        private bool fullscreen;
        private Settings settings;
        private Snapshot snapshot;
        private Palette palette;
        private string hovered;
        private int hoveredWork = int.MinValue;
        private int dragIndex = -1, dropSlot = -1;
        private Point dragStart;
        private bool draggingWork, suppressWorkClick, hasPendingSnapshot;
        private Snapshot pendingSnapshot;
        internal bool WorkDragActive { get { return draggingWork; } }
        internal int WorkDropPosition { get { return dropSlot; } }
        internal bool InspectionSample;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly ContextMenuStrip barMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false };
        internal ContextMenuStrip BarMenu { get { return barMenu; } }
        internal event EventHandler DetailsRequested, SettingsRequested, ExplorerRestarted;
        internal event Action<string> ModuleRequested;
        internal event Action<int> WorkRequested;
        internal event Action<int, int> WorkReorderRequested;
        internal bool Registered { get { return registered; } }
        internal bool WaitingForShell { get { return shellRetry.Enabled && !registered; } }
        internal int LayoutChanges { get; private set; }

        internal DockForm(Settings configuration) : this(configuration, Native.SHAppBarMessage, delegate { return Native.FindWindow("Shell_TrayWnd", null); }) { }
        internal DockForm(Settings configuration, AppBarMessage shellMessage, Func<IntPtr> taskbarWindow)
        {
            appBarMessage = shellMessage; findTaskbar = taskbarWindow;
            shellRetry.Tick += delegate
            {
                if (!registered || shellResetPending || findTaskbar() != registeredTaskbar) PositionBar();
            };
            settings = configuration;
            palette = Palette.Current(settings);
            Text = BarReadiness.Caption;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = settings.AlwaysOnTop;
            DoubleBuffered = true;
            MinimumSize = Size.Empty;
            Size = new Size(1, 1);
            barMenu.Items.Add("Work notes", null, delegate { if (WorkRequested != null) WorkRequested(-2); }).Name = "work";
            barMenu.Items.Add("Resource details", null, delegate { if (DetailsRequested != null) DetailsRequested(this, EventArgs.Empty); }).Name = "details";
            barMenu.Items.Add(new ToolStripSeparator());
            barMenu.Items.Add("Settings…", null, delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); }).Name = "settings";
            barMenu.Opening += delegate
            {
                var c = SettingsColors.From(settings);
                barMenu.Renderer = SystemInformation.HighContrast ? (ToolStripRenderer)new ToolStripSystemRenderer() : new ToolStripProfessionalRenderer(new BarMenuColors(c));
                barMenu.BackColor = c.Background; barMenu.ForeColor = c.Text;
                foreach (ToolStripItem item in barMenu.Items) item.ForeColor = c.Text;
            };
            callback = (int)Native.RegisterWindowMessage("moolean.TaskbarSystemMonitor.AppBar.2");
            taskbarCreated = (int)Native.RegisterWindowMessage("TaskbarCreated");
            SystemEvents.UserPreferenceChanged += ThemeChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= (InspectionSample ? 0 : 0x80) | 0x08000000; return p; }
        }
        internal void Apply(Settings value)
        {
            CancelWorkDrag();
            settings = value; palette = Palette.Current(settings);
            ApplyZOrder(false);
            if (Visible) PositionBar();
            Invalidate();
        }
        internal void UpdateSnapshot(Snapshot value)
        {
            // Keep hit targets stable while the mouse is held down. New samples
            // are applied after release; changed keyword lists cancel the drop.
            if (dragIndex >= 0) { pendingSnapshot = value; hasPendingSnapshot = true; return; }
            snapshot = value;
            string text = value == null ? "Sampling unavailable. Retrying…" : "CPU " + value.CpuText + "  RAM " + value.MemoryText + "\n" + value.MemoryDetail + "\n" + value.NetworkName + "  ↓" + Snapshot.Speed(value.RxKbps) + " ↑" + Snapshot.Speed(value.TxKbps);
            tooltip.SetToolTip(this, hovered == "City" && value != null ? value.Briefing.Location.FullDetails :
                text + "\nRight-click for menu · Click ··· for settings");
            Invalidate();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (disposing || !IsHandleCreated) return;
            if (Visible) { shellRetry.Start(); PositionBar(); }
            else { shellRetry.Stop(); shellResetPending = false; Unregister(); }
        }
        private bool Register(IntPtr taskbar)
        {
            if (registered) return true;
            var bar = Native.BarData(Handle); bar.Callback = (uint)callback;
            if (appBarMessage(Native.NewBar, ref bar) == UIntPtr.Zero) return false;
            registered = true;
            registeredTaskbar = taskbar;
            reservationSet = false;
            return true;
        }
        private void Unregister()
        {
            if (!registered) return;
            // Clear local state before Shell can send synchronous callbacks.
            registered = false; reservationSet = false; registeredTaskbar = IntPtr.Zero;
            var bar = Native.BarData(Handle);
            appBarMessage(Native.RemoveBar, ref bar);
        }
        private void WaitForShell()
        {
            Unregister();
            // Do not cover another app while there is no valid reservation.
            Bounds = new Rectangle(-32000, -32000, 1, 1);
            if (!shellWaiting) Log.Info("AppBar waiting for Explorer; retrying every 2 seconds.");
            shellWaiting = true;
        }

        internal void PositionBar()
        {
            if (!Visible || !IsHandleCreated || positioning || disposing) return;
            positioning = true;
            try
            {
                IntPtr taskbar = findTaskbar();
                if (shellResetPending || (registered && taskbar != registeredTaskbar))
                {
                    // TaskbarCreated may be repeated even when the old Shell
                    // registration still exists. Remove only our own HWND first.
                    Unregister(); shellResetPending = false;
                }
                if (taskbar == IntPtr.Zero || !Register(taskbar)) { WaitForShell(); return; }
                Rectangle monitor = Native.PrimaryMonitor().Monitor.Rectangle;
                var bar = Native.BarData(Handle);
                // Query from the monitor bounds, NOT its working area (which includes this appbar).
                // Reusing the working area would eat another row on each notification.
                bar.Edge = Native.BottomEdge;
                bar.Bounds = new Native.Rect(monitor);
                if (appBarMessage(Native.QueryPos, ref bar) == UIntPtr.Zero) { WaitForShell(); return; }
                int height = BarRenderer.Scale(settings.Height, Native.Dpi(Handle));
                bar.Bounds.Top = bar.Bounds.Bottom - height;
                Rectangle target = bar.Bounds.Rectangle;
                // A hidden-then-shown bar keeps its old Bounds but loses its Shell
                // reservation. Only skip SETPOS while that reservation still exists.
                if (reservationSet && target == Bounds) { ApplyZOrder(false); return; }
                if (appBarMessage(Native.SetPos, ref bar) == UIntPtr.Zero) { WaitForShell(); return; }
                target = bar.Bounds.Rectangle;
                if (target.Width <= 0 || target.Height <= 0) { WaitForShell(); return; }
                reservationSet = true;
                Bounds = target;
                Native.SetWindowPos(Handle, IntPtr.Zero, Left, Top, Width, Height, Native.NoActivate | Native.NoZOrder);
                ApplyZOrder(false);
                LayoutChanges++;
                if (shellWaiting) Log.Info("AppBar recovered; desktop space reserved.");
                shellWaiting = false;
            }
            catch (System.ComponentModel.Win32Exception error) { Log.Error(error); WaitForShell(); }
            finally { positioning = false; }
        }
        private void ApplyZOrder(bool force)
        {
            if (!IsHandleCreated || disposing) return;
            bool desired = settings.AlwaysOnTop && !fullscreen;
            if (!force && Native.IsTopmost(Handle) == desired) return;
            TopMost = desired;
            Native.SetWindowPos(Handle, fullscreen ? Native.Bottom : desired ? Native.Topmost : Native.NotTopmost, 0, 0, 0, 0, Native.NoActivate | Native.NoMove | Native.NoSize);
        }
        internal void SetFullscreenState(bool active) { fullscreen = active; ApplyZOrder(true); }
        private void QueuePosition()
        {
            if (queued || positioning || disposing || !IsHandleCreated || (shellWaiting && !shellResetPending)) return;
            queued = true;
            BeginInvoke(new MethodInvoker(delegate
            {
                queued = false;
                // A failed attempt can leave Shell layout notifications queued.
                // Let the retry timer handle these instead of a busy retry loop.
                if (!disposing && (!shellWaiting || shellResetPending)) PositionBar();
            }));
        }
        private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (!IsHandleCreated || disposing) return;
            BeginInvoke(new MethodInvoker(delegate { if (!disposing) { palette = Palette.Current(settings); Invalidate(); } }));
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // Never steal focus on hover/click.
            if (m.Msg == callback)
            {
                if (m.WParam.ToInt32() == 1) QueuePosition();
                if (m.WParam.ToInt32() == 2)
                {
                    SetFullscreenState(m.LParam != IntPtr.Zero);
                }
            }
            if (m.Msg == taskbarCreated && Visible)
            {
                // Never register synchronously inside a native Shell callback:
                // Explorer can still be initializing, or broadcast this twice.
                shellResetPending = true;
                QueuePosition();
                if (ExplorerRestarted != null) ExplorerRestarted(this, EventArgs.Empty);
            }
            if (m.Msg == 0x7E || m.Msg == 0x2E0) QueuePosition();
            if (m.Msg == 0x2E0) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
            if (registered && !positioning && !disposing && (m.Msg == 0x47 || m.Msg == 6))
            {
                var bar = Native.BarData(Handle);
                if (m.Msg == 6) bar.Parameter = new IntPtr((m.WParam.ToInt64() & 0xffff) == 0 ? 0 : 1);
                appBarMessage(m.Msg == 6 ? Native.ActivateBar : Native.WindowPosChanged, ref bar);
            }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && (draggingWork || suppressWorkClick)) return;
            if (e.Button == MouseButtons.Right) { barMenu.Show(this, e.Location); return; }
            if (e.Button == MouseButtons.Left && BarRenderer.MenuBounds(ClientRectangle, settings, Native.Dpi(Handle)).Contains(e.Location))
            { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); }
            else if (e.Button == MouseButtons.Left)
            {
                string id = Hit(e.Location);
                // Read-only text must not fall through to the resource panel.
                if (BarRenderer.IsReadOnly(id)) return;
                var work = id == "Tracks" ? HitWork(e.Location) : null;
                if (work != null && WorkRequested != null) WorkRequested(work.Index);
                else if (id != null && ModuleRequested != null) ModuleRequested(id);
                else if (DetailsRequested != null) DetailsRequested(this, EventArgs.Empty);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            CancelWorkDrag(); suppressWorkClick = false;
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || snapshot == null) return;
            var target = HitWork(e.Location);
            if (target == null || target.Index < 0) return;
            snapshot = snapshot.FrozenCopy();
            dragIndex = target.Index; dragStart = e.Location; Capture = true;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int source = dragIndex, target = -1;
            if (e.Button == MouseButtons.Left && draggingWork && snapshot != null)
            {
                dropSlot = DropPosition(e.Location);
                bool unchanged = !hasPendingSnapshot || (pendingSnapshot != null && snapshot.Briefing.WorkKeywords.SequenceEqual(pendingSnapshot.Briefing.WorkKeywords));
                if (unchanged) target = WorkOrder.TargetIndex(source, dropSlot, snapshot.Briefing.WorkKeywords.Count);
            }
            CancelWorkDrag();
            if (target >= 0 && target != source && WorkReorderRequested != null) WorkReorderRequested(source, target);
            base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && dragIndex >= 0) CancelWorkDrag();
        }
        private void CancelWorkDrag()
        {
            suppressWorkClick |= draggingWork;
            dragIndex = dropSlot = -1; draggingWork = false;
            if (Capture) Capture = false;
            tooltip.Active = true; Cursor = Cursors.Default; hovered = null; hoveredWork = int.MinValue;
            if (hasPendingSnapshot) { var latest = pendingSnapshot; pendingSnapshot = null; hasPendingSnapshot = false; UpdateSnapshot(latest); }
            Invalidate();
        }
        private int DropPosition(Point point)
        {
            using (var graphics = CreateGraphics())
            {
                int hidden, dpi = Native.Dpi(Handle);
                foreach (var cell in BarRenderer.Layout(graphics, ClientRectangle, settings, snapshot, dpi, out hidden))
                    if (cell.Id == "Tracks") return BarRenderer.WorkDropSlot(BarRenderer.WorkTargets(graphics, cell, snapshot, settings, dpi), cell.Bounds, point, dpi);
            }
            return -1;
        }
        private string Hit(Point point)
        {
            using (var graphics = CreateGraphics())
            { int hidden; foreach (var cell in BarRenderer.Layout(graphics, ClientRectangle, settings, snapshot, Native.Dpi(Handle), out hidden)) if (cell.Bounds.Contains(point)) return cell.Id; }
            return null;
        }
        private WorkTarget HitWork(Point point)
        {
            using (var graphics = CreateGraphics())
            {
                int hidden;
                foreach (var cell in BarRenderer.Layout(graphics, ClientRectangle, settings, snapshot, Native.Dpi(Handle), out hidden))
                    if (cell.Id == "Tracks") foreach (var target in BarRenderer.WorkTargets(graphics, cell, snapshot, settings, Native.Dpi(Handle))) if (target.Bounds.Contains(point)) return target;
            }
            return null;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragIndex >= 0 && e.Button == MouseButtons.Left)
            {
                var threshold = new Rectangle(dragStart.X - SystemInformation.DragSize.Width / 2, dragStart.Y - SystemInformation.DragSize.Height / 2, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
                if (draggingWork || !threshold.Contains(e.Location))
                {
                    draggingWork = suppressWorkClick = true; tooltip.Active = false;
                    dropSlot = DropPosition(e.Location); Cursor = dropSlot < 0 ? Cursors.No : Cursors.SizeWE; Invalidate(); return;
                }
            }
            string id = Hit(e.Location);
            var work = id == "Tracks" ? HitWork(e.Location) : null;
            int workIndex = work == null ? int.MinValue : work.Index;
            if (hovered == id && hoveredWork == workIndex) return;
            hoveredWork = workIndex;
            hovered = id; Cursor = id == null || BarRenderer.IsReadOnly(id) ? Cursors.Default : Cursors.Hand;
            tooltip.SetToolTip(this, id == null ? "Right-click for menu" : BarRenderer.Label(id) + " " + (snapshot == null ? "—" : snapshot.Value(id)) + (BarRenderer.IsReadOnly(id) ? "\nRight-click for menu" : "\nClick for details · Right-click for menu"));
            if (id == "Tracks")
            {
                tooltip.SetToolTip(this, work == null || work.Index == -2 ? "Browse all keywords · Drag to reorder in the list" : work.Index == -1 ? "Type a keyword, Enter to create" : work.Text + "\nClick to write · Drag to reorder");
            }
            if (id == "MouseBattery" && snapshot != null) tooltip.SetToolTip(this, snapshot.Briefing.MouseBattery.FullDetails);
            if (id == "City" && snapshot != null) tooltip.SetToolTip(this, snapshot.Briefing.Location.FullDetails);
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = null; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) { BarRenderer.Draw(e.Graphics, ClientRectangle, settings, snapshot, palette, Native.Dpi(Handle), hovered, hoveredWork, draggingWork ? dragIndex : -1, dropSlot); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Visible && !disposing) { shellRetry.Start(); QueuePosition(); }
        }
        protected override void OnHandleDestroyed(EventArgs e) { if (!disposing) shellRetry.Stop(); Unregister(); base.OnHandleDestroyed(e); }
        protected override void Dispose(bool value)
        {
            if (value && !disposing)
            {
                disposing = true;
                shellRetry.Stop(); shellRetry.Dispose();
                SystemEvents.UserPreferenceChanged -= ThemeChanged;
                Unregister();
                tooltip.Dispose();
                barMenu.Dispose();
            }
            base.Dispose(value);
        }
    }
    internal sealed class BarMenuColors : ProfessionalColorTable
    {
        private readonly SettingsColors colors;
        internal BarMenuColors(SettingsColors value) { colors = value; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return colors.Background; } }
        public override Color MenuItemSelected { get { return colors.Selected; } }
        public override Color MenuItemBorder { get { return colors.Selected; } }
        public override Color MenuBorder { get { return colors.Border; } }
        public override Color SeparatorDark { get { return colors.Border; } }
        public override Color SeparatorLight { get { return colors.Background; } }
    }
}
