using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal static class CityTests
    {
        internal static void Run()
        {
            Require(InfoHub.CityRefreshSeconds == 60 && !new Settings().GeoEnabled);
            Require(InfoHub.ParseLocation("{\"success\":true,\"city\":\"  Beijing  \"}").Short == "Beijing");
            foreach (var json in new[] { "{\"success\":false}", "{\"success\":true,\"region\":\"Province\"}", "{\"success\":true,\"city\":\"  \"}" })
            {
                bool rejected = false;
                try { InfoHub.ParseLocation(json); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
            string path = Path.Combine(Path.GetTempPath(), "monitor-city-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var original = XElement.Parse("<settings geoEnabled='false'><item>Tracks</item><workItems><work keyword='A'><notes>  note  </notes></work></workItems><future>keep</future></settings>");
                original.Save(path); Require(Settings.EnableExitCity(path) && !Settings.EnableExitCity(path));
                original.SetAttributeValue("geoEnabled", true); Require(XNode.DeepEquals(original, XElement.Load(path)));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            using (var bitmap = new Bitmap(2200, 50)) using (var g = Graphics.FromImage(bitmap))
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            foreach (int width in new[] { 280, 900, 1920 })
            foreach (string align in new[] { "Left", "Center", "Right" })
            {
                var bounds = new Rectangle(7, 2, BarRenderer.Scale(width, dpi), BarRenderer.Scale(24, dpi));
                var settings = new Settings { Alignment = align, GeoEnabled = true, FillBar = false };
                var sample = InfoTests.Demo();
                int hidden; var cells = BarRenderer.Layout(g, bounds, settings, sample, dpi, out hidden);
                var menu = BarRenderer.MenuBounds(bounds, settings, dpi); var city = cells.Last();
                Require(city.Id == "City" && BarRenderer.IsReadOnly(city.Id) && city.Value == "Singapore" && sample.Value("Ip") == sample.LocalIp);
                Require(city.Bounds.Right == bounds.Right && menu.Right == city.Bounds.Left && cells.Take(cells.Count - 1).All(c => c.Bounds.Right <= menu.Left));
                sample.Briefing.Location = new Reading("A very long city name here", "test", true);
                Require(BarRenderer.Layout(g, bounds, settings, sample, dpi, out hidden).Last().Bounds == city.Bounds);
                settings.GeoEnabled = false; Require(!BarRenderer.Layout(g, bounds, settings, sample, dpi, out hidden).Any(c => c.Id == "City"));
                Require(BarRenderer.MenuBounds(bounds, settings, dpi).Right == bounds.Right);
            }
        }
        private static void Require(bool ok) { if (!ok) throw new InvalidOperationException("Exit city regression failed."); }
    }
}
