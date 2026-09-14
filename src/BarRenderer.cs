using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class MetricCell
    {
        internal Rectangle Bounds;
        internal string Id, Label, Value;
        internal int LabelWidth;
    }

    internal static class BarRenderer
    {
        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        internal static Font FontFor(Settings settings, int dpi) { return new Font("Segoe UI", settings.FontSize * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel); }
        internal static int Scale(int value, int dpi) { return Math.Max(1, (int)Math.Round(value * dpi / 96.0)); }
        internal static string Label(string id)
        {
            switch (id) { case "Cpu": return "CPU"; case "Memory": return "RAM"; case "MemoryUsed": return "内存"; case "Download": return "↓"; case "Upload": return "↑"; case "Battery": return "电量"; case "Clock": return "时间"; default: return id; }
        }
        private static string Reserve(string id)
        {
            switch (id) { case "Cpu": case "Memory": return "100%"; case "MemoryUsed": return "99.9 / 999.9 GiB"; case "Upload": case "Download": return "999.9 MiB/s"; case "Battery": return "充电 100%"; case "Clock": return "00:00:00"; default: return "—"; }
        }
        internal static List<MetricCell> Layout(Graphics graphics, Rectangle bounds, Settings settings, Snapshot snapshot, int dpi, out int hidden)
        {
            var cells = new List<MetricCell>();
            int padding = Scale(10, dpi), gap = Scale(6, dpi), button = Scale(28, dpi);
            int available = Math.Max(0, bounds.Width - button - padding * 2), total = 0;
            hidden = 0;
            using (Font font = FontFor(settings, dpi))
            {
                foreach (string id in settings.Items)
                {
                    string value = snapshot == null ? "—" : snapshot.Value(id);
                    int labelWidth = TextRenderer.MeasureText(graphics, Label(id), font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    int valueWidth = TextRenderer.MeasureText(graphics, Reserve(id), font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    valueWidth = Math.Max(valueWidth, TextRenderer.MeasureText(graphics, value, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
                    int width = padding * 2 + gap + labelWidth + valueWidth;
                    if (total + width > available) { hidden++; continue; }
                    cells.Add(new MetricCell { Bounds = new Rectangle(0, bounds.Top, width, bounds.Height), Id = id, Label = Label(id), Value = value, LabelWidth = labelWidth });
                    total += width;
                }
            }
            int start = bounds.Left + padding;
            if (settings.Alignment == "Right") start += available - total;
            if (settings.Alignment == "Center") start += (available - total) / 2;
            foreach (MetricCell cell in cells) { cell.Bounds = new Rectangle(start, bounds.Top, cell.Bounds.Width, bounds.Height); start += cell.Bounds.Width; }
            return cells;
        }
        internal static void Draw(Graphics graphics, Rectangle bounds, Settings settings, Snapshot snapshot, Palette colors, int dpi)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (settings.SoftBackground && !SystemInformation.HighContrast)
            {
                using (var gradient = new LinearGradientBrush(bounds, colors.Surface, colors.Background, LinearGradientMode.Vertical)) graphics.FillRectangle(gradient, bounds);
            }
            else using (var brush = new SolidBrush(colors.Background)) graphics.FillRectangle(brush, bounds);
            using (var line = new Pen(colors.Border)) graphics.DrawLine(line, bounds.Left, bounds.Top, bounds.Right, bounds.Top);

            int hidden;
            List<MetricCell> cells = Layout(graphics, bounds, settings, snapshot, dpi, out hidden);
            int pad = Scale(10, dpi), gap = Scale(6, dpi);
            using (Font font = FontFor(settings, dpi))
            {
                foreach (MetricCell cell in cells)
                {
                    Rectangle label = new Rectangle(cell.Bounds.Left + pad, bounds.Top + 1, cell.LabelWidth, bounds.Height - 2);
                    Color accent = cell.Id == "Cpu" ? colors.Cpu : cell.Id == "Memory" || cell.Id == "MemoryUsed" ? colors.Memory : colors.Muted;
                    TextRenderer.DrawText(graphics, cell.Label, font, label, accent, Flags);
                    Rectangle value = new Rectangle(label.Right + gap, label.Top, cell.Bounds.Right - pad - label.Right - gap, label.Height);
                    TextRenderer.DrawText(graphics, cell.Value, font, value, colors.Text, Flags | TextFormatFlags.Right);
                    if (cell != cells[cells.Count - 1])
                        using (var pen = new Pen(colors.Border)) graphics.DrawLine(pen, cell.Bounds.Right, bounds.Top + bounds.Height / 3, cell.Bounds.Right, bounds.Bottom - bounds.Height / 3);
                }
                // This remains visible even if every metric is too wide for the screen.
                Rectangle menu = new Rectangle(bounds.Right - Scale(28, dpi), bounds.Top + 1, Scale(24, dpi), bounds.Height - 2);
                TextRenderer.DrawText(graphics, hidden > 0 ? "+" + hidden : "···", font, menu, colors.Muted, Flags | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    internal sealed class BarPreview : Control
    {
        internal Settings Settings = new Settings();
        internal Snapshot Snapshot = new Snapshot { Cpu = 12, Memory = 42, UsedBytes = 14431090114, TotalBytes = 34359738368, RxKbps = 2355, TxKbps = 86, Battery = "78%", Time = new DateTime(2026, 1, 1, 14, 36, 28) };
        internal BarPreview() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            int dpi = Native.Dpi(Handle);
            int height = Math.Min(Height, BarRenderer.Scale(Settings.Height, dpi));
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            BarRenderer.Draw(e.Graphics, new Rectangle(0, (Height - height) / 2, Width, height), Settings, Snapshot, Palette.Current(Settings.Theme), dpi);
        }
    }
}
