using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace TiqianXiaban
{
    /// <summary>
    /// 自检（仅开发用，TIQIAN_MOUSETEST=1 时运行）：用真实的鼠标输入右键卡片、点"开始摸鱼"，
    /// 看摸鱼是不是开始了，再结束它。结束后把鼠标放回原处。
    /// </summary>
    static class MouseTest
    {
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        static string Describe(IntPtr h)
        {
            var c = new StringBuilder(256); var t = new StringBuilder(256);
            GetClassName(h, c, 256); GetWindowText(h, t, 256);
            return $"0x{h.ToInt64():X} class={c} title={t}";
        }
        const uint LDOWN = 0x02, LUP = 0x04, RDOWN = 0x08, RUP = 0x10;

        static void Click(Point p, bool right)
        {
            SetCursorPos((int)p.X, (int)p.Y);
            mouse_event(right ? RDOWN : LDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(right ? RUP : LUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void Run(App app, CardWindow card, StringBuilder log, Action done)
        {
            GetCursorPos(out var home);
            int step = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            t.Tick += (_, _) =>
            {
                switch (++step)
                {
                    case 1:
                        log.AppendLine($"mouse: before slacking={app.Slacking}");
                        var p0 = card.Card.PointToScreen(new Point(150, 150));
                        SetCursorPos((int)p0.X, (int)p0.Y);
                        GetCursorPos(out var at);
                        var me = new System.Windows.Interop.WindowInteropHelper(card).Handle;
                        var under = GetAncestor(WindowFromPoint(at), 2);
                        log.AppendLine($"mouse: card at {card.Left:F0},{card.Top:F0} dip, target {p0.X:F0},{p0.Y:F0} px, cursor {at.X},{at.Y}");
                        log.AppendLine($"mouse: card hwnd {Describe(me)}");
                        log.AppendLine($"mouse: under cursor {Describe(under)} isCard={under == me}");
                        Click(p0, right: true);
                        break;
                    case 2:
                        var item = card.SlackItemForTest;
                        log.AppendLine($"mouse: menu open={card.ContextMenu.IsOpen}");
                        if (card.ContextMenu.IsOpen)
                            Click(item.PointToScreen(new Point(item.ActualWidth / 2, item.ActualHeight / 2)), right: false);
                        break;
                    case 3:
                        log.AppendLine($"mouse: after click slacking={app.Slacking} menu open={card.ContextMenu.IsOpen}");
                        Click(card.Card.PointToScreen(new Point(150, 150)), right: true);
                        break;
                    case 4:
                        log.AppendLine($"mouse: reopened open={card.ContextMenu.IsOpen}");
                        if (app.Slacking) app.ToggleSlack();
                        card.ContextMenu.IsOpen = false;
                        SetCursorPos(home.X, home.Y);
                        t.Stop();
                        done();
                        break;
                }
            };
            t.Start();
        }
    }
}
