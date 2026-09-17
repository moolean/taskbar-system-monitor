using System;
using System.Drawing;
using System.Linq;
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
        internal int LayoutChanges { get; private set; }

        internal DockForm(Settings configuration)
        {
            settings = configuration;
            palette = Palette.Current(settings);
            Text = "Taskbar System Monitor AppBar";
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
            tooltip.SetToolTip(this, text + "\nRight-click for menu · Click ··· for settings");
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
                if (reservationSet && target == Bounds) { ApplyZOrder(false); return; }
                Native.SHAppBarMessage(Native.SetPos, ref bar);
                reservationSet = true;
                target = bar.Bounds.Rectangle;
                if (target.Width <= 0 || target.Height <= 0) throw new InvalidOperationException("Windows 返回了无效的资源栏位置。");
                Bounds = target;
                Native.SetWindowPos(Handle, IntPtr.Zero, Left, Top, Width, Height, Native.NoActivate | Native.NoZOrder);
                ApplyZOrder(false);
                LayoutChanges++;
            }
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
            if (queued || positioning || disposing || !IsHandleCreated) return;
            queued = true;
            BeginInvoke(new MethodInvoker(delegate { queued = false; if (!disposing) PositionBar(); }));
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
                if (m.Msg == 6) bar.Parameter = new IntPtr((m.WParam.ToInt64() & 0xffff) == 0 ? 0 : 1);
                Native.SHAppBarMessage(m.Msg == 6 ? Native.ActivateBar : Native.WindowPosChanged, ref bar);
            }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && (draggingWork || suppressWorkClick)) return;
            if (e.Button == MouseButtons.Right) { barMenu.Show(this, e.Location); return; }
            if (e.Button == MouseButtons.Left && e.X >= Width - BarRenderer.Scale(30, Native.Dpi(Handle)))
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
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = null; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) { BarRenderer.Draw(e.Graphics, ClientRectangle, settings, snapshot, palette, Native.Dpi(Handle), hovered, hoveredWork, draggingWork ? dragIndex : -1, dropSlot); }
        protected override void OnHandleDestroyed(EventArgs e) { Unregister(); base.OnHandleDestroyed(e); }
        protected override void Dispose(bool value)
        {
            if (value && !disposing)
            {
                disposing = true;
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
