using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class WorkTextBox : TextBox
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr SendCue(IntPtr window, uint message, IntPtr wParam, string text);
        internal string Cue = "";
        internal bool Composing { get; private set; }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Multiline && Cue.Length > 0) SendCue(Handle, 0x1501, new IntPtr(1), Cue);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x10D) Composing = true; // WM_IME_STARTCOMPOSITION
            base.WndProc(ref m);
            if (m.Msg == 0x10E) Composing = false; // WM_IME_ENDCOMPOSITION
        }
    }
    internal sealed class RemovedKeyword
    {
        internal WorkItem Item;
        internal int Index;
    }
    internal sealed class WorkUndoHistory
    {
        internal readonly List<RemovedKeyword> Removed = new List<RemovedKeyword>();
    }
    internal sealed class WorkWorkspace : UserControl
    {
        private List<WorkItem> items = new List<WorkItem>();
        private string baseline = "";
        private bool updating, dragging, suppressListClick;
        private int dropSlot = -1;
        private readonly Stopwatch dragScroll = Stopwatch.StartNew();
        internal bool Reordering { get { return dragging; } }
        private Point dragStart;
        private WorkItem dragged;
        private readonly WorkUndoHistory history;
        private readonly SettingsCard sidebar, editor;
        private readonly Panel gap;
        private readonly Panel empty;
        private readonly Label emptyText, count, hint, linkHint, notesLabel, linkLabel;
        private readonly WorkTextBox search, keyword, notes, link;
        private readonly ListBox list;
        private readonly UiButton add, remove, up, down, open, undo;
        private SettingsColors colors;
        internal event Action Changed;
        internal event Action ViewChanged;
        private bool compact, compactList = true, compactLink;
        private int compactDpi = 96;
        internal bool ListVisible { get { return compactList; } }
        internal bool LinkVisible { get { return compactLink; } }
        internal bool HasLink { get { return Selected != null && !string.IsNullOrWhiteSpace(Selected.Link); } }
        internal bool CanUndoRemove { get { return history.Removed.Count > 0 && items.Count < 30; } }
        internal bool IsDirty { get { return Signature(items) != baseline; } }
        internal bool IsComposing { get { return search.Composing || keyword.Composing || notes.Composing || link.Composing; } }
        internal int Count { get { return items.Count; } }
        internal int SelectedIndex { get { return Selected == null ? -1 : items.IndexOf(Selected); } }
        private WorkItem Selected { get { return list.SelectedItem as WorkItem; } }

        internal WorkWorkspace(WorkUndoHistory undoHistory = null)
        {
            history = undoHistory ?? new WorkUndoHistory();
            AutoScaleMode = AutoScaleMode.None; Font = new Font("Segoe UI", 9.5f);
            Size = new Size(720, 400);
            sidebar = new SettingsCard { Dock = DockStyle.Left, Width = 220 }; Controls.Add(sidebar);
            gap = new Panel { Dock = DockStyle.Left, Width = 12 }; Controls.Add(gap); gap.BringToFront();
            editor = new SettingsCard { Dock = DockStyle.Fill }; Controls.Add(editor); editor.BringToFront();

            count = SettingsUi.Label(sidebar, "搜索 / 新建关键词", 14, 14, 192, 24, 10);
            search = Input(sidebar, "搜索或新建关键词", 12, 44, 154, 34, false); search.MaxLength = 150;
            add = SettingsUi.Button(sidebar, "＋", 174, 44, 34, true); add.AccessibleName = "添加输入的关键词"; add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hint = SettingsUi.Label(sidebar, "输入关键词，Enter 添加", 14, 86, 192, 22, 8.5f, true);
            list = new ListBox { AccessibleName = "关键词列表", Location = new Point(12, 116), Size = new Size(196, 224),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36, AllowDrop = true };
            sidebar.Controls.Add(list); list.DrawItem += DrawKeyword;
            list.FontChanged += delegate { list.ItemHeight = Math.Max(36, list.Font.Height * 2 + 6); };
            up = SettingsUi.Button(sidebar, "↑", 12, 354, 32); up.AccessibleName = "关键词上移"; up.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            down = SettingsUi.Button(sidebar, "↓", 48, 354, 32); down.AccessibleName = "关键词下移"; down.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            remove = SettingsUi.Button(sidebar, "移除", 84, 354, 58); remove.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            undo = SettingsUi.Button(sidebar, "撤销", 148, 354, 60); undo.AccessibleName = "撤销移除"; undo.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            keyword = Input(editor, "关键词", 20, 20, 448, 38, false); keyword.MaxLength = 150; keyword.Font = new Font("Segoe UI", 13);
            notesLabel = SettingsUi.Label(editor, "笔记", 20, 76, 400, 22, 10);
            notes = Input(editor, "关键词笔记", 20, 104, 448, 178, true); notes.MaxLength = 8000;
            notes.Parent.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            linkLabel = SettingsUi.Label(editor, "关联链接 · 可选", 20, 298, 400, 22, 9, true); linkLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            link = Input(editor, "关键词链接", 20, 326, 356, 34, false); link.MaxLength = 2000; link.Parent.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            open = SettingsUi.Button(editor, "打开 ↗", 388, 326, 80); open.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            linkHint = SettingsUi.Label(editor, "链接只在点击“打开”时访问。", 20, 370, 448, 22, 8.5f, true); linkHint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            empty = new Panel { Dock = DockStyle.Fill }; editor.Controls.Add(empty); empty.BringToFront();
            emptyText = SettingsUi.Label(empty, "", 24, 136, 440, 120, 12, true); emptyText.TextAlign = ContentAlignment.MiddleCenter; emptyText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            add.Click += delegate { CommitQuickEntry(); };
            up.Click += delegate { MoveSelected(-1); }; down.Click += delegate { MoveSelected(1); };
            remove.Click += delegate { RemoveSelected(); }; undo.Click += delegate { UndoRemove(); };
            search.TextChanged += delegate { if (!updating) RebuildList(Selected); };
            search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (search.Composing) return;
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitQuickEntry(); }
                else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { e.SuppressKeyPress = true; list.Focus(); }
            };
            list.SelectedIndexChanged += delegate { if (!updating) ShowSelection(); };
            list.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && !dragging && !suppressListClick) FocusNotes(); };
            list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FocusNotes(); }
                else if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; RemoveSelected(); }
                else if (e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)) { e.SuppressKeyPress = true; MoveSelected(e.KeyCode == Keys.Up ? -1 : 1); }
            };
            list.MouseDown += delegate(object sender, MouseEventArgs e)
            { suppressListClick = false; dragStart = e.Location; int index = list.IndexFromPoint(e.Location); dragged = e.Button == MouseButtons.Left && index >= 0 ? list.Items[index] as WorkItem : null; };
            list.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (dragged == null || search.Text.Length > 0 || e.Button != MouseButtons.Left) return;
                var threshold = new Rectangle(dragStart.X - SystemInformation.DragSize.Width / 2, dragStart.Y - SystemInformation.DragSize.Height / 2, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
                if (threshold.Contains(e.Location)) return;
                dragging = suppressListClick = true;
                try { list.DoDragDrop(dragged, DragDropEffects.Move); }
                finally { dragged = null; dragging = false; dropSlot = -1; list.Invalidate(); }
            };
            list.DragEnter += PreviewDrop;
            list.DragOver += PreviewDrop;
            list.DragLeave += delegate { dropSlot = -1; list.Invalidate(); };
            list.DragDrop += delegate(object sender, DragEventArgs e)
            {
                var item = e.Data.GetData(typeof(WorkItem)) as WorkItem;
                if (item == null || item != dragged || !items.Contains(item) || search.Text.Length > 0) return;
                int source = items.IndexOf(item);
                int target = WorkOrder.TargetIndex(source, ListDropSlot(list.PointToClient(new Point(e.X, e.Y))), items.Count);
                MoveItem(source, target); dropSlot = -1; list.Invalidate();
            };
            keyword.TextChanged += Edit; notes.TextChanged += Edit; link.TextChanged += Edit;
            keyword.KeyDown += delegate(object sender, KeyEventArgs e) { if (!keyword.Composing && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FocusNotes(); } };
            open.Click += delegate
            {
                string target = link.Text.Trim(); if (!ModulePopup.IsWebLink(target)) return;
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
                catch { linkHint.Text = "无法打开，请检查默认浏览器。"; }
            };
            LoadItems(new WorkItem[0], false);
        }
        private static WorkTextBox Input(Control parent, string name, int x, int y, int width, int height, bool multiline)
        {
            var frame = new SettingsCard { Location = new Point(x, y), Size = new Size(width, height), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            parent.Controls.Add(frame);
            var box = new WorkTextBox { AccessibleName = name, BorderStyle = BorderStyle.None, Location = new Point(10, 8), Size = new Size(width - 20, height - 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Multiline = multiline,
                AcceptsReturn = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
            frame.Controls.Add(box); return box;
        }
        internal void ApplyTheme(Settings settings)
        {
            colors = SettingsColors.From(settings); SettingsUi.Theme(this, colors);
            if (compact) { keyword.BackColor = list.BackColor = colors.Background; }
            list.Invalidate();
        }
        internal void LoadItems(IEnumerable<WorkItem> values, bool resetUndo = true)
        {
            items = values.Select(x => x.Copy()).ToList(); baseline = Signature(items);
            if (resetUndo) history.Removed.Clear();
            updating = true; search.Clear(); updating = false; RebuildList(items.FirstOrDefault());
        }
        internal List<WorkItem> Snapshot() { return items.Select(x => x.Copy()).ToList(); }
        internal void MarkSaved() { baseline = Signature(items); Notify(); }
        private static string Signature(IEnumerable<WorkItem> values) { return new System.Xml.Linq.XElement("workItems", values.Select(x => x.Save())).ToString(); }
        internal bool TryGetItems(out List<WorkItem> value, out string error, bool focusError = true)
        {
            value = Snapshot(); error = "";
            for (int i = 0; i < value.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(value[i].Keyword))
                {
                    error = "关键词不能为空，内容仍保留在窗口中。";
                    if (focusError) { updating = true; search.Clear(); updating = false; RebuildList(items[i]); if (compact) SetCompactView(false, compactLink); keyword.Focus(); }
                    return false;
                }
                // Partial links can be saved as text without blocking typing.
                // The Open action alone enforces the http/https safety boundary.
                value[i].Validate();
            }
            return true;
        }
        internal void FocusQuickEntry() { if (compact) SetCompactView(true, compactLink); search.Focus(); search.SelectAll(); }
        internal void FocusNotes() { if (Selected != null) { if (compact) SetCompactView(false, compactLink); notes.Focus(); notes.SelectionStart = notes.TextLength; } }
        internal void SelectItem(int index)
        {
            if (index < 0 || index >= items.Count) { FocusQuickEntry(); return; }
            updating = true; search.Clear(); updating = false; RebuildList(items[index]); FocusNotes();
        }
        internal bool CommitQuickEntry()
        {
            if (search.Composing) return false;
            string text = search.Text.Trim(); if (text.Length == 0) { FocusQuickEntry(); return false; }
            var existing = items.FirstOrDefault(x => string.Equals(x.Keyword.Trim(), text, StringComparison.CurrentCultureIgnoreCase));
            if (existing != null)
            { updating = true; search.Clear(); updating = false; RebuildList(existing); FocusNotes(); return true; }
            if (items.Count >= 30) { hint.Text = "已达 30 项上限，可移除旧项"; return false; }
            var item = new WorkItem { Keyword = text }; items.Add(item);
            updating = true; search.Clear(); updating = false; RebuildList(item); FocusNotes(); Notify(); return true;
        }
        internal void RemoveSelected()
        {
            var item = Selected; if (item == null) return;
            int index = items.IndexOf(item);
            history.Removed.Add(new RemovedKeyword { Item = item.Copy(), Index = index });
            if (history.Removed.Count > 30) history.Removed.RemoveAt(0);
            items.Remove(item);
            RebuildList(items.Count == 0 ? null : items[Math.Min(index, items.Count - 1)]); Notify();
        }
        internal bool UndoRemove()
        {
            if (history.Removed.Count == 0 || items.Count >= 30) return false;
            var removed = history.Removed[history.Removed.Count - 1]; history.Removed.RemoveAt(history.Removed.Count - 1);
            var item = removed.Item.Copy(); items.Insert(Math.Min(removed.Index, items.Count), item);
            updating = true; search.Clear(); updating = false; RebuildList(item); FocusNotes(); Notify(); return true;
        }
        internal void MoveSelected(int offset) { MoveItem(SelectedIndex, SelectedIndex + offset); }
        internal void MoveItem(int index, int target)
        {
            if (search.Text.Length > 0 || index < 0 || target < 0 || index >= items.Count || target >= items.Count || index == target) return;
            var item = items[index]; WorkOrder.Move(items, index, target); RebuildList(item); Notify();
        }
        private int ListDropSlot(Point point)
        {
            if (!list.ClientRectangle.Contains(point) || items.Count == 0) return -1;
            int index = list.IndexFromPoint(point);
            if (index < 0) return items.Count;
            var row = list.GetItemRectangle(index);
            return index + (point.Y >= row.Top + row.Height / 2 ? 1 : 0);
        }
        private void PreviewDrop(object sender, DragEventArgs e)
        {
            bool valid = dragged != null && search.Text.Length == 0 && e.Data.GetDataPresent(typeof(WorkItem)) && e.Data.GetData(typeof(WorkItem)) == dragged && items.Contains(dragged);
            e.Effect = valid ? DragDropEffects.Move : DragDropEffects.None;
            var point = list.PointToClient(new Point(e.X, e.Y));
            if (valid && dragScroll.ElapsedMilliseconds >= 120 && list.ClientRectangle.Contains(point))
            {
                int direction = point.Y < Px(18) ? -1 : point.Y > list.Height - Px(18) ? 1 : 0;
                if (direction != 0) { list.TopIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.TopIndex + direction)); dragScroll.Restart(); }
            }
            int slot = valid ? ListDropSlot(point) : -1;
            if (dropSlot != slot) { dropSlot = slot; list.Invalidate(); }
        }
        private void RebuildList(WorkItem selected)
        {
            updating = true; list.BeginUpdate(); list.Items.Clear();
            string query = search.Text.Trim();
            foreach (var item in items.Where(x => query.Length == 0 || x.Keyword.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)) list.Items.Add(item);
            if (selected != null && list.Items.Contains(selected)) list.SelectedItem = selected;
            else if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.EndUpdate(); updating = false; ShowSelection();
        }
        private void ShowSelection()
        {
            updating = true; var item = Selected;
            keyword.Text = item == null ? "" : item.Keyword;
            notes.Text = item == null ? "" : item.Notes.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            link.Text = item == null ? "" : item.Link;
            empty.Visible = item == null;
            emptyText.Text = items.Count == 0 ? "在左侧输入一个关键词\n按 Enter，开始记录。" : "没有找到这个关键词\n按 Enter，直接新建。";
            updating = false; UpdateActions(); NotifyView();
        }
        private void Edit(object sender, EventArgs e)
        {
            if (updating || Selected == null) return;
            Selected.Keyword = keyword.Text; Selected.Notes = notes.Text; Selected.Link = link.Text;
            list.Invalidate(); UpdateActions(); Notify(); NotifyView();
        }
        private void UpdateActions()
        {
            int index = SelectedIndex; count.Text = "关键词  " + items.Count + " / 30" + (compact ? search.Text.Length == 0 ? "  ·  拖动排序" : "  ·  清空搜索可排序" : "");
            hint.Text = search.Text.Trim().Length == 0 ? "输入搜索 · Enter 新建" : "Enter 打开同名项，或新建";
            add.Enabled = search.Text.Trim().Length > 0; remove.Enabled = index >= 0;
            undo.Enabled = history.Removed.Count > 0 && items.Count < 30;
            up.Enabled = search.Text.Length == 0 && index > 0;
            down.Enabled = search.Text.Length == 0 && index >= 0 && index < items.Count - 1;
            open.Enabled = ModulePopup.IsWebLink(link.Text.Trim());
            linkHint.Text = link.Text.Trim().Length > 0 && !open.Enabled ? "链接文字会保存；补全 http / https 后可打开。" : "链接只在点击“打开”时访问。";
        }
        private void Notify() { if (Changed != null) Changed(); }
        private void NotifyView() { if (compact) { LayoutCompact(); if (ViewChanged != null) ViewChanged(); } }
        internal void EnableCompact()
        {
            compact = true; gap.Visible = false; sidebar.Dock = editor.Dock = DockStyle.None;
            sidebar.Frameless = editor.Frameless = ((SettingsCard)keyword.Parent).Frameless = true;
            search.Cue = "搜索关键词，Enter 新建"; link.Cue = "https://…";
            foreach (Control parent in new Control[] { sidebar, editor }) foreach (Control child in parent.Controls) child.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            notesLabel.Visible = linkLabel.Visible = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            keyword.Font = new Font("Microsoft YaHei UI", 10.5f);
            notes.Font = link.Font = search.Font = list.Font = new Font("Microsoft YaHei UI", 9f);
            count.Font = new Font("Microsoft YaHei UI", 8.5f); linkHint.Font = new Font("Microsoft YaHei UI", 8f);
            open.Text = "↗"; open.AccessibleName = "打开关键词链接";
            open.Quiet = true;
            Resize += delegate { LayoutCompact(); }; UpdateActions(); NotifyView();
        }
        internal void SetCompactView(bool showList, bool showLink)
        {
            bool changed = compactList != showList || compactLink != showLink;
            compactList = showList; compactLink = showLink;
            if (changed) NotifyView();
        }
        internal void ToggleCompactLink()
        {
            if (Selected == null) return;
            SetCompactView(false, !compactLink);
            if (compactLink) { link.Focus(); link.SelectionStart = link.TextLength; } else FocusNotes();
        }
        internal void SetCompactDpi(int dpi) { compactDpi = dpi; LayoutCompact(); }
        private int Px(int value) { return (int)Math.Round(value * compactDpi / 96.0); }
        internal int PreferredCompactHeight
        {
            get
            {
                if (compactList) return Px(126 + Math.Max(2, Math.Min(6, list.Items.Count)) * 30);
                int measured = TextRenderer.MeasureText(notes.Text.Length == 0 ? " " : notes.Text, notes.Font,
                    new Size(Math.Max(40, Px(350)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding).Height;
                int noteHeight = Math.Max(96, Math.Min(208, (int)Math.Ceiling((measured * 96.0 / compactDpi + 20) / 24) * 24));
                return Px(64 + noteHeight + (compactLink ? 62 : 0));
            }
        }
        private void LayoutCompact()
        {
            if (!compact || keyword == null) return;
            SuspendLayout();
            sidebar.Visible = compactList; editor.Visible = !compactList;
            sidebar.Bounds = editor.Bounds = ClientRectangle;
            int width = Width, height = Height;
            count.SetBounds(Px(12), Px(8), width - Px(24), Px(20));
            search.Parent.SetBounds(Px(12), Px(32), Math.Max(40, width - Px(62)), Px(32));
            add.SetBounds(width - Px(42), Px(32), Px(30), Px(32));
            hint.Visible = false;
            list.SetBounds(Px(8), Px(74), Math.Max(40, width - Px(16)), Math.Max(Px(30), height - Px(122)));
            list.ItemHeight = Px(30);
            up.SetBounds(Px(12), height - Px(38), Px(28), Px(28));
            down.SetBounds(Px(44), height - Px(38), Px(28), Px(28));
            remove.SetBounds(Px(80), height - Px(38), Px(56), Px(28));
            undo.SetBounds(width - Px(68), height - Px(38), Px(56), Px(28));
            bool canOpen = ModulePopup.IsWebLink(link.Text.Trim());
            keyword.Parent.SetBounds(Px(10), Px(10), Math.Max(40, width - Px(canOpen ? 56 : 20)), Px(34));
            open.Visible = canOpen; open.SetBounds(width - Px(40), Px(10), Px(30), Px(34));
            notes.Parent.SetBounds(Px(10), Px(52), Math.Max(40, width - Px(20)), Math.Max(Px(36), height - Px(64 + (compactLink ? 62 : 0))));
            link.Parent.Visible = linkHint.Visible = compactLink;
            link.Parent.SetBounds(Px(10), height - Px(66), Math.Max(40, width - Px(20)), Px(32));
            linkHint.SetBounds(Px(12), height - Px(28), Math.Max(40, width - Px(24)), Px(20));
            foreach (var box in new[] { search, keyword, notes, link })
                box.SetBounds(Px(8), Px(7), Math.Max(24, box.Parent.Width - Px(16)), Math.Max(Px(16), box.Parent.Height - Px(14)));
            int contentHeight = TextRenderer.MeasureText(notes.Text.Length == 0 ? " " : notes.Text, notes.Font,
                new Size(Math.Max(40, notes.Width - SystemInformation.VerticalScrollBarWidth), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding).Height;
            var scroll = contentHeight > notes.ClientSize.Height ? ScrollBars.Vertical : ScrollBars.None;
            if (!notes.Composing && notes.ScrollBars != scroll) notes.ScrollBars = scroll;
            emptyText.SetBounds(Px(16), Px(36), Math.Max(40, width - Px(32)), Px(70));
            ResumeLayout(false);
        }
        private void DrawKeyword(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var c = colors ?? SettingsColors.From(new Settings()); bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(compact ? c.Background : selected ? c.Selected : c.Card)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (compact && selected)
                using (var brush = new SolidBrush(c.Selected)) using (var path = SettingsUi.Round(Rectangle.Inflate(e.Bounds, -2, -2), 6)) e.Graphics.FillPath(brush, path);
            if (search.Text.Length == 0)
                using (var grip = new SolidBrush(c.Muted))
                    for (int row = -1; row <= 1; row++) for (int column = 0; column < 2; column++)
                        e.Graphics.FillEllipse(grip, e.Bounds.Left + Px(10 + column * 4), e.Bounds.Top + e.Bounds.Height / 2 + Px(row * 4) - 1, Px(2), Px(2));
            var textBounds = new Rectangle(e.Bounds.Left + Px(26), e.Bounds.Top + 2, Math.Max(1, e.Bounds.Width - Px(34)), e.Bounds.Height - 4);
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), list.Font, textBounds,
                selected && SystemInformation.HighContrast ? c.ActionText : c.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (dropSlot == e.Index || (dropSlot == list.Items.Count && e.Index == list.Items.Count - 1))
            {
                int y = dropSlot == e.Index ? e.Bounds.Top + 1 : e.Bounds.Bottom - 2;
                using (var pen = new Pen(c.Action, Px(2))) e.Graphics.DrawLine(pen, e.Bounds.Left + Px(6), y, e.Bounds.Right - Px(6), y);
            }
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
    }

    internal sealed class WorkForm : DpiDialog
    {
        private readonly WorkWorkspace workspace;
        private readonly UiButton retry, browse, newItem, linkToggle, pin, close, deleteItem, undoDelete;
        private readonly Label status, caption;
        private readonly SettingsColors colors;
        private readonly Func<List<WorkItem>, bool> persist;
        private readonly Timer autosave = new Timer { Interval = 650 };
        private readonly Timer motion = new Timer { Interval = 15 };
        private readonly Stopwatch motionClock = new Stopwatch();
        private Rectangle motionFrom, motionTo;
        private double opacityFrom, opacityTo;
        private int motionDuration, dpi = 96;
        private Action motionDone;
        private Point anchor;
        private bool saving, ready, moving, applyingBounds, dismissing;
        private int requestedIndex = -1;
        internal int SelectedIndex { get { return workspace.SelectedIndex; } }
        internal bool Animating { get { return motion.Enabled; } }
        internal bool Browsing { get { return workspace.ListVisible; } }
        internal bool KeepOpen { get { return pin.Selected; } set { pin.Selected = value; pin.Invalidate(); } }
        internal WorkForm(Settings settings, Func<List<WorkItem>, bool> saveItems, WorkUndoHistory undoHistory = null)
        {
            persist = saveItems; SuspendLayout();
            Text = "工作追踪 · 随手记"; ClientSize = new Size(420, 242);
            FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Font = new Font("Microsoft YaHei UI", 9f); DoubleBuffered = true;
            caption = SettingsUi.Label(this, "随手记", 14, 11, 142, 24, 10);
            caption.Font = new Font("Microsoft YaHei UI", 9.5f);
            caption.Cursor = Cursors.SizeAll;
            caption.MouseDown += DragPanel;
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Y < Px(42)) DragPanel(sender, e); };
            browse = SettingsUi.Button(this, "列表", 188, 8, 54); browse.AccessibleName = "切换关键词列表";
            browse.Click += delegate { if (workspace.ListVisible && workspace.SelectedIndex >= 0) workspace.FocusNotes(); else workspace.FocusQuickEntry(); };
            newItem = SettingsUi.Button(this, "＋", 248, 8, 32); newItem.AccessibleName = "快速新建关键词";
            newItem.Click += delegate { workspace.FocusQuickEntry(); };
            linkToggle = SettingsUi.Button(this, "链接", 286, 8, 72); linkToggle.AccessibleName = "展开或收起链接";
            linkToggle.Click += delegate { workspace.ToggleCompactLink(); };
            pin = SettingsUi.Button(this, "固定", 316, 8, 48); pin.AccessibleName = "保持面板展开（不置顶）";
            pin.Click += delegate { KeepOpen = !KeepOpen; };
            close = SettingsUi.Button(this, "×", 370, 8, 32); close.AccessibleName = "收起随手记"; close.Click += delegate { Dismiss(); };
            foreach (var button in new[] { browse, newItem, linkToggle, pin, close }) button.Quiet = true;
            workspace = new WorkWorkspace(undoHistory) { Location = new Point(12, 44), Size = new Size(396, 160) };
            Controls.Add(workspace); workspace.LoadItems(settings.WorkItems, false); workspace.EnableCompact();
            status = SettingsUi.Label(this, "自动保存 · Esc 收起", 14, 214, 286, 22, 8.5f, true);
            status.Font = new Font("Microsoft YaHei UI", 8.5f);
            retry = SettingsUi.Button(this, "重试", 344, 208, 62); retry.Visible = false; retry.Click += delegate { SaveDraft(); };
            deleteItem = SettingsUi.Button(this, "删除", 354, 210, 52);
            deleteItem.AccessibleName = "删除当前关键词"; deleteItem.Quiet = true; deleteItem.Danger = true;
            deleteItem.Click += delegate
            {
                if (workspace.IsComposing) return;
                workspace.RemoveSelected();
                if (workspace.Count == 0) workspace.FocusQuickEntry(); else workspace.FocusNotes();
            };
            undoDelete = SettingsUi.Button(this, "撤销", 298, 210, 52);
            undoDelete.AccessibleName = "撤销删除关键词"; undoDelete.Quiet = true;
            undoDelete.Click += delegate { if (!workspace.IsComposing) workspace.UndoRemove(); };
            workspace.Changed += OnDraftChanged;
            workspace.ViewChanged += delegate { UpdatePanel(true); };
            autosave.Tick += delegate { autosave.Stop(); if (workspace.IsComposing) { autosave.Start(); return; } SaveDraft(); };
            motion.Tick += delegate { StepMotion(); };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (workspace.IsComposing) return;
                if (e.Control && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.S)) { e.SuppressKeyPress = true; SaveDraft(); }
                else if (e.Control && (e.KeyCode == Keys.N || e.KeyCode == Keys.F)) { e.SuppressKeyPress = true; workspace.FocusQuickEntry(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Dismiss(); }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                // A successful flush needs no confirmation. Failed writes never
                // silently discard the only in-memory copy.
                if (e.CloseReason == CloseReason.UserClosing && !SaveDraft(true)) e.Cancel = true;
            };
            colors = SettingsColors.From(settings); SettingsUi.Theme(this, colors); workspace.ApplyTheme(settings);
            ResumeLayout(true);
        }
        private int Px(int value) { return (int)Math.Round(value * dpi / 96.0); }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e); dpi = Native.Dpi(Handle); workspace.SetCompactDpi(dpi);
            anchor = new Point(Cursor.Position.X, Screen.FromPoint(Cursor.Position).WorkingArea.Bottom - Px(6));
            UpdatePanel(false);
            if (PanelMotion.Enabled) Opacity = 0.01;
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (requestedIndex >= 0) workspace.SelectItem(requestedIndex); else workspace.FocusQuickEntry();
            UpdatePanel(false); ready = true;
            if (PanelMotion.Enabled)
            {
                var target = Bounds; applyingBounds = true; Top += Px(8); Opacity = 0.01; applyingBounds = false;
                Animate(target, 1, 150, null);
            }
        }
        internal void OpenItem(int index)
        {
            // Shown can be queued after Show returns; preserve the click target
            // so initial focus cannot jump back to search on the next message.
            requestedIndex = index;
            if (dismissing) { motion.Stop(); motionDone = null; dismissing = false; workspace.Enabled = true; Opacity = 1; }
            Activate();
            if (index >= 0) { workspace.SetCompactView(false, false); workspace.SelectItem(index); } else workspace.FocusQuickEntry();
            UpdatePanel(ready);
        }
        internal bool ReorderItem(int source, int target)
        {
            if (workspace.IsComposing || !SaveDraft(true)) return false;
            int selected = workspace.SelectedIndex;
            bool browsing = workspace.ListVisible;
            // Clear only the search filter; keep the same notes and current view.
            workspace.SelectItem(source); workspace.MoveItem(source, target);
            if (selected == source) selected = target;
            else if (source < selected && target >= selected) selected--;
            else if (source > selected && target <= selected) selected++;
            workspace.SelectItem(selected);
            if (browsing) workspace.FocusQuickEntry();
            return SaveDraft(true);
        }
        private void UpdatePanel(bool animate)
        {
            if (workspace == null || status == null || dismissing) return;
            browse.Text = workspace.ListVisible ? "返回" : "列表";
            linkToggle.Text = workspace.LinkVisible ? "收起链接" : workspace.HasLink ? "链接 •" : "链接";
            linkToggle.Enabled = !workspace.ListVisible && workspace.SelectedIndex >= 0;
            newItem.Visible = !workspace.ListVisible;
            caption.Text = workspace.ListVisible ? "关键词" : "随手记";
            deleteItem.Visible = !workspace.ListVisible && workspace.SelectedIndex >= 0;
            undoDelete.Visible = !workspace.ListVisible && workspace.CanUndoRemove;
            if (!IsHandleCreated) return;
            var target = PanelMotion.Place(anchor, new Size(Px(420), workspace.PreferredCompactHeight + Px(82)), Screen.FromPoint(anchor).WorkingArea);
            if (motion.Enabled && motionTo == target) return;
            if (Bounds == target && !motion.Enabled) { LayoutPanel(); return; }
            if (animate && ready) Animate(target, 1, 150, null);
            else { SetPanelBounds(target); LayoutPanel(); }
        }
        private void LayoutPanel()
        {
            if (workspace == null || status == null) return;
            caption.SetBounds(Px(14), Px(12), Math.Max(Px(60), Width - Px(300)), Px(24));
            browse.SetBounds(Width - Px(280), Px(8), Px(54), Px(28));
            newItem.SetBounds(Width - Px(220), Px(8), Px(32), Px(28));
            linkToggle.SetBounds(Width - Px(182), Px(8), Px(72), Px(28));
            pin.SetBounds(Width - Px(104), Px(8), Px(48), Px(28));
            close.SetBounds(Width - Px(48), Px(8), Px(36), Px(28));
            workspace.SetBounds(Px(12), Px(44), Math.Max(Px(100), ClientSize.Width - Px(24)), Math.Max(Px(100), ClientSize.Height - Px(82)));
            int right = Width - Px(14);
            foreach (var button in new[] { deleteItem, undoDelete, retry })
            {
                if (button == null || !button.Visible) continue;
                right -= Px(52); button.SetBounds(right, ClientSize.Height - Px(32), Px(52), Px(26)); right -= Px(4);
            }
            status.SetBounds(Px(14), ClientSize.Height - Px(28), Math.Max(Px(100), right - Px(22)), Px(22));
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutPanel(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (colors == null || Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(colors.Border)) using (var path = SettingsUi.Round(new Rectangle(0, 0, Width - 1, Height - 1), Px(10))) e.Graphics.DrawPath(pen, path);
        }
        private void DragPanel(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || dismissing) return;
            motion.Stop(); Opacity = 1; moving = true;
            try { PanelMotion.ReleaseCapture(); PanelMotion.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
            finally
            {
                moving = false; anchor = new Point(Left + Width / 2, Bottom);
                SetPanelBounds(PanelMotion.Place(anchor, Size, Screen.FromRectangle(Bounds).WorkingArea));
                anchor = new Point(Left + Width / 2, Bottom);
            }
        }
        private void SetPanelBounds(Rectangle bounds) { applyingBounds = true; try { Bounds = bounds; } finally { applyingBounds = false; } }
        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (ready && !applyingBounds && !motion.Enabled) anchor = new Point(Left + Width / 2, Bottom);
        }
        private void Animate(Rectangle target, double opacity, int duration, Action complete)
        {
            motion.Stop(); motionDone = null;
            if (!PanelMotion.Enabled) { SetPanelBounds(target); Opacity = opacity; if (complete != null) complete(); return; }
            motionFrom = Bounds; motionTo = target; opacityFrom = Opacity; opacityTo = opacity; motionDuration = duration; motionDone = complete;
            motionClock.Restart(); motion.Start();
        }
        private void StepMotion()
        {
            double progress = Math.Min(1, motionClock.Elapsed.TotalMilliseconds / motionDuration);
            if (!PanelMotion.Enabled) progress = 1;
            SetPanelBounds(PanelMotion.Blend(motionFrom, motionTo, progress));
            Opacity = opacityFrom + (opacityTo - opacityFrom) * PanelMotion.Ease(progress);
            if (progress < 1) return;
            motion.Stop(); var complete = motionDone; motionDone = null; if (complete != null) complete();
        }
        internal void Dismiss()
        {
            if (dismissing || !SaveDraft(true)) return;
            if (!Visible) { Close(); return; }
            dismissing = true; workspace.Enabled = false;
            var target = Bounds; target.Offset(0, Px(6));
            Animate(target, 0, 110, delegate { Close(); });
        }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!ready || moving || dismissing || KeepOpen || workspace.Reordering || !IsHandleCreated) return;
            BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed && Visible && !ContainsFocus && !moving && !KeepOpen && !workspace.IsComposing && !workspace.Reordering) Dismiss(); }));
        }
        private void OnDraftChanged()
        {
            if (saving) return;
            autosave.Stop(); retry.Visible = false;
            LayoutPanel();
            if (!workspace.IsDirty) { status.Text = "已保存 · Esc 收起"; return; }
            status.Text = "正在自动保存…"; autosave.Start();
        }
        internal bool HasUnsavedChanges { get { return workspace.IsDirty; } }
        internal bool SaveDraft(bool focusError = false)
        {
            autosave.Stop();
            if (workspace.IsComposing) { status.Text = "请完成输入后再收起"; autosave.Start(); return false; }
            if (!workspace.IsDirty) return true;
            List<WorkItem> result; string error;
            if (!workspace.TryGetItems(out result, out error, focusError)) { status.Text = error; return false; }
            bool success;
            try { saving = true; success = persist(result); }
            catch { success = false; }
            finally { saving = false; }
            if (!success) { status.Text = "保存失败 · 草稿已保留，请重试"; retry.Visible = true; LayoutPanel(); return false; }
            saving = true; try { workspace.MarkSaved(); } finally { saving = false; }
            status.Text = "已保存 · Esc 收起"; retry.Visible = false; LayoutPanel(); return true;
        }
        protected override void Dispose(bool disposing) { if (disposing) { autosave.Dispose(); motion.Dispose(); } base.Dispose(disposing); }
    }
}
