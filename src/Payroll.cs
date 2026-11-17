using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TiqianXiaban
{
    public sealed class CalcResult
    {
        public string Status = "休息中";
        public double Earned;
        public double SpeedPerSec;
        public double SpeedPerMin;
        public double WorkedHours;
        public double TotalHours = 8;
        public string Countdown = "";
        public double PeriodTotal;
        public int DaysToPayday;
        public double FullDay;
        public double PayDays;      // 本月计薪天数（月薪模式；自动时是当月实际工作日数）
        public DateTime PeriodStart; // 累计从哪天算起
        public int SecondsToEnd = -1;   // 上班中：离下班还有几秒（下班冲刺用），其他状态 -1
    }

    /// <summary>
    /// 计薪：先把一天里"算钱"的时间段排出来（上班到下班，勾了扣除午休就把午休挖掉），
    /// 此刻赚了多少 = 已经走过的那部分时间段 × 时薪；状态和倒计时看此刻落在哪一段。
    /// 月薪默认按当月实际工作日折算日薪，也可以改成固定天数（如法定的 21.75 天）。
    /// </summary>
    public static class Payroll
    {
        const int MinPaidSeconds = 300;     // 作息填得离谱（下班早于上班）时也按 5 分钟算，免得除以零

        public static string DayKey(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>这天算不算工作日：法定假日不算，调休补班算，其余看勾选的周几</summary>
        public static bool IsWorkingDay(DateTime date, Config c)
        {
            var key = DayKey(date);
            if (Holidays.Legal.Contains(key)) return false;
            return Holidays.Makeup.Contains(key) || c.Workdays[((int)date.DayOfWeek + 6) % 7];
        }

        /// <summary>某月的发薪日：设置的那一天碰上休息日，就往前找最近的工作日（中午 12 点，方便按天比较）</summary>
        public static DateTime PaydayDate(int year, int month, Config c)
        {
            var day = new DateTime(year, month, Math.Clamp(c.Payday, 1, 28), 12, 0, 0);
            var found = Enumerable.Range(0, 20).Select(back => day.AddDays(-back)).FirstOrDefault(d => IsWorkingDay(d, c));
            return found == default ? day.AddDays(-20) : found;
        }

        /// <summary>某年某月的计薪工作日数：按周工作日，扣掉法定节假日，加上调休上班日</summary>
        public static int WorkdaysInMonth(int year, int month, Config c) =>
            Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Count(d => IsWorkingDay(new DateTime(year, month, d), c));

        /// <summary>月薪模式下某月的计薪天数</summary>
        public static double PayDays(int year, int month, Config c) =>
            c.AutoPayDays ? Math.Max(WorkdaysInMonth(year, month, c), 1) : Math.Max(c.PayDaysPerMonth, 1);

        /// <summary>
        /// 累计的起点（含这一天）：
        ///   自然月      —— 本月 1 号（当月发当月工资的公司）
        ///   上次发薪日  —— 最近一个已到的发薪日（遇周末、节假日提前），发上个月工资的公司
        ///   每月固定日  —— 最近一个已到的"每月 N 号"（考勤周期跨月的公司，如 26 号到次月 25 号）
        /// </summary>
        public static DateTime PeriodStart(Config c, DateTime now)
        {
            var thisMonth = new DateTime(now.Year, now.Month, 1);
            var lastMonth = thisMonth.AddMonths(-1);
            switch (c.PeriodMode)
            {
                case 1:
                    var pay = PaydayDate(now.Year, now.Month, c).Date;
                    return pay <= now.Date ? pay : PaydayDate(lastMonth.Year, lastMonth.Month, c).Date;
                case 2:
                    int day = Math.Clamp(c.PeriodStartDay, 1, 28) - 1;
                    var cut = thisMonth.AddDays(day);
                    return cut <= now.Date ? cut : lastMonth.AddDays(day);
                default:
                    return thisMonth;
            }
        }

        /// <summary>今天或以后最近的一个发薪日</summary>
        static DateTime NextPayday(Config c, DateTime now)
        {
            var pay = PaydayDate(now.Year, now.Month, c);
            if (pay.Date >= now.Date) return pay;
            var next = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            return PaydayDate(next.Year, next.Month, c);
        }

        /// <summary>
        /// 明天休息时（周五，或者放假前一天）下午的倒计时说法："周末""元旦假期""休息日"；明天还上班则 null。
        /// 明天是调休补班日就不算。
        /// </summary>
        public static string? BreakAhead(DateTime now, Config c)
        {
            var tomorrow = now.Date.AddDays(1);
            if (IsWorkingDay(tomorrow, c)) return null;
            var p = Holidays.CurrentOrNext(tomorrow);
            if (p != null && p.Start <= tomorrow) return p.Name + "假期";
            return tomorrow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "休息日";
        }

        static string Hms(int s) => $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}";

        static int Seconds(string hhmm)
        {
            var (h, m) = Config.ParseTime(hhmm);
            return h * 3600 + m * 60;
        }

        /// <summary>一天里的作息：上下班时刻、午休（落在上下班之间的那部分）、算钱的时间段</summary>
        sealed class Day
        {
            public int Start, End;
            public (int From, int To)? Lunch;
            public readonly List<(int From, int To)> Paid = new List<(int From, int To)>();
            public int PaidSeconds => Math.Max(Paid.Sum(p => p.To - p.From), MinPaidSeconds);

            /// <summary>到 t 时刻为止走过了多少算钱的秒数</summary>
            public int PaidUntil(int t) => Paid.Sum(p => Math.Max(0, Math.Min(p.To, t) - p.From));

            public static Day Of(Config c)
            {
                var d = new Day { Start = Seconds(c.Start) };
                d.End = Math.Max(Seconds(c.End), d.Start);
                int cursor = d.Start;
                if (c.LunchDeduct)
                {
                    int from = Math.Max(Seconds(c.LunchStart), d.Start), to = Math.Min(Seconds(c.LunchEnd), d.End);
                    if (to > from)
                    {
                        d.Lunch = (from, to);
                        d.Paid.Add((d.Start, from));
                        cursor = to;
                    }
                }
                d.Paid.Add((cursor, d.End));
                return d;
            }
        }

        public static CalcResult Calc(Config c, DateTime now)
        {
            var r = new CalcResult();
            var day = Day.Of(c);
            double paidHours = day.PaidSeconds / 3600.0;

            // 时薪按月算：自动模式下每个月的日薪不同（10 月有国庆，工作日少，日薪就高），
            // 一个月的工作日全部满勤，正好赚满一个月的工资
            var perMonth = new Dictionary<(int, int), double>();
            double Hourly(DateTime d)
            {
                if (!perMonth.TryGetValue((d.Year, d.Month), out var h))
                {
                    h = c.Mode == 0 ? c.Amount / PayDays(d.Year, d.Month, c) / paidHours
                        : c.Mode == 1 ? c.Amount / paidHours
                        : c.Amount;
                    perMonth[(d.Year, d.Month)] = h = double.IsNaN(h) || h < 0 ? 0 : h;
                }
                return h;
            }

            double hourly = Hourly(now);
            r.PayDays = PayDays(now.Year, now.Month, c);
            r.TotalHours = paidHours;
            r.FullDay = hourly * paidHours;
            r.SpeedPerSec = hourly / 3600;
            r.SpeedPerMin = hourly / 60;

            // 此刻落在作息的哪一段
            int t = (int)now.TimeOfDay.TotalSeconds;
            bool workday = IsWorkingDay(now, c);
            bool before = t < day.Start, after = t >= day.End;
            bool lunch = day.Lunch is (int lf, int lt) && t >= lf && t < lt;

            // 走过的算钱秒数：上班前 0，下班后整天，中间按时间段累加；加班按倍率另算
            double paid = !workday || before ? 0 : after ? day.PaidSeconds : day.PaidUntil(t);
            double overtime = workday && after && c.Overtime ? (t - day.End) * c.OtRate : 0;
            r.Earned = hourly * (paid + overtime) / 3600;
            r.WorkedHours = paid / 3600;

            if (!workday) (r.Status, r.Countdown) = ("休息中", "休息日，好好放松");
            else if (before) (r.Status, r.Countdown) = ("上班前", "距离上班还有 " + Hms(day.Start - t));
            else if (lunch) (r.Status, r.Countdown) = ("午休中", "午休结束还有 " + Hms(day.Lunch!.Value.To - t));
            else if (!after)
            {
                r.Status = "上班中";
                r.SecondsToEnd = day.End - t;
                string until = t >= 12 * 3600 && BreakAhead(now, c) is string brk ? brk : "下班";
                r.Countdown = $"距离{until}还有 " + Hms(r.SecondsToEnd);
            }
            else if (c.Overtime) (r.Status, r.Countdown) = ("加班中", $"按 {c.OtRate:0.##} 倍计薪");
            else (r.Status, r.Countdown) = ("已下班", "收工啦，辛苦了");

            // 累计：从起点（含当天）到昨天，每个工作日按它那个月的日薪算满勤，再加上今天已赚
            r.PeriodStart = PeriodStart(c, now);
            int days = Math.Clamp((now.Date - r.PeriodStart).Days, 0, 40);
            r.PeriodTotal = r.Earned + Enumerable.Range(0, days).Select(i => r.PeriodStart.AddDays(i))
                .Where(d => IsWorkingDay(d, c)).Sum(d => Hourly(d) * paidHours);

            r.DaysToPayday = (NextPayday(c, now).Date - now.Date).Days;
            return r;
        }
    }
}
