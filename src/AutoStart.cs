using System;
using Microsoft.Win32;

namespace TiqianXiaban
{
    /// <summary>开机自启：写当前用户的 Run 注册表项，不需要管理员权限</summary>
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "TiqianXiaban";

        static string Command => "\"" + Environment.ProcessPath + "\"";

        public static bool IsEnabled
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(Name) is string v && v.Length > 0;
            }
        }

        public static void Set(bool on)
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(Name, Command);
            else k.DeleteValue(Name, false);
        }

        /// <summary>程序被挪了位置时，把已开启的自启项改指向现在的位置</summary>
        public static void Refresh()
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k?.GetValue(Name) is string v && v.Length > 0 && !string.Equals(v, Command, StringComparison.OrdinalIgnoreCase))
                k.SetValue(Name, Command);
        }
    }
}
