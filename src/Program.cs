using System;
using System.Threading;
using System.Windows;

namespace TiqianXiaban
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // 开发 / 截图用：把某一时刻的卡片渲染成 PNG，不弹窗
            if (args.Length > 0 && args[0] == "--render") return RenderTool.Run(args);
            if (args.Length > 0 && args[0] == "--calc") return RenderTool.Calc(args);
            if (args.Length > 0 && args[0] == "--funtest") return RenderTool.FunTest(args);

            // 自检用自己的配置目录，可以和正在使用的那一份同时运行
            bool selfTest = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TIQIAN_SELFTEST") ?? Environment.GetEnvironmentVariable("TIQIAN_MOUSETEST"));
            using var mutex = new Mutex(true, @"Local\TiqianXiaban.SingleInstance" + (selfTest ? ".SelfTest" : ""), out bool first);
            if (!first)
            {
                MessageBox.Show("提钱下班已经在运行了。\n卡片被隐藏的话，点任务栏右下角托盘里的图标就能找回来。", "提钱下班");
                return 0;
            }
            return new App().Run();
        }
    }
}
