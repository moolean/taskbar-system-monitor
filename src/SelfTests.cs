using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Linq;

namespace TaskbarSystemMonitor
{
    internal static class SelfTests
    {
        internal static int Run(string[] args)
        {
            try
            {
                InfoTests.Run();
                var sampler = new Sampler(); Snapshot first = sampler.Sample(); System.Threading.Thread.Sleep(120); Snapshot second = sampler.Sample();
                if (first == null || second == null || second.TotalBytes == 0 || second.Memory < 0 || second.Memory > 100) return 2;
                if (first.Cpu != null || Sampler.Calculate(4, 10, 20, 5, 10, 20) != null || Sampler.Calculate(0, 0, 0, 50, 100, 100) != 75 || Sampler.Calculate(100, 100, 100, 0, 0, 0) != null) return 3;
                if (NetworkSampler.Rate(1024, 3072, 2) != 1 || NetworkSampler.Rate(10, 1, 1) != null || NetworkSampler.Rate(0, 1, 31) != null || NetworkSampler.Rate(0, 1, 0) != null) return 11;
                var palette = Palette.Create(true); if (palette.Background == palette.Surface || palette.Text == palette.Muted) return 4;
                var renderSettings = new Settings { Items = Settings.MetricIds.ToList(), FontSize = 11, Height = 32 };
                using (var image = new Bitmap(1800, 40)) using (Graphics graphics = Graphics.FromImage(image))
                {
                    int hidden; var cells = BarRenderer.Layout(graphics, new Rectangle(0, 0, image.Width, image.Height), renderSettings, second, 96, out hidden);
                    if (hidden != 0 || cells.Any(x => x.Bounds.Left < 0 || x.Bounds.Right > image.Width - 28)) return 5;
                    for (int i = 1; i < cells.Count; i++) if (cells[i - 1].Bounds.Right > cells[i].Bounds.Left) return 6;
                }
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                foreach (int pointSize in new[] { 8, 9, 11 })
                foreach (string alignment in new[] { "Left", "Center", "Right" })
                foreach (int logicalWidth in new[] { 280, 1024, 1920 })
                {
                    var config = renderSettings.Copy(); config.FontSize = pointSize; config.Alignment = alignment;
                    int width = BarRenderer.Scale(logicalWidth, dpi), height = BarRenderer.Scale(24, dpi);
                    using (var bitmap = new Bitmap(width, height)) using (var graphics = Graphics.FromImage(bitmap))
                    {
                        int hidden; var cells = BarRenderer.Layout(graphics, new Rectangle(0, 0, width, height), config, second, dpi, out hidden);
                        if (cells.Count + hidden != config.Items.Count || cells.Any(x => x.Bounds.Left < 0 || x.Bounds.Right > width - BarRenderer.Scale(28, dpi))) return 12;
                        for (int i = 1; i < cells.Count; i++) if (cells[i - 1].Bounds.Right > cells[i].Bounds.Left) return 13;
                        BarRenderer.Draw(graphics, new Rectangle(0, 0, width, height), config, second, Palette.Create(true), dpi);
                        BarRenderer.Draw(graphics, new Rectangle(0, 0, width, height), config, first, Palette.Create(false), dpi);
                    }
                }
                string settingsPath = Path.Combine(Path.GetTempPath(), "TaskbarSystemMonitor-settings-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    renderSettings.Save(settingsPath);
                    renderSettings.Alignment = "Center"; renderSettings.Theme = "Dark"; renderSettings.Interval = 5000; renderSettings.FirstRun = false;
                    renderSettings.Save(settingsPath); var loaded = Settings.Load(settingsPath);
                    if (loaded.Items.Count != renderSettings.Items.Count || loaded.Height != 32 || loaded.FontSize != 11 || loaded.Alignment != "Center" || loaded.Theme != "Dark" || loaded.Interval != 5000 || loaded.FirstRun) return 7;
                }
                finally { if (File.Exists(settingsPath)) File.Delete(settingsPath); }
                var invalid = new Settings { Height = 0, FontSize = 100, Interval = 0, Items = new System.Collections.Generic.List<string> { "unknown", "Cpu", "Cpu" } };
                invalid.Validate(); if (invalid.Height != 24 || invalid.FontSize != 11 || invalid.Interval != 1000 || invalid.Items.Count != 1) return 14;
                if (Array.Exists(args, x => x.StartsWith("--write-preview=", StringComparison.OrdinalIgnoreCase)))
                {
                    string previewPath = args.First(x => x.StartsWith("--write-preview=", StringComparison.OrdinalIgnoreCase)).Substring(16);
                    InfoTests.Preview(previewPath);
                }
                using (var settingsForm = new SettingsForm(new Settings()))
                using (var bitmap = new Bitmap(settingsForm.Width, settingsForm.Height))
                {
                    CreateHandles(settingsForm);
                    settingsForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                }
                var form = new DetailsForm(); var history = new History(); history.Add(second); form.SetData(second, history); using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height)) form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); form.Dispose();
                for (int i = 0; i < 140; i++) history.Add(second);
                if (history.Cpu.Count != 120 || history.Memory.Count != 120) return 15;
                return 0;
            }
            catch (Exception error) { Log.Error(error); return 10; }
        }
        private static void CreateHandles(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) CreateHandles(child);
        }
    }
}
