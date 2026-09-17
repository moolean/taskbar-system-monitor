using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal static class WorkTests
    {
        internal static Settings Demo()
        {
            var settings = new Settings { FirstRun = false };
            settings.WorkItems.Add(new WorkItem { Keyword = "发布计划", Notes = "整理发布说明\n检查安装与升级流程\n\n相关资料放在下方链接中。", Link = "https://example.com/release" });
            settings.WorkItems.Add(new WorkItem { Keyword = "数据复核", Notes = "核对输入与输出样例。" });
            settings.WorkItems.Add(new WorkItem { Keyword = "阅读清单" });
            return settings;
        }
        internal static int BarSample(string report, bool dark)
        {
            var settings = Demo(); settings.Theme = dark ? "Dark" : "Light";
            var sample = InfoTests.Demo(); int moves = 0;
            using (var dock = new DockForm(settings) { InspectionSample = true, ShowInTaskbar = true, Text = "WORK bar · Sample (no saved data)" })
            {
                Action refresh = delegate { sample.Briefing.WorkKeywords = settings.WorkItems.Select(x => x.Keyword).ToList(); sample.Briefing.WorkSummary = BriefingData.SummarizeWork(settings.WorkItems); dock.UpdateSnapshot(sample); };
                refresh();
                dock.WorkReorderRequested += delegate(int source, int target) { if (WorkOrder.Move(settings.WorkItems, source, target)) { moves++; refresh(); } };
                dock.BarMenu.Items.Add(new ToolStripSeparator());
                dock.BarMenu.Items.Add("Close sample", null, delegate { dock.Close(); });
                Application.Run(dock);
            }
            File.WriteAllText(report, "PASS: sample bar closed; no real configuration written. Reorders=" + moves + "; Order=" + string.Join(", ", settings.WorkItems.Select(x => x.Keyword)));
            return 0;
        }
        internal static void Run()
        {
            TestMigration();
            var sample = Demo();
            Require(BriefingData.SummarizeWork(sample.WorkItems) == "发布计划  ·  数据复核  ·  阅读清单", "Bar contains keywords only");
            sample.WorkItems.Add(new WorkItem { Keyword = "第四项" });
            var history = new WorkUndoHistory();
            using (var workspace = new WorkWorkspace(history))
            {
                workspace.ApplyTheme(sample); workspace.LoadItems(sample.WorkItems);
                var keyword = Find<TextBox>(workspace, "关键词");
                var notes = Find<TextBox>(workspace, "关键词笔记");
                var link = Find<TextBox>(workspace, "关键词链接");
                var search = Find<TextBox>(workspace, "搜索或新建关键词");
                var list = Find<ListBox>(workspace, "关键词列表");
                Require(!workspace.IsDirty, "Initial draft is clean");
                notes.Text = "Unsaved note"; list.SelectedIndex = 1; list.SelectedIndex = 0;
                Require(notes.Text == "Unsaved note" && workspace.IsDirty && sample.WorkItems[0].Notes != "Unsaved note", "Selection and timer refresh cannot overwrite private drafts");
                keyword.Text = "New keyword";
                search.Text = "数据";
                Require(list.Items.Count == 1 && keyword.Text == "数据复核", "Keyword filtering");
                workspace.MoveSelected(-1); Require(workspace.Snapshot()[0].Keyword == "New keyword", "Reorder disabled while filtering");
                search.Clear(); list.SelectedIndex = 1; workspace.MoveSelected(-1);
                Require(workspace.Snapshot()[0].Keyword == "数据复核" && list.SelectedIndex == 0, "Reorder retains selection");
                workspace.MoveItem(0, 3); Require(workspace.Snapshot()[3].Keyword == "数据复核", "Drag-style reorder takes one operation");
                search.Text = "   "; Require(!workspace.CommitQuickEntry() && workspace.Count == 4, "Blank input never creates an empty item");
                search.Text = "Keyword only"; Require(workspace.CommitQuickEntry(), "Enter creates directly from search");
                List<WorkItem> result; string error;
                Require(workspace.TryGetItems(out result, out error) && result.Last().Keyword == "Keyword only" && result.Last().Notes == "" && result.Last().Link == "", "Only keyword is required");
                search.Text = "keyword ONLY"; Require(workspace.CommitQuickEntry() && workspace.Count == 5, "Enter on exact match selects without duplicate");
                keyword.Text = ""; Require(!workspace.TryGetItems(out result, out error, false), "Blank rename cannot erase a saved item");
                keyword.Text = "Keyword only";
                link.Text = "https://"; Require(workspace.TryGetItems(out result, out error) && result.Last().Link == "https://", "Incomplete link doesn't block saving other edits");
                Require(!All(workspace).OfType<UiButton>().Single(x => x.Text == "打开 ↗").Enabled, "Incomplete link cannot execute");
                link.Text = "file:///C:/Windows/notepad.exe";
                Require(!All(workspace).OfType<UiButton>().Single(x => x.Text == "打开 ↗").Enabled, "Unsafe links remain non-executable text");
                link.Text = "https://example.com";
                Require(All(workspace).OfType<UiButton>().Single(x => x.Text == "打开 ↗").Enabled, "Web links can be explicitly opened");
                workspace.MarkSaved(); Require(!workspace.IsDirty, "Successful save clears dirty state");
                notes.Text = "Recover these notes";
                workspace.RemoveSelected(); Require(workspace.IsDirty && workspace.Count == 4, "Remove takes one action");
                Require(workspace.UndoRemove() && workspace.Snapshot().Last().Notes == "Recover these notes" && workspace.Snapshot().Last().Link == "https://example.com", "Undo restores complete deleted data and position");
                workspace.RemoveSelected();
                var remaining = workspace.Snapshot();
                workspace.LoadItems(remaining, false);
                Require(workspace.UndoRemove(), "Undo survives window reload within the same app session");
                search.Text = "does not match"; Require(list.Items.Count == 0, "No-results search");
                Require(workspace.CommitQuickEntry() && workspace.Snapshot().Last().Keyword == "does not match" && search.Text.Length == 0, "No-results Enter creates immediately");
                workspace.LoadItems(Enumerable.Range(0, 30).Select(i => new WorkItem { Keyword = "Item " + i }));
                search.Text = "Over limit";
                Require(!workspace.CommitQuickEntry() && workspace.Count == 30 && !workspace.IsDirty, "30 item limit");
                workspace.LoadItems(new WorkItem[0]); Require(workspace.Count == 0 && !workspace.IsDirty, "Empty workspace");
                Require(workspace.TryGetItems(out result, out error) && result.Count == 0, "Removing all is valid");
            }
            int saves = 0;
            using (var form = new WorkForm(Demo(), delegate { saves++; return false; }))
            {
                Require(Find<TextBox>(form, "关键词笔记").Text.Contains("\r\n"), "LF notes display with native line breaks");
                Find<TextBox>(form, "关键词笔记").Text = "Draft after failed save";
                Require(!form.SaveDraft() && saves == 1 && form.HasUnsavedChanges && Find<TextBox>(form, "关键词笔记").Text == "Draft after failed save", "Failed write retains draft");
                form.Dismiss(); Require(!form.IsDisposed && form.HasUnsavedChanges && !form.Animating, "Failed save never fades away the only draft");
            }
            using (var form = new WorkForm(Demo(), delegate(List<WorkItem> saved) { saves++; return saved[0].Keyword == "Saved keyword"; }))
            {
                Find<TextBox>(form, "关键词").Text = "Saved keyword";
                Require(form.SaveDraft() && saves == 3 && !form.HasUnsavedChanges, "Successful flush commits");
                Require(form.SaveDraft() && saves == 3, "Unchanged close performs no extra disk write");
                form.OpenItem(2); Require(Find<TextBox>(form, "关键词").Text == "阅读清单", "Direct bar entry selects the exact keyword");
            }
            TestAutosave();
            TestBarTargets();
            TestCompactLayout();
            TestCompactPanel();
            TestDirectDelete();
            TestReorder();
            TestWorkColors();
        }
        private static void TestReorder()
        {
            for (int count = 2; count <= 30; count++)
            for (int source = 0; source < count; source++)
            for (int slot = 0; slot <= count; slot++)
            {
                var items = Enumerable.Range(0, count).Select(i => new WorkItem { Keyword = "K" + i, Notes = "Note " + i, Link = "https://example.com/" + i }).ToList();
                var moved = items[source]; var others = items.Where(x => x != moved).ToArray();
                int target = WorkOrder.TargetIndex(source, slot, count); WorkOrder.Move(items, source, target);
                Require(items.Count == count && items[target] == moved && items.Where(x => x != moved).SequenceEqual(others), "Every insertion gap preserves the item and other relative order");
                Require(moved.Notes == "Note " + source && moved.Link.EndsWith("/" + source), "Reorder keeps notes and links");
            }
            Require(WorkOrder.TargetIndex(0, -1, 3) == -1 && WorkOrder.TargetIndex(3, 0, 3) == -1 && WorkOrder.TargetIndex(1, 4, 3) == -1, "Invalid drops are rejected");
            using (var workspace = new WorkWorkspace())
            {
                workspace.LoadItems(Demo().WorkItems); workspace.EnableCompact(); workspace.FocusQuickEntry();
                workspace.MoveItem(0, 2);
                Require(workspace.ListVisible && workspace.SelectedIndex == 2 && workspace.Snapshot()[2].Keyword == "发布计划", "Reorder stays in the list with its selection");
                Find<TextBox>(workspace, "搜索或新建关键词").Text = "计划"; workspace.MoveItem(2, 0);
                Require(workspace.Snapshot()[2].Keyword == "发布计划", "Filtered list cannot reorder invisible items");
            }
            List<WorkItem> saved = null;
            using (var form = new WorkForm(Demo(), delegate(List<WorkItem> items) { saved = items; return true; }))
            {
                form.OpenItem(1); Find<TextBox>(form, "关键词笔记").Text = "Pending note before bar reorder";
                Require(form.ReorderItem(0, 2) && saved[0].Keyword == "数据复核" && saved[0].Notes == "Pending note before bar reorder" && form.SelectedIndex == 0 && !form.Browsing, "Bar reorder flushes pending notes and retains the same open item");
            }
            using (var form = new WorkForm(Demo(), delegate { return false; }))
            {
                form.OpenItem(1); Find<TextBox>(form, "关键词笔记").Text = "Failed draft";
                Require(!form.ReorderItem(0, 2) && form.SelectedIndex == 1 && form.HasUnsavedChanges, "Failed draft save prevents reorder without losing notes");
            }
            var sample = InfoTests.Demo(); var frozen = sample.FrozenCopy(); sample.Briefing.WorkKeywords.Reverse();
            Require(frozen.Briefing.WorkKeywords[0] == "项目交付", "Drag layout copy is isolated from in-place snapshot changes");
            string path = Path.Combine(Path.GetTempPath(), "work-order-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var settings = Demo(); var moved = settings.WorkItems[0].Copy();
                WorkOrder.Move(settings.WorkItems, 0, 2); settings.Save(path); var loaded = Settings.Load(path);
                Require(loaded.WorkItems[2].Keyword == moved.Keyword && loaded.WorkItems[2].Notes.Replace("\r\n", "\n") == moved.Notes && loaded.WorkItems[2].Link == moved.Link, "Saved order survives reload with complete notes and link");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            using (var form = new WorkForm(Demo(), delegate { return false; }))
            {
                Require(!form.ReorderItem(0, 2) && form.HasUnsavedChanges, "Failed reorder write keeps the reordered draft for retry");
            }
        }
        private static double Luminance(Color color)
        {
            Func<byte, double> channel = delegate(byte value) { double v = value / 255.0; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); };
            return .2126 * channel(color.R) + .7152 * channel(color.G) + .0722 * channel(color.B);
        }
        private static void TestWorkColors()
        {
            foreach (bool light in new[] { true, false })
            {
                var palette = Palette.Create(light); var colors = WorkColors.From(palette);
                foreach (var fill in new[] { colors.Fill, colors.Hover })
                    Require((Math.Max(Luminance(fill), Luminance(colors.Text)) + .05) / (Math.Min(Luminance(fill), Luminance(colors.Text)) + .05) >= 4.5, "Work text remains readable in both themes");
                using (var normal = new Bitmap(900, 28)) using (var hovered = new Bitmap(900, 28))
                using (var g = Graphics.FromImage(normal)) using (var h = Graphics.FromImage(hovered))
                {
                    var config = new Settings { Items = new List<string> { "Tracks" } }; var sample = InfoTests.Demo(); var bounds = new Rectangle(0, 0, 900, 28);
                    BarRenderer.Draw(g, bounds, config, sample, palette, 96);
                    BarRenderer.Draw(h, bounds, config, sample, palette, 96, "Tracks", 0);
                    int hidden; var cell = BarRenderer.Layout(g, bounds, config, sample, 96, out hidden).Single();
                    var targets = BarRenderer.WorkTargets(g, cell, sample, config, 96);
                    bool different = false;
                    for (int x = 0; x < 900; x++) for (int y = 0; y < 28; y++)
                    {
                        if (targets[0].Bounds.Contains(x, y)) different |= normal.GetPixel(x, y) != hovered.GetPixel(x, y);
                        else Require(normal.GetPixel(x, y) == hovered.GetPixel(x, y), "Hover affects only the pointed keyword, not the entire work strip");
                    }
                    Require(different, "Keyword hover gives visible feedback");
                    Require(BarRenderer.WorkDropSlot(targets, cell.Bounds, new Point(targets[0].Bounds.Left, 14), 96) == 0, "Left half selects the gap before the item");
                    Require(BarRenderer.WorkDropSlot(targets, cell.Bounds, new Point(targets[1].Bounds.Right, 14), 96) == 2, "Right half selects the gap after the item");
                    Require(BarRenderer.WorkDropSlot(targets, cell.Bounds, new Point(targets[0].Bounds.Left, -40), 96) == -1, "Outside release cancels the drop");
                }
            }
        }
        private static void TestDirectDelete()
        {
            var settings = Demo(); List<WorkItem> saved = null;
            var history = new WorkUndoHistory();
            using (var form = new WorkForm(settings, delegate(List<WorkItem> items) { saved = items; return true; }, history))
            {
                var remove = Find<UiButton>(form, "删除当前关键词");
                var undo = Find<UiButton>(form, "撤销删除关键词");
                var click = typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                form.OpenItem(1);
                Find<TextBox>(form, "关键词笔记").Text = "Latest unsaved note";
                Find<TextBox>(form, "关键词链接").Text = "https://example.com/keep";
                click.Invoke(remove, new object[] { EventArgs.Empty });
                Require(!form.Browsing && form.SelectedIndex == 1 && Find<TextBox>(form, "关键词").Text == "阅读清单", "Direct delete keeps detail view on the next item");
                Require(form.SaveDraft() && saved.Count == 2 && saved.All(x => x.Keyword != "数据复核"), "Direct delete persists without visiting the list");
                click.Invoke(undo, new object[] { EventArgs.Empty });
                Require(form.SelectedIndex == 1 && form.SaveDraft() && saved.Count == 3 && saved[1].Keyword == "数据复核" && saved[1].Notes == "Latest unsaved note" && saved[1].Link == "https://example.com/keep", "Detail undo restores the latest draft, link and order after saving");
                form.OpenItem(2); click.Invoke(remove, new object[] { EventArgs.Empty });
                Require(form.SelectedIndex == 1, "Deleting the last item selects its previous neighbor");
                click.Invoke(remove, new object[] { EventArgs.Empty });
                click.Invoke(remove, new object[] { EventArgs.Empty });
                Require(form.Browsing && form.SelectedIndex == -1 && form.SaveDraft() && saved.Count == 0, "Deleting the final keyword opens quick entry");
                click.Invoke(Find<UiButton>(form, "撤销移除"), new object[] { EventArgs.Empty });
                Require(!form.Browsing && form.SaveDraft() && saved.Count == 1 && saved[0].Keyword == "发布计划", "Empty-list undo returns straight to restored notes");
            }
            using (var form = new WorkForm(settings, delegate { return false; }))
            {
                form.OpenItem(0);
                var click = typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                click.Invoke(Find<UiButton>(form, "删除当前关键词"), new object[] { EventArgs.Empty });
                Require(!form.SaveDraft() && form.HasUnsavedChanges, "A failed deletion write keeps the draft and recovery history");
                click.Invoke(Find<UiButton>(form, "撤销删除关键词"), new object[] { EventArgs.Empty });
                Require(!form.HasUnsavedChanges && Find<TextBox>(form, "关键词笔记").Text.Replace("\r\n", "\n") == settings.WorkItems[0].Notes, "Undo after failure restores the original complete item");
            }
            Require(settings.WorkItems.Count == 3, "Delete tests never mutate caller configuration");
        }
        private static void TestCompactPanel()
        {
            using (var workspace = new WorkWorkspace())
            {
                workspace.LoadItems(Demo().WorkItems); workspace.EnableCompact();
                workspace.SetCompactView(false, false); workspace.Size = new Size(396, workspace.PreferredCompactHeight);
                int shortHeight = workspace.PreferredCompactHeight;
                Require(shortHeight < 220 && !workspace.ListVisible && !workspace.LinkVisible, "Default panel is a compact single editor");
                var notes = Find<TextBox>(workspace, "关键词笔记"); string original = notes.Text;
                workspace.ToggleCompactLink(); Require(workspace.LinkVisible && workspace.PreferredCompactHeight == shortHeight + 62, "Link field expands only on demand");
                workspace.FocusQuickEntry(); Require(workspace.ListVisible && notes.Text == original, "List switch preserves note content");
                workspace.FocusNotes(); Require(!workspace.ListVisible && notes.Text == original, "Returning from list restores current editor");
                workspace.SetCompactView(false, false); notes.Text = string.Join("\r\n", Enumerable.Repeat("Long example note", 80));
                Require(workspace.PreferredCompactHeight > shortHeight && workspace.PreferredCompactHeight <= 272, "Long notes grow only up to the scrollable limit");
                workspace.LoadItems(Enumerable.Range(0, 30).Select(i => new WorkItem { Keyword = "Item " + i })); workspace.FocusQuickEntry();
                Require(workspace.PreferredCompactHeight <= 306, "Long lists scroll instead of filling the screen");
            }
            foreach (var area in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1280, 0, 1280, 980), new Rectangle(0, 0, 360, 520) })
            foreach (var point in new[] { area.Location, new Point(area.Right, area.Bottom), new Point(area.Left + area.Width / 2, area.Bottom - 8) })
            foreach (int dpi in new[] { 96, 120, 144, 192 })
                Require(area.Contains(PanelMotion.Place(point, new Size(BarRenderer.Scale(420, dpi), BarRenderer.Scale(354, dpi)), area)), "Popup stays within small and negative-coordinate work areas");
            var from = new Rectangle(20, 200, 420, 240); var to = new Rectangle(20, 140, 420, 300);
            Require(PanelMotion.Blend(from, to, 0) == from && PanelMotion.Blend(from, to, 1) == to, "Motion lands exactly at both endpoints");
            double previous = 0;
            for (int i = 0; i <= 100; i++) { double value = PanelMotion.Ease(i / 100.0); Require(value >= previous && value <= 1, "Motion is monotonic and never overshoots"); previous = value; }
        }
        private static void TestAutosave()
        {
            string path = Path.Combine(Path.GetTempPath(), "autosave-test-" + Guid.NewGuid().ToString("N") + ".xml");
            int writes = 0;
            try
            {
                using (var form = new WorkForm(Demo(), delegate(List<WorkItem> items)
                {
                    var config = new Settings { FirstRun = false }; config.WorkItems = items; config.Save(path); writes++; return true;
                }))
                {
                    var notes = Find<TextBox>(form, "关键词笔记");
                    notes.Text = "a"; notes.Text = "ab"; notes.Text = "  indented\r\n\r\n";
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 950) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
                    Require(writes == 1 && !form.HasUnsavedChanges, "Typing burst debounces to one automatic write");
                    Require(Settings.Load(path).WorkItems[0].Notes.Replace("\r\n", "\n") == "  indented\n\n", "Autosave preserves whitespace and trailing blank lines");
                    notes.Text = "Flush immediately before close";
                    Require(form.SaveDraft(true) && writes == 2 && Settings.Load(path).WorkItems[0].Notes == notes.Text, "Close flushes before debounce expires");
                }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private static void TestBarTargets()
        {
            using (var bitmap = new Bitmap(2200, 64)) using (var g = Graphics.FromImage(bitmap))
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            foreach (int fontSize in new[] { 8, 9, 11 })
            foreach (int width in new[] { 230, 460, 900, 2600 })
            foreach (int count in new[] { 0, 1, 3, 5, 10, 30 })
            {
                var config = new Settings { FontSize = fontSize }; var snapshot = new Snapshot();
                snapshot.Briefing.WorkKeywords = Enumerable.Range(0, count).Select(i => "关键词长名称 " + i).ToList();
                var cell = new MetricCell { Id = "Tracks", LabelWidth = BarRenderer.Scale(26, dpi), Bounds = new Rectangle(17, 5, BarRenderer.Scale(width, dpi), BarRenderer.Scale(24, dpi)) };
                var targets = BarRenderer.WorkTargets(g, cell, snapshot, config, dpi);
                Require(targets.Count > 0 && targets.Last().Index == -1, "Quick-entry plus always reachable");
                Require(targets.All(x => cell.Bounds.Contains(x.Bounds) && x.Bounds.Width > 0 && x.Bounds.Height > 0), "Click targets inside cell");
                for (int i = 1; i < targets.Count; i++) Require(targets[i].Bounds.Left - targets[i - 1].Bounds.Right == BarRenderer.Scale(3, dpi), "Targets stay adjacent without overlap or extra mouse travel");
                Require(targets[0].Bounds.Left == cell.Bounds.Left + cell.LabelWidth + BarRenderer.Scale(5, dpi) + BarRenderer.Scale(4, dpi), "Empty-state entry also stays next to the work label");
                Require(targets.Where(x => x.Index >= 0).All(x => x.Index < count && x.Text == snapshot.Briefing.WorkKeywords[x.Index]), "Label/index mapping is exact");
                int shown = targets.Count(x => x.Index >= 0);
                if (shown < count) Require(targets.Any(x => x.Index == -2 && x.Text == "+" + (count - shown)), "Hidden keywords have a full-list entry");
                Require(targets.Where(x => x.Index >= 0).Select(x => x.Index).SequenceEqual(Enumerable.Range(0, shown)), "Overflow preserves user order and click indices");
                var wide = new MetricCell { Id = cell.Id, LabelWidth = cell.LabelWidth, Bounds = new Rectangle(cell.Bounds.Location, new Size(BarRenderer.Scale(10000, dpi), cell.Bounds.Height)) };
                var all = BarRenderer.WorkTargets(g, wide, snapshot, config, dpi);
                Require(all.Count(x => x.Index >= 0) == count && all.All(x => x.Index != -2), "No fixed item-count limit when space is sufficient");
                int required = all.Last().Bounds.Right - wide.Bounds.Left + BarRenderer.Scale(5, dpi);
                Require((shown == count) == (required <= cell.Bounds.Width), "Fold only when complete content actually exceeds available width");
                // Exact-fit boundary catches premature +N reservation even when
                // another keyword would fit in the space held for that badge.
                wide.Bounds = new Rectangle(wide.Bounds.Location, new Size(required, wide.Bounds.Height));
                Require(BarRenderer.WorkTargets(g, wide, snapshot, config, dpi).Count(x => x.Index >= 0) == count, "Exact fit must show every keyword");
            }
        }
        private static void TestCompactLayout()
        {
            using (var bitmap = new Bitmap(2600, 64)) using (var g = Graphics.FromImage(bitmap))
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            foreach (int fontSize in new[] { 8, 9, 11 })
            {
                var config = new Settings { FontSize = fontSize }; var snapshot = InfoTests.Demo();
                snapshot.Briefing.WorkKeywords = Enumerable.Range(0, 8).Select(i => "工作" + i).ToList();
                int hidden;
                var narrow = BarRenderer.Layout(g, new Rectangle(0, 0, BarRenderer.Scale(1024, dpi), BarRenderer.Scale(24, dpi)), config, snapshot, dpi, out hidden);
                Require(hidden == 0, "Default modules fit a compact 1024 logical-pixel bar");
                var wide = BarRenderer.Layout(g, new Rectangle(0, 0, BarRenderer.Scale(2048, dpi), BarRenderer.Scale(24, dpi)), config, snapshot, dpi, out hidden);
                Require(hidden == 0, "Wide bar keeps all modules");
                foreach (var cell in wide.Where(x => x.Id != "Tracks"))
                    Require(cell.Bounds.Width == narrow.First(x => x.Id == cell.Id).Bounds.Width, "Non-work modules do not inflate on wide screens");
                var work = wide.Single(x => x.Id == "Tracks");
                Require(work.Bounds.Width - narrow.Single(x => x.Id == "Tracks").Bounds.Width == BarRenderer.Scale(1024, dpi), "All spare width belongs to keywords");
                Require(BarRenderer.WorkTargets(g, work, snapshot, config, dpi).Count(x => x.Index >= 0) == 8, "More than three keywords show in integrated bar layout");
                config.FillBar = false;
                var compact = BarRenderer.Layout(g, new Rectangle(0, 0, BarRenderer.Scale(2048, dpi), BarRenderer.Scale(24, dpi)), config, snapshot, dpi, out hidden);
                var compactWork = compact.Single(x => x.Id == "Tracks");
                Require(compactWork.Bounds.Width < work.Bounds.Width, "Non-fill mode takes only content width");
                Require(BarRenderer.WorkTargets(g, compactWork, snapshot, config, dpi).Count(x => x.Index >= 0) == 8, "Non-fill mode must not impose an artificial fold either");
            }
        }
        private static void TestMigration()
        {
            string path = Path.Combine(Path.GetTempPath(), "keyword-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var old = XElement.Parse("<settings version='4' geoEnabled='false' alwaysOnTop='false'><item>Tracks</item><futureSetting>keep</futureSetting><workItems><work title='旧标题' status='完成' progress='not-a-number'><notes> keep all notes </notes><link>https://example.com</link></work><work keyword='New' title='Old' status='阻塞' progress='20'><notes>Second</notes></work></workItems></settings>");
                old.Save(path);
                var before = Settings.Load(path); Require(before.WorkItems.Count == 2 && before.WorkItems[0].Keyword == "旧标题", "Legacy title loads regardless of obsolete status/progress");
                Require(Settings.MigrateWorkKeywords(path), "Legacy work migrated");
                var migrated = XElement.Load(path);
                Require((string)migrated.Element("futureSetting") == "keep" && (string)migrated.Attribute("geoEnabled") == "false" && (string)migrated.Attribute("alwaysOnTop") == "false", "Unrelated settings retained");
                var items = migrated.Element("workItems").Elements("work").ToList();
                Require((string)items[0].Attribute("keyword") == "旧标题" && (string)items[0].Element("notes") == " keep all notes " && (string)items[0].Element("link") == "https://example.com", "Title, notes and link preserved");
                Require(items.All(x => x.Attribute("title") == null && x.Attribute("status") == null && x.Attribute("progress") == null), "Obsolete fields removed");
                Require((string)items[1].Attribute("keyword") == "New", "Existing keyword wins");
                string clean = File.ReadAllText(path); Require(!Settings.MigrateWorkKeywords(path) && File.ReadAllText(path) == clean, "Keyword migration is idempotent");
                var loaded = Settings.Load(path);
                Require(BriefingData.SummarizeWork(loaded.WorkItems).Contains("旧标题"), "Previously completed items remain visible");
                loaded.Save(path); Require(XElement.Load(path).Descendants("work").All(x => x.Attribute("keyword") != null && x.Attribute("progress") == null && x.Attribute("status") == null), "New saves contain no status/progress");
                File.WriteAllText(path, "<settings>");
                bool rejected = false; try { Settings.MigrateWorkKeywords(path); } catch (System.Xml.XmlException) { rejected = true; }
                Require(rejected && File.ReadAllText(path) == "<settings>", "Malformed migration is non-destructive");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        internal static T Find<T>(Control parent, string name) where T : Control
        { return All(parent).OfType<T>().First(x => x.AccessibleName == name); }
        private static IEnumerable<Control> All(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Work test: " + message); }
    }
}
