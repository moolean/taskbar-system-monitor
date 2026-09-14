using System;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class SettingsForm : Form
    {
        private readonly CheckedListBox metrics;
        private readonly ComboBox height, font, theme, align, network, interval;
        private readonly CheckBox dock, soft;
        private readonly BarPreview preview;
        internal Settings Value { get; private set; }

        internal SettingsForm(Settings current)
        {
            SuspendLayout();
            Value = current.Copy();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "资源栏设置"; ClientSize = new Size(700, 430); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var heading = new Label { Text = "任务栏资源栏", Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 16, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) }; Controls.Add(heading);
            var hint = new Label { Text = "选择要显示的内容，资源栏会自动压缩并保留可读间距。", AutoSize = true, ForeColor = Color.FromArgb(100,110,125), Location = new Point(26, 50) }; Controls.Add(hint);
            var content = new GroupBox { Text = "显示内容", Location = new Point(24, 86), Size = new Size(205, 230) }; Controls.Add(content);
            metrics = new CheckedListBox { AccessibleName = "显示内容", BorderStyle = BorderStyle.None, CheckOnClick = true, Location = new Point(12, 24), Size = new Size(175, 190) }; content.Controls.Add(metrics);
            for (int i = 0; i < Settings.MetricIds.Length; i++) { metrics.Items.Add(Settings.MetricNames[i]); metrics.SetItemChecked(i, Value.Items.Contains(Settings.MetricIds[i])); }
            var layout = new GroupBox { Text = "布局与背景", Location = new Point(250, 86), Size = new Size(205, 230) }; Controls.Add(layout);
            dock = Check("任务栏上方独立栏", Value.Dock, 18, 28, layout); soft = Check("柔和背景渐变", Value.SoftBackground, 18, 58, layout);
            height = Combo(layout, "栏高", new[] { "24", "28", "32" }, Value.Height.ToString(), 18, 94);
            font = Combo(layout, "字号", new[] { "8", "9", "10", "11" }, Value.FontSize.ToString(), 18, 130);
            theme = Combo(layout, "主题", new[] { "自动跟随系统", "浅色", "深色" }, Value.Theme == "Light" ? "浅色" : Value.Theme == "Dark" ? "深色" : "自动跟随系统", 18, 166);
            align = Combo(layout, "对齐", new[] { "左侧", "居中", "右侧" }, Value.Alignment == "Left" ? "左侧" : Value.Alignment == "Center" ? "居中" : "右侧", 18, 202);
            var source = new GroupBox { Text = "网络速度来源", Location = new Point(480, 86), Size = new Size(195, 230) }; Controls.Add(source);
            network = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 28), Width = 168 };
            network.Items.Add(new NetworkChoice("自动选择活动网卡", ""));
            try { foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback)) network.Items.Add(new NetworkChoice(item.Name, item.Id)); }
            catch (NetworkInformationException error) { Log.Error(error); }
            if (Value.NetworkId.Length > 0 && !network.Items.Cast<NetworkChoice>().Any(x => x.Id == Value.NetworkId))
                network.Items.Add(new NetworkChoice("已选网卡（当前不可用）", Value.NetworkId));
            network.SelectedIndex = Math.Max(0, Enumerable.Range(0, network.Items.Count).FirstOrDefault(i => ((NetworkChoice)network.Items[i]).Id == Value.NetworkId)); source.Controls.Add(network);
            var note = new Label { Text = "自动模式选择一张活动网卡。\n暂不可用时速度显示“—”。", AutoSize = true, ForeColor = Color.FromArgb(100,110,125), Location = new Point(14, 72) }; source.Controls.Add(note);
            interval = Combo(source, "刷新", new[] { "1 秒", "2 秒", "5 秒" }, (Value.Interval / 1000) + " 秒", 12, 132);
            interval.Width = 100;
            source.Controls.Add(new Label { Text = "更长的间隔可减少采样开销。", AutoSize = true, ForeColor = Color.FromArgb(100,110,125), Location = new Point(14, 174) });
            preview = new BarPreview { AccessibleName = "资源栏预览（示例数据）", Location = new Point(24, 332), Size = new Size(651, 32), Settings = Value }; Controls.Add(preview);
            var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(515, 380), Size = new Size(75, 28) }; ok.Click += Save; Controls.Add(ok);
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(600, 380), Size = new Size(75, 28) }; Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel; dock.CheckedChanged += delegate { Changed(); }; soft.CheckedChanged += delegate { Changed(); }; height.SelectedIndexChanged += delegate { Changed(); }; font.SelectedIndexChanged += delegate { Changed(); }; theme.SelectedIndexChanged += delegate { Changed(); }; align.SelectedIndexChanged += delegate { Changed(); }; metrics.ItemCheck += delegate { if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed) Changed(); })); };
            ResumeLayout(false);
        }
        private CheckBox Check(string text, bool value, int x, int y, Control parent) { var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Location = new Point(x, y) }; parent.Controls.Add(c); return c; }
        private ComboBox Combo(Control parent, string label, string[] values, string selected, int x, int y)
        {
            var l = new Label { Text = label, AutoSize = true, Location = new Point(x, y + 4) }; parent.Controls.Add(l); var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x + 65, y), Width = 110 }; c.Items.AddRange(values); c.SelectedItem = selected; parent.Controls.Add(c); return c;
        }
        private void Changed()
        {
            Value.Dock = dock.Checked; Value.SoftBackground = soft.Checked; Value.Height = int.Parse((string)height.SelectedItem); Value.FontSize = int.Parse((string)font.SelectedItem); Value.Theme = (string)theme.SelectedItem == "浅色" ? "Light" : (string)theme.SelectedItem == "深色" ? "Dark" : "Auto"; Value.Items.Clear();
            for (int i = 0; i < metrics.Items.Count; i++) if (metrics.GetItemChecked(i)) Value.Items.Add(Settings.MetricIds[i]);
            Value.Alignment = (string)align.SelectedItem == "左侧" ? "Left" : (string)align.SelectedItem == "居中" ? "Center" : "Right"; preview.Settings = Value; preview.Invalidate();
        }
        private void Save(object sender, EventArgs e)
        {
            Changed();
            if (Value.Items.Count == 0) { DialogResult = DialogResult.None; MessageBox.Show(this, "请至少选择一项显示内容。", "资源栏设置", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Value.Interval = int.Parse(((string)interval.SelectedItem).Substring(0, 1)) * 1000;
            Value.NetworkId = ((NetworkChoice)network.SelectedItem).Id; Value.Validate();
        }
        private sealed class NetworkChoice { internal string Name, Id; internal NetworkChoice(string name, string id) { Name = name; Id = id; } public override string ToString() { return Name; } }
    }
}
