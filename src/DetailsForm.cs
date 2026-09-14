using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarSystemMonitor
{
    internal sealed class DetailsForm : Form
    {
        private Snapshot data; private History history; private Palette palette;
        private string theme = "Auto";
        private int interval = 1000;
        internal event EventHandler SettingsRequested;
        internal DetailsForm()
        {
            SuspendLayout();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "系统监控"; ClientSize = new Size(480, 330); MinimumSize = new Size(430, 300); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; ShowInTaskbar = true; palette = Palette.Current(); DoubleBuffered = true;
            SystemEvents.UserPreferenceChanged += ThemeChanged;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            var settings = new Button { Text = "设置…", Location = new Point(356, 286), Size = new Size(100, 28), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            settings.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            Controls.Add(settings);
            ResumeLayout(false);
        }
        internal void Apply(Settings settings) { theme = settings.Theme; interval = settings.Interval; palette = Palette.Current(theme); Invalidate(); }
        private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed) { palette = Palette.Current(theme); Invalidate(); } }));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) SystemEvents.UserPreferenceChanged -= ThemeChanged;
            base.Dispose(disposing);
        }
        internal void SetData(Snapshot snapshot, History values) { data = snapshot; history = values; if (Visible) Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(palette.Background);
            float scale = Native.Dpi(Handle) / 96f;
            g.ScaleTransform(scale, scale);
            int logicalWidth = (int)(ClientSize.Width / scale);
            using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18, FontStyle.Bold))
            using (var title = new SolidBrush(palette.Text)) using (var muted = new SolidBrush(palette.Muted))
            { g.DrawString("系统资源", font, title, 24, 22); g.DrawString("每 " + interval / 1000 + " 秒刷新 · 最近 120 个采样点", SystemFonts.MessageBoxFont, muted, 26, 54); }
            DrawCard(g, new Rectangle(24, 84, (logicalWidth - 60) / 2, 100), "CPU", data == null ? "—" : data.CpuText, palette.Cpu, history == null ? null : history.Cpu);
            DrawCard(g, new Rectangle(36 + (logicalWidth - 60) / 2, 84, (logicalWidth - 60) / 2, 100), "内存", data == null ? "—" : data.MemoryText, palette.Memory, history == null ? null : history.Memory);
            using (var brush = new SolidBrush(palette.Muted)) g.DrawString(data == null ? "等待第一次采样…" : "已使用 " + data.MemoryDetail + (data.Battery == null ? "" : "   " + data.Battery), SystemFonts.MessageBoxFont, brush, 25, 206);
            using (var line = new Pen(palette.Border)) g.DrawLine(line, 24, 238, logicalWidth - 24, 238);
            using (var brush = new SolidBrush(palette.Muted))
                g.DrawString(data == null ? "在资源栏右键 → 设置，选择更多显示内容" : data.NetworkName + "\n↓ " + Snapshot.Speed(data.RxKbps) + "    ↑ " + Snapshot.Speed(data.TxKbps), SystemFonts.MessageBoxFont, brush, 25, 255);
        }
        private void DrawCard(Graphics g, Rectangle box, string label, string value, Color accent, System.Collections.Generic.List<double?> values)
        {
            using (var background = new SolidBrush(palette.Surface)) using (var border = new Pen(palette.Border)) { g.FillRectangle(background, box); g.DrawRectangle(border, box); }
            using (var accentBrush = new SolidBrush(accent)) g.FillRectangle(accentBrush, box.Left, box.Top, 4, box.Height);
            using (var brush = new SolidBrush(palette.Muted)) g.DrawString(label, SystemFonts.MessageBoxFont, brush, box.Left + 16, box.Top + 14);
            using (var brush = new SolidBrush(palette.Text)) { var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 22, FontStyle.Bold); g.DrawString(value, font, brush, box.Left + 15, box.Top + 34); font.Dispose(); }
            if (values == null || values.Count < 2) return;
            using (var pen = new Pen(accent, 1.5f)) { PointF previous = PointF.Empty; for (int i = 0; i < values.Count; i++) if (values[i].HasValue) { var point = new PointF(box.Left + 15 + i * (box.Width - 30) / 119f, box.Bottom - 12 - (float)Math.Min(100, values[i].Value) * 0.28f); if (!previous.IsEmpty) g.DrawLine(pen, previous, point); previous = point; } else previous = PointF.Empty; }
        }
    }
}
