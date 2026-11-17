using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TiqianXiaban
{
    /// <summary>用户配置，存在 %APPDATA%\TiqianXiaban\config.json</summary>
    public sealed class Config
    {
        public int Mode { get; set; } = 0;                 // 0 月薪 1 日薪 2 时薪
        public double Amount { get; set; } = 9800;
        public bool AutoPayDays { get; set; } = true;          // 月薪模式：按当月实际工作日计薪；false 用下面的固定天数
        public double PayDaysPerMonth { get; set; } = 21.75;
        public string Start { get; set; } = "09:00";
        public string End { get; set; } = "18:00";
        public bool LunchDeduct { get; set; } = true;
        public string LunchStart { get; set; } = "12:00";
        public string LunchEnd { get; set; } = "14:00";
        public bool[] Workdays { get; set; } = { true, true, true, true, true, false, false }; // 周一..周日
        public bool Overtime { get; set; } = false;
        public double OtRate { get; set; } = 1.5;
        public int Payday { get; set; } = 15;
        public int PeriodMode { get; set; } = 0;              // 累计方式：0 自然月（每月 1 号起） 1 从上次发薪日起 2 每月固定某天起
        public int PeriodStartDay { get; set; } = 26;         // PeriodMode = 2 时，每月几号开始新一期
        public double Opacity { get; set; } = 0.92;
        public int Theme { get; set; } = 1;                // 0 深色 1 白色
        public bool Mini { get; set; } = false;
        public double Scale { get; set; } = 1.0;           // 50% ~ 150%
        public double PosX { get; set; } = double.NaN;     // 卡片左上角（DIP），NaN = 放到屏幕右上角
        public double PosY { get; set; } = double.NaN;
        public bool Sound { get; set; } = true;            // 下班、提钱下班时放钱币音效
        public bool EdgeHide { get; set; } = true;         // 拖到屏幕边缘时自动隐藏，只露一条边
        public string DockEdge { get; set; } = "";         // 上次贴着哪条边（Left / Right / Top），空 = 没贴；PosX/PosY 是展开时的位置
        public string CustomQuotes { get; set; } = "";     // 每行一句，留空用内置鼓励语
        public string ConvertItems { get; set; } = "";     // 实物换算，每行"名称 价格 单位"，留空用内置的

        // 自检时用单独的目录，不碰正在使用的配置
        public static string Dir =>
            (Environment.GetEnvironmentVariable("TIQIAN_SELFTEST") ?? Environment.GetEnvironmentVariable("TIQIAN_MOUSETEST")) is string t && t.Length > 0
                ? Path.Combine(t, "config")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiqianXiaban");
        static string FilePath => Path.Combine(Dir, "config.json");

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Json);
                    if (c != null) return c.Normalized();
                }
            }
            catch (Exception)
            {
                // 配置损坏就用默认值，不让程序起不来
            }
            return new Config();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception)
            {
                // 写不进去（比如目录只读）也不影响计时
            }
        }

        public Config Clone() => JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(this, Json), Json)!.Normalized();

        Config Normalized()
        {
            if (Workdays == null || Workdays.Length != 7)
            {
                var w = new bool[7];
                for (int i = 0; i < 7; i++) w[i] = Workdays != null && i < Workdays.Length ? Workdays[i] : i < 5;
                Workdays = w;
            }
            Start ??= "09:00"; End ??= "18:00"; LunchStart ??= "12:00"; LunchEnd ??= "14:00";
            CustomQuotes ??= "";
            ConvertItems ??= "";
            DockEdge ??= "";
            PeriodMode = Math.Clamp(PeriodMode, 0, 2);
            PeriodStartDay = Math.Clamp(PeriodStartDay, 1, 28);
            Opacity = Math.Clamp(double.IsFinite(Opacity) ? Opacity : 0.92, 0.4, 1.0);
            Scale = Math.Clamp(double.IsFinite(Scale) ? Scale : 1.0, 0.5, 1.5);
            return this;
        }

        /// <summary>"09:30" → (9, 30)；"9" → (9, 0)；解析不了的部分忽略，越界的夹到合法范围</summary>
        public static (int H, int M) ParseTime(string s)
        {
            var p = (s ?? "").Split(':', '：')
                .Select(x => int.TryParse(x.Trim(), out var v) ? (int?)v : null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            if (p.Length >= 2) return (Math.Clamp(p[0], 0, 23), Math.Clamp(p[1], 0, 59));
            if (p.Length == 1) return (Math.Clamp(p[0], 0, 23), 0);
            return (0, 0);
        }

        public string[] QuoteList()
        {
            var own = CustomQuotes.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            return own.Length > 0 ? own : Quotes.BuiltIn;
        }
    }
}
