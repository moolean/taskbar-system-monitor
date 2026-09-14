using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class SettingsForm : DpiDialog
    {
        private readonly CheckedListBox modules;
        private readonly ComboBox height, font, theme, accent, align, network, interval;
        private readonly CheckBox dock, soft, fill, tint, geo, clearSecret;
        private readonly TextBox codexPath, calendarUrl, calendarUser, calendarPassword;
        private readonly BarPreview preview;
        private readonly BindingList<WorkItem> work;
        private readonly DataGridView workGrid;
        internal Settings Value { get; private set; }

        internal SettingsForm(Settings current, string page = null)
        {
            SuspendLayout(); Value = current.Copy();
            Text = "信息工作台设置"; ClientSize = new Size(900, 600); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false; MinimizeBox = false;
            Controls.Add(new Label { Text = "让这一行，装下你的工作上下文", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Location = new Point(22, 16) });
            var tabs = new TabControl { Location = new Point(20, 62), Size = new Size(860, 424) }; Controls.Add(tabs);
            var appearance = new TabPage("内容与外观"); var connections = new TabPage("数据连接"); var tracking = new TabPage("工作追踪"); tabs.TabPages.AddRange(new[] { appearance, connections, tracking });
            var content = Group(appearance, "显示内容 · 可排序", 12, 12, 250, 364);
            modules = new CheckedListBox { AccessibleName = "显示模块", BorderStyle = BorderStyle.None, CheckOnClick = true, Location = new Point(12, 24), Size = new Size(224, 280) }; content.Controls.Add(modules);
            foreach (string id in Value.Items.Concat(Settings.MetricIds.Where(x => !Value.Items.Contains(x))))
            { modules.Items.Add(new ModuleChoice { Id = id }); modules.SetItemChecked(modules.Items.Count - 1, Value.Items.Contains(id)); }
            var up = Button(content, "上移", 12, 320, 100); var down = Button(content, "下移", 132, 320, 100);
            up.Click += delegate { MoveModule(-1); }; down.Click += delegate { MoveModule(1); };
            var style = Group(appearance, "整行布局", 280, 12, 260, 364);
            dock = Check(style, "启用任务栏上方信息栏", Value.Dock, 14, 28);
            fill = Check(style, "利用整行宽度（推荐）", Value.FillBar, 14, 62);
            soft = Check(style, "柔和渐变背景", Value.SoftBackground, 14, 96);
            height = Combo(style, "栏高", new[] { "24", "28", "32" }, Value.Height.ToString(), 14, 142, 120);
            font = Combo(style, "字号", new[] { "8", "9", "10", "11" }, Value.FontSize.ToString(), 14, 186, 120);
            align = Combo(style, "对齐", new[] { "左侧", "居中", "右侧" }, Value.Alignment == "Left" ? "左侧" : Value.Alignment == "Center" ? "居中" : "右侧", 14, 230, 120);
            Note(style, "日程和工作区会自动伸展。\n项目过多时折叠，点击可查看。\n仍然只占用一行高度。", 14, 280, 230, 62);
            var color = Group(appearance, "与任务栏呼应", 558, 12, 272, 364);
            theme = Combo(color, "主题", new[] { "跟随系统", "浅色", "深色" }, Value.Theme == "Light" ? "浅色" : Value.Theme == "Dark" ? "深色" : "跟随系统", 14, 30, 160);
            accent = Combo(color, "色调", new[] { "系统强调色", "雾蓝", "淡紫", "青玉" }, Value.Accent == "Ocean" ? "雾蓝" : Value.Accent == "Plum" ? "淡紫" : Value.Accent == "Jade" ? "青玉" : "系统强调色", 14, 78, 160);
            tint = Check(color, "使用低饱和任务栏色调", Value.TintTaskbar, 14, 130);
            Note(color, "跟随系统明暗与强调色，\n使用细分隔、浅色块和悬停反馈。\n\n完整绘制不透明背景，\n避免透明叠加造成文字重影。", 14, 180, 242, 150);

            var codex = Group(connections, "Codex 额度 · 每 5 分钟刷新", 12, 12, 405, 140);
            Note(codex, "自动查找已安装的 Codex，也可指定 codex.exe：", 12, 24, 380, 24);
            codexPath = Input(codex, Value.CodexPath, 12, 54, 304);
            var browse = Button(codex, "选择…", 324, 52, 68);
            browse.Click += delegate { using (var file = new OpenFileDialog { Filter = "Codex 程序|*.exe", CheckFileExists = true }) if (file.ShowDialog(this) == DialogResult.OK) codexPath.Text = file.FileName; };
            Note(codex, "只读当前 CLI 登录账户；不会创建任务或使用重置额度。", 12, 94, 380, 36);
            var calendar = Group(connections, "飞书日历 · CalDAV 只读同步", 12, 170, 405, 206);
            Label(calendar, "服务器", 12, 28); calendarUrl = Input(calendar, Value.CalendarUrl, 84, 24, 306);
            Label(calendar, "专用账号", 12, 66); calendarUser = Input(calendar, Value.CalendarUser, 84, 62, 306);
            Label(calendar, "专用密码", 12, 104); calendarPassword = Input(calendar, "", 84, 100, 306); calendarPassword.UseSystemPasswordChar = true;
            clearSecret = Check(calendar, "清除已保存的凭据", false, 12, 140);
            Note(calendar, Value.CalendarSecret.Length > 0 ? "密码已加密保存；留空则保留。不要填飞书登录密码。" : "在飞书日历设置生成 CalDAV 专用账号；仅本机加密保存。", 12, 170, 380, 28);
            var connection = Group(connections, "IP / 位置与采样", 440, 12, 390, 210);
            network = new ComboBox { AccessibleName = "网络接口", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 28), Width = 366 }; connection.Controls.Add(network);
            network.Items.Add(new NetworkChoice("自动选择活动网卡", ""));
            try { foreach (var item in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback)) network.Items.Add(new NetworkChoice(item.Name, item.Id)); } catch (NetworkInformationException) { }
            if (Value.NetworkId.Length > 0 && !network.Items.Cast<NetworkChoice>().Any(x => x.Id == Value.NetworkId)) network.Items.Add(new NetworkChoice("已选网卡（当前不可用）", Value.NetworkId));
            network.SelectedIndex = Enumerable.Range(0, network.Items.Count).FirstOrDefault(i => ((NetworkChoice)network.Items[i]).Id == Value.NetworkId);
            interval = Combo(connection, "资源刷新", new[] { "1 秒", "2 秒", "5 秒" }, Value.Interval / 1000 + " 秒", 12, 74, 142);
            geo = Check(connection, "查询公网 IP 与大致位置", Value.GeoEnabled, 12, 122);
            Note(connection, "每 10 分钟请求 ipwho.is。位置不是 GPS；代理 / VPN\n会改变出口位置。关闭后仅显示本地 IP。", 12, 158, 366, 42);
            var privacy = Group(connections, "连接与隐私", 440, 242, 390, 134);
            Note(privacy, "会议：每 2 分钟读取未来 14 天日程，不写回飞书。\n工作追踪：仅保存在本机，不上传。\n点击栏内模块可查看详情、更新时间或手动重试。\n未连接 / 超时会明确显示，不用示例数据冒充。", 12, 24, 366, 96);

            work = new BindingList<WorkItem>(Value.WorkItems);
            workGrid = new DataGridView { Location = new Point(12, 14), Size = new Size(818, 300), AutoGenerateColumns = false, DataSource = work, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle };
            workGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "工作项", DataPropertyName = "Title", Width = 240 });
            workGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", DataPropertyName = "Status", Width = 80 });
            workGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "进度 %", DataPropertyName = "Progress", Width = 70 });
            workGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "详情摘要", DataPropertyName = "Notes", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            tracking.Controls.Add(workGrid);
            Button(tracking, "添加工作项", 12, 330, 116).Click += delegate { EditWork(true); };
            Button(tracking, "编辑详情", 140, 330, 116).Click += delegate { EditWork(false); };
            Button(tracking, "移除所选", 268, 330, 116).Click += delegate { var item = SelectedWork(); if (item != null && MessageBox.Show(this, "移除“" + item.Title + "”？保存设置后生效。", "工作追踪", MessageBoxButtons.OKCancel) == DialogResult.OK) work.Remove(item); };
            Note(tracking, "支持标题、状态、进度、详细笔记和链接；点击信息栏的工作区展开。", 12, 368, 806, 20);
            workGrid.CellDoubleClick += delegate { EditWork(false); };

            Controls.Add(new Label { Text = "样式预览 · 示例数据（窄预览会折叠部分模块）", AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(22, 496) });
            preview = new BarPreview { Location = new Point(20, 518), Size = new Size(860, 32), Settings = Value, Snapshot = InfoTests.Demo() }; Controls.Add(preview);
            var ok = Button(this, "保存", 684, 564, 92); ok.DialogResult = DialogResult.OK; ok.Click += Save;
            var cancel = Button(this, "取消", 788, 564, 92); cancel.DialogResult = DialogResult.Cancel; AcceptButton = ok; CancelButton = cancel;
            foreach (var box in new[] { dock, soft, fill, tint }) box.CheckedChanged += delegate { Changed(); };
            foreach (var combo in new[] { height, font, theme, accent, align }) combo.SelectedIndexChanged += delegate { Changed(); };
            modules.ItemCheck += delegate { if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed) Changed(); })); };
            if (page == "Tracks") tabs.SelectedTab = tracking; else if (page == "Connections") tabs.SelectedTab = connections;
            ResumeLayout(false);
        }
        private void MoveModule(int offset)
        {
            int index = modules.SelectedIndex, target = index + offset;
            if (index < 0 || target < 0 || target >= modules.Items.Count) return;
            object item = modules.Items[index]; bool enabled = modules.GetItemChecked(index);
            modules.Items.RemoveAt(index); modules.Items.Insert(target, item); modules.SetItemChecked(target, enabled); modules.SelectedIndex = target; Changed();
        }
        private WorkItem SelectedWork() { return workGrid.CurrentRow == null ? null : workGrid.CurrentRow.DataBoundItem as WorkItem; }
        private void EditWork(bool add)
        {
            WorkItem item = add ? new WorkItem() : SelectedWork(); if (item == null) return;
            using (var form = new WorkEditor(item))
                if (form.ShowDialog(this) == DialogResult.OK) { if (add) work.Add(form.Value); else work[work.IndexOf(item)] = form.Value; }
        }
        private void Changed()
        {
            if (preview == null) return;
            Value.Dock = dock.Checked; Value.SoftBackground = soft.Checked; Value.FillBar = fill.Checked; Value.TintTaskbar = tint.Checked;
            Value.Height = int.Parse((string)height.SelectedItem); Value.FontSize = int.Parse((string)font.SelectedItem);
            Value.Theme = theme.SelectedIndex == 1 ? "Light" : theme.SelectedIndex == 2 ? "Dark" : "Auto";
            Value.Accent = new[] { "System", "Ocean", "Plum", "Jade" }[accent.SelectedIndex];
            Value.Alignment = align.SelectedIndex == 0 ? "Left" : align.SelectedIndex == 1 ? "Center" : "Right";
            Value.Items = modules.CheckedItems.Cast<ModuleChoice>().Select(x => x.Id).ToList();
            preview.Settings = Value; preview.Invalidate();
        }
        private void Save(object sender, EventArgs e)
        {
            try
            {
                Changed(); if (Value.Items.Count == 0) throw new InvalidOperationException("请至少选择一个模块。");
                Value.CodexPath = codexPath.Text.Trim(); Value.CalendarUrl = calendarUrl.Text.Trim(); Value.CalendarUser = calendarUser.Text.Trim();
                if (Value.CalendarUser.Contains(':')) throw new InvalidOperationException("CalDAV 用户名不能包含冒号。");
                if (Value.CalendarUser.Length > 0) CalendarSource.ValidateUrl(Value.CalendarUrl);
                if (clearSecret.Checked) Value.CalendarSecret = "";
                else if (calendarPassword.Text.Length > 0) Value.CalendarSecret = LocalSecret.Seal(calendarPassword.Text);
                Value.GeoEnabled = geo.Checked; Value.NetworkId = ((NetworkChoice)network.SelectedItem).Id;
                Value.Interval = int.Parse(((string)interval.SelectedItem).Substring(0, 1)) * 1000;
                Value.WorkItems = work.ToList(); Value.Validate();
            }
            catch (Exception error) { DialogResult = DialogResult.None; MessageBox.Show(this, InfoHub.SafeError(error), "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private static GroupBox Group(Control parent, string text, int x, int y, int w, int h) { var group = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h) }; parent.Controls.Add(group); return group; }
        private static void Label(Control parent, string text, int x, int y) { parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) }); }
        private static void Note(Control parent, string text, int x, int y, int w, int h) { parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = SystemColors.GrayText }); }
        private static TextBox Input(Control parent, string value, int x, int y, int w) { var control = new TextBox { Text = value, Location = new Point(x, y), Width = w }; parent.Controls.Add(control); return control; }
        private static Button Button(Control parent, string text, int x, int y, int w) { var control = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 28) }; parent.Controls.Add(control); return control; }
        private static CheckBox Check(Control parent, string text, bool value, int x, int y) { var control = new CheckBox { Text = text, Checked = value, Location = new Point(x, y), AutoSize = true }; parent.Controls.Add(control); return control; }
        private static ComboBox Combo(Control parent, string name, string[] values, string selected, int x, int y, int width)
        { Label(parent, name, x, y + 4); var control = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x + 70, y), Width = width }; control.Items.AddRange(values); control.SelectedItem = selected; parent.Controls.Add(control); return control; }
        private sealed class ModuleChoice { internal string Id; public override string ToString() { return Settings.MetricNames[Array.IndexOf(Settings.MetricIds, Id)]; } }
        private sealed class NetworkChoice { internal string Id, Name; internal NetworkChoice(string name, string id) { Name = name; Id = id; } public override string ToString() { return Name; } }
    }
}
