using System.Windows.Media;

namespace TiqianXiaban
{
    /// <summary>深色 / 白色两套配色，外加发薪日当天的金色版</summary>
    public sealed class Palette
    {
        public Brush Bg = null!, Border = null!, Title = null!, Date = null!, Status = null!, Money = null!, Speed = null!,
            Quote = null!, ProgressText = null!, Track = null!, Bar = null!, Sep = null!, GridTitle = null!, GridValue = null!;

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Brush Hex(uint argb) => Frozen(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        static Brush GoldBar()
        {
            var b = new LinearGradientBrush(Color.FromRgb(0xF7, 0xC8, 0x4F), Color.FromRgb(0xD9, 0x96, 0x0B), 0);
            b.Freeze();
            return b;
        }

        /// <summary>发薪日：金色的边框、金额和进度条</summary>
        public static Palette Gold(int theme, double opacity)
        {
            byte a = (byte)System.Math.Round(opacity * 255);
            if (theme == 1)
                return new Palette
                {
                    Bg = Frozen(Color.FromArgb(a, 0xFF, 0xF7, 0xE1)), Border = Hex(0xCCE0AE3A), Title = Hex(0xFF5C4300), Date = Hex(0xFF8A6D1F),
                    Status = Hex(0xFFB07800), Money = Hex(0xFFC98A00), Speed = Hex(0xFF7A6430), Quote = Hex(0xFF9A7B2E),
                    ProgressText = Hex(0xFF5C4A1A), Track = Hex(0x26B07800), Bar = GoldBar(), Sep = Hex(0x2EB07800),
                    GridTitle = Hex(0xFF9A7B2E), GridValue = Hex(0xFF4A3800),
                };
            return new Palette
            {
                Bg = Frozen(Color.FromArgb(a, 0x24, 0x1D, 0x0E)), Border = Hex(0xB3D4A93A), Title = Hex(0xFFF6E3B0), Date = Hex(0xFFCDB57A),
                Status = Hex(0xFFFFD15C), Money = Hex(0xFFFFC93C), Speed = Hex(0xFFCDB57A), Quote = Hex(0xFFB59A5A),
                ProgressText = Hex(0xFFEADBB0), Track = Hex(0x26FFD25A), Bar = GoldBar(), Sep = Hex(0x2EFFD25A),
                GridTitle = Hex(0xFFB59A5A), GridValue = Hex(0xFFF6E9C6),
            };
        }

        /// <summary>白色是暖白底配琥珀橙，深色是暖炭灰底配琥珀橙，和唱机的黑胶、金色标签搭</summary>
        public static Palette Of(int theme, double opacity)
        {
            byte a = (byte)System.Math.Round(opacity * 255);
            if (theme == 1)
                return new Palette
                {
                    Bg = Frozen(Color.FromArgb(a, 0xFF, 0xFD, 0xF8)), Border = Hex(0x1C000000), Title = Hex(0xFF26221D), Date = Hex(0xFF7A736A),
                    Status = Hex(0xFF2F6DB5), Money = Hex(0xFFD9771C), Speed = Hex(0xFF6B655C), Quote = Hex(0xFF8A8378),
                    ProgressText = Hex(0xFF4A453E), Track = Hex(0x14000000), Bar = Hex(0xFFD9771C), Sep = Hex(0x12000000),
                    GridTitle = Hex(0xFF8A8378), GridValue = Hex(0xFF2C2823),
                };
            return new Palette
            {
                Bg = Frozen(Color.FromArgb(a, 0x1C, 0x1A, 0x17)), Border = Hex(0x1AFFFFFF), Title = Hex(0xFFF3EDE2), Date = Hex(0xFFA39D93),
                Status = Hex(0xFF7FB3E6), Money = Hex(0xFFF0A33A), Speed = Hex(0xFFA8A196), Quote = Hex(0xFF857F76),
                ProgressText = Hex(0xFFD4CEC3), Track = Hex(0x1FFFFFFF), Bar = Hex(0xFFF0A33A), Sep = Hex(0x17FFFFFF),
                GridTitle = Hex(0xFF8E887E), GridValue = Hex(0xFFEDE7DC),
            };
        }
    }
}
