// ================================================================
// AutoStart.cs — 开机自启动（当前用户 Run 注册表项，无需管理员权限）
//
// 原理：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
//       值名固定为 YBHLuckyPicker，内容为当前 exe 路径 + /min 参数；
//       带 /min 启动时主窗体不弹出，仅显示悬浮球。
// 卸载程序与「关闭开机自启动」都会删除该注册表项。
// ================================================================
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Reflection;

namespace LuckyPicker
{
    public static class AutoStart
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "YBHLuckyPicker";
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
                    return PathsEqual(val, ExePath());
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
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>删除自启动项（卸载时调用，忽略错误）。</summary>
        public static void Remove()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null && key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, false);
                }
            }
            catch { }
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
                    System.IO.Path.GetFullPath(a.Trim('"', ' ')),
                    System.IO.Path.GetFullPath(b.Trim('"', ' ')),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
