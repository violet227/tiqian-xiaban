using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Forms = System.Windows.Forms;

namespace TiqianXiaban
{
    /// <summary>任务栏右下角的托盘图标：单击显示 / 隐藏卡片，右键菜单，悬停显示今日已赚</summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
        readonly List<(Forms.ToolStripMenuItem Item, Func<bool> State)> toggles = new List<(Forms.ToolStripMenuItem, Func<bool>)>();
        readonly HashSet<Forms.ToolStripItem> stayOpen = new HashSet<Forms.ToolStripItem>();
        bool keepOpen;

        public TrayIcon(App app)
        {
            icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "提钱下班", Visible = true };
            var slack = new Forms.ToolStripMenuItem("开始摸鱼 🐟", null, (_, _) => app.ToggleSlack());
            menu.Items.Add(slack);
            var leave = new Forms.ToolStripMenuItem("提钱下班 🏃", null, (_, _) => app.ToggleLeave());
            menu.Items.Add(leave);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("显示 / 隐藏卡片", null, (_, _) => app.ToggleCard());
            menu.Items.Add(Toggle("极简模式", app.ToggleMini, () => app.Config.Mini, keep: false));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("设置…", null, (_, _) => app.OpenSettings());
            menu.Items.Add(Toggle("开机自动启动", app.ToggleAutoStart, () => AutoStart.IsEnabled));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => app.Quit());
            menu.Opening += (_, _) =>
            {
                foreach (var (i, state) in toggles) i.Checked = state();
                slack.Text = app.Slacking ? "结束摸鱼，看看老板付了多少" : "开始摸鱼 🐟";
                leave.Text = app.LeftEarly ? "撤销提前下班，回去上班" : "提钱下班 🏃";
                leave.Enabled = app.CanLeave;
            };
            // 点开关项时菜单不关，勾当场跟着变
            menu.ItemClicked += (_, e) => keepOpen = e.ClickedItem != null && stayOpen.Contains(e.ClickedItem);
            menu.Closing += (_, e) =>
            {
                if (e.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked && keepOpen) e.Cancel = true;
                keepOpen = false;
            };
            icon.ContextMenuStrip = menu;
            icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) app.ToggleCard(); };
        }

        Forms.ToolStripMenuItem Toggle(string text, Action act, Func<bool> state, bool keep = true)
        {
            var i = new Forms.ToolStripMenuItem(text);
            i.Click += (_, _) => { act(); i.Checked = state(); };
            toggles.Add((i, state));
            if (keep) stayOpen.Add(i);
            return i;
        }

        static Icon LoadIcon()
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
            return s != null ? new Icon(s, Forms.SystemInformation.SmallIconSize) : SystemIcons.Application;
        }

        /// <summary>悬停提示，系统限制最多 127 个字符</summary>
        public void SetTip(string text)
        {
            if (text.Length > 127) text = text.Substring(0, 127);
            if (icon.Text != text) icon.Text = text;
        }

        public void Notify(string title, string text) =>
            icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.None);

        // 自检用：弹出托盘菜单、点"下班 / 发薪提醒"，看菜单是否还开着、勾是否跟着变
        public void ShowMenuAt(int x, int y) => menu.Show(x, y);
        public void HideMenu() => menu.Close();
        public bool MenuVisible => menu.Visible;
        public System.Collections.Generic.IEnumerable<string> MenuTextsForTest()
        {
            foreach (Forms.ToolStripItem i in menu.Items) if (i is Forms.ToolStripMenuItem m) yield return m.Text ?? "";
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
