using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TiqianXiaban
{
    public sealed class SettingsWindow : Window
    {
        readonly App app;
        readonly ComboBox mode = new ComboBox { Width = 80 };
        readonly TextBox amount = Field(100), payDays = Field(56);
        readonly ComboBox payDaysMode = new ComboBox { Width = 90 };
        readonly TextBlock payDaysHint = Text("", 11, Hint);
        readonly TextBox start = Field(64), end = Field(64), lunchStart = Field(64), lunchEnd = Field(64);
        readonly CheckBox lunch = new CheckBox { Content = "扣除午休", VerticalAlignment = VerticalAlignment.Center };
        readonly CheckBox[] days = new CheckBox[7];
        readonly CheckBox overtime = new CheckBox { Content = "加班计薪", VerticalAlignment = VerticalAlignment.Center };
        readonly TextBox otRate = Field(56), payday = Field(56), periodDay = Field(44);
        readonly ComboBox periodMode = new ComboBox { Width = 130 };
        readonly TextBlock periodHint = Text("", 11, Hint);
        readonly ComboBox theme = new ComboBox { Width = 80 };
        readonly Slider opacity = new Slider { Minimum = 0.4, Maximum = 1.0, Width = 220 };
        readonly Slider scale = new Slider { Minimum = 0.5, Maximum = 1.5, Width = 220, TickFrequency = 0.05, IsSnapToTickEnabled = true };
        readonly CheckBox autoStart = new CheckBox { Content = "开机自动启动" };
        readonly CheckBox sound = new CheckBox { Content = "下班时播放钱币音效", Margin = new Thickness(18, 0, 0, 0) };
        readonly CheckBox edgeHide = new CheckBox { Content = "拖到屏幕边缘时自动隐藏，鼠标移过去再出来" };
        readonly TextBox quotes = Multiline(250), convert = Multiline(180);

        static TextBox Multiline(double w) => new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 106, Width = w,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        static TextBox Field(double w) => new TextBox { Width = w, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(3, 2, 3, 2) };
        static TextBlock Text(string s, double size = 12, Brush? color = null) =>
            new TextBlock { Text = s, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Foreground = color ?? Brushes.Black, Margin = new Thickness(6, 0, 0, 0) };
        static readonly Brush Hint = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        public SettingsWindow(App app)
        {
            this.app = app;
            Title = "提钱下班 · 设置";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            FontSize = 12;
            Topmost = true;

            var c = app.Config;
            mode.Items.Add("月薪"); mode.Items.Add("日薪"); mode.Items.Add("时薪");
            mode.SelectedIndex = Math.Clamp(c.Mode, 0, 2);
            amount.Text = Num(c.Amount);
            payDaysMode.Items.Add("自动计算");
            payDaysMode.Items.Add("固定天数");
            payDaysMode.SelectedIndex = c.AutoPayDays ? 0 : 1;
            payDays.Text = Num(c.PayDaysPerMonth);
            start.Text = c.Start; end.Text = c.End;
            lunch.IsChecked = c.LunchDeduct;
            lunchStart.Text = c.LunchStart; lunchEnd.Text = c.LunchEnd;
            overtime.IsChecked = c.Overtime;
            otRate.Text = Num(c.OtRate);
            payday.Text = c.Payday.ToString(CultureInfo.InvariantCulture);
            periodMode.Items.Add("自然月");
            periodMode.Items.Add("从上次发薪日起");
            periodMode.Items.Add("每月固定日起");
            periodMode.SelectedIndex = Math.Clamp(c.PeriodMode, 0, 2);
            periodDay.Text = c.PeriodStartDay.ToString(CultureInfo.InvariantCulture);
            theme.Items.Add("深色"); theme.Items.Add("白色");
            theme.SelectedIndex = Math.Clamp(c.Theme, 0, 1);
            opacity.Value = c.Opacity;
            scale.Value = c.Scale;
            autoStart.IsChecked = AutoStart.IsEnabled;
            edgeHide.IsChecked = c.EdgeHide;
            sound.IsChecked = c.Sound;
            quotes.Text = c.CustomQuotes;
            convert.Text = string.IsNullOrWhiteSpace(c.ConvertItems) ? ConvertItem.Default : c.ConvertItems;

            var rows = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            Row(rows, "收入模式", mode, amount, Text("元"));
            Row(rows, "每月计薪天数", payDaysMode, payDays, payDaysHint);
            Row(rows, "上下班时间", start, Text("到"), end, Text("格式 09:00", 11, Hint));
            Row(rows, "", lunch, lunchStart, Text("到"), lunchEnd);
            var dayPanel = new StackPanel { Orientation = Orientation.Horizontal };
            string[] names = { "一", "二", "三", "四", "五", "六", "日" };
            for (int i = 0; i < 7; i++)
            {
                days[i] = new CheckBox { Content = names[i], IsChecked = c.Workdays[i], Margin = new Thickness(0, 0, 12, 0) };
                dayPanel.Children.Add(days[i]);
            }
            Row(rows, "工作日", dayPanel);
            // 计薪天数的提示跟着收入模式、计薪方式和勾选的工作日实时变
            payDaysMode.SelectionChanged += (_, _) => UpdatePayDays();
            mode.SelectionChanged += (_, _) => UpdatePayDays();
            foreach (var d in days) { d.Checked += (_, _) => UpdatePayDays(); d.Unchecked += (_, _) => UpdatePayDays(); }
            UpdatePayDays();
            Row(rows, "", overtime, otRate, Text("倍率（下班后继续计薪）", 11, Hint));
            Row(rows, "发薪日", payday, Text("每月几号；遇周末、法定节假日提前到最近的工作日", 11, Hint));
            Row(rows, "累计方式", periodMode, periodDay, periodHint);
            periodMode.SelectionChanged += (_, _) => UpdatePeriod();
            periodDay.TextChanged += (_, _) => UpdatePeriod();
            UpdatePeriod();
            Row(rows, "卡片配色", theme);
            var opLabel = Text("");
            opacity.ValueChanged += (_, _) => opLabel.Text = $"{opacity.Value * 100:F0}%";
            opLabel.Text = $"{opacity.Value * 100:F0}%";
            Row(rows, "卡片不透明度", opacity, opLabel);
            var scLabel = Text("");
            scale.ValueChanged += (_, _) => scLabel.Text = $"{scale.Value * 100:F0}%";
            scLabel.Text = $"{scale.Value * 100:F0}%";
            Row(rows, "界面缩放", scale, scLabel);
            Row(rows, "", autoStart, sound);
            Row(rows, "", edgeHide);
            // 鼓励语和实物换算并排放，窗口不用再加高
            var texts = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            texts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            texts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            texts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            texts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void Cell(UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); texts.Children.Add(e); }
            Cell(new TextBlock { Text = "自定义鼓励语（每行一句，留空用内置的）", Foreground = Hint, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) }, 0, 0);
            Cell(quotes, 1, 0);
            var convertTitle = new TextBlock { Text = "实物换算（名称 价格 单位）", Foreground = Hint, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
            convertTitle.ToolTip = "每行一项，如“奶茶 18 杯”：卡片上显示今天赚够了几杯。\n单位写“月”表示按本月累计算进度，如“房租 5000 月”。\n清空就恢复内置的。";
            convert.ToolTip = convertTitle.ToolTip;
            Cell(convertTitle, 0, 2);
            Cell(convert, 1, 2);
            rows.Children.Add(texts);

            var save = new Button { Content = "保存", Width = 76, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
            var cancel = new Button { Content = "取消", Width = 76, Height = 28, IsCancel = true };
            save.Click += (_, _) => Save();
            cancel.Click += (_, _) => Close();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            rows.Children.Add(buttons);
            Content = rows;
        }

        /// <summary>
        /// 设置窗口开着时，右键菜单或托盘菜单切换了开关：同步这两个勾选框，
        /// 免得之后点"保存"把刚切换的开关又改回去
        /// </summary>

        public void SyncToggles()
        {
            autoStart.IsChecked = AutoStart.IsEnabled;
        }

        // 卡片上的"本月累计 / 本期累计"从哪天算起，不同公司规则不同
        void UpdatePeriod()
        {
            int m = periodMode.SelectedIndex;
            periodDay.Visibility = m == 2 ? Visibility.Visible : Visibility.Collapsed;
            int day = Math.Clamp(I(periodDay, 26), 1, 28);
            periodHint.Text = m switch
            {
                0 => "每月 1 号清零（当月发当月工资）",
                1 => "从上次发薪日起算（发上个月工资）",
                _ => day == 1 ? "号起，和自然月相同" : $"号起，到次月 {day - 1} 号（考勤跨月）",
            };
        }

        void UpdatePayDays()
        {
            bool monthly = mode.SelectedIndex == 0, auto = payDaysMode.SelectedIndex == 0;
            payDaysMode.IsEnabled = monthly;
            payDays.IsEnabled = monthly && !auto;
            payDays.Visibility = auto ? Visibility.Collapsed : Visibility.Visible;
            if (!monthly) { payDaysHint.Text = "只在月薪模式下使用"; return; }
            if (!auto) { payDaysHint.Text = "每个月都按这个天数折算日薪，法定口径是 21.75"; payDaysHint.ToolTip = null; return; }
            var probe = new Config { AutoPayDays = true };
            for (int i = 0; i < 7; i++) probe.Workdays[i] = days[i].IsChecked == true;
            var now = DateTime.Now;
            var next = now.AddMonths(1);
            payDaysHint.Text = $"按当月实际工作日：本月 {Payroll.WorkdaysInMonth(now.Year, now.Month, probe)} 天，{next.Month} 月 {Payroll.WorkdaysInMonth(next.Year, next.Month, probe)} 天";
            payDaysHint.ToolTip = "按勾选的工作日计数，扣掉法定节假日，算上调休补班日；每个月的工作日全部满勤，正好赚满一个月工资";
        }

        static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        static void Row(Panel parent, string title, params UIElement[] items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = title, Width = 96, VerticalAlignment = VerticalAlignment.Center });
            foreach (var it in items)
            {
                if (it is FrameworkElement fe && !(it is TextBlock)) fe.Margin = new Thickness(fe.Margin.Left, 0, Math.Max(fe.Margin.Right, 6), 0);
                if (it is FrameworkElement f2) f2.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(it);
            }
            parent.Children.Add(row);
        }

        static double D(TextBox t, double old) =>
            double.TryParse(t.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : old;
        static int I(TextBox t, int old) =>
            int.TryParse(t.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : old;

        public void Save()
        {
            var c = app.Config.Clone();
            c.Mode = mode.SelectedIndex;
            c.Amount = Math.Max(0, D(amount, c.Amount));
            c.AutoPayDays = payDaysMode.SelectedIndex == 0;
            c.PayDaysPerMonth = D(payDays, c.PayDaysPerMonth);
            c.Start = start.Text.Trim();
            c.End = end.Text.Trim();
            c.LunchDeduct = lunch.IsChecked == true;
            c.LunchStart = lunchStart.Text.Trim();
            c.LunchEnd = lunchEnd.Text.Trim();
            for (int i = 0; i < 7; i++) c.Workdays[i] = days[i].IsChecked == true;
            c.Overtime = overtime.IsChecked == true;
            c.OtRate = D(otRate, c.OtRate);
            c.Payday = Math.Clamp(I(payday, c.Payday), 1, 31);
            c.PeriodMode = Math.Clamp(periodMode.SelectedIndex, 0, 2);
            c.PeriodStartDay = Math.Clamp(I(periodDay, c.PeriodStartDay), 1, 28);
            c.Theme = theme.SelectedIndex;
            c.Opacity = opacity.Value;
            c.Scale = scale.Value;
            c.EdgeHide = edgeHide.IsChecked == true;
            c.Sound = sound.IsChecked == true;
            c.CustomQuotes = quotes.Text.Replace("\r", "");
            var conv = convert.Text.Replace("\r", "").Trim();
            c.ConvertItems = conv == ConvertItem.Default ? "" : conv;     // 没改过就存空，以后内置清单更新也能跟上
            try
            {
                if ((autoStart.IsChecked == true) != AutoStart.IsEnabled) AutoStart.Set(autoStart.IsChecked == true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "没能修改开机启动设置：" + ex.Message, "提钱下班");
            }
            app.Apply(c);
            Close();
        }
    }
}
