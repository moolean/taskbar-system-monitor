using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class WorkEditor : DpiDialog
    {
        internal WorkItem Value { get; private set; }
        internal WorkEditor(WorkItem item)
        {
            Value = item.Copy(); SuspendLayout();
            Text = "工作追踪详情"; ClientSize = new Size(560, 450); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            Controls.Add(new Label { Text = "标题", AutoSize = true, Location = new Point(20, 20) });
            var title = new TextBox { Text = Value.Title, Location = new Point(20, 44), Width = 520, MaxLength = 150 }; Controls.Add(title);
            var status = new ComboBox { Location = new Point(20, 86), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList }; status.Items.AddRange(new[] { "待办", "进行中", "阻塞", "完成" }); status.SelectedItem = Value.Status; Controls.Add(status);
            Controls.Add(new Label { Text = "进度 %", AutoSize = true, Location = new Point(190, 91) });
            var progress = new NumericUpDown { Location = new Point(266, 86), Width = 86, Maximum = 100, Value = Value.Progress }; Controls.Add(progress);
            Controls.Add(new Label { Text = "详细笔记 / 下一步", AutoSize = true, Location = new Point(20, 132) });
            var notes = new TextBox { Text = Value.Notes, Location = new Point(20, 156), Size = new Size(520, 178), Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, MaxLength = 8000 }; Controls.Add(notes);
            Controls.Add(new Label { Text = "关联链接（可选，仅 https / http）", AutoSize = true, Location = new Point(20, 346) });
            var link = new TextBox { Text = Value.Link, Location = new Point(20, 368), Width = 520, MaxLength = 2000 }; Controls.Add(link);
            var save = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(356, 410), Size = new Size(86, 28) }; Controls.Add(save);
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(454, 410), Size = new Size(86, 28) }; Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
            save.Click += delegate
            {
                if (string.IsNullOrWhiteSpace(title.Text) || (link.Text.Trim().Length > 0 && !ModulePopup.IsWebLink(link.Text.Trim())))
                { DialogResult = DialogResult.None; MessageBox.Show(this, "标题不能为空；链接只能是 http 或 https 地址。", "请检查输入"); return; }
                Value.Title = title.Text; Value.Status = (string)status.SelectedItem; Value.Progress = (int)progress.Value; Value.Notes = notes.Text; Value.Link = link.Text.Trim(); Value.Validate();
            };
            ResumeLayout(false);
        }
    }

    internal sealed class ModulePopup : DpiDialog
    {
        private readonly ListBox list;
        private readonly TextBox body;
        private readonly Button configure, refresh, edit, open;
        private Settings settings;
        private Snapshot snapshot;
        private string module, selectedUrl = "";
        internal event Action<string> ConfigureRequested;
        internal event Action RefreshRequested;
        internal event Action<int, WorkItem> WorkEdited;
        internal ModulePopup()
        {
            SuspendLayout();
            Text = "信息详情"; ClientSize = new Size(620, 388); ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedSingle;
            list = new ListBox { AccessibleName = "会议或工作项", Location = new Point(16, 16), Size = new Size(588, 100), BorderStyle = BorderStyle.FixedSingle }; Controls.Add(list);
            body = new TextBox { AccessibleName = "模块详情", Location = new Point(16, 128), Size = new Size(588, 198), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None }; Controls.Add(body);
            configure = new Button { Text = "配置…", Location = new Point(16, 346), Size = new Size(100, 28) }; Controls.Add(configure);
            refresh = new Button { Text = "刷新数据", Location = new Point(128, 346), Size = new Size(100, 28) }; Controls.Add(refresh);
            edit = new Button { Text = "编辑所选", Location = new Point(384, 346), Size = new Size(100, 28) }; Controls.Add(edit);
            open = new Button { Text = "打开链接", Location = new Point(504, 346), Size = new Size(100, 28) }; Controls.Add(open);
            list.SelectedIndexChanged += delegate { RenderDetails(); };
            configure.Click += delegate { Hide(); if (ConfigureRequested != null) ConfigureRequested(module == "Tracks" ? "Tracks" : "Connections"); };
            refresh.Click += delegate { if (RefreshRequested != null) RefreshRequested(); };
            edit.Click += delegate
            {
                int index = list.SelectedIndex;
                if (module != "Tracks" || index < 0 || index >= settings.WorkItems.Count) return;
                using (var form = new WorkEditor(settings.WorkItems[index])) if (form.ShowDialog(this) == DialogResult.OK && WorkEdited != null) WorkEdited(index, form.Value);
            };
            open.Click += delegate { if (IsWebLink(selectedUrl)) try { Process.Start(new ProcessStartInfo(selectedUrl) { UseShellExecute = true }); } catch { MessageBox.Show(this, "无法打开链接。", "信息工作台"); } };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
            ResumeLayout(false);
        }
        internal void OpenModule(string id, Settings configuration, Snapshot data)
        {
            module = id; settings = configuration; snapshot = data;
            Text = id == "Tracks" ? "工作追踪" : id == "Calendar" ? "下一场会议" : id == "Codex" ? "Codex 剩余额度" : "网络与位置";
            Render();
            Show(); Render();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Cursor.Position.X - Width / 2, area.Right - Width)), Math.Max(area.Top, area.Bottom - Height - 8));
            Activate();
        }
        internal void UpdateData(Settings configuration, Snapshot data) { settings = configuration; snapshot = data; if (Visible) Render(); }
        private void Render()
        {
            if (settings == null) return;
            Palette colors = Palette.Current(settings); BackColor = colors.Background; ForeColor = colors.Text; body.BackColor = BackColor; body.ForeColor = ForeColor; list.BackColor = colors.Surface; list.ForeColor = ForeColor;
            string[] titles = module == "Tracks" ? settings.WorkItems.Select(x => x.ToString()).ToArray() : module == "Calendar" && snapshot != null ? snapshot.Briefing.Meetings.Select(x => x.Summary(DateTimeOffset.Now)).ToArray() : new string[0];
            if (!list.Items.Cast<string>().SequenceEqual(titles))
            {
                int selected = list.SelectedIndex; list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(titles); if (titles.Length > 0) list.SelectedIndex = Math.Max(0, Math.Min(selected, titles.Length - 1)); list.EndUpdate();
            }
            list.Visible = module == "Tracks" || module == "Calendar";
            body.Top = list.Visible ? list.Bottom + 12 : list.Top;
            body.Height = refresh.Top - body.Top - 20;
            edit.Visible = module == "Tracks"; edit.Enabled = list.SelectedIndex >= 0;
            refresh.Visible = module != "Tracks"; RenderDetails();
        }
        private void RenderDetails()
        {
            if (settings == null) return;
            string details = "正在读取…"; selectedUrl = "";
            var briefing = snapshot == null ? new BriefingData() : snapshot.Briefing;
            int index = list.SelectedIndex;
            if (module == "Tracks")
            {
                if (index >= 0 && index < settings.WorkItems.Count) { var item = settings.WorkItems[index]; details = item.Title + "\r\n" + item.Status + " · " + item.Progress + "%\r\n\r\n" + item.Notes; selectedUrl = item.Link; }
                else details = "还没有工作追踪项。\r\n\r\n点击“配置…”添加项目、状态、进度、笔记和关联链接。数据仅保存在本机。";
            }
            else if (module == "Calendar")
            {
                if (index >= 0 && index < briefing.Meetings.Count) { var meeting = briefing.Meetings[index]; details = meeting.Details + "\r\n\r\n" + briefing.Calendar.FullDetails; selectedUrl = meeting.Url; }
                else details = briefing.Calendar.FullDetails;
            }
            else if (module == "Codex") details = briefing.Codex.FullDetails;
            else details = "本地 IP：" + (snapshot == null ? "—" : snapshot.LocalIp) + "\r\n网卡：" + (snapshot == null ? "—" : snapshot.NetworkName) + "\r\n\r\n" + briefing.Location.FullDetails;
            if (body.Text != details) body.Text = details;
            open.Enabled = IsWebLink(selectedUrl);
        }
        internal static bool IsWebLink(string value) { Uri uri; return Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && uri.UserInfo.Length == 0; }
    }
}
