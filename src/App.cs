using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace TiqianXiaban
{
    public sealed class App : Application
    {
        public Config Config { get; private set; } = new Config();
        CardWindow card = null!;
        TrayIcon tray = null!;
        SettingsWindow? settings;
        Fun fun = null!;
        readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
        DateTime lastQuote = DateTime.Now;
        string? lastStatus;
        DateTime paydayNoticed;
        int lastTipSecond = -1;
        bool goldOn;                        // 卡片现在是不是发薪日的金色
        DateTime paydayCelebrated;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Config = Config.Load();
            fun = new Fun(Stats.Load());
            try { AutoStart.Refresh(); } catch (Exception) { }

            card = new CardWindow(this);
            card.Card.PlayClicked += TogglePlay;
            card.Card.LeaveClicked += ToggleLeave;
            card.Card.Build(Config);
            tray = new TrayIcon(this);
            Tick();
            card.Show();
            card.PlaceAt(Config.PosX, Config.PosY);
            card.Dock.Restore(Config.DockEdge, Config.EdgeHide);

            timer.Tick += (_, _) => Tick();
            timer.Start();

            // 自检（仅开发用）：截卡片 → 打开设置并保存 → 极简模式 → 复原 → 退出，截图写到这个目录
            if (Environment.GetEnvironmentVariable("TIQIAN_SELFTEST") is string dir && dir.Length > 0) SelfTest(dir);
            if (Environment.GetEnvironmentVariable("TIQIAN_MOUSETEST") is string md && md.Length > 0)
            {
                var log = new System.Text.StringBuilder();
                var start = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                start.Tick += (_, _) =>
                {
                    start.Stop();
                    MouseTest.Run(this, card, log, () => { System.IO.File.WriteAllText(System.IO.Path.Combine(md, "mousetest.txt"), log.ToString()); Quit(); });
                };
                start.Start();
            }
        }

        void SelfTest(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            Sounds.Muted = true;
            var log = new System.Text.StringBuilder();
            void Note(string what) => log.AppendLine($"{what}: pos {card.Left:F0},{card.Top:F0} size {card.ActualWidth:F0}x{card.ActualHeight:F0}");
            int step = 0;
            double discAngle = 0, miniAngle = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) =>
            {
                switch (++step)
                {
                    case 1: Snap(card.Card, System.IO.Path.Combine(dir, "card.png")); Note("card"); log.AppendLine($"startup dock: {card.Dock.Describe()} config DockEdge={Config.DockEdge}"); OpenSettings(); break;
                    case 2:
                        var s = settings!;
                        SnapScreen(s, System.IO.Path.Combine(dir, "settings.png"));
                        Snap(s, System.IO.Path.Combine(dir, "settings-render.png"));
                        log.AppendLine($"settings: {s.ActualWidth:F0}x{s.ActualHeight:F0}");
                        s.Save(); Note("saved"); ToggleMini(); break;
                    case 3: Snap(card.Card, System.IO.Path.Combine(dir, "mini.png")); Note("mini"); ToggleMini(); break;
                    // 右键菜单和托盘菜单里有哪些项
                    case 4: card.Activate(); card.ContextMenu.PlacementTarget = card; card.ContextMenu.IsOpen = true; break;
                    case 5:
                        Snap(card.ContextMenu, System.IO.Path.Combine(dir, "menu.png"));
                        log.AppendLine("card menu: " + string.Join(" | ", card.MenuHeadersForTest()));
                        card.ContextMenu.IsOpen = false;
                        log.AppendLine("tray menu: " + string.Join(" | ", tray.MenuTextsForTest()));
                        break;
                    case 6:
                    case 7:
                        break;
                    case 8:
                        ToggleSlack();      // 开始摸鱼
                        break;
                    case 9:
                        Snap(card.Card, System.IO.Path.Combine(dir, "slacking.png"));
                        log.AppendLine($"slacking={Slacking}");
                        ToggleSlack();      // 结束摸鱼：记账、存盘、弹通知
                        var statsFile = System.IO.Path.Combine(Config.Dir, "stats.json");
                        log.AppendLine($"after ending: slacking={Slacking} stats.json exists={System.IO.File.Exists(statsFile)}");
                        if (System.IO.File.Exists(statsFile)) log.AppendLine(System.IO.File.ReadAllText(statsFile).Replace("\n", " ").Replace("  ", ""));
                        card.Card.PlayCelebration("下班啦！");
                        break;
                    case 10:
                        // 动画放到一半时截一张
                        Snap(card.Card, System.IO.Path.Combine(dir, "celebrate.png"));
                        var pay = Config.Clone();
                        pay.Payday = DateTime.Now.Day;      // 把发薪日改成今天：卡片应该自己变金色并撒彩纸
                        Apply(pay);
                        break;
                    case 11:
                        Snap(card.Card, System.IO.Path.Combine(dir, "payday.png"));
                        log.AppendLine($"payday today: gold={goldOn}");
                        // 贴边：主屏左边外面还有一块显示器，不该贴上
                        card.Dock.TestMoveToEdge(EdgeDock.Edge.Left);
                        card.Dock.AfterDrag(true);
                        log.AppendLine($"dock left (another monitor beside it): {card.Dock.Describe()}");
                        card.Dock.TestMoveToEdge(EdgeDock.Edge.Right);
                        card.Dock.AfterDrag(true);
                        log.AppendLine($"dock right: {card.Dock.Describe()}");
                        break;
                    case 12:
                        log.AppendLine($"mouse away 1.5 s later: {card.Dock.Describe()}");
                        SavePosition();
                        log.AppendLine($"saved: DockEdge={Config.DockEdge} PosX={Config.PosX:F0} PosY={Config.PosY:F0}");
                        card.Dock.SlideOut();       // 相当于鼠标移到那条边上
                        break;
                    case 13:
                        log.AppendLine($"hover: {card.Dock.Describe()}");
                        ToggleMini();
                        log.AppendLine($"mini while docked: {card.Dock.Describe()}");
                        ToggleMini();
                        card.Dock.TestMoveToEdge(EdgeDock.Edge.Top);
                        card.Dock.AfterDrag(true);
                        log.AppendLine($"dock top: {card.Dock.Describe()}");
                        break;
                    case 14:
                        log.AppendLine($"top, mouse away: {card.Dock.Describe()}");
                        var off = Config.Clone();
                        off.EdgeHide = false;         // 关掉贴边隐藏：应该滑出来、不再贴
                        Apply(off);
                        break;
                    case 15:
                        log.AppendLine($"edge hide turned off: {card.Dock.Describe()} DockEdge='{Config.DockEdge}'");
                        var on = Config.Clone();
                        on.EdgeHide = true;
                        Apply(on);
                        // 副屏（在主屏左边）：它的左边是最外侧，能贴；右边挨着主屏，不能贴
                        card.Dock.TestMoveTo(-900, 500);
                        card.Dock.TestMoveToEdge(EdgeDock.Edge.Right);
                        card.Dock.AfterDrag(true);
                        log.AppendLine($"secondary, right edge (primary beside it): {card.Dock.Describe()}");
                        card.Dock.TestMoveToEdge(EdgeDock.Edge.Left);
                        card.Dock.AfterDrag(true);
                        log.AppendLine($"secondary, left edge: {card.Dock.Describe()}");
                        break;
                    case 16:
                        log.AppendLine($"secondary, left, mouse away: {card.Dock.Describe()}");
                        card.Dock.Undock();
                        // 提钱下班：把作息改成全天上班，保证现在是上班时间
                        var allDay = Config.Clone();
                        allDay.Start = "00:00"; allDay.End = "23:59"; allDay.LunchDeduct = false;
                        for (int i = 0; i < 7; i++) allDay.Workdays[i] = true;
                        Apply(allDay);
                        break;
                    case 17:
                        // 唱机：▶ 开工 → 唱片转；⏸ → 摸鱼、唱片停；▶ → 结束摸鱼、接着转
                        log.AppendLine($"before play: spinning={card.Card.SpinningForTest} slacking={Slacking}");
                        TogglePlay();
                        discAngle = card.Card.DiscAngleForTest;
                        break;
                    case 18:
                        log.AppendLine($"after ▶: spinning={card.Card.SpinningForTest} disc moved {card.Card.DiscAngleForTest - discAngle:F0}° in 1.5 s (360° = 3.6 s)");
                        Snap(card.Card, System.IO.Path.Combine(dir, "playing.png"));
                        TogglePlay();
                        discAngle = card.Card.DiscAngleForTest;
                        break;
                    case 19:
                        log.AppendLine($"after ⏸: spinning={card.Card.SpinningForTest} slacking={Slacking} disc moved {card.Card.DiscAngleForTest - discAngle:F0}°");
                        TogglePlay();
                        log.AppendLine($"after ▶ again: spinning={card.Card.SpinningForTest} slacking={Slacking}");
                        ToggleMini();       // 极简模式的小唱片也该接着转
                        miniAngle = card.Card.DiscAngleForTest;
                        log.AppendLine($"mini: spinning={card.Card.SpinningForTest}");
                        break;
                    case 20:
                        log.AppendLine($"mini 1.5 s later: disc moved {card.Card.DiscAngleForTest - miniAngle:F0}°");
                        Snap(card.Card, System.IO.Path.Combine(dir, "mini-playing.png"));
                        ToggleMini();
                        log.AppendLine($"before leaving: status={Payroll.Calc(Config, DateTime.Now).Status} canLeave={CanLeave}");
                        log.AppendLine($"coin sound: resource ok={Sounds.CoinReady} sound setting={Config.Sound} played so far={Sounds.CoinCount}");
                        ToggleLeave();
                        log.AppendLine($"coin after 下班: played={Sounds.CoinCount}");
                        break;
                    case 21:
                        Snap(card.Card, System.IO.Path.Combine(dir, "leave.png"));
                        var statsPath = System.IO.Path.Combine(Config.Dir, "stats.json");
                        log.AppendLine($"after clicking 下班: leftEarly={LeftEarly} saved={System.IO.File.ReadAllText(statsPath).Contains("LeftEarlyAt\": \"")}");
                        ToggleLeave();      // 再点一下：撤销
                        log.AppendLine($"after clicking again: leftEarly={LeftEarly} coin played={Sounds.CoinCount} (undo is silent)");
                        var quiet = Config.Clone();
                        quiet.Sound = false;        // 关掉音效：再提钱下班不该响
                        Apply(quiet);
                        ToggleLeave();
                        log.AppendLine($"sound off, 下班 again: coin played={Sounds.CoinCount}");
                        ToggleLeave();
                        break;
                    default:
                        t.Stop();
                        Note("restored");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "selftest.txt"), log.ToString());
                        Quit();
                        break;
                }
            };
            t.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

        /// <summary>截屏幕上一块区域，带 CAPTUREBLT，分层窗口（带阴影的菜单）也截得到</summary>
        static void Grab(RECT r, string path)
        {
            using var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                IntPtr screen = GetDC(IntPtr.Zero), dst = g.GetHdc();
                BitBlt(dst, 0, 0, bmp.Width, bmp.Height, screen, r.Left, r.Top, 0x00CC0020 | 0x40000000);   // SRCCOPY | CAPTUREBLT
                g.ReleaseHdc(dst);
                ReleaseDC(IntPtr.Zero, screen);
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        /// <summary>截屏幕上这个窗口实际显示的样子（含标题栏）</summary>
        static void SnapScreen(Window w, string path)
        {
            GetWindowRect(new System.Windows.Interop.WindowInteropHelper(w).Handle, out var r);
            Grab(r, path);
        }

        static void SnapPopup(System.Windows.Media.Visual v, string path)
        {
            var src = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(v);
            GetWindowRect(src.Handle, out var r);
            Grab(r, path);
        }

        static void Snap(System.Windows.Media.Visual v, string path)
        {
            var fe = (FrameworkElement)v;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(v);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(fe.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(fe.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(v);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = System.IO.File.Create(path);
            enc.Save(f);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            FinishSlackAndSave();
            tray?.Dispose();
            base.OnExit(e);
        }

        // 注销 / 关机时程序会被直接结束，先把摸鱼记录存下来
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            FinishSlackAndSave();
            base.OnSessionEnding(e);
        }

        void FinishSlackAndSave()
        {
            if (fun == null) return;
            if (fun.Slacking) { var now = DateTime.Now; fun.EndSlack(now, Payroll.Calc(Config, now)); }
            fun.Save();
        }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        /// <summary>多久没碰键盘鼠标</summary>
        static TimeSpan IdleTime()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        }

        public bool Slacking => fun.Slacking;

        /// <summary>开始 / 结束带薪摸鱼；结束时弹一条通知说老板付了多少</summary>
        public void ToggleSlack()
        {
            var now = DateTime.Now;
            var r = Payroll.Calc(Config, now);
            if (!fun.Slacking)
            {
                fun.StartSlack(now, r);
            }
            else
            {
                var (dur, money) = fun.EndSlack(now, r);
                fun.Save();
                var (daySec, dayMoney) = fun.Today(now);
                var inv = CultureInfo.InvariantCulture;
                string took = Fun.Minutes(dur.TotalSeconds);
                if (money < 0.005)
                    tray.Notify($"这次摸了 {took}", "是在休息时间摸的，不花老板的钱，放心摸 😄");
                else
                    tray.Notify($"本次带薪摸鱼 {took}",
                        $"老板为你付了 ¥{money.ToString("F2", inv)}。今天一共摸了 {Fun.Minutes(daySec)}，¥{dayMoney.ToString("F2", inv)}。");
            }
            Tick();
        }

        public bool LeftEarly => fun.LeftEarly(DateTime.Now);

        /// <summary>
        /// 唱机中间的按钮：上班时间第一次点 ▶ 是开工（唱片转起来）；转着的时候点 ⏸ 是带薪摸鱼（唱片停下）；
        /// 摸鱼时点 ▶ 结束摸鱼、接着转。收入照样按设置的上下班时间算。
        /// </summary>
        public void TogglePlay()
        {
            var now = DateTime.Now;
            if (fun.Slacking)
            {
                ToggleSlack();          // 结束摸鱼，弹"老板为你付了多少"
                fun.StartWork(now);
                Tick();
                return;
            }
            var r = Payroll.Calc(Config, now);
            if ((r.Status != "上班中" && r.Status != "午休中") || fun.LeftEarly(now)) return;
            if (!fun.Started(now)) { fun.StartWork(now); Tick(); return; }
            ToggleSlack();              // 开始摸鱼
        }
        public bool CanLeave { get; private set; }      // 现在能不能点"下班"（菜单用）

        /// <summary>
        /// 提钱下班：今天剩下的工资一次性到手，金额往上滚、撒彩纸、弹"提钱下班！"。
        /// 已经提前下班了再点就是撤销（点错了能回去）。
        /// </summary>
        public void ToggleLeave()
        {
            var now = DateTime.Now;
            if (fun.LeftEarly(now)) { fun.UndoLeave(now); Tick(); return; }
            var before = Payroll.Calc(Config, now);
            if (before.Status != "上班中" && before.Status != "午休中") return;
            if (fun.Slacking) fun.EndSlack(now, before);
            double from = before.Earned;
            fun.LeaveEarly(now);
            Tick();
            var after = Payroll.Calc(Config, now);
            card.Card.RollMoney(from, after.FullDay);
            Celebrate("提钱下班！", coin: true);
            var (eh, em) = Config.ParseTime(Config.End);
            var early = now.Date.AddHours(eh).AddMinutes(em) - now;
            tray.Notify("提钱下班！",
                $"今天的 ¥{after.FullDay.ToString("F2", CultureInfo.InvariantCulture)} 已经到手，提前 {(int)early.TotalHours}:{early.Minutes:00}:{early.Seconds:00} 下班，路上注意安全～");
        }

        void Tick()
        {
            var now = DateTime.Now;
            var r = Payroll.Calc(Config, now);
            var notes = new List<(string, string)>();
            var view = fun.Update(Config, r, now, IdleTime(), notes);
            CanLeave = view.CanLeave;
            // 发薪日当天整张卡片换成金色，过了零点换回来
            bool gold = r.DaysToPayday == 0;
            if (gold != goldOn)
            {
                goldOn = gold;
                card.Card.Build(Config, goldOn);
            }
            card.Card.Update(r, now);
            card.Card.UpdateFun(view);

            if ((now - lastQuote).TotalMinutes >= 5) { card.Card.NextQuote(); lastQuote = now; }

            if (now.Second != lastTipSecond)
            {
                lastTipSecond = now.Second;
                tray.SetTip($"提钱下班\n{r.Status} · 今日已赚 ¥{r.Earned.ToString("F2", CultureInfo.InvariantCulture)}\n{r.Countdown}");
            }

            // 下班那一刻撒彩纸；发薪日当天第一次看到卡片也撒一次
            bool clockedOut = lastStatus == "上班中" && (r.Status == "已下班" || r.Status == "加班中" || r.Status == "免费加班中");
            if (clockedOut) Celebrate("下班啦！", coin: true);
            else if (gold && paydayCelebrated != now.Date && card.IsVisible)
            {
                paydayCelebrated = now.Date;
                Celebrate("发薪日快乐！");
            }

            // 鼓励：下班那一刻、发薪当天各提醒一次
            // 下班、发薪日、放假前一天、免费加班满 1 小时：都弹通知
            if (clockedOut)
                tray.Notify("下班啦，今天辛苦了！",
                    $"今日已赚 ¥{r.Earned.ToString("F2", CultureInfo.InvariantCulture)}，{(Config.PeriodMode == 0 ? "本月" : "本期")}累计 ¥{r.PeriodTotal.ToString("F2", CultureInfo.InvariantCulture)}。");
            foreach (var (title, text) in notes) tray.Notify(title, text);
            if (r.DaysToPayday == 0 && paydayNoticed != now.Date && now.Hour >= 9)
            {
                paydayNoticed = now.Date;
                tray.Notify("今天发工资！", "这个月的辛苦，今天到账。");
            }
            lastStatus = r.Status;
        }

        public void QuoteTimerReset() => lastQuote = DateTime.Now;

        /// <summary>撒彩纸；卡片贴边缩着的话先滑出来，放完再缩回去。下班的两种庆祝还会"叮铛"一声钱币响</summary>
        void Celebrate(string text, bool coin = false)
        {
            card.Dock.PeekFor(TimeSpan.FromSeconds(3.5));
            card.Card.PlayCelebration(text);
            if (coin && Config.Sound) Sounds.PlayCoin();
        }

        /// <summary>配置改了以后：重建卡片布局，保持左上角位置不动，存盘</summary>
        public void Apply(Config c)
        {
            c.PosX = card.RestLeft;
            c.PosY = card.RestTop;
            if (!c.EdgeHide) card.Dock.Undock();
            c.DockEdge = card.Dock.Docked == EdgeDock.Edge.None ? "" : card.Dock.Docked.ToString();
            Config = c;
            card.Card.Build(Config, goldOn);
            Tick();
            card.PlaceAt(Config.PosX, Config.PosY);
            Config.Save();
            settings?.SyncToggles();
        }

        public void SavePosition()
        {
            Config.PosX = card.RestLeft;
            Config.PosY = card.RestTop;
            Config.DockEdge = card.Dock.Docked == EdgeDock.Edge.None ? "" : card.Dock.Docked.ToString();
            Config.Save();
        }

        public void ToggleCard()
        {
            if (card.IsVisible) card.Hide();
            else
            {
                card.Show();
                card.PlaceAt(card.RestLeft, card.RestTop);
                card.Dock.PeekFor(TimeSpan.FromSeconds(2));     // 贴着边的话先滑出来让人看见
            }
        }

        public void ToggleMini()
        {
            var c = Config.Clone();
            c.Mini = !c.Mini;
            Apply(c);
        }


        public void ToggleAutoStart()
        {
            try { AutoStart.Set(!AutoStart.IsEnabled); }
            catch (Exception ex) { MessageBox.Show("没能修改开机启动设置：" + ex.Message, "提钱下班"); }
            settings?.SyncToggles();
        }

        public void OpenSettings()
        {
            if (settings == null)
            {
                settings = new SettingsWindow(this);
                settings.Closed += (_, _) => settings = null;
                settings.Show();
            }
            settings.Activate();
        }

        public void Quit()
        {
            SavePosition();
            settings?.Close();
            Shutdown();
        }
    }
}
