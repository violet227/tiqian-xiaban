using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TiqianXiaban
{
    /// <summary>
    /// 开发 / 截图用：把某一时刻的卡片渲染成 PNG，不弹窗，用默认配置（不读用户配置）。
    ///   TiqianXiaban.exe --render out.png [--at "2026-11-10 11:14:10"] [--mini] [--theme 0|1] [--scale 1.0] [--dpi 2]
    /// </summary>
    public static class RenderTool
    {
        /// <summary>TiqianXiaban.exe --calc out.txt [--overtime] "2026-11-10 12:30:00" ...：默认配置下各时刻的计算结果</summary>
        public static int Calc(string[] args)
        {
            var c = new Config();
            using var w = new StreamWriter(args.Length > 1 ? args[1] : "calc.txt");
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--user") { c = Config.Load(); continue; }        // 只读你的真实配置，不写
                if (args[i] == "--overtime") { c.Overtime = true; continue; }
                if (args[i] == "--fixed") { c.AutoPayDays = false; continue; }   // 固定 21.75 天（法定口径）
                if (args[i] == "--period") { c.PeriodMode = int.Parse(args[++i], CultureInfo.InvariantCulture); continue; }   // 0 自然月 1 发薪日 2 固定日
                if (args[i] == "--period-day") { c.PeriodStartDay = int.Parse(args[++i], CultureInfo.InvariantCulture); continue; }
                if (args[i] == "--payday") { c.Payday = int.Parse(args[++i], CultureInfo.InvariantCulture); continue; }
                var at = DateTime.Parse(args[i], CultureInfo.InvariantCulture);
                var r = Payroll.Calc(c, at);
                w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd ddd HH:mm:ss} | {1} | earned {2:F3} | {3} | worked {4:F2}/{5:F2} h | period {6:F2} | payday in {7} d | pay days {8:0.##} | full day {9:F2}",
                    at, r.Status, r.Earned, r.Countdown, r.WorkedHours, r.TotalHours, r.PeriodTotal, r.DaysToPayday, r.PayDays, r.FullDay) + $" | from {r.PeriodStart:yyyy-MM-dd}");
            }
            return 0;
        }

        /// <summary>
        /// TiqianXiaban.exe --funtest out.txt：在模拟的时间线上跑 Fun，检查免费加班只在人在时累计、
        /// 跨午休的摸鱼只算计薪的那段、跨零点的摸鱼拆到两天
        /// </summary>
        public static int FunTest(string[] args)
        {
            var c = new Config();      // 9:00-18:00，午休 12:00-14:00，月薪 9800，不计加班
            var stats = new Stats();
            var fun = new Fun(stats, persist: false);
            var w = new StringWriter(CultureInfo.InvariantCulture);

            // 1) 18:00 下班后：前 20 分钟人在（idle 0），之后 40 分钟没人碰电脑（idle 增长）
            var t = new DateTime(2026, 11, 10, 17, 59, 0);
            for (int i = 0; i <= 3600 + 60; i++, t = t.AddSeconds(1))
            {
                var idle = t < new DateTime(2026, 11, 10, 18, 20, 0) ? TimeSpan.Zero : t - new DateTime(2026, 11, 10, 18, 20, 0);
                var r = Payroll.Calc(c, t);
                fun.Update(c, r, t, idle);
                if (t == new DateTime(2026, 11, 10, 18, 10, 0) || t == new DateTime(2026, 11, 10, 18, 59, 0))
                    w.WriteLine($"{t:HH:mm:ss} status={r.Status} countdown={r.Countdown}");
            }
            w.WriteLine($"free overtime counted: {stats.Day(t).FreeOtSec / 60:F1} min (expect ~20)");

            // 1b) 11-13：18:00-18:30 在，去吃饭 45 分钟，19:15-19:45 回来接着干，之后回家，22:00-22:30 在家开电脑
            DateTime T(int h, int m) => new DateTime(2026, 11, 13, h, m, 0);
            DateTime seen = T(17, 59);
            for (var u = T(17, 59); u <= T(22, 40); u = u.AddSeconds(1))
            {
                bool here = (u >= T(18, 0) && u < T(18, 30)) || (u >= T(19, 15) && u < T(19, 45)) || (u >= T(22, 0) && u < T(22, 30)) || u < T(18, 0);
                if (here) seen = u;
                fun.Update(c, Payroll.Calc(c, u), u, u - seen);
            }
            w.WriteLine($"dinner break then home: {stats.Day(T(12, 0)).FreeOtSec / 60:F1} min (expect 60: 30 before dinner + 30 after, not the 22:00 home session)");

            // 2) 11:50 开始摸鱼，12:10 结束（12:00 起午休不计薪）：只算 10 分钟
            var a = new DateTime(2026, 11, 11, 11, 50, 0);
            fun.StartSlack(a, Payroll.Calc(c, a));
            var b = new DateTime(2026, 11, 11, 12, 10, 0);
            var (dur, money) = fun.EndSlack(b, Payroll.Calc(c, b));
            double perMin = Payroll.Calc(c, a).SpeedPerMin;
            w.WriteLine($"slack across lunch: {dur.TotalMinutes:F0} min, ¥{money:F2} (expect 10 paid min = ¥{perMin * 10:F2})");

            // 3) 加班计薪打开，23:50 开始摸鱼，次日 00:10 还在摸：前一天记 10 分钟
            c.Overtime = true;
            var x = new DateTime(2026, 11, 11, 23, 50, 0);
            fun.StartSlack(x, Payroll.Calc(c, x));
            var y = new DateTime(2026, 11, 12, 0, 10, 0);
            fun.Update(c, Payroll.Calc(c, y), y, TimeSpan.Zero);
            w.WriteLine($"slack across midnight: 11-11 got {stats.Day(x).SlackSec / 60:F1} min ¥{stats.Day(x).SlackMoney:F2} (paid: 11:50-12:00 earlier ¥{perMin * 10:F2} + 10 min overtime x1.5 ¥{perMin * 1.5 * 10:F2}); still slacking={fun.Slacking}");
            File.WriteAllText(args.Length > 1 ? args[1] : "funtest.txt", w.ToString());
            return 0;
        }

        public static int Run(string[] args)
        {
            string output = args.Length > 1 ? args[1] : "card.png";
            var c = new Config();
            var at = DateTime.Now;
            double dpi = 2, slackMin = 0, freeOtMin = 0, leftMin = 0;
            bool started = false;
            for (int i = 2; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : "";
                switch (args[i])
                {
                    case "--at": at = DateTime.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--mini": c.Mini = true; break;
                    case "--theme": c.Theme = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--scale": c.Scale = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--dpi": dpi = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--overtime": c.Overtime = true; break;
                    case "--fixed": c.AutoPayDays = false; break;
                    case "--slack-min": slackMin = double.Parse(Next(), CultureInfo.InvariantCulture); break;      // 假装已经摸鱼这么多分钟
                    case "--started": started = true; break;                                                   // 今天已经点过 ▶ 开工
                    case "--left-min": leftMin = double.Parse(Next(), CultureInfo.InvariantCulture); break;        // 假装这么多分钟前已经提钱下班
                    case "--free-ot-min": freeOtMin = double.Parse(Next(), CultureInfo.InvariantCulture); break;  // 假装已经免费加班这么多分钟
                    case "--period": c.PeriodMode = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                }
            }

            var card = new CardView();
            var r = Payroll.Calc(c, at);
            card.Build(c, payday: r.DaysToPayday == 0);
            var fun = new Fun(new Stats(), persist: false);
            if (slackMin > 0) fun.FakeSlack(at, TimeSpan.FromMinutes(slackMin), c);
            if (freeOtMin > 0) fun.FakeFreeOvertime(at, freeOtMin * 60);
            if (leftMin > 0) fun.LeaveEarly(at.AddMinutes(-leftMin));
            if (started) fun.StartWork(at.Date.AddHours(9));
            var view = fun.Update(c, r, at, TimeSpan.Zero);
            card.Update(r, at);
            card.UpdateFun(view);
            // 放在一块桌面色的底板上，看得出卡片的透明度与边框
            var host = new System.Windows.Controls.Border
            {
                Padding = new Thickness(16),
                Background = new SolidColorBrush(c.Theme == 1 ? Color.FromRgb(0xE9, 0xEC, 0xF1) : Color.FromRgb(0x3A, 0x4A, 0x5E)),
                Child = card,
            };
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();

            var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * dpi), (int)Math.Ceiling(host.ActualHeight * dpi),
                96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bmp.Render(host);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(output);
            enc.Save(f);
            return 0;
        }
    }
}
