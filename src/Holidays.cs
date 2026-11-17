using System;
using System.Collections.Generic;
using System.Linq;

namespace TiqianXiaban
{
    /// <summary>
    /// 法定节假日与调休上班日（国务院办公厅通知，2026 / 2027 两年）。
    /// 数据来源：国务院办公厅每年发布的放假安排通知。每年 11 月新一年的安排公布后需要补充。
    /// </summary>
    public static class Holidays
    {
        public sealed record Period(string Name, DateTime Start, int Days)
        {
            public DateTime End => Start.AddDays(Days - 1);
        }

        static Period P(string name, int y, int m, int d, int days) => new Period(name, new DateTime(y, m, d), days);

        /// <summary>每段假期：名称、第一天、放几天</summary>
        public static readonly Period[] Periods =
        {
            P("元旦", 2026, 1, 1, 3),
            P("春节", 2026, 2, 15, 9),
            P("清明", 2026, 4, 4, 3),
            P("劳动节", 2026, 5, 1, 5),
            P("端午", 2026, 6, 19, 3),
            P("中秋", 2026, 9, 25, 3),
            P("国庆", 2026, 10, 1, 7),

            P("元旦", 2027, 1, 1, 3),
            P("春节", 2027, 2, 5, 9),     // 除夕至正月初八
            P("清明", 2027, 4, 3, 3),
            P("劳动节", 2027, 5, 1, 5),
            P("端午", 2027, 6, 9, 1),
            P("中秋", 2027, 9, 15, 1),
            P("国庆", 2027, 10, 1, 7),
        };

        /// <summary>法定节假日的每一天（yyyy-MM-dd）</summary>
        public static readonly HashSet<string> Legal = new HashSet<string>(
            Periods.SelectMany(p => Enumerable.Range(0, p.Days).Select(i => Payroll.DayKey(p.Start.AddDays(i)))));

        /// <summary>调休上班日（落在周末但正常上班）</summary>
        public static readonly HashSet<string> Makeup = new HashSet<string>
        {
            // 2026
            "2026-01-04",               // 周日
            "2026-02-14", "2026-02-28", // 周六
            "2026-05-09",               // 周六
            "2026-09-20",               // 周日
            "2026-10-10",               // 周六
            // 2027
            "2027-01-31",               // 周日
            "2027-02-14",               // 周日
            "2027-05-08",               // 周六
            "2027-09-26",               // 周日
            "2027-10-09",               // 周六
        };

        /// <summary>今天所在的假期；不在假期里则是下一个还没开始的假期；数据里没有了就是 null</summary>
        public static Period? CurrentOrNext(DateTime today)
        {
            today = today.Date;
            return Periods.FirstOrDefault(p => p.End >= today);
        }
    }
}
