using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    // Legacy Framework WinForms can scale tab contents twice when a form mixes
    // per-monitor DPI and nested containers. Scale these fixed dialogs once.
    internal class DpiDialog : Form
    {
        private bool scaled;
        protected DpiDialog() { AutoScaleMode = AutoScaleMode.None; Font = new Font("Microsoft YaHei UI", 9f); }
        protected override void OnLoad(EventArgs e)
        {
            if (!scaled)
            {
                scaled = true;
                Size logical = ClientSize; float factor = Native.Dpi(Handle) / 96f;
                if (Math.Abs(factor - 1) > 0.01f)
                {
                    Scale(new SizeF(factor, factor));
                    ClientSize = new Size((int)Math.Round(logical.Width * factor), (int)Math.Round(logical.Height * factor));
                }
            }
            base.OnLoad(e);
        }
    }
}
