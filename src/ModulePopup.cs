using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    // Read-only data-source details. Work editing lives in its own workspace.
    internal sealed class ModulePopup : DpiDialog
    {
        private readonly TextBox body;
        private Settings settings;
        private Snapshot snapshot;
        private string module;
        internal event Action<string> ConfigureRequested;
        internal event Action RefreshRequested;
        internal ModulePopup()
        {
            SuspendLayout();
            Text = "信息详情"; ClientSize = new Size(620, 388); ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedSingle;
            body = new TextBox { AccessibleName = "模块详情", Location = new Point(20, 20), Size = new Size(580, 300), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None }; Controls.Add(body);
            SettingsUi.Button(this, "数据连接…", 20, 340, 116).Click += delegate { Hide(); if (ConfigureRequested != null) ConfigureRequested("Connections"); };
            SettingsUi.Button(this, "刷新数据", 480, 340, 120).Click += delegate { if (RefreshRequested != null) RefreshRequested(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
            ResumeLayout(false);
        }
        internal void OpenModule(string id, Settings configuration, Snapshot data)
        {
            module = id; settings = configuration; snapshot = data;
            Text = id == "Codex" ? "Codex 剩余额度" : "网络与位置";
            SettingsUi.Theme(this, SettingsColors.From(settings));
            Render(); Show(); Render();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Cursor.Position.X - Width / 2, area.Right - Width)), Math.Max(area.Top, area.Bottom - Height - 8));
            Activate();
        }
        internal void UpdateData(Settings configuration, Snapshot data) { settings = configuration; snapshot = data; if (Visible) Render(); }
        private void Render()
        {
            if (settings == null) return;
            var briefing = snapshot == null ? new BriefingData() : snapshot.Briefing;
            string details = module == "Codex" ? briefing.Codex.FullDetails :
                "本地 IP：" + (snapshot == null ? "—" : snapshot.LocalIp) + "\r\n网卡：" + (snapshot == null ? "—" : snapshot.NetworkName) + "\r\n\r\n" + briefing.Location.FullDetails;
            if (body.Text != details) body.Text = details;
        }
        internal static bool IsWebLink(string value) { Uri uri; return Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && uri.UserInfo.Length == 0; }
    }
}
