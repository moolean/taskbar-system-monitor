using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class SettingsForm : DpiDialog
    {
        private CheckedListBox modules;
        private ComboBox height, font, theme, accent, align, network, interval;
        private ToggleSwitch dock, topmost, soft, fill, tint, geo;
        private TextBox codexPath;
        private BarPreview preview;
        private WorkWorkspace workWorkspace;
        private Label title, subtitle, status;
        private UiButton testCodex;
        private readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        private readonly Dictionary<string, UiButton> navigation = new Dictionary<string, UiButton>();
        private bool initializing = true;
        internal Settings Value { get; private set; }

        internal SettingsForm(Settings current, string page = null)
        {
            SuspendLayout(); Value = current.Copy();
            Text = "设置 · 信息工作台"; ClientSize = new Size(980, 720); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9.5f);
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 186, Tag = "sidebar" }; Controls.Add(sidebar);
            SettingsUi.Label(sidebar,"信息工作台",20,28,152,30,14);
            SettingsUi.Label(sidebar,"WORKSPACE BAR",20,62,152,22,8,true);
            string[] ids = { "General", "Appearance", "Connections", "Tracks" };
            string[] names = { "常规与显示", "外观", "数据连接", "工作追踪" };
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var item = SettingsUi.Button(sidebar,names[i],12,120+i*46,162); item.Height=40; item.Navigation=true;
                item.Click += delegate { SelectPage(id); }; navigation[id]=item;
            }
            SettingsUi.Label(sidebar,"本机优先 · 只读连接",20,632,150,24,8.5f,true);
            SettingsUi.Label(sidebar,"VERSION 3.7.0",20,658,150,22,8,true);

            var main = new Panel { Dock = DockStyle.Fill }; Controls.Add(main); main.BringToFront();
            var header = new Panel { Dock = DockStyle.Top, Height = 104 }; main.Controls.Add(header);
            title = SettingsUi.Label(header,"",26,26,738,36,22);
            subtitle = SettingsUi.Label(header,"",27,70,730,26,9.5f,true);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 116 }; main.Controls.Add(footer);
            SettingsUi.Label(footer,"实时样式预览 · 内容为示例",26,6,700,20,8.5f,true);
            preview = new BarPreview { Location = new Point(26,28), Size = new Size(740,32), Snapshot=InfoTests.Demo(), Settings=Value }; footer.Controls.Add(preview);
            status = SettingsUi.Label(footer,"更改在保存后生效",26,82,480,24,8.5f,true);
            var save = SettingsUi.Button(footer,"保存更改",644,72,122,true); save.Click += Save;
            var cancel = SettingsUi.Button(footer,"取消",534,72,96); cancel.DialogResult=DialogResult.Cancel;
            AcceptButton=save; CancelButton=cancel;
            foreach (string id in ids)
            {
                var panel = new Panel { Dock=DockStyle.Fill, AutoScroll=true, Visible=false };
                main.Controls.Add(panel); panel.BringToFront(); pages[id]=panel;
            }
            BuildGeneral(pages["General"]); BuildAppearance(pages["Appearance"]); BuildConnections(pages["Connections"]); BuildWork(pages["Tracks"]);
            foreach (var box in new[] { dock,topmost,soft,fill,tint }) box.CheckedChanged += delegate { Changed(); };
            foreach (var combo in new[] { height,font,theme,accent,align }) combo.SelectedIndexChanged += delegate { Changed(); };
            modules.ItemCheck += delegate { if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed) Changed(); })); };
            initializing=false;
            ApplyTheme(); SelectPage(page == "Connections" || page == "Tracks" ? page : "General");
            ResumeLayout(true);
        }
        private SettingsCard Card(Panel parent,string heading,string description,int y,int h)
        {
            var card = new SettingsCard { Location=new Point(26,y),Size=new Size(720,h) }; parent.Controls.Add(card);
            SettingsUi.Label(card,heading,20,16,680,26,11);
            if (description.Length>0) SettingsUi.Label(card,description,20,44,680,24,9,true);
            return card;
        }
        private static ToggleSwitch Toggle(Control parent,string name,string description,bool selected,int y)
        {
            SettingsUi.Label(parent,name,20,y,594,24);
            SettingsUi.Label(parent,description,20,y+26,630,24,8.5f,true);
            var toggle=new ToggleSwitch { AccessibleName=name,Checked=selected,Location=new Point(654,y+8) }; parent.Controls.Add(toggle); return toggle;
        }
        private void BuildGeneral(Panel page)
        {
            var behavior=Card(page,"横条行为","为常用信息保留一行，不与最大化窗口重叠。",0,286);
            dock=Toggle(behavior,"显示信息栏","位于主屏幕任务栏上方；关闭后释放预留高度。",Value.Dock,78);
            topmost=Toggle(behavior,"保持在普通窗口上方","全屏应用时自动让位；关闭后允许其他窗口覆盖。",Value.AlwaysOnTop,146);
            fill=Toggle(behavior,"利用整行宽度","信息模块紧凑显示，剩余宽度留给工作关键词。",Value.FillBar,214);
            var content=Card(page,"显示模块","勾选需要的内容，选中后可调整排列顺序。",302,366);
            modules=new CheckedListBox { AccessibleName="显示模块", BorderStyle=BorderStyle.None, CheckOnClick=true, IntegralHeight=false, Location=new Point(20,80), Size=new Size(678,224), ItemHeight=28 };
            content.Controls.Add(modules);
            foreach (string id in Value.Items.Concat(Settings.MetricIds.Where(x=>!Value.Items.Contains(x)))) { modules.Items.Add(new ModuleChoice { Id=id }); modules.SetItemChecked(modules.Items.Count-1,Value.Items.Contains(id)); }
            SettingsUi.Button(content,"↑ 上移",20,314,94).Click+=delegate { MoveModule(-1); };
            SettingsUi.Button(content,"↓ 下移",126,314,94).Click+=delegate { MoveModule(1); };
            SettingsUi.Label(content,"窄屏会折叠放不下的模块",368,322,310,22,8.5f,true);
            page.AutoScrollMinSize=new Size(0,686);
        }
        private void BuildAppearance(Panel page)
        {
            var colors=Card(page,"主题与色调","设置窗口采用中性色；信息栏轻量呼应任务栏强调色。",0,322);
            theme=SelectRow(colors,"明暗主题","与 Windows 系统保持一致。",new[] { "跟随系统","浅色","深色" },Value.Theme=="Light"?1:Value.Theme=="Dark"?2:0,80);
            accent=SelectRow(colors,"信息栏色调","保留低饱和配色，让文字更易读。",new[] { "系统强调色","雾蓝","淡紫","青玉" },Array.IndexOf(new[] { "System","Ocean","Plum","Jade" },Value.Accent),140);
            tint=Toggle(colors,"使用任务栏色调","关闭后使用中性底色。",Value.TintTaskbar,200);
            soft=Toggle(colors,"柔和渐变背景","完整重绘背景，避免透明叠加导致重影。",Value.SoftBackground,260);
            var density=Card(page,"尺寸与排版","字号与栏高独立调整，底部预览即时响应。",338,272);
            height=SelectRow(density,"栏高","逻辑像素，随系统 DPI 缩放。",new[] { "24","28","32" },Value.Height==28?1:Value.Height==32?2:0,80);
            font=SelectRow(density,"字号","使用常规字重，与系统界面协调。",new[] { "8","9","10","11" },Value.FontSize-8,140);
            align=SelectRow(density,"内容对齐","关闭整行填充时最明显。",new[] { "左侧","居中","右侧" },Value.Alignment=="Left"?0:Value.Alignment=="Center"?1:2,200);
            page.AutoScrollMinSize=new Size(0,630);
        }
        private void BuildConnections(Panel page)
        {
            var codex=Card(page,"Codex 额度","只读 CLI 当前账户，每 5 分钟刷新，不创建任务或使用重置额度。",0,190);
            codexPath=Field(codex,"程序路径",Value.CodexPath,78,430);
            SettingsUi.Button(codex,"选择…",594,76,104).Click+=delegate { using(var file=new OpenFileDialog { Filter="Codex 程序|*.exe",CheckFileExists=true }) if(file.ShowDialog(this)==DialogResult.OK) codexPath.Text=file.FileName; };
            SettingsUi.Label(codex,"留空自动查找本机 codex.exe",20,138,452,28,8.5f,true);
            testCodex=SettingsUi.Button(codex,"测试额度",578,132,120); testCodex.Click+=delegate { TestCodex(); };
            var ip=Card(page,"网络与位置","默认只显示本地 IP，公网位置服务需单独启用。",206,272);
            network=SelectRow(ip,"网络接口","自动选择或固定一个网卡。",new string[0],-1,78);
            network.Items.Add(new NetworkChoice("自动选择活动网卡",""));
            try { foreach(var item in NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.NetworkInterfaceType!=NetworkInterfaceType.Loopback)) network.Items.Add(new NetworkChoice(item.Name,item.Id)); } catch(NetworkInformationException) { }
            if(Value.NetworkId.Length>0 && !network.Items.Cast<NetworkChoice>().Any(x=>x.Id==Value.NetworkId)) network.Items.Add(new NetworkChoice("已选网卡（当前不可用）",Value.NetworkId));
            network.SelectedIndex=Enumerable.Range(0,network.Items.Count).FirstOrDefault(i=>((NetworkChoice)network.Items[i]).Id==Value.NetworkId);
            interval=SelectRow(ip,"资源采样间隔","CPU、内存、网速的刷新频率。",new[] { "1 秒","2 秒","5 秒" },Value.Interval==2000?1:Value.Interval==5000?2:0,140);
            geo=Toggle(ip,"查询公网 IP 与大致位置","每 10 分钟向 ipwho.is 发送出口 IP；不是 GPS，可随时关闭。",Value.GeoEnabled,200);
            SettingsUi.Label(page,"配置存储："+Settings.DefaultPath,28,496,710,52,8.5f,true);
            page.AutoScrollMinSize=new Size(0,564);
        }
        private void BuildWork(Panel page)
        {
            workWorkspace = new WorkWorkspace { Location = new Point(26, 0), Size = new Size(720, 472) };
            page.Controls.Add(workWorkspace); workWorkspace.LoadItems(Value.WorkItems);
            workWorkspace.Changed += delegate { status.Text = "有未保存的更改"; };
            page.AutoScrollMinSize = new Size(0, 490);
        }
        private void SelectPage(string id)
        {
            string[] ids={ "General","Appearance","Connections","Tracks" };
            string[] titles={ "常规与显示","外观","数据连接","工作追踪" };
            string[] descriptions={ "选择常驻信息，以及横条如何与桌面共处。","舒适的密度、克制的颜色，专注信息本身。","连接状态要可见，错误原因要可理解。","输入关键词按 Enter 新建；此页随底部设置一起保存。" };
            int index=Array.IndexOf(ids,id); if(index<0) index=0;
            title.Text=titles[index]; subtitle.Text=descriptions[index];
            foreach(var item in pages) item.Value.Visible=item.Key==ids[index];
            foreach(var item in navigation) { item.Value.Selected=item.Key==ids[index]; item.Value.Invalidate(); }
        }
        private void ApplyTheme() { SettingsUi.Theme(this,SettingsColors.From(Value)); if (workWorkspace != null) workWorkspace.ApplyTheme(Value); }
        private void MoveModule(int offset)
        {
            int index=modules.SelectedIndex,target=index+offset; if(index<0||target<0||target>=modules.Items.Count)return;
            object item=modules.Items[index]; bool enabled=modules.GetItemChecked(index);
            modules.Items.RemoveAt(index); modules.Items.Insert(target,item); modules.SetItemChecked(target,enabled); modules.SelectedIndex=target; Changed();
        }
        private void Changed()
        {
            if(initializing)return;
            Value.Dock=dock.Checked; Value.AlwaysOnTop=topmost.Checked; Value.SoftBackground=soft.Checked; Value.FillBar=fill.Checked; Value.TintTaskbar=tint.Checked;
            Value.Height=int.Parse((string)height.SelectedItem); Value.FontSize=int.Parse((string)font.SelectedItem);
            Value.Theme=new[] { "Auto","Light","Dark" }[theme.SelectedIndex]; Value.Accent=new[] { "System","Ocean","Plum","Jade" }[accent.SelectedIndex]; Value.Alignment=new[] { "Left","Center","Right" }[align.SelectedIndex];
            Value.Items=modules.CheckedItems.Cast<ModuleChoice>().Select(x=>x.Id).ToList(); preview.Settings=Value; ApplyTheme(); preview.Invalidate(); status.Text="有未保存的更改";
        }
        private void Post(Action action)
        { if(IsDisposed||!IsHandleCreated)return; try { BeginInvoke(new MethodInvoker(delegate { if(!IsDisposed) action(); })); } catch(InvalidOperationException) { } }
        private void TestCodex()
        {
            string path=codexPath.Text.Trim(); testCodex.Enabled=false; status.Text="正在读取 Codex 额度…";
            Task.Run(delegate { string result; try { result=CodexQuota.Read(path).Short; } catch(Exception error) { result=InfoHub.SafeError(error); } Post(delegate { status.Text=result; testCodex.Enabled=true; }); });
        }
        private void Save(object sender,EventArgs e)
        {
            try
            {
                List<WorkItem> keywords; string workError;
                if (!workWorkspace.TryGetItems(out keywords, out workError)) { status.Text = workError; SelectPage("Tracks"); return; }
                Changed(); if(Value.Items.Count==0)throw new InvalidOperationException("请至少选择一个显示模块。");
                Value.CodexPath=codexPath.Text.Trim(); Value.GeoEnabled=geo.Checked; Value.NetworkId=((NetworkChoice)network.SelectedItem).Id; Value.Interval=new[] { 1000,2000,5000 }[interval.SelectedIndex]; Value.WorkItems=keywords; Value.Validate();
                DialogResult=DialogResult.OK; Close();
            }
            catch(Exception error) { status.Text=InfoHub.SafeError(error); SelectPage("Connections"); }
        }
        private static TextBox Field(Control parent,string label,string value,int y,int width=546)
        {
            SettingsUi.Label(parent,label,20,y+5,124,28);
            var frame=new SettingsCard { Location=new Point(152,y),Size=new Size(width,34) }; parent.Controls.Add(frame);
            var input=new TextBox { AccessibleName=label,Text=value,Location=new Point(10,7),Width=width-20,BorderStyle=BorderStyle.None }; frame.Controls.Add(input); return input;
        }
        private static ComboBox SelectRow(Control parent,string title,string note,string[] values,int selected,int y)
        {
            SettingsUi.Label(parent,title,20,y,440,24); SettingsUi.Label(parent,note,20,y+26,440,22,8.5f,true);
            var input=new ComboBox { AccessibleName=title,Location=new Point(480,y+5),Width=218,DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat }; input.Items.AddRange(values); if(selected>=0 && selected<values.Length)input.SelectedIndex=selected; parent.Controls.Add(input); return input;
        }
        private sealed class ModuleChoice { internal string Id; public override string ToString() { return Settings.MetricNames[Array.IndexOf(Settings.MetricIds,Id)]; } }
        private sealed class NetworkChoice { internal string Id,Name; internal NetworkChoice(string name,string id) {Name=name;Id=id;} public override string ToString() {return Name;} }
    }
}
