using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TiqianXiaban
{
    /// <summary>
    /// 贴边隐藏：卡片拖到屏幕左、右、上边缘就贴住，鼠标离开后缩进屏幕外，只露一条边；
    /// 鼠标移到那条边上再滑出来。只贴"外侧"的边，旁边还有别的显示器的那条边不贴，
    /// 不然缩进去的部分会出现在另一块屏幕上。位置和动画都用物理像素，跨不同缩放的显示器也准。
    /// </summary>
    public sealed class EdgeDock
    {
        public enum Edge { None, Left, Right, Top }

        const double SnapDip = 12;          // 离边缘这么近（或者拖出去了）就贴边
        const double PeekDip = 8;           // 缩进去以后露出来的宽度
        const int SlideMs = 180;
        const int LeaveDelayMs = 600;

        readonly Window w;
        readonly DispatcherTimer anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        readonly DispatcherTimer leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LeaveDelayMs) };
        RECT mon, work;                     // 贴着的那块显示器
        int along;                          // 沿着边的位置：贴左右边时是 y，贴上边时是 x
        int fromX, fromY, toX, toY;
        DateTime animStart;
        DateTime holdUntil;                 // 庆祝动画期间先别缩回去

        public Edge Docked { get; private set; } = Edge.None;
        public bool Hidden { get; private set; }
        public bool Dragging;               // 正在拖动时不自动缩回
        public Func<bool> Busy = () => false;   // 右键菜单开着等情况，不缩回

        public EdgeDock(Window w)
        {
            this.w = w;
            anim.Tick += (_, _) => Step();
            leave.Tick += (_, _) =>
            {
                if (DateTime.Now < holdUntil) return;           // 庆祝动画还没放完
                leave.Stop();
                if (!MouseOver() && !Busy() && !Dragging) SlideIn();
            };
            w.MouseEnter += (_, _) => { leave.Stop(); if (Hidden) SlideOut(); };
            w.MouseLeave += (_, _) => { if (Docked != Edge.None) leave.Start(); };
        }

        IntPtr Hwnd => new WindowInteropHelper(w).Handle;
        double Scale => GetDpiForWindow(Hwnd) / 96.0;
        int Px(double dip) => (int)Math.Round(dip * Scale);

        /// <summary>拖完以后：靠边了就贴边，否则取消贴边</summary>
        public void AfterDrag(bool enabled)
        {
            if (!enabled) { Undock(); return; }
            GetWindowRect(Hwnd, out var r);
            if (!Monitor(r, out mon, out work)) { Undock(); return; }
            int snap = Px(SnapDip);
            Edge e = Edge.None;
            if (r.Left <= work.Left + snap && Outer(Edge.Left, r)) e = Edge.Left;
            else if (r.Right >= work.Right - snap && Outer(Edge.Right, r)) e = Edge.Right;
            else if (r.Top <= work.Top + snap && Outer(Edge.Top, r)) e = Edge.Top;
            if (e == Edge.None) { Undock(); return; }
            Docked = e;
            along = e == Edge.Top ? r.Left : r.Top;
            Hidden = false;
            MoveTo(Shown(), animate: true);
            if (!MouseOver()) leave.Start();
        }

        /// <summary>启动时恢复上次的贴边（直接缩进去，不放动画）</summary>
        public void Restore(string edge, bool enabled)
        {
            if (!enabled || !Enum.TryParse<Edge>(edge, out var e) || e == Edge.None) return;
            GetWindowRect(Hwnd, out var r);
            if (!Monitor(r, out mon, out work) || !Outer(e, r)) return;
            Docked = e;
            along = e == Edge.Top ? r.Left : r.Top;
            Hidden = true;
            MoveTo(HiddenPos(), animate: false);
        }

        /// <summary>取消贴边：缩着的先滑出来</summary>
        public void Undock()
        {
            if (Docked == Edge.None) return;
            if (Hidden) MoveTo(Shown(), animate: true);
            Docked = Edge.None;
            Hidden = false;
            leave.Stop();
        }

        /// <summary>卡片尺寸变了（极简模式、缩放）：按新尺寸重新贴</summary>
        public void Relayout()
        {
            if (Docked == Edge.None) return;
            MoveTo(Hidden ? HiddenPos() : Shown(), animate: false);
        }

        /// <summary>缩着的时候临时滑出来一会儿（下班、发薪日的庆祝动画）</summary>
        public void PeekFor(TimeSpan t)
        {
            if (Docked == Edge.None) return;
            holdUntil = DateTime.Now + t;
            if (Hidden) SlideOut();
            leave.Start();
        }

        /// <summary>右键菜单关了、设置窗口关了之类：鼠标不在卡片上就过一会儿缩回去</summary>
        public void HideLater()
        {
            if (Docked != Edge.None && !Hidden) leave.Start();
        }

        public void SlideOut() { if (Docked == Edge.None) return; Hidden = false; MoveTo(Shown(), animate: true); }
        public void SlideIn() { if (Docked == Edge.None) return; Hidden = true; MoveTo(HiddenPos(), animate: true); }

        /// <summary>贴着边时，"展开"状态的左上角（DIP）——存进配置的是这个，不是缩进去的位置</summary>
        public Point? ShownDip()
        {
            if (Docked == Edge.None) return null;
            var (x, y) = Shown();
            var src = PresentationSource.FromVisual(w);
            return src?.CompositionTarget?.TransformFromDevice.Transform(new Point(x, y)) ?? new Point(x / Scale, y / Scale);
        }

        (int, int) Size()
        {
            GetWindowRect(Hwnd, out var r);
            return (r.Right - r.Left, r.Bottom - r.Top);
        }

        (int X, int Y) Shown()
        {
            var (cw, ch) = Size();
            return Docked switch
            {
                Edge.Left => (work.Left, Math.Clamp(along, work.Top, Math.Max(work.Top, work.Bottom - ch))),
                Edge.Right => (work.Right - cw, Math.Clamp(along, work.Top, Math.Max(work.Top, work.Bottom - ch))),
                _ => (Math.Clamp(along, work.Left, Math.Max(work.Left, work.Right - cw)), work.Top),
            };
        }

        (int X, int Y) HiddenPos()
        {
            var (cw, ch) = Size();
            var (x, y) = Shown();
            int peek = Px(PeekDip);
            return Docked switch
            {
                Edge.Left => (mon.Left - cw + peek, y),
                Edge.Right => (mon.Right - peek, y),
                _ => (x, mon.Top - ch + peek),
            };
        }

        void MoveTo((int X, int Y) to, bool animate)
        {
            GetWindowRect(Hwnd, out var r);
            (fromX, fromY, toX, toY) = (r.Left, r.Top, to.X, to.Y);
            if (!animate) { anim.Stop(); Place(toX, toY); return; }
            animStart = DateTime.Now;
            anim.Start();
        }

        void Step()
        {
            double t = Math.Min(1, (DateTime.Now - animStart).TotalMilliseconds / SlideMs);
            double e = 1 - Math.Pow(1 - t, 3);      // 先快后慢
            Place((int)Math.Round(fromX + (toX - fromX) * e), (int)Math.Round(fromY + (toY - fromY) * e));
            if (t >= 1) anim.Stop();
        }

        void Place(int x, int y) => SetWindowPos(Hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        bool MouseOver()
        {
            GetCursorPos(out var p);
            GetWindowRect(Hwnd, out var r);
            return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        }

        static bool Monitor(RECT r, out RECT mon, out RECT work)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            var m = MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);
            bool ok = m != IntPtr.Zero && GetMonitorInfo(m, ref mi);
            mon = mi.rcMonitor;
            work = mi.rcWork;
            return ok;
        }

        /// <summary>这条边外面没有别的显示器（取卡片沿边的三个点看）</summary>
        bool Outer(Edge e, RECT r)
        {
            for (int i = 0; i < 3; i++)
            {
                POINT p = e switch
                {
                    Edge.Left => new POINT { X = mon.Left - 2, Y = r.Top + (r.Bottom - r.Top) * i / 2 },
                    Edge.Right => new POINT { X = mon.Right + 1, Y = r.Top + (r.Bottom - r.Top) * i / 2 },
                    _ => new POINT { X = r.Left + (r.Right - r.Left) * i / 2, Y = mon.Top - 2 },
                };
                if (MonitorFromPoint(p, MONITOR_DEFAULTTONULL) != IntPtr.Zero) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- Win32
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTONEAREST = 2;
        const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

        // 自检用：当前窗口在屏幕上的矩形和所在显示器
        public string Describe()
        {
            GetWindowRect(Hwnd, out var r);
            return $"edge={Docked} hidden={Hidden} rect={r.Left},{r.Top},{r.Right},{r.Bottom} monitor={mon.Left},{mon.Top},{mon.Right},{mon.Bottom}";
        }

        // 自检用：把窗口挪到某个屏幕坐标（物理像素）上，比如另一块显示器
        public void TestMoveTo(int x, int y) => Place(x, y);

        // 自检用：把窗口挪到当前显示器的某条边上（物理像素）
        public void TestMoveToEdge(Edge e)
        {
            GetWindowRect(Hwnd, out var r);
            Monitor(r, out var m, out var wk);
            int cw = r.Right - r.Left;
            if (e == Edge.Left) Place(wk.Left - 5, r.Top);
            else if (e == Edge.Right) Place(wk.Right - cw + 5, r.Top);
            else Place((wk.Left + wk.Right - cw) / 2, wk.Top - 5);
        }
    }
}
