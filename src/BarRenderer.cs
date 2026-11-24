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

    internal sealed class WorkTarget
    {
        internal Rectangle Bounds;
        internal string Text;
        // -1: quick entry; -2: full list; nonnegative: exact work item.
        internal int Index;
    }

    internal sealed class WorkColors
    {
        internal Color Fill, Hover, Border, Text, Accent;
        internal static WorkColors From(Palette colors)
        {
            if (SystemInformation.HighContrast) return new WorkColors { Fill = SystemColors.Window, Hover = SystemColors.Highlight, Border = SystemColors.WindowText, Text = SystemColors.WindowText, Accent = SystemColors.WindowText };
            bool light = colors.Background.GetBrightness() > .5f;
            Color tint = light ? Color.FromArgb(205, 194, 227) : Color.FromArgb(105, 86, 145);
            return new WorkColors {
                Fill = Palette.Blend(colors.Background, tint, light ? .32 : .23),
                Hover = Palette.Blend(colors.Background, tint, light ? .76 : .55),
                Border = Palette.Blend(colors.Background, tint, light ? .48 : .42),
                Text = light ? Color.FromArgb(68, 53, 92) : Color.FromArgb(230, 221, 247),
                Accent = light ? Color.FromArgb(110, 86, 149) : Color.FromArgb(187, 166, 221)
            };
        }
    }

    internal static class BarRenderer
    {
        private const int CellPadding = 5, LabelGap = 4, KeywordGap = 3, KeywordPadding = 10;
        internal static bool IsReadOnly(string id) { return id == "Codex" || id == "Ip" || id == "MouseBattery" || id == "City"; }
        internal static Rectangle CityBounds(Rectangle bounds, Settings settings, int dpi)
        {
            if (!settings.GeoEnabled) return Rectangle.Empty;
            int width = Math.Min(Scale(112 + (settings.FontSize - 9) * 7, dpi), bounds.Width / 3);
            return width < Scale(48, dpi) ? Rectangle.Empty : Rectangle.FromLTRB(bounds.Right - width, bounds.Top, bounds.Right, bounds.Bottom);
        }
        internal static Rectangle MenuBounds(Rectangle bounds, Settings settings, int dpi)
        {
            int right = bounds.Right - CityBounds(bounds, settings, dpi).Width;
            return Rectangle.FromLTRB(Math.Max(bounds.Left, right - Scale(28, dpi)), bounds.Top, right, bounds.Bottom);
        }
        internal static Font FontFor(Settings settings, int dpi) { return new Font("Segoe UI", settings.FontSize * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel); }
        internal static int Scale(int value, int dpi) { return Math.Max(1, (int)Math.Round(value * dpi / 96.0)); }
        internal static string Label(string id)
        {
            switch (id) { case "Cpu": return "CPU"; case "Memory": return "RAM"; case "MemoryUsed": return "MEM"; case "Download": return "↓"; case "Upload": return "↑"; case "Battery": return "BAT"; case "MouseBattery": return "MOUSE"; case "Clock": return ""; case "Codex": return "CODEX"; case "Ip": return "IP"; case "Tracks": return "WORK"; default: return id; }
        }
        private static string Reserve(string id)
        {
            switch (id) { case "Cpu": case "Memory": return "100%"; case "MouseBattery": return "100%+"; case "MemoryUsed": return "99.9 / 999.9 GiB"; case "Upload": case "Download": return "999.9 MiB/s"; case "Battery": return "AC 100%"; case "Clock": return "09/14 Wed 23:59"; default: return "—"; }
        }
        internal static List<MetricCell> Layout(Graphics graphics, Rectangle bounds, Settings settings, Snapshot snapshot, int dpi, out int hidden)
        {
            var cells = new List<MetricCell>();
            int padding = Scale(CellPadding, dpi), gap = Scale(LabelGap, dpi);
            int available = Math.Max(0, MenuBounds(bounds, settings, dpi).Left - bounds.Left - padding * 2), total = 0;
            int workPreferred = 0;
            hidden = 0;
            using (Font font = FontFor(settings, dpi))
            {
                foreach (string id in settings.Items)
                {
                    string value = snapshot == null ? "—" : snapshot.Value(id);
                    int labelWidth = Measure(graphics, Label(id), font);
                    int width;
                    if (id == "Tracks")
                    {
                        workPreferred = WorkWidth(graphics, font, labelWidth, snapshot, dpi);
                        // Reserve a usable entry before later modules, then let
                        // work consume spare width after every module is placed.
                        width = Math.Min(workPreferred, labelWidth + padding * 2 + gap + Scale(100, dpi));
                    }
                    else
                    {
                        int valueWidth = Math.Max(Measure(graphics, Reserve(id), font), Measure(graphics, value, font));
                        width = padding * 2 + (labelWidth > 0 ? gap : 0) + labelWidth + valueWidth;
                        // Information modules size to content instead of sharing
                        // stretch space. Unusually long details remain in popups.
                        if (id == "Codex" || id == "Ip") width = Math.Min(width, Scale(id == "Codex" ? 240 : 220, dpi));
                    }
                    if (total + width > available) { hidden++; continue; }
                    cells.Add(new MetricCell { Bounds = new Rectangle(0, bounds.Top, width, bounds.Height), Id = id, Label = Label(id), Value = value, LabelWidth = labelWidth });
                    total += width;
                }
            }
            foreach (var cell in cells)
            {
                if (cell.Id != "Tracks") continue;
                int grow = Math.Max(0, Math.Min(available - total, settings.FillBar ? available : workPreferred - cell.Bounds.Width));
                cell.Bounds = new Rectangle(0, bounds.Top, cell.Bounds.Width + grow, bounds.Height);
                total += grow;
            }
            int start = bounds.Left + padding;
            if (settings.Alignment == "Right") start += available - total;
            if (settings.Alignment == "Center") start += (available - total) / 2;
            foreach (MetricCell cell in cells) { cell.Bounds = new Rectangle(start, bounds.Top, cell.Bounds.Width, bounds.Height); start += cell.Bounds.Width; }
            var city = CityBounds(bounds, settings, dpi);
            if (!city.IsEmpty)
                using (Font font = FontFor(settings, dpi))
                    cells.Add(new MetricCell { Id = "City", Label = "CITY", LabelWidth = Measure(graphics, "CITY", font), Value = snapshot == null ? "Locating…" : snapshot.Value("City"), Bounds = city });
            return cells;
        }
        internal static Rectangle ContentBounds(Graphics graphics, MetricCell cell, Font font, int dpi)
        {
            int pad = Scale(CellPadding, dpi);
            int available = Math.Max(0, cell.Bounds.Width - pad * 2);
            int width = available;
            if (cell.Id != "Tracks")
            {
                // Reserve the cell width for changing numbers, not the gap
                // between the label and value. Center the actual text as one
                // compact group so unused space is balanced on both sides.
                int textWidth = cell.LabelWidth + (cell.LabelWidth > 0 ? Scale(LabelGap, dpi) : 0) + Measure(graphics, cell.Value, font);
                width = Math.Min(available, textWidth);
            }
            return new Rectangle(cell.Bounds.Left + pad + (available - width) / 2,
                cell.Bounds.Top + 1, width, Math.Max(0, cell.Bounds.Height - 2));
        }
        internal static void Draw(Graphics graphics, Rectangle bounds, Settings settings, Snapshot snapshot, Palette colors, int dpi, string hovered = null, int hoveredWork = int.MinValue, int draggedWork = -1, int workDropSlot = -1)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (settings.SoftBackground && !SystemInformation.HighContrast)
            {
                using (var gradient = new LinearGradientBrush(bounds, colors.Surface, colors.Background, LinearGradientMode.Vertical)) graphics.FillRectangle(gradient, bounds);
            }
            else using (var brush = new SolidBrush(colors.Background)) graphics.FillRectangle(brush, bounds);
            using (var line = new Pen(colors.Border)) graphics.DrawLine(line, bounds.Left, bounds.Top, bounds.Right, bounds.Top);

            int hidden;
            List<MetricCell> cells = Layout(graphics, bounds, settings, snapshot, dpi, out hidden);
            int pad = Scale(CellPadding, dpi), gap = Scale(LabelGap, dpi);
            using (Font font = FontFor(settings, dpi))
            {
                var workColors = WorkColors.From(colors);
                for (int i = 0; i < cells.Count; i++)
                {
                    MetricCell cell = cells[i];
                    var workTargets = cell.Id == "Tracks" ? WorkTargets(graphics, cell, snapshot, settings, dpi) : null;
                    if (!IsReadOnly(cell.Id) && cell.Id != "Tracks" && cell.Id == hovered)
                    {
                        var surface = cell.Bounds;
                        if (workTargets != null && workTargets.Count > 0)
                            surface.Width = Math.Min(surface.Width, workTargets[workTargets.Count - 1].Bounds.Right + pad - surface.Left);
                        var chip = Rectangle.Inflate(surface, -Scale(2, dpi), -Scale(3, dpi));
                        using (var fill = new SolidBrush(Palette.Blend(colors.Background, colors.Cpu, cell.Id == hovered ? 0.15 : 0.045)))
                        using (var path = Rounded(chip, Scale(4, dpi))) graphics.FillPath(fill, path);
                    }
                    Rectangle content = ContentBounds(graphics, cell, font, dpi);
                    Rectangle label = new Rectangle(content.Left, content.Top, cell.LabelWidth, content.Height);
                    Color accent = cell.Id == "Tracks" ? workColors.Accent : cell.Id == "City" ? colors.Cpu : colors.Muted;
                    DrawText(graphics, cell.Label, font, label, accent, StringAlignment.Near);
                    int valueLeft = label.Right + (cell.LabelWidth > 0 ? gap : 0);
                    Rectangle value = new Rectangle(valueLeft, label.Top, Math.Max(0, content.Right - valueLeft), label.Height);
                    if (cell.Id == "Tracks")
                    {
                        foreach (var target in workTargets)
                        {
                            bool active = target.Index == hoveredWork || (draggedWork >= 0 && target.Index == draggedWork);
                            var chip = target.Bounds; chip.Width = Math.Max(1, chip.Width - 1); chip.Height = Math.Max(1, chip.Height - 1);
                            using (var brush = new SolidBrush(active ? workColors.Hover : workColors.Fill))
                            using (var border = new Pen(active ? workColors.Accent : workColors.Border))
                            using (var path = Rounded(chip, Scale(5, dpi))) { graphics.FillPath(brush, path); graphics.DrawPath(border, path); }
                            Color text = SystemInformation.HighContrast && active ? SystemColors.HighlightText : target.Index < 0 ? workColors.Accent : workColors.Text;
                            DrawText(graphics, target.Text, font, Rectangle.Inflate(target.Bounds, -Scale(4, dpi), 0), text, StringAlignment.Center);
                        }
                        if (draggedWork >= 0 && workDropSlot >= 0)
                        {
                            int x = -1;
                            foreach (var target in workTargets)
                            {
                                if (target.Index == workDropSlot) { x = target.Bounds.Left - Scale(2, dpi); break; }
                                if (target.Index >= 0 && target.Index + 1 == workDropSlot) x = target.Bounds.Right + Scale(1, dpi);
                            }
                            if (x >= 0) using (var pen = new Pen(workColors.Accent, Scale(2, dpi))) graphics.DrawLine(pen, x, cell.Bounds.Top + Scale(3, dpi), x, cell.Bounds.Bottom - Scale(3, dpi));
                        }
                    }
                    else DrawText(graphics, cell.Value, font, value, colors.Text, StringAlignment.Near);
                    if (i + 1 < cells.Count && Group(cell.Id) != Group(cells[i + 1].Id))
                        using (var pen = new Pen(colors.Border)) graphics.DrawLine(pen, cell.Bounds.Right, bounds.Top + bounds.Height / 3, cell.Bounds.Right, bounds.Bottom - bounds.Height / 3);
                }
                // This remains visible even if every metric is too wide for the screen.
                Rectangle menu = MenuBounds(bounds, settings, dpi);
                DrawText(graphics, hidden > 0 ? "+" + hidden : "···", font, menu, colors.Muted, StringAlignment.Center);
            }
        }
        private static string Group(string id)
        {
            switch (id) { case "Cpu": case "Memory": case "MemoryUsed": return "system"; case "Upload": case "Download": return "traffic"; case "MouseBattery": case "Battery": return "power"; default: return id; }
        }
        internal static int WorkDropSlot(List<WorkTarget> targets, Rectangle cellBounds, Point point, int dpi)
        {
            var area = cellBounds; area.Inflate(0, Scale(16, dpi));
            if (!area.Contains(point)) return -1;
            int slot = -1;
            foreach (var target in targets)
            {
                if (target.Index < 0) continue;
                if (point.X < target.Bounds.Left + target.Bounds.Width / 2) return target.Index;
                slot = target.Index + 1;
            }
            return slot;
        }
        internal static List<WorkTarget> WorkTargets(Graphics graphics, MetricCell cell, Snapshot snapshot, Settings settings, int dpi)
        {
            var result = new List<WorkTarget>();
            var words = Keywords(snapshot);
            int gap = Scale(KeywordGap, dpi), left = cell.Bounds.Left + Scale(CellPadding, dpi) + Scale(LabelGap, dpi) + cell.LabelWidth;
            int right = cell.Bounds.Right - Scale(CellPadding, dpi), top = cell.Bounds.Top + Scale(3, dpi);
            int height = Math.Max(1, cell.Bounds.Height - Scale(6, dpi));
            int x = left;
            using (var font = FontFor(settings, dpi))
            {
                int plus = Math.Min(EntryWidth(graphics, font, words.Count, dpi), Math.Max(0, right - left));
                if (plus < Scale(16, dpi)) return result;
                var widths = new List<int>(); int total = plus;
                foreach (var word in words) { int width = KeywordWidth(graphics, font, word, dpi); widths.Add(width); total += width + gap; }
                int shown = words.Count;
                if (total > right - left)
                {
                    // Only reserve +N when the whole list genuinely does not
                    // fit; speculative reservation used to hide fitting items.
                    shown = 0; int prefix = 0;
                    for (int count = 0; count < words.Count; count++)
                    {
                        int overflow = OverflowWidth(graphics, font, words.Count - count, dpi);
                        if (prefix + overflow + gap + plus <= right - left) shown = count;
                        prefix += widths[count] + gap;
                    }
                }
                for (int i = 0; i < shown; i++)
                {
                    result.Add(new WorkTarget { Index = i, Text = words[i], Bounds = new Rectangle(x, top, widths[i], height) });
                    x += widths[i] + gap;
                }
                if (shown < words.Count)
                {
                    int width = Math.Min(OverflowWidth(graphics, font, words.Count - shown, dpi), right - x - plus - gap);
                    if (width > 0)
                    {
                        result.Add(new WorkTarget { Index = -2, Text = "+" + (words.Count - shown), Bounds = new Rectangle(x, top, width, height) });
                        x += width + gap;
                    }
                }
                result.Add(new WorkTarget { Index = -1, Text = words.Count == 0 ? "+ New" : "+", Bounds = new Rectangle(Math.Min(x, right - plus), top, plus, height) });
            }
            return result;
        }
        private static List<string> Keywords(Snapshot snapshot) { return snapshot == null ? new List<string>() : snapshot.Briefing.WorkKeywords; }
        private static int KeywordWidth(Graphics g, Font font, string text, int dpi)
        { return Math.Max(Scale(24, dpi), Math.Min(Scale(160, dpi), Measure(g, text, font) + Scale(KeywordPadding, dpi))); }
        private static int OverflowWidth(Graphics g, Font font, int count, int dpi)
        { return Math.Max(Scale(24, dpi), Measure(g, "+" + count, font) + Scale(KeywordPadding, dpi)); }
        private static int EntryWidth(Graphics g, Font font, int count, int dpi)
        { return count == 0 ? Math.Max(Scale(64, dpi), Measure(g, "+ New", font) + Scale(KeywordPadding, dpi)) : Scale(22, dpi); }
        private static int WorkWidth(Graphics g, Font font, int labelWidth, Snapshot snapshot, int dpi)
        {
            var words = Keywords(snapshot);
            int width = Scale(CellPadding, dpi) * 2 + Scale(LabelGap, dpi) + labelWidth + EntryWidth(g, font, words.Count, dpi);
            foreach (var word in words) width += KeywordWidth(g, font, word, dpi) + Scale(KeywordGap, dpi);
            return width;
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
