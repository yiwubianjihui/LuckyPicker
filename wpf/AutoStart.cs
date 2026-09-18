// ================================================================
// AutoStart.cs — 开机自启动（当前用户 Run 注册表项，无需管理员权限）
//
// 原理：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
//       值名固定为 YBHLuckyPicker，内容为 "exe 路径" /min；
//       带 /min 启动时主窗体不弹出，仅显示悬浮球。
// 卸载程序与「关闭开机自启动」都会删除该注册表项。
//
// ---------------------------------------------------------------
// 【Build 18 修的两个 bug】（用户报的「覆盖安装后开机自启不正常」）
//
// ① 「勾选开机自启动后，重进设置页又变回未勾选」（所有设备都复现）
//    根因：Run 值的内容是 `"C:\...\LuckyPicker.exe" /min`，而 IsEnabled() 把
//    它 **Trim('"',' ') 之后当作路径**去和 exe 路径比较 —— Trim 只去掉**首尾**的
//    引号/空格，尾部会残留 `" /min`，于是**永远不相等、IsEnabled() 永远返回 false**。
//    现在改为先从命令行里**取出被引号包住的 exe 路径**再比较。
//
// ② 「开机弹出『已在运行』提示，但程序起不来」
//    根因：WPF 安装器早期把 Run 值名写成了中文的 `YBH幸运摇人器`，而主程序用
//    `YBHLuckyPicker` —— 两个值名同时存在 ⇒ 开机**启动两次**。
//    又因为 App.OnStartup 里那句 `others > 0` 的"同名进程去重"是**竞态**：
//    两个实例同时启动时各自都能看到对方进程 ⇒ **双双弹窗并退出**，结果谁都起不来。
//    现在：主程序与安装器统一用 `YBHLuckyPicker`，并在启动/开关时**清理遗留值名**；
//    竞态判据已从 App.OnStartup 移除（命名互斥体是唯一权威判据）。
// ================================================================
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace LuckyPickerWpf
{
    public static class AutoStart
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>本程序写入的**规范**值名。安装器必须用同一个名字（见 SetupWpf.cs）。</summary>
        public const string ValueName = "YBHLuckyPicker";

        /// <summary>
        /// 历史遗留值名。WPF 安装器早期版本写过这些名字，与规范名并存会导致
        /// 开机启动两次（进而触发"双双退出"的竞态）。发现即清理。
        /// </summary>
        static readonly string[] LegacyValueNames = { "YBH幸运摇人器" };

        public const string BootArg = "/min";   // 开机自启时的启动参数：仅悬浮球

        /// <summary>当前是否已开启开机自启动（且指向本程序）。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    var val = key.GetValue(ValueName) as string;
                    if (string.IsNullOrEmpty(val)) return false;
                    return PathsEqual(CommandPath(val), ExePath());
                }
            }
            catch { return false; }
        }

        /// <summary>开启 / 关闭开机自启动。返回是否成功。</summary>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + ExePath() + "\" " + BootArg,
                            RegistryValueKind.String);
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                    // 无论开还是关，都把遗留值名清掉 —— 否则关掉之后它还会开机拉起程序
                    RemoveLegacy(key);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// 清理遗留值名。主程序启动时调用一次：
        /// 老用户被"两个值名"坑过，只要跑起一次新版本就自动恢复干净。
        /// </summary>
        public static void CleanupLegacy()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null) RemoveLegacy(key);
                }
            }
            catch { }
        }

        /// <summary>删除自启动项（卸载时调用，忽略错误）。规范名与遗留名一起删。</summary>
        public static void Remove()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    RemoveLegacy(key);
                }
            }
            catch { }
        }

        static void RemoveLegacy(RegistryKey key)
        {
            foreach (var name in LegacyValueNames)
            {
                try
                {
                    if (key.GetValue(name) != null) key.DeleteValue(name, false);
                }
                catch { }
            }
        }

        /// <summary>
        /// 从 Run 值里取出 exe 路径。
        ///
        /// ⚠️ 值形如 `"C:\...\LuckyPicker.exe" /min`。
        /// 早期实现直接 `val.Trim('"',' ')` 当路径用 —— Trim 只去**首尾**引号，
        /// 尾部残留 `" /min`，于是与真实路径永远不相等（这就是"勾选状态丢失"的根因）。
        /// </summary>
        static string CommandPath(string commandLine)
        {
            var s = (commandLine ?? string.Empty).Trim();
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) return s.Substring(1, end - 1);
            }
            // 没加引号：路径含空格时无法可靠切分 ⇒ 优先整串；但带 " /min" 这类参数时切掉
            int sp = s.IndexOf(" /", StringComparison.Ordinal);
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        static string ExePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                return Assembly.GetExecutingAssembly().Location;
            }
        }

        static bool PathsEqual(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a.Trim('"', ' ')),
                    Path.GetFullPath(b.Trim('"', ' ')),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
