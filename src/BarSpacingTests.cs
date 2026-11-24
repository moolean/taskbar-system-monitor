using System;
using System.Drawing;
using System.Linq;

namespace TaskbarSystemMonitor
{
    internal static class BarSpacingTests
    {
        internal static void Run()
        {
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            foreach (int fontSize in new[] { 8, 9, 11 })
            foreach (string alignment in new[] { "Left", "Center", "Right" })
            using (var bitmap = new Bitmap(BarRenderer.Scale(2400, dpi), BarRenderer.Scale(28, dpi)))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = BarRenderer.FontFor(new Settings { FontSize = fontSize }, dpi))
            {
                var settings = new Settings { FontSize = fontSize, Alignment = alignment, Items = Settings.MetricIds.ToList() };
                var sample = InfoTests.Demo();
                sample.Cpu = 24; sample.Memory = 45; sample.RxKbps = 17; sample.TxKbps = 4;
                sample.Briefing.MouseBattery = new Reading("8%+", "Sample", true);
                var bounds = new Rectangle(13, 7, bitmap.Width - 26, bitmap.Height - 7);
                int hidden;
                var cells = BarRenderer.Layout(graphics, bounds, settings, sample, dpi, out hidden);
                Require(hidden == 0, "All modules fit the spacing test");
                foreach (var cell in cells)
                {
                    var content = BarRenderer.ContentBounds(graphics, cell, font, dpi);
                    Require(cell.Bounds.Contains(content), "Content stays within its cell");
                    Require(content.Top == bounds.Top + 1, "Text shares one vertical baseline");
                    int left = content.Left - cell.Bounds.Left, right = cell.Bounds.Right - content.Right;
                    Require(left >= BarRenderer.Scale(5, dpi) && right >= BarRenderer.Scale(5, dpi), "Consistent minimum side padding");
                    if (cell.Id == "Tracks")
                    {
                        var targets = BarRenderer.WorkTargets(graphics, cell, sample, settings, dpi);
                        Require(targets.First().Bounds.Left == content.Left + cell.LabelWidth + BarRenderer.Scale(4, dpi), "Work click targets still match text layout");
                        continue;
                    }
                    Require(Math.Abs(left - right) <= 1, "Unused width is balanced to within one physical pixel");
                    using (var format = new StringFormat(StringFormat.GenericTypographic))
                    {
                        int measured = (int)Math.Ceiling(graphics.MeasureString(cell.Value, font, int.MaxValue, format).Width);
                        Require(content.Width == cell.LabelWidth + (cell.LabelWidth > 0 ? BarRenderer.Scale(4, dpi) : 0) + measured,
                            "Label/value group has exactly the same compact gap for every module");
                    }
                }
                // A value refresh must not move separators, later modules or
                // the work drag targets. Only text inside a fixed cell moves.
                foreach (double value in new[] { 0, 1, 9, 10, 99, 100 })
                {
                    sample.Cpu = value; sample.Memory = value;
                    sample.RxKbps = value == 100 ? 1024 * 999.9 : value;
                    sample.TxKbps = value == 100 ? 1048576 * 9.9 : value;
                    sample.Briefing.MouseBattery = new Reading(value + "%+", "Sample", true);
                    var refreshed = BarRenderer.Layout(graphics, bounds, settings, sample, dpi, out hidden);
                    Require(hidden == 0 && cells.Select(x => x.Bounds).SequenceEqual(refreshed.Select(x => x.Bounds)), "Numeric updates keep cell boundaries stable");
                }
                // Long passive status strings retain their bounded ellipsis
                // area, including in right-aligned bars.
                sample.Briefing.Codex = new Reading(new string('W', 100), "Sample", true);
                sample.LocalIp = new string('W', 100);
                foreach (var cell in BarRenderer.Layout(graphics, bounds, settings, sample, dpi, out hidden).Where(x => x.Id == "Codex" || x.Id == "Ip"))
                {
                    var content = BarRenderer.ContentBounds(graphics, cell, font, dpi);
                    Require(content.Left - cell.Bounds.Left == BarRenderer.Scale(5, dpi) && cell.Bounds.Right - content.Right == BarRenderer.Scale(5, dpi), "Ellipsized information keeps equal side padding");
                }
                foreach (bool light in new[] { true, false }) BarRenderer.Draw(graphics, bounds, settings, sample, Palette.Create(light), dpi);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Bar spacing test: " + message); }
    }
}
