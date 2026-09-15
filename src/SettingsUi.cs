using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class SettingsColors
    {
        internal Color Background, Sidebar, Card, Text, Muted, Border, Selected, Action, ActionText;
        internal static SettingsColors From(Settings settings)
        {
            bool light = Palette.Current(settings).Background.GetBrightness() > .5f;
            if (SystemInformation.HighContrast) return new SettingsColors { Background = SystemColors.Window, Sidebar = SystemColors.Window, Card = SystemColors.Window, Text = SystemColors.WindowText, Muted = SystemColors.WindowText, Border = SystemColors.WindowText, Selected = SystemColors.Highlight, Action = SystemColors.Highlight, ActionText = SystemColors.HighlightText };
            return light ? new SettingsColors { Background = Color.FromArgb(250,249,247), Sidebar = Color.FromArgb(242,241,238), Card = Color.White, Text = Color.FromArgb(36,36,34), Muted = Color.FromArgb(111,111,106), Border = Color.FromArgb(226,225,221), Selected = Color.FromArgb(229,228,223), Action = Color.FromArgb(40,40,37), ActionText = Color.White }
                : new SettingsColors { Background = Color.FromArgb(28,28,28), Sidebar = Color.FromArgb(24,24,24), Card = Color.FromArgb(35,35,35), Text = Color.FromArgb(237,237,234), Muted = Color.FromArgb(158,158,153), Border = Color.FromArgb(59,59,56), Selected = Color.FromArgb(48,48,45), Action = Color.FromArgb(232,232,227), ActionText = Color.FromArgb(28,28,28) };
        }
    }
    internal static class SettingsUi
    {
        internal static GraphicsPath Round(Rectangle bounds, int radius)
        {
            int d = Math.Min(Math.Min(bounds.Width, bounds.Height), Math.Max(2, radius * 2));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        internal static Label Label(Control parent, string text, int x, int y, int width, int height, float size = 9.5f, bool muted = false)
        {
            var label = new Label { Text = text, Location = new Point(x,y), Size = new Size(width,height), Font = new Font("Segoe UI", size), Tag = muted ? "muted" : "text", AutoEllipsis = true };
            parent.Controls.Add(label); return label;
        }
        internal static UiButton Button(Control parent, string text, int x, int y, int width = 104, bool primary = false)
        { var button = new UiButton { Text = text, Location = new Point(x,y), Size = new Size(width,34), Primary = primary }; parent.Controls.Add(button); return button; }
        internal static void Theme(Control control, SettingsColors c)
        {
            control.ForeColor = c.Text;
            var card = control as SettingsCard;
            var button = control as UiButton;
            var toggle = control as ToggleSwitch;
            if (card != null) { card.Colors = c; card.BackColor = c.Card; }
            else if (button != null) { button.Colors = c; button.BackColor = control.Parent == null ? c.Background : control.Parent.BackColor; }
            else if (toggle != null) { toggle.Colors = c; toggle.BackColor = control.Parent == null ? c.Card : control.Parent.BackColor; }
            else if (control is Label) { control.ForeColor = object.Equals(control.Tag,"muted") ? c.Muted : c.Text; control.BackColor = Color.Transparent; }
            else if (control is TextBoxBase || control is ComboBox || control is ListBox) control.BackColor = c.Card;
            else if (control is CheckBox) control.BackColor = control.Parent == null ? c.Card : control.Parent.BackColor;
            else control.BackColor = object.Equals(control.Tag, "sidebar") ? c.Sidebar : c.Background;
            var grid = control as DataGridView;
            if (grid != null)
            {
                grid.BackgroundColor = c.Card; grid.GridColor = c.Border; grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = c.Sidebar; grid.ColumnHeadersDefaultCellStyle.ForeColor = c.Muted;
                grid.DefaultCellStyle.BackColor = c.Card; grid.DefaultCellStyle.ForeColor = c.Text; grid.DefaultCellStyle.SelectionBackColor = c.Selected; grid.DefaultCellStyle.SelectionForeColor = c.Text;
            }
            foreach (Control child in control.Controls) Theme(child,c);
            control.Invalidate();
        }
    }
    internal sealed class SettingsCard : Panel
    {
        internal SettingsColors Colors;
        internal SettingsCard() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Colors == null || Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Colors.Border)) using (var path = SettingsUi.Round(new Rectangle(0,0,Width-1,Height-1), 10)) e.Graphics.DrawPath(pen,path);
        }
    }
    internal sealed class UiButton : Button
    {
        internal SettingsColors Colors;
        internal bool Primary, Navigation, Selected;
        private bool hover;
        internal UiButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; DoubleBuffered = true; UseVisualStyleBackColor = false; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Colors == null) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = Primary ? Colors.Action : Selected || hover ? Colors.Selected : Navigation ? BackColor : Colors.Card;
            using (var brush = new SolidBrush(background)) using (var path = SettingsUi.Round(new Rectangle(1,1,Width-3,Height-3),8))
            { e.Graphics.FillPath(brush,path); if (!Primary && !Navigation) using (var pen = new Pen(Colors.Border)) e.Graphics.DrawPath(pen,path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Navigation ? 14 : 4,0,Width-(Navigation ? 20 : 8),Height), !Enabled ? Colors.Muted : Primary ? Colors.ActionText : Colors.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle,-5,-5));
        }
    }
    internal sealed class ToggleSwitch : CheckBox
    {
        internal SettingsColors Colors;
        internal ToggleSwitch() { AutoSize = false; Size = new Size(44,26); Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Colors == null) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(2, (Height - Math.Min(Height-4,22))/2, Width-4, Math.Min(Height-4,22));
            using (var brush = new SolidBrush(Checked ? Colors.Action : Colors.Border)) using (var path = SettingsUi.Round(track,track.Height/2)) e.Graphics.FillPath(brush,path);
            int d = track.Height-6;
            using (var brush = new SolidBrush(Checked ? Colors.ActionText : Colors.Card)) e.Graphics.FillEllipse(brush, Checked ? track.Right-d-3 : track.Left+3, track.Top+3, d,d);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle);
        }
    }
}
