using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TiqianXiaban
{
    /// <summary>
    /// 悬浮窗：无边框、透明、置顶，不进任务栏也不进 Alt+Tab。
    /// 拖动移动（位置会记住），双击切换极简模式，右键弹菜单。
    /// </summary>
    public sealed class CardWindow : Window
    {
        readonly App app;
        public readonly CardView Card = new CardView();
        public readonly EdgeDock Dock;
        readonly List<(MenuItem Item, Func<bool> State)> toggles = new List<(MenuItem, Func<bool>)>();
        MenuItem slackItem = null!, leaveItem = null!;
        public MenuItem SlackItemForTest => slackItem;             // 自检用
        public System.Collections.Generic.IEnumerable<string> MenuHeadersForTest()
        {
            foreach (var i in ContextMenu.Items) if (i is MenuItem m) yield return m.Header?.ToString() ?? "";
        }

        public CardWindow(App app)
        {
            this.app = app;
            Title = "提钱下班";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = Card;
            ContextMenu = BuildMenu();
            Dock = new EdgeDock(this) { Busy = () => ContextMenu.IsOpen };
            ContextMenu.Closed += (_, _) => Dock.HideLater();
            Card.QuoteClicked += () => app.QuoteTimerReset();

            MouseLeftButtonDown += OnMouseDown;
            SourceInitialized += (_, _) => HideFromAltTab();
        }

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            slackItem = Item("开始摸鱼 🐟", app.ToggleSlack);
            m.Items.Add(slackItem);
            leaveItem = Item("提钱下班 🏃", app.ToggleLeave);
            m.Items.Add(leaveItem);
            m.Items.Add(new Separator());
            m.Items.Add(Item("设置…", app.OpenSettings));
            m.Items.Add(Toggle("极简模式", app.ToggleMini, () => app.Config.Mini, stayOpen: false));
            m.Items.Add(Item("隐藏卡片（托盘图标可找回）", app.ToggleCard));
            m.Items.Add(new Separator());
            m.Items.Add(Toggle("开机自动启动", app.ToggleAutoStart, () => AutoStart.IsEnabled));
            m.Items.Add(new Separator());
            m.Items.Add(Item("退出", app.Quit));
            m.Opened += (_, _) =>
            {
                foreach (var (i, state) in toggles) i.IsChecked = state();
                slackItem.Header = app.Slacking ? "结束摸鱼，看看老板付了多少" : "开始摸鱼 🐟";
                leaveItem.Header = app.LeftEarly ? "撤销提前下班，回去上班" : "提钱下班 🏃";
                leaveItem.IsEnabled = app.CanLeave;
            };
            return m;
        }

        static MenuItem Item(string header, Action act)
        {
            var i = new MenuItem { Header = header };
            i.Click += (_, _) => act();
            return i;
        }

        // 开关项：点了以后菜单不关，勾当场跟着变，看得见开关生效了。
        // 勾始终按实际状态显示（WPF 先自己翻一次勾，这里再按真实状态校正）。
        MenuItem Toggle(string header, Action act, Func<bool> state, bool stayOpen = true)
        {
            var i = new MenuItem { Header = header, IsCheckable = true, StaysOpenOnClick = stayOpen };
            i.Click += (_, _) => { act(); i.IsChecked = state(); };
            toggles.Add((i, state));
            return i;
        }

        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { app.ToggleMini(); return; }
            Dock.Dragging = true;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            finally { Dock.Dragging = false; }
            Dock.AfterDrag(app.Config.EdgeHide);     // 拖到边上就贴边
            app.SavePosition();
        }

        /// <summary>该存进配置的位置：贴边缩着的时候是展开时的位置，不是缩到屏幕外的位置</summary>
        public double RestLeft => Dock.ShownDip()?.X ?? Left;
        public double RestTop => Dock.ShownDip()?.Y ?? Top;

        /// <summary>按配置放到上次的位置；位置不在任何屏幕上（比如拔了外接显示器）就放回主屏右上角</summary>
        public void PlaceAt(double x, double y)
        {
            UpdateLayout();
            double w = ActualWidth > 0 ? ActualWidth : 300, h = ActualHeight > 0 ? ActualHeight : 320;
            bool onScreen = !double.IsNaN(x) && !double.IsNaN(y)
                && x + w > SystemParameters.VirtualScreenLeft + 40
                && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40
                && y > SystemParameters.VirtualScreenTop - 10
                && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
            if (!onScreen)
            {
                var wa = SystemParameters.WorkArea;
                x = wa.Right - w - 24;
                y = wa.Top + 24;
            }
            Left = x;
            Top = y;
            Dock.Relayout();        // 贴着边的话按新尺寸重新贴
        }

        // ---------------------------------------------------------------- Alt+Tab
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

        void HideFromAltTab()
        {
            var h = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        }
    }
}
