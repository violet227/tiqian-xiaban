using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TiqianXiaban
{
    /// <summary>一天的摸鱼与免费加班记录</summary>
    public sealed class DayStats
    {
        public double SlackSec { get; set; }
        public double SlackMoney { get; set; }
        public double FreeOtSec { get; set; }
        public DateTime? LeftEarlyAt { get; set; }     // 这天点了"下班"（提钱下班）的时刻
        public DateTime? StartedAt { get; set; }       // 这天点了 ▶ 开工的时刻（唱片开始转）
    }

    /// <summary>每天的记录，存在配置目录的 stats.json，保留最近 400 天</summary>
    public sealed class Stats
    {
        public Dictionary<string, DayStats> Days { get; set; } = new Dictionary<string, DayStats>();

        static string FilePath => Path.Combine(Config.Dir, "stats.json");

        public static Stats Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Stats>(File.ReadAllText(FilePath)) ?? new Stats();
            }
            catch (Exception) { }
            return new Stats();
        }

        public void Save()
        {
            try
            {
                var cutoff = Payroll.DayKey(DateTime.Today.AddDays(-400));
                foreach (var k in Days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) Days.Remove(k);
                Directory.CreateDirectory(Config.Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception) { }
        }

        public DayStats Day(DateTime d)
        {
            var k = Payroll.DayKey(d);
            if (!Days.TryGetValue(k, out var s)) Days[k] = s = new DayStats();
            return s;
        }
    }

    /// <summary>卡片上"情绪价值"那几块要显示的内容</summary>
    public sealed class FunView
    {
        public bool Slacking;
        public string SlackButton = "🐟 摸鱼";
        public string SlackTip = "";
        public string Convert = "";
        public string Holiday = "";
        public string HolidayTip = "";
        public bool HolidaySoon;
        public bool CanLeave;                // 显示"下班"按钮：工作日的上班时间里，或者今天已经提前下班了
        public bool LeftEarly;
        public string LeaveButton = "🏃 下班";
        public string LeaveTip = "";
        // 播放器：唱片转不转、中间按钮是 ▶ 还是 ⏸、能不能点
        public bool Started;                 // 今天点过 ▶ 开工
        public bool Spinning;                // 开工了、没在摸鱼、正在上班（午休时停）
        public bool ShowPause;               // 中间按钮显示 ⏸（点了就是带薪摸鱼）
        public bool CanPlay;                 // 上班时间（含午休）才能点
        public string PlayTip = "";
        public string HolidayShort = "";     // 播放器底部一格放得下的短写法
        public string[]? QuoteOverride;      // 免费加班时换成劝下班的话
    }

    /// <summary>
    /// 摸鱼计价器、实物换算、假期倒计时、免费加班心疼值。
    /// 计薪本身仍由 Payroll 算；这里只在它的结果上加东西。
    /// </summary>
    public sealed class Fun
    {
        public const double ActiveIdleLimitMin = 5;      // 这么久没碰键鼠，就当人已经走了，不再算免费加班
        public const double LeftForDayMin = 90;          // 下班后离开超过这么久（不是吃个晚饭就回来），当天就不再算

        static readonly string[] GoHome =
        {
            "该下班啦，工作是做不完的。",
            "今天的工资早就赚满了，回家吧。",
            "你的时间也很值钱。",
            "再不走，晚饭都要凉了。",
        };

        static readonly string[] Payday =
        {
            "今天发工资！这个月的辛苦都值了。",
            "工资到账，先奖励自己一杯奶茶。",
            "钱包鼓起来了，腰杆也挺起来了。",
            "发薪日快乐，打工人的节日。",
            "今天的每一秒都闪着金光。",
        };

        readonly Stats stats;
        readonly bool persist;
        DateTime? slackStart;
        double slackStartEarned;
        DateTime? lastInput;                // 上一次刷新时看到的"最后一次键鼠操作"时刻
        DateTime freeOtOver;                // 这一天下班后人已经走了，不再算免费加班
        DateTime lastSave = DateTime.Now;
        bool dirty;
        string notifiedHoliday = "", notifiedFreeOt = "";

        public Fun(Stats stats, bool persist = true)
        {
            this.stats = stats;
            this.persist = persist;
        }

        public bool Slacking => slackStart != null;

        // ------------------------------------------------------------ 提钱下班
        public bool LeftEarly(DateTime now) => stats.Day(now).LeftEarlyAt != null;

        // ------------------------------------------------------------ ▶ 开工
        public bool Started(DateTime now) => stats.Day(now).StartedAt != null;

        public void StartWork(DateTime now)
        {
            if (stats.Day(now).StartedAt != null) return;
            stats.Day(now).StartedAt = now;
            dirty = true;
            Save();
        }

        public void LeaveEarly(DateTime now)
        {
            stats.Day(now).LeftEarlyAt = now;
            dirty = true;
            Save();
        }

        public void UndoLeave(DateTime now)
        {
            stats.Day(now).LeftEarlyAt = null;
            dirty = true;
            Save();
        }

        // ------------------------------------------------------------ 摸鱼
        public void StartSlack(DateTime now, CalcResult r)
        {
            slackStart = now;
            slackStartEarned = r.Earned;
        }

        /// <summary>结束这次摸鱼并记账，返回这次的时长与金额</summary>
        public (TimeSpan Duration, double Money) EndSlack(DateTime now, CalcResult r)
        {
            if (slackStart == null) return (TimeSpan.Zero, 0);
            var (dur, money) = Session(now, r);
            var day = stats.Day(now);
            day.SlackSec += dur.TotalSeconds;
            day.SlackMoney += money;
            slackStart = null;
            dirty = true;
            return (dur, money);
        }

        /// <summary>今天一共摸了多久、多少钱（不含正在进行的这次）</summary>
        public (double Sec, double Money) Today(DateTime now)
        {
            var d = stats.Day(now);
            return (d.SlackSec, d.SlackMoney);
        }

        (TimeSpan, double) Session(DateTime now, CalcResult r) =>
            slackStart == null ? (TimeSpan.Zero, 0) : (now - slackStart.Value, Math.Max(0, r.Earned - slackStartEarned));

        /// <summary>摸鱼跨过了零点：前一天的那段记到前一天，今天从零点重新算</summary>
        void SplitAtMidnight(Config c, DateTime now)
        {
            if (slackStart == null || slackStart.Value.Date == now.Date) return;
            var endOfDay = slackStart.Value.Date.AddDays(1).AddSeconds(-1);
            var last = Payroll.Calc(c, endOfDay);
            var day = stats.Day(slackStart.Value);
            day.SlackSec += (endOfDay - slackStart.Value).TotalSeconds + 1;
            day.SlackMoney += Math.Max(0, last.Earned - slackStartEarned);
            slackStart = now.Date;
            slackStartEarned = 0;
            dirty = true;
        }

        // ------------------------------------------------------------ 每次刷新
        /// <summary>
        /// 在 Payroll 的结果上加上免费加班（会改 r 的状态与倒计时文字），算出卡片要显示的内容。
        /// idle：多久没碰键盘鼠标。notes 收集要弹的通知（标题, 正文）。
        /// </summary>
        public FunView Update(Config c, CalcResult r, DateTime now, TimeSpan idle, List<(string, string)>? notes = null)
        {
            var inv = CultureInfo.InvariantCulture;
            var v = new FunView();
            SplitAtMidnight(c, now);
            var today = stats.Day(now);

            // 提钱下班：点了"下班"以后，今天剩下的工资一次性到手（月薪不会因为早走扣钱），
            // 状态变成"提前下班"，不再有下班冲刺和免费加班
            var (leh, lem) = Config.ParseTime(c.End);
            var workEnd = now.Date.AddHours(leh).AddMinutes(lem);
            bool leftEarly = today.LeftEarlyAt != null && (r.Status == "上班中" || r.Status == "午休中" || r.Status == "已下班" || r.Status == "上班前");
            if (leftEarly)
            {
                var early = workEnd - today.LeftEarlyAt!.Value;
                string ahead = early.TotalSeconds > 0 ? $"{(int)early.TotalHours}:{early.Minutes:00}:{early.Seconds:00}" : "0:00:00";
                r.PeriodTotal += r.FullDay - r.Earned;
                r.Earned = r.FullDay;
                r.WorkedHours = r.TotalHours;
                r.SecondsToEnd = -1;
                if (now < workEnd) { r.Status = "提前下班"; r.Countdown = $"提前 {ahead} 下班 · 工资照拿"; }
                else { r.Status = "已下班"; r.Countdown = $"今天提前 {ahead} 下班"; }
            }
            v.LeftEarly = leftEarly;
            v.CanLeave = Payroll.IsWorkingDay(now, c) && now < workEnd && (leftEarly || r.Status == "上班中" || r.Status == "午休中");
            v.LeaveButton = leftEarly ? "↩ 回来上班" : "🏃 下班";
            v.LeaveTip = leftEarly ? "点错了？再点一下撤销，回去接着上班" : "提钱下班：今天剩下的工资一次性到手，提前走人";

            // 免费加班：没开加班计薪、已经下班后，按"最后一次键鼠操作"往前走了多少来累计。
            // 两次操作隔得不超过 5 分钟算人一直在；人走了这个时刻就不动了，也就不再累计。
            // 离开一会儿（吃个晚饭）回来接着干，离开的那段不算、回来接着算；
            // 离开超过 90 分钟（多半是回家了，晚上再开电脑不算加班），当天就不再算。
            bool active = idle.TotalMinutes < ActiveIdleLimitMin;
            var input = now - idle;
            if (!c.Overtime && !leftEarly && r.Status == "已下班" && freeOtOver != now.Date && lastInput is DateTime prev && input > prev)
            {
                var (eh, em) = Config.ParseTime(c.End);
                var end = now.Date.AddHours(eh).AddMinutes(em);
                double gap = (input - prev).TotalMinutes;
                if (prev > end && gap > LeftForDayMin) freeOtOver = now.Date;
                else if (gap <= ActiveIdleLimitMin)
                {
                    var from = prev > end ? prev : end;
                    if (input > from) { today.FreeOtSec += (input - from).TotalSeconds; dirty = true; }
                }
            }
            if (lastInput == null || input > lastInput) lastInput = input;
            if (freeOtOver == now.Date) active = false;
            if (!c.Overtime && !leftEarly && r.Status == "已下班" && today.FreeOtSec >= 60)
            {
                double money = r.SpeedPerSec * Math.Max(c.OtRate, 1) * today.FreeOtSec;
                string took = Duration(today.FreeOtSec);     // 2:59、1:05:30，和金额对得上
                if (active)
                {
                    r.Status = "免费加班中";
                    r.Countdown = $"{took} · 老板白嫖 ¥{money.ToString("F2", inv)}";
                    v.QuoteOverride = GoHome;
                }
                else
                {
                    r.Countdown = $"免费加班 {took} · 老板白嫖 ¥{money.ToString("F2", inv)}";
                }
                var key = Payroll.DayKey(now);
                if (today.FreeOtSec >= 3600 && notifiedFreeOt != key)
                {
                    notifiedFreeOt = key;
                    notes?.Add(("已经免费加班 1 小时了", $"按 {c.OtRate:0.##} 倍加班费算，老板白嫖了你 ¥{money.ToString("F2", inv)}。该下班啦～"));
                }
            }

            // 摸鱼
            var (dur, sm) = Session(now, r);
            v.Slacking = slackStart != null;
            bool working = r.Status == "上班中", lunch = r.Status == "午休中";
            v.Started = today.StartedAt != null;
            v.CanPlay = working || lunch;
            v.Spinning = v.Started && !v.Slacking && working;
            v.ShowPause = v.Started && !v.Slacking && v.CanPlay;
            v.PlayTip = !v.CanPlay ? (leftEarly ? "今天已经提钱下班了" : "现在不是上班时间")
                : v.Slacking ? "结束摸鱼，接着干活"
                : !v.Started ? "开工：唱片转起来"
                : "暂停 = 带薪摸鱼（钱照拿）";
            v.SlackButton = v.Slacking ? $"🐟 {Clock(dur)} · ¥{sm.ToString("F2", inv)}" : "🐟 摸鱼";
            double daySec = today.SlackSec + dur.TotalSeconds, dayMoney = today.SlackMoney + sm;
            double monSec = dur.TotalSeconds, monMoney = sm;
            foreach (var (k, s) in stats.Days)
                if (k.StartsWith(now.ToString("yyyy-MM-", inv), StringComparison.Ordinal)) { monSec += s.SlackSec; monMoney += s.SlackMoney; }
            v.SlackTip = (v.Slacking ? "正在带薪摸鱼，再点一下结束\n" : "点一下开始带薪摸鱼\n")
                + $"今日 {Minutes(daySec)} · ¥{dayMoney.ToString("F2", inv)}\n本月 {Minutes(monSec)} · ¥{monMoney.ToString("F2", inv)}";

            // 实物换算：每 8 秒换一样
            var items = ConvertItem.Parse(c.ConvertItems);
            if (items.Count > 0)
            {
                var it = items[(int)(now.Ticks / TimeSpan.TicksPerSecond / 8 % items.Count)];
                v.Convert = it.Describe(r, c);
            }

            // 假期倒计时
            var p = Holidays.CurrentOrNext(now);
            if (p == null)
            {
                v.Holiday = "等新安排公布";
                v.HolidayShort = "待公布";
                v.HolidayTip = "内置的放假安排已经用完，等新一年的通知";
            }
            else if (p.Start <= now.Date)
            {
                int left = (p.End - now.Date).Days + 1;
                v.Holiday = $"{p.Name}假期中 · 还剩 {left} 天";
                v.HolidayShort = $"{p.Name}中 · 剩 {left} 天";
                v.HolidayTip = $"{p.Start:M月d日}–{p.End:M月d日}，共 {p.Days} 天";
                v.HolidaySoon = true;
            }
            else
            {
                int days = (p.Start - now.Date).Days;
                v.Holiday = days == 1 ? $"{p.Name} · 明天就放！" : $"{p.Name} · 还有 {days} 天";
                v.HolidayShort = days == 1 ? $"{p.Name} · 明天！" : $"{p.Name} · {days} 天";
                v.HolidayTip = $"{p.Start:M月d日}起放 {p.Days} 天" + (p.Days > 1 ? $"（到 {p.End:M月d日}）" : "");
                v.HolidaySoon = days <= 3;
                var key = Payroll.DayKey(p.Start);
                if (days == 1 && now.Hour >= 9 && notifiedHoliday != key)
                {
                    notifiedHoliday = key;
                    notes?.Add(("明天就放假啦！", $"{p.Name}放 {p.Days} 天（{p.Start:M月d日}" + (p.Days > 1 ? $"–{p.End:M月d日}" : "") + "），站好最后一班岗～"));
                }
            }

            // 发薪日当天换成庆祝版鼓励语（免费加班劝下班的话优先）
            if (v.QuoteOverride == null && r.DaysToPayday == 0) v.QuoteOverride = Payday;

            if (persist && dirty && (now - lastSave).TotalSeconds >= 30) Save();
            return v;
        }

        public void Save()
        {
            if (!persist) return;
            stats.Save();
            dirty = false;
            lastSave = DateTime.Now;
        }

        // 只用于截图 / 自检：假装已经摸了这么久、已经免费加班这么久
        public void FakeSlack(DateTime now, TimeSpan ago, Config c) { slackStart = now - ago; slackStartEarned = Payroll.Calc(c, now - ago).Earned; }
        public void FakeFreeOvertime(DateTime now, double seconds) => stats.Day(now).FreeOtSec = seconds;

        public static string Minutes(double sec)
        {
            if (sec < 60) return $"{(int)sec} 秒";
            int m = (int)(sec / 60);
            return m < 60 ? $"{m} 分钟" : $"{m / 60} 小时 {m % 60:00} 分";
        }

        /// <summary>免费加班时长：不到 1 小时写"分:秒"，否则"时:分:秒"</summary>
        static string Duration(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Floor(sec));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }

        static string Clock(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>实物换算的一项："奶茶 18 杯"；单位写"月"表示按本月（本期）累计算进度，如"房租 5000 月"</summary>
    public sealed class ConvertItem
    {
        public string Name = "";
        public double Price;
        public string Unit = "个";
        public bool Monthly => Unit == "月";

        public const string Default = "奶茶 18 杯\n外卖 30 份\n电影票 45 张\n火锅 150 顿\n房租 5000 月";

        public static List<ConvertItem> Parse(string text)
        {
            var list = new List<ConvertItem>();
            foreach (var raw in (string.IsNullOrWhiteSpace(text) ? Default : text).Split('\n'))
            {
                var t = raw.Trim().Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 2 || !double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var price) || price <= 0) continue;
                list.Add(new ConvertItem { Name = t[0], Price = price, Unit = t.Length > 2 ? t[2] : "个" });
            }
            return list;
        }

        public string Describe(CalcResult r, Config c)
        {
            var inv = CultureInfo.InvariantCulture;
            if (Monthly)
            {
                string when = c.PeriodMode == 0 ? "本月" : "本期";
                double pct = r.PeriodTotal / Price * 100;
                return pct >= 100 ? $"{when}{Name}已经赚够啦 ✓" : $"{when}{Name}已赚到 {pct.ToString("F0", inv)}%";
            }
            if (r.Status == "休息中") return $"今天休息，{Name}照样安排上";
            int n = (int)Math.Floor(r.Earned / Price);
            if (n >= 1) return $"今天已经赚够 {n} {Unit}{Name}";
            return $"再赚 ¥{(Price - r.Earned).ToString("F2", inv)} 就够一{Unit}{Name}了";
        }
    }
}
