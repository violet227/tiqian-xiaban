using System;
using System.IO;
using System.Media;

namespace TiqianXiaban
{
    /// <summary>
    /// 下班时的钱币音效。coin.wav 嵌在 exe 里（scripts/make_coin_sound.py 合成），第一次用时载入内存，
    /// 之后每次直接播。SoundPlayer.Play 在后台线程放，不卡界面；没有声卡之类的错误一律吞掉。
    /// </summary>
    public static class Sounds
    {
        static SoundPlayer? coin;
        static bool failed;

        /// <summary>自检时设成 true：只计数不出声</summary>
        public static bool Muted;
        public static int CoinCount { get; private set; }

        static SoundPlayer? Coin()
        {
            if (coin != null || failed) return coin;
            try
            {
                using var res = typeof(Sounds).Assembly.GetManifestResourceStream("coin.wav");
                if (res == null) { failed = true; return null; }
                var mem = new MemoryStream();
                res.CopyTo(mem);
                mem.Position = 0;
                coin = new SoundPlayer(mem);
                coin.Load();
            }
            catch { failed = true; coin = null; }
            return coin;
        }

        /// <summary>音效能不能放（资源在、能解码），自检用</summary>
        public static bool CoinReady => Coin() != null;

        public static void PlayCoin()
        {
            CoinCount++;
            if (Muted) return;
            try { Coin()?.Play(); } catch { }
        }
    }
}
