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
        internal int LabelWidth, Weight;
    }

    internal static class BarRenderer
    {
        internal static Font FontFor(Settings settings, int dpi) { return new Font("Segoe UI", settings.FontSize * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel); }
        internal static int Scale(int value, int dpi) { return Math.Max(1, (int)Math.Round(value * dpi / 96.0)); }
        internal static string Label(string id)
        {
            switch (id) { case "Cpu": return "CPU"; case "Memory": return "RAM"; case "MemoryUsed": return "内存"; case "Download": return "↓"; case "Upload": return "↑"; case "Battery": return "电量"; case "Clock": return ""; case "Codex": return "CODEX"; case "Calendar": return "日程"; case "Ip": return "IP"; case "Tracks": return "工作"; default: return id; }
        }
        private static string Reserve(string id)
        {
            switch (id) { case "Cpu": case "Memory": return "100%"; case "MemoryUsed": return "99.9 / 999.9 GiB"; case "Upload": case "Download": return "999.9 MiB/s"; case "Battery": return "充电 100%"; case "Clock": return "09/14 周一 23:59"; default: return "—"; }
        }
        internal static List<MetricCell> Layout(Graphics graphics, Rectangle bounds, Settings settings, Snapshot snapshot, int dpi, out int hidden)
        {
            var cells = new List<MetricCell>();
            int padding = Scale(8, dpi), gap = Scale(6, dpi), button = Scale(28, dpi);
            int available = Math.Max(0, bounds.Width - button - padding * 2), total = 0;
            hidden = 0;
            using (Font font = FontFor(settings, dpi))
            {
                foreach (string id in settings.Items)
                {
                    string value = snapshot == null ? "—" : snapshot.Value(id);
                    int labelWidth = Measure(graphics, Label(id), font);
                    int valueWidth = Measure(graphics, Reserve(id), font);
                    valueWidth = Math.Max(valueWidth, Measure(graphics, value, font));
                    int weight = id == "Calendar" || id == "Tracks" ? 3 : id == "Codex" || id == "Ip" ? 1 : 0;
                    int width = weight > 0 ? Scale(id == "Calendar" || id == "Tracks" ? 230 : id == "Codex" ? 194 : 200, dpi) : padding * 2 + gap + labelWidth + valueWidth;
                    if (total + width > available) { hidden++; continue; }
                    cells.Add(new MetricCell { Bounds = new Rectangle(0, bounds.Top, width, bounds.Height), Id = id, Label = Label(id), Value = value, LabelWidth = labelWidth, Weight = weight });
                    total += width;
                }
            }
            if (settings.FillBar && cells.Count > 0)
            {
                int weight = 0; foreach (var cell in cells) weight += cell.Weight;
                if (weight > 0)
                {
                    int extra = available - total;
                    foreach (var cell in cells)
                    {
                        if (cell.Weight == 0) continue;
                        int grow = extra * cell.Weight / weight;
                        cell.Bounds = new Rectangle(0, bounds.Top, cell.Bounds.Width + grow, bounds.Height);
                        total += grow; extra -= grow; weight -= cell.Weight;
                    }
                }
            }
            int start = bounds.Left + padding;
            if (settings.Alignment == "Right") start += available - total;
            if (settings.Alignment == "Center") start += (available - total) / 2;
            foreach (MetricCell cell in cells) { cell.Bounds = new Rectangle(start, bounds.Top, cell.Bounds.Width, bounds.Height); start += cell.Bounds.Width; }
            return cells;
        }
        internal static void Draw(Graphics graphics, Rectangle bounds, Settings settings, Snapshot snapshot, Palette colors, int dpi, string hovered = null)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (settings.SoftBackground && !SystemInformation.HighContrast)
            {
                using (var gradient = new LinearGradientBrush(bounds, colors.Surface, colors.Background, LinearGradientMode.Vertical)) graphics.FillRectangle(gradient, bounds);
            }
            else using (var brush = new SolidBrush(colors.Background)) graphics.FillRectangle(brush, bounds);
            using (var line = new Pen(colors.Border)) graphics.DrawLine(line, bounds.Left, bounds.Top, bounds.Right, bounds.Top);

            int hidden;
            List<MetricCell> cells = Layout(graphics, bounds, settings, snapshot, dpi, out hidden);
            int pad = Scale(8, dpi), gap = Scale(6, dpi);
            using (Font font = FontFor(settings, dpi))
            {
                foreach (MetricCell cell in cells)
                {
                    if (cell.Id == hovered || cell.Weight > 0)
                    {
                        var chip = Rectangle.Inflate(cell.Bounds, -Scale(2, dpi), -Scale(3, dpi));
                        using (var fill = new SolidBrush(Palette.Blend(colors.Background, colors.Cpu, cell.Id == hovered ? 0.15 : 0.045)))
                        using (var path = Rounded(chip, Scale(4, dpi))) graphics.FillPath(fill, path);
                    }
                    Rectangle label = new Rectangle(cell.Bounds.Left + pad, bounds.Top + 1, cell.LabelWidth, bounds.Height - 2);
                    Color accent = cell.Id == "Cpu" ? colors.Cpu : cell.Id == "Memory" || cell.Id == "MemoryUsed" ? colors.Memory : colors.Muted;
                    DrawText(graphics, cell.Label, font, label, accent, StringAlignment.Near);
                    Rectangle value = new Rectangle(label.Right + gap, label.Top, cell.Bounds.Right - pad - label.Right - gap, label.Height);
                    DrawText(graphics, cell.Value, font, value, colors.Text, cell.Weight > 0 ? StringAlignment.Near : StringAlignment.Far);
                    if (cell != cells[cells.Count - 1])
                        using (var pen = new Pen(colors.Border)) graphics.DrawLine(pen, cell.Bounds.Right, bounds.Top + bounds.Height / 3, cell.Bounds.Right, bounds.Bottom - bounds.Height / 3);
                }
                // This remains visible even if every metric is too wide for the screen.
                Rectangle menu = new Rectangle(bounds.Right - Scale(28, dpi), bounds.Top + 1, Scale(24, dpi), bounds.Height - 2);
                DrawText(graphics, hidden > 0 ? "+" + hidden : "···", font, menu, colors.Muted, StringAlignment.Center);
            }
        }
        private static int Measure(Graphics graphics, string text, Font font)
        { using (var format = new StringFormat(StringFormat.GenericTypographic)) return (int)Math.Ceiling(graphics.MeasureString(text, font, int.MaxValue, format).Width); }
        private static void DrawText(Graphics graphics, string text, Font font, Rectangle bounds, Color color, StringAlignment alignment)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || text.Length == 0) return;
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = alignment, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                graphics.DrawString(text, font, brush, bounds, format);
        }
        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath(); int d = Math.Max(1, Math.Min(radius * 2, rect.Height));
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
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
            BarRenderer.Draw(e.Graphics, new Rectangle(0, (Height - height) / 2, Width, height), Settings, Snapshot, Palette.Current(Settings), dpi);
        }
    }
}
