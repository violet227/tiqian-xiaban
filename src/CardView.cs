using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace TiqianXiaban
{
    /// <summary>
    /// 卡片本体。完整模式 300×456 是一台唱机：黑胶唱片（开工后转，摸鱼、午休时停）、唱臂、
    /// 状态行、今日已赚（歌名）、实物换算（歌手）、时间轴（已工作 / 剩余）、⏮ ▶/⏸ ⏭、
    /// 累计 / 发薪 / 假期三格、鼓励语。极简模式 240×66：左边一张小唱片，右边金额和"7:04:22 后下班"。
    /// 缩放用 LayoutTransform，文字按新尺寸重新排版，任意比例都清晰。
    /// </summary>
    public sealed class CardView : Border
    {
        static readonly FontFamily Ui = new FontFamily("Microsoft YaHei UI, Segoe UI");
        static readonly FontFamily Digits = new FontFamily("Segoe UI, Microsoft YaHei UI");
        static readonly CultureInfo Zh = new CultureInfo("zh-CN");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // 唱机的几何：唱片圆心、半径；唱臂支点
        const double DiscX = 150, DiscY = 112, DiscR = 72;
        const double PivotX = 254, PivotY = 46;
        const double ArmRest = -28, ArmPlay = 0;      // 唱臂抬起 / 落在唱片上的角度
        const double BarX = 14, BarW = 272, BarY = 282;

        readonly Dictionary<string, TextBlock> labels = new Dictionary<string, TextBlock>();
        Canvas canvas = new Canvas();
        Config config = new Config();
        string[] quotes = Quotes.BuiltIn;
        string[]? quoteOverride;            // 免费加班、发薪日时换成别的话
        int quoteIdx;
        Palette palette = Palette.Of(1, 1);
        Border? bar;
        Ellipse? knob;
        RotateTransform? arm;
        AnimationClock? spin;               // 唱片转动；暂停 / 继续，角度接着上次
        bool spinning, armDown;
        Path? playGlyph;
        FrameworkElement? playBtn, nextBtn;
        CalcResult? last;
        System.Windows.Threading.DispatcherTimer? roll;     // 提钱下班时金额往上滚
        double rollFrom, rollTo;
        DateTime rollStart;
        bool sprintOn, pulsing;             // 下班前 10 分钟 / 最后 1 分钟

        public event Action? QuoteClicked;
        public event Action? PlayClicked;   // ▶ 开工 / ⏸ 摸鱼 / ▶ 结束摸鱼
        public event Action? LeaveClicked;  // ⏭ 提钱下班

        public CardView()
        {
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Child = canvas;
        }

        string[] ShownQuotes => quoteOverride ?? quotes;

        public void NextQuote()
        {
            quoteIdx++;
            ShowQuote();
        }

        void ShowQuote()
        {
            var list = ShownQuotes;
            Set("quote", "“" + list[quoteIdx % list.Length] + "”");
        }

        TextBlock Label(string key, double x, double y, double w, double size, Brush color,
            bool bold = false, TextAlignment align = TextAlignment.Left, FontFamily? font = null)
        {
            var t = new TextBlock
            {
                Width = w, FontSize = size, Foreground = color, FontFamily = font ?? Ui,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            canvas.Children.Add(t);
            if (key.Length > 0) labels[key] = t;
            return t;
        }

        void Box(double x, double y, double w, double h, Brush fill, double radius = 0)
        {
            var b = new Border { Width = w, Height = h, Background = fill, CornerRadius = new CornerRadius(radius) };
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);
            canvas.Children.Add(b);
        }

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>按配置重建布局（切换主题 / 极简模式 / 缩放 / 鼓励语 / 发薪日金色时调用）</summary>
        public void Build(Config c, bool payday = false)
        {
            config = c;
            quotes = c.QuoteList();
            quoteIdx %= quotes.Length;
            var p = palette = payday ? Palette.Gold(c.Theme, c.Opacity) : Palette.Of(c.Theme, c.Opacity);
            labels.Clear();
            sprintOn = pulsing = false;
            spinning = armDown = false;
            bar = null; knob = null; arm = null; spin = null; playGlyph = null; playBtn = nextBtn = null;
            canvas = new Canvas();
            Child = canvas;

            double w = c.Mini ? 240 : 300, h = c.Mini ? 66 : 456;
            double bt = payday ? 1.5 : 1;     // 发薪日边框粗一点，外框尺寸不变
            canvas.Width = w - 2 * bt;
            canvas.Height = h - 2 * bt;
            Background = p.Bg;
            BorderBrush = p.Border;
            BorderThickness = new Thickness(bt);
            CornerRadius = new CornerRadius(16);
            LayoutTransform = new ScaleTransform(c.Scale, c.Scale);

            // 坐标以卡片外框为准，边框占掉 1px
            const double o = -1;
            if (c.Mini)
            {
                // 左边一张小唱片（跟着开工 / 摸鱼转和停，点它等于 ▶ / ⏸），右边金额和"7:04:22 后下班"
                var mini = BuildDisc(p, 33 + o, 33 + o, 23, big: false);
                mini.Cursor = System.Windows.Input.Cursors.Hand;
                mini.MouseLeftButtonDown += (_, e) => { e.Handled = true; if (e.ClickCount == 1) PlayClicked?.Invoke(); };
                playBtn = mini;
                Label("mini_money", 66 + o, 8 + o, 162, 20, p.Money, true, font: Digits).FontWeight = FontWeights.Bold;
                Label("mini_cd", 66 + o, 39 + o, 162, 11, p.Status);
                return;
            }

            Label("", 14 + o, 12 + o, 170, 13, p.Title, true).Text = payday ? "🎉 发薪日快乐" : "⏱ 提钱下班";
            Label("date", 120 + o, 14 + o, 166, 11, p.Date, align: TextAlignment.Right);

            BuildDisc(p, DiscX + o, DiscY + o, DiscR, big: true);
            BuildArm(p, o);

            Label("np", 14 + o, 194 + o, 272, 10, p.Money, true);             // NOW PLAYING · 上班中
            var money = Label("money", 12 + o, 207 + o, 276, 28, p.Title, true, font: Digits);
            money.FontWeight = FontWeights.Bold;
            Label("convert", 14 + o, 248 + o, 272, 13, p.Money);              // 今天已经赚够 3 杯奶茶

            Box(BarX + o, BarY + o, BarW, 4, p.Track, 2);
            bar = new Border { Width = 0, Height = 4, Background = p.Bar, CornerRadius = new CornerRadius(2) };
            Canvas.SetLeft(bar, BarX + o);
            Canvas.SetTop(bar, BarY + o);
            canvas.Children.Add(bar);
            knob = new Ellipse { Width = 11, Height = 11, Fill = p.Bar, Stroke = p.Bg, StrokeThickness = 2 };
            Canvas.SetLeft(knob, BarX + o - 5.5);
            Canvas.SetTop(knob, BarY + o - 3.5);
            canvas.Children.Add(knob);
            Label("elapsed", 14 + o, 292 + o, 120, 10.5, p.Speed, font: Digits);
            Label("remain", 166 + o, 292 + o, 120, 10.5, p.Speed, align: TextAlignment.Right, font: Digits);

            BuildControls(p, o);

            Box(14 + o, 380 + o, 272, 1, p.Sep);
            var cells = new (string Title, string Key, double X, double W)[]
            {
                (c.PeriodMode == 0 ? "本月累计" : "本期累计", "g_累计", 14, 100),
                ("距发薪日", "g_距发薪日", 118, 82),
                ("下个假期", "g_假期", 204, 84),
            };
            foreach (var (title, key, x, cw) in cells)
            {
                Label("", x + o, 389 + o, cw, 9.5, p.GridTitle).Text = title;
                Label(key, x + o, 403 + o, cw, 11.5, p.GridValue, true, font: Digits);
            }

            var quote = Label("quote", 14 + o, 430 + o, 272, 10, p.Quote, align: TextAlignment.Center);
            quote.Cursor = System.Windows.Input.Cursors.Hand;
            quote.ToolTip = "点一下换一句";
            quote.MouseLeftButtonDown += (_, e) => { e.Handled = true; NextQuote(); QuoteClicked?.Invoke(); };
            ShowQuote();
        }

        // ------------------------------------------------------------ 唱片
        /// <summary>
        /// 一张黑胶：圆心 (cx, cy)、半径 R。转的部分是纹路、放射状细纹和标签（不对称才看得出在转），
        /// 反光不转。big 时标签上印"提钱 / 下班"；极简模式的小唱片只有标签色。
        /// </summary>
        Canvas BuildDisc(Palette p, double cx, double cy, double R, bool big)
        {
            double d = R * 2;
            var disc = new Canvas { Width = d, Height = d };
            Canvas.SetLeft(disc, cx - R);
            Canvas.SetTop(disc, cy - R);
            canvas.Children.Add(disc);

            var vinyl = new RadialGradientBrush(Color.FromRgb(0x24, 0x20, 0x1C), Color.FromRgb(0x0C, 0x0B, 0x0A));
            vinyl.Freeze();
            disc.Effect = new DropShadowEffect { BlurRadius = big ? 16 : 6, ShadowDepth = big ? 3 : 1, Opacity = 0.45, Color = Colors.Black };
            disc.Children.Add(new Ellipse { Width = d, Height = d, Fill = vinyl, Stroke = Frozen(Color.FromArgb(0x55, 0x6A, 0x5C, 0x4C)), StrokeThickness = big ? 1.5 : 1 });

            double lr = R * 0.39;                   // 标签半径
            var rotating = new Canvas { Width = d, Height = d };
            var groove = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            double step = Math.Max(2.2, R * 0.046);
            for (double r = R - R * 0.08; r > lr + 4; r -= step)
            {
                var g = new Ellipse { Width = r * 2, Height = r * 2, Stroke = groove, StrokeThickness = big ? 0.6 : 0.5 };
                Canvas.SetLeft(g, R - r);
                Canvas.SetTop(g, R - r);
                rotating.Children.Add(g);
            }
            var ray = Frozen(Color.FromArgb(big ? (byte)0x10 : (byte)0x22, 0xFF, 0xE6, 0xB0));
            int rays = big ? 72 : 24;
            for (int i = 0; i < rays; i++)
            {
                double a = i * 2 * Math.PI / rays, r0 = lr + 3, r1 = R - R * 0.055 - (i % 3) * R * 0.04;
                rotating.Children.Add(new Line
                {
                    X1 = R + r0 * Math.Cos(a), Y1 = R + r0 * Math.Sin(a),
                    X2 = R + r1 * Math.Cos(a), Y2 = R + r1 * Math.Sin(a),
                    Stroke = ray, StrokeThickness = big ? 0.7 : 0.6,
                });
            }
            var labelFill = new RadialGradientBrush(Lighter(p.Money, 0.18), (p.Money as SolidColorBrush)?.Color ?? Colors.Orange);
            labelFill.Freeze();
            var label = new Ellipse { Width = lr * 2, Height = lr * 2, Fill = labelFill };
            Canvas.SetLeft(label, R - lr);
            Canvas.SetTop(label, R - lr);
            rotating.Children.Add(label);
            var ink = Frozen(Color.FromArgb(0xD0, 0x2A, 0x1C, 0x0C));
            if (big)
            {
                var t1 = new TextBlock { Text = "提钱", FontSize = 10.5, FontWeight = FontWeights.Bold, FontFamily = Ui, Foreground = ink, Width = 40, TextAlignment = TextAlignment.Center };
                var t2 = new TextBlock { Text = "下班", FontSize = 8.5, FontFamily = Ui, Foreground = ink, Width = 40, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(t1, R - 20); Canvas.SetTop(t1, R - 16);
                Canvas.SetLeft(t2, R - 20); Canvas.SetTop(t2, R + 2);
                rotating.Children.Add(t1);
                rotating.Children.Add(t2);
            }
            else
            {
                // 小标签上一道深色的弧，转起来才看得出
                rotating.Children.Add(new Path
                {
                    Data = Geometry.Parse(FormattableString.Invariant($"M {R - lr * 0.62},{R - lr * 0.2} A {lr * 0.65},{lr * 0.65} 0 0 1 {R + lr * 0.2},{R - lr * 0.62}")),
                    Stroke = ink, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                });
            }
            double hr = big ? 2 : 1.3;
            var hole = new Ellipse { Width = hr * 2, Height = hr * 2, Fill = Frozen(Color.FromRgb(0x14, 0x11, 0x0E)) };
            Canvas.SetLeft(hole, R - hr); Canvas.SetTop(hole, R - hr);
            rotating.Children.Add(hole);
            var rot = new RotateTransform(0, R, R);
            rotating.RenderTransform = rot;
            disc.Children.Add(rotating);

            var sheen = new LinearGradientBrush(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 45);
            sheen.Freeze();
            disc.Children.Add(new Path
            {
                Data = Geometry.Parse(FormattableString.Invariant($"M {R},{R} L {R + R * 0.97},{R - R * 0.26} A {R},{R} 0 0 0 {R + R * 0.5},{R - R * 0.86} Z")),
                Fill = sheen, IsHitTestVisible = false,
            });

            var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.6)) { RepeatBehavior = RepeatBehavior.Forever };
            spin = anim.CreateClock();
            rot.ApplyAnimationClock(RotateTransform.AngleProperty, spin);
            spin.Controller!.Pause();
            return disc;
        }

        static Color Lighter(Brush b, double k)
        {
            var c = (b as SolidColorBrush)?.Color ?? Colors.Orange;
            byte L(byte v) => (byte)Math.Min(255, v + (255 - v) * k);
            return Color.FromRgb(L(c.R), L(c.G), L(c.B));
        }

        // ------------------------------------------------------------ 唱臂
        void BuildArm(Palette p, double o)
        {
            var metal = config.Theme == 1 ? Frozen(Color.FromRgb(0x8E, 0x86, 0x7C)) : Frozen(Color.FromRgb(0xC9, 0xC1, 0xB5));
            var g = new Canvas();
            // 支点下面的底座
            var baseDisc = new Ellipse { Width = 22, Height = 22, Fill = Frozen(Color.FromArgb(0x40, 0x80, 0x76, 0x6A)) };
            Canvas.SetLeft(baseDisc, PivotX - 11 + o); Canvas.SetTop(baseDisc, PivotY - 11 + o);
            canvas.Children.Add(baseDisc);
            g.Children.Add(new Line
            {
                X1 = PivotX + o, Y1 = PivotY + o, X2 = PivotX - 36 + o, Y2 = PivotY + 60 + o,
                Stroke = metal, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
            var head = new Rectangle { Width = 7, Height = 13, RadiusX = 1.5, RadiusY = 1.5, Fill = metal, RenderTransformOrigin = new Point(0.5, 0.5) };
            head.RenderTransform = new RotateTransform(31);
            Canvas.SetLeft(head, PivotX - 36 - 3.5 + o); Canvas.SetTop(head, PivotY + 60 - 6 + o);
            g.Children.Add(head);
            var pivot = new Ellipse { Width = 12, Height = 12, Fill = metal, Stroke = p.Bg, StrokeThickness = 1.5 };
            Canvas.SetLeft(pivot, PivotX - 6 + o); Canvas.SetTop(pivot, PivotY - 6 + o);
            arm = new RotateTransform(ArmRest, PivotX + o, PivotY + o);
            g.RenderTransform = arm;
            canvas.Children.Add(g);
            canvas.Children.Add(pivot);
        }

        void SetArm(bool down)
        {
            if (arm == null || down == armDown) return;
            armDown = down;
            if (!IsLoaded)                  // 还没显示（离屏渲染截图）：直接放到位
            {
                arm.BeginAnimation(RotateTransform.AngleProperty, null);
                arm.Angle = down ? ArmPlay : ArmRest;
                return;
            }
            arm.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(down ? ArmPlay : ArmRest, TimeSpan.FromSeconds(0.5)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
        }

        // 自检用：唱片在不在转、现在转到多少度（转过的总角度，不取模）
        public bool SpinningForTest => spinning;
        public double DiscAngleForTest => spin?.CurrentTime is TimeSpan t ? t.TotalSeconds / 3.6 * 360 : 0;

        void SetSpinning(bool on)
        {
            if (spin?.Controller == null || on == spinning) return;
            spinning = on;
            if (on) spin.Controller.Resume(); else spin.Controller.Pause();
        }

        // ------------------------------------------------------------ ⏮ ▶/⏸ ⏭
        static readonly Geometry PlayGeo = Geometry.Parse("M 2,0 L 17,9.5 L 2,19 Z");
        static readonly Geometry PauseGeo = Geometry.Parse("M 0,0 H 5.5 V 19 H 0 Z M 10.5,0 H 16 V 19 H 10.5 Z");

        void BuildControls(Palette p, double o)
        {
            const double cy = 340;
            // 中间：▶ 开工 / ⏸ 摸鱼
            var glyphInk = Frozen(Color.FromRgb(0x1E, 0x1A, 0x15));
            var play = new Grid { Width = 50, Height = 50, Cursor = System.Windows.Input.Cursors.Hand };
            var moneyColor = (p.Money as SolidColorBrush)?.Color ?? Colors.Orange;
            play.Children.Add(new Ellipse
            {
                Fill = new RadialGradientBrush(Lighter(p.Money, 0.15), moneyColor),
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55, Color = moneyColor },
            });
            playGlyph = new Path { Data = PlayGeo, Fill = glyphInk, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            play.Children.Add(playGlyph);
            play.MouseLeftButtonDown += (_, e) => { e.Handled = true; if (e.ClickCount == 1) PlayClicked?.Invoke(); };
            Canvas.SetLeft(play, 150 - 25 + o); Canvas.SetTop(play, cy - 25 + o);
            canvas.Children.Add(play);
            playBtn = play;

            // 左：⏮ 换一句鼓励；右：⏭ 提钱下班
            nextBtn = SideButton("M 0,0 L 11,7.5 L 0,15 Z M 12,0 H 15 V 15 H 12 Z", 215, cy, p, o, () => LeaveClicked?.Invoke());
            var prev = SideButton("M 15,0 L 4,7.5 L 15,15 Z M 0,0 H 3 V 15 H 0 Z", 85, cy, p, o, () => { NextQuote(); QuoteClicked?.Invoke(); });
            prev.ToolTip = "换一句鼓励";
        }

        FrameworkElement SideButton(string geo, double cx, double cy, Palette p, double o, Action act)
        {
            var b = new Grid { Width = 34, Height = 34, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            b.Children.Add(new Path { Data = Geometry.Parse(geo), Fill = p.Speed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            b.MouseLeftButtonDown += (_, e) => { e.Handled = true; if (e.ClickCount == 1 && b.Opacity > 0.5) act(); };
            Canvas.SetLeft(b, cx - 17 + o); Canvas.SetTop(b, cy - 17 + o);
            canvas.Children.Add(b);
            return b;
        }

        /// <summary>下班前 10 分钟：倒计时变大、变成金额的颜色；最后 1 分钟一闪一闪</summary>
        void Sprint(string key, bool on, int secondsLeft, double normal, double big, Brush normalColor, double top, double bigTop)
        {
            if (!labels.TryGetValue(key, out var t)) return;
            if (on != sprintOn)
            {
                sprintOn = on;
                t.FontSize = on ? big : normal;
                t.FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
                t.Foreground = on ? palette.Money : normalColor;
                Canvas.SetTop(t, on ? bigTop : top);
            }
            bool pulse = on && secondsLeft <= 60;
            if (pulse != pulsing)
            {
                pulsing = pulse;
                t.BeginAnimation(OpacityProperty, pulse
                    ? new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                    : null);
            }
        }

        static readonly Brush[] Confetti =
        {
            new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)), new SolidColorBrush(Color.FromRgb(0xF7, 0xC8, 0x4F)),
            new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0xFF)), new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x9A)),
            new SolidColorBrush(Color.FromRgb(0x5C, 0xD6, 0x8A)), new SolidColorBrush(Color.FromRgb(0xB2, 0x7C, 0xFF)),
        };
        static readonly Random Rng = new Random();

        /// <summary>撒一把彩纸，中间弹出一行字（下班那一刻、发薪日、提钱下班），3 秒后自己消失</summary>
        public void PlayCelebration(string text)
        {
            double w = canvas.Width, h = canvas.Height;
            var layer = new Canvas { Width = w, Height = h, IsHitTestVisible = false, ClipToBounds = true };
            canvas.Children.Add(layer);
            for (int i = 0; i < (config.Mini ? 18 : 32); i++)
            {
                var piece = new Rectangle
                {
                    Width = 4 + Rng.NextDouble() * 4, Height = 7 + Rng.NextDouble() * 6, RadiusX = 1.5, RadiusY = 1.5,
                    Fill = Confetti[i % Confetti.Length], RenderTransformOrigin = new Point(0.5, 0.5),
                };
                double angle = Rng.NextDouble() * 360, x = Rng.NextDouble() * w;
                var spinPiece = new RotateTransform(angle);
                piece.RenderTransform = spinPiece;
                Canvas.SetLeft(piece, x);
                Canvas.SetTop(piece, -20);
                layer.Children.Add(piece);
                var dur = TimeSpan.FromSeconds(1.4 + Rng.NextDouble() * 1.3);
                var delay = TimeSpan.FromMilliseconds(Rng.NextDouble() * 450);
                piece.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(-20 - Rng.NextDouble() * 30, h + 12, dur)
                    { BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                piece.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, x + (Rng.NextDouble() - 0.5) * 70, dur) { BeginTime = delay });
                spinPiece.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, angle + (Rng.Next(2) == 0 ? 360 : -360), dur) { BeginTime = delay });
            }
            if (!config.Mini)
            {
                var label = new TextBlock
                {
                    Text = text, FontSize = 30, FontWeight = FontWeights.Bold, FontFamily = Ui, Foreground = palette.Money,
                    TextAlignment = TextAlignment.Center,
                };
                var banner = new Border
                {
                    Child = label, Width = w - 40, Padding = new Thickness(0, 10, 0, 12), CornerRadius = new CornerRadius(14),
                    Background = palette.Bg, BorderBrush = palette.Money, BorderThickness = new Thickness(1.5),
                    RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0,
                };
                var scale = new ScaleTransform(0.6, 0.6);
                banner.RenderTransform = scale;
                Canvas.SetLeft(banner, 20);
                Canvas.SetTop(banner, h / 2 - 40);
                layer.Children.Add(banner);
                var fade = new DoubleAnimationUsingKeyFrames();
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));
                banner.BeginAnimation(OpacityProperty, fade);
                var pop = new DoubleAnimationUsingKeyFrames();
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            var done = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            done.Tick += (_, _) => { done.Stop(); (layer.Parent as Panel)?.Children.Remove(layer); };
            done.Start();
        }

        /// <summary>提钱下班：金额在 1.2 秒里从 from 滚到 to</summary>
        public void RollMoney(double from, double to)
        {
            rollFrom = from;
            rollTo = to;
            rollStart = DateTime.Now;
            roll ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            roll.Tick -= RollTick;
            roll.Tick += RollTick;
            roll.Start();
            RollTick(null, EventArgs.Empty);
        }

        bool Rolling => roll != null && roll.IsEnabled;

        void RollTick(object? sender, EventArgs e)
        {
            double t = Math.Min(1, (DateTime.Now - rollStart).TotalMilliseconds / 1200);
            double v = rollFrom + (rollTo - rollFrom) * (1 - Math.Pow(1 - t, 3));
            Set(config.Mini ? "mini_money" : "money", "¥ " + v.ToString("F2", Inv));
            if (t >= 1) roll!.Stop();
        }

        static string Hms(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(sec)));
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }

        static void Tip(FrameworkElement? e, string tip)
        {
            if (e != null && e.ToolTip as string != tip) e.ToolTip = tip;
        }

        /// <summary>唱片、播放键、状态行、实物换算、假期、换着说的鼓励语</summary>
        public void UpdateFun(FunView v)
        {
            SetSpinning(v.Spinning);
            if (config.Mini)
            {
                Tip(playBtn, v.Slacking ? "点唱片：结束摸鱼" : v.PlayTip.Replace("开工：", "点唱片开工：").Replace("暂停 = ", "点唱片暂停 = "));
                if (v.Slacking) Set("mini_cd", "带薪摸鱼 " + v.SlackButton.Replace("🐟 ", ""));
                return;
            }
            SetArm(v.Spinning);
            if (playGlyph != null) playGlyph.Data = v.ShowPause ? PauseGeo : PlayGeo;
            if (playGlyph != null) playGlyph.Margin = new Thickness(v.ShowPause ? 0 : 3, 0, 0, 0);
            if (playBtn != null) { playBtn.Opacity = v.CanPlay || v.Slacking ? 1 : 0.4; Tip(playBtn, v.Slacking ? v.SlackTip.Replace("再点一下结束", "点 ▶ 结束") : v.PlayTip); }
            if (nextBtn != null) { nextBtn.Opacity = v.CanLeave ? 1 : 0.3; Tip(nextBtn, v.CanLeave ? v.LeaveTip : "上班时间才能提钱下班"); }

            Set("convert", v.Convert);
            Set("g_假期", v.HolidayShort);
            if (labels.TryGetValue("g_假期", out var h))
            {
                h.Foreground = v.HolidaySoon ? palette.Money : palette.GridValue;
                Tip(h, v.Holiday + "\n" + v.HolidayTip);
            }
            if (last != null) Set("np", NowPlaying(last, v));
            if (!ReferenceEquals(quoteOverride, v.QuoteOverride))
            {
                quoteOverride = v.QuoteOverride;
                ShowQuote();
            }
        }

        // 唱机状态行：NOW PLAYING · 上班中 / PAUSED · 🐟 12:24 · ¥13.78 / 已下班 · 收工啦，辛苦了 …
        static string NowPlaying(CalcResult r, FunView v)
        {
            bool sprint = r.Status == "上班中" && r.SecondsToEnd >= 0 && r.SecondsToEnd <= 600;
            string extra = r.Countdown.StartsWith("距离下班还有", StringComparison.Ordinal) ? "" : " · " + r.Countdown;
            if (v.Slacking) return "PAUSED · 带薪摸鱼 " + v.SlackButton.Replace("🐟 ", "");
            if (r.Status == "上班中")
                return (v.Started ? "NOW PLAYING · " : "READY · 点 ▶ 开工 · ") + (sprint ? "下班冲刺" : "上班中") + extra;
            if (r.Status == "午休中") return "INTERMISSION · 午休中 · " + r.Countdown;
            return r.Status + " · " + r.Countdown;
        }

        // 极简模式第二行：不写状态，只说还要多久："7:04:22 后下班""2:15:30 后周末""0:30:00 后午休结束"
        static readonly System.Text.RegularExpressions.Regex Until =
            new System.Text.RegularExpressions.Regex(@"^(?:距离(.+?)|(午休结束))还有 (\d+:\d\d:\d\d)$");

        static string MiniLine(CalcResult r)
        {
            var m = Until.Match(r.Countdown);
            if (m.Success) return $"{m.Groups[3].Value} 后{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}";
            string cd = r.Countdown.Replace("老板白嫖", "白嫖");
            return r.Status == "免费加班中" ? "免费加班 " + cd : cd;
        }

        void Set(string key, string text)
        {
            if (labels.TryGetValue(key, out var t) && t.Text != text) t.Text = text;
        }

        public void Update(CalcResult r, DateTime now)
        {
            last = r;
            string money = "¥ " + r.Earned.ToString("F2", Inv);   // 金额统一两位小数
            bool sprint = r.Status == "上班中" && r.SecondsToEnd >= 0 && r.SecondsToEnd <= 600;
            if (config.Mini)
            {
                if (!Rolling) Set("mini_money", money);
                Set("mini_cd", MiniLine(r));
                Sprint("mini_cd", sprint, r.SecondsToEnd, 11, 12.5, palette.Status, 38, 37);
                return;
            }
            Set("date", now.ToString("M月d日 dddd HH:mm:ss", Zh));
            if (!Rolling) Set("money", money);
            if (labels.TryGetValue("money", out var m))
                Tip(m, r.Status == "提前下班" ? "今天的钱已经提走啦 💰"
                    : string.Format(Inv, "今日满勤可得 ¥{0:F2}\n当前赚钱速度 ¥{1:F4}/秒 · ¥{2:F2}/分钟", r.FullDay, r.SpeedPerSec, r.SpeedPerMin));

            // 时间轴：左边已经工作了多久，右边还剩多久（不在上班时间就显示一天的总时长）
            double f = Math.Clamp(r.WorkedHours / Math.Max(r.TotalHours, 0.01), 0, 1);
            if (bar != null) bar.Width = BarW * f;
            if (knob != null) Canvas.SetLeft(knob, BarX - 1 + BarW * f - 5.5);
            Tip(bar, string.Format(Inv, "今日工时进度 {0:F1}%（{1:F2} / {2:F2} 小时）", f * 100, r.WorkedHours, r.TotalHours));
            bool onClock = r.Status == "上班中" || r.Status == "午休中";
            Set("elapsed", Hms(r.WorkedHours * 3600));
            Set("remain", onClock ? "-" + Hms((r.TotalHours - r.WorkedHours) * 3600) : Hms(r.TotalHours * 3600));
            Sprint("remain", sprint, r.SecondsToEnd, 10.5, 13, palette.Speed, 291, 289);

            Set("g_累计", "¥ " + r.PeriodTotal.ToString("F2", Inv));
            if (labels.TryGetValue("g_累计", out var total)) Tip(total, $"从 {r.PeriodStart:M月d日} 起累计\n今日满勤可得 ¥{r.FullDay.ToString("F2", Inv)}");
            Set("g_距发薪日", r.DaysToPayday <= 0 ? "就是今天！" : $"还有 {r.DaysToPayday} 天");
        }
    }
}
