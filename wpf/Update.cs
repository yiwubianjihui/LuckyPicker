// ================================================================
// Update.cs — 版本信息 / 检查更新接口 / 关于与版本窗口
//
// 更新检查协议（部署到个人网站时使用）：
//   GET https://你的网站/任意路径/ybh-luckypicker/update.json
//   返回 UTF-8 JSON：
//   {
//     "product":  "YBH幸运摇人器",
//     "version":  "26H2 Build 13",     // 必填，展示给用户
//     "build":    12,                   // 可选，建议填写，用于精确比较
//     "channel":  "26H2",               // 可选
//     "url":      "https://你的网站/download.zip",  // 可选，新版本下载页
//     "notes":    "1.新增...\n2.修复...",            // 可选，更新说明
//     "releaseDate": "2025-06-01",      // 可选
//     "mandatory": false                // 可选，是否强制更新
//   }
//
// 本地调试：在“版本 · 更新”窗口中可以直接填本机 update.json 路径，
// 程序同样按上述格式解析，便于上线前自测。
// ================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;


namespace LuckyPickerWpf
{
    // ---------------- 应用版本 ----------------
    public static class AppVersion
    {
        public const string ProductName = "YBH幸运摇人器";
        public const string Display = "26H2 Build 14";
        public const string Channel = "26H2";
        public const int Build = 14;
        public const string UpdateUrlDefault = "https://lr.yibianhui.cn/update.json";
        // 备用接口：主接口失效时自动回退尝试（镜像站 / 历史地址）
        public const string UpdateUrlLegacy = "https://app.yibianhui.cn/luckypicker/update.json";
        // 单实例保护：全局命名互斥体 + 「唤起主窗口」命名事件
        public const string SingleInstanceMutex = "YBH-LuckyPicker-SingleInstance";
        public const string ShowMainEvent = "YBH-LuckyPicker-ShowMainEvent";
    }

    // ---------------- 更新检查结果 ----------------
    public class UpdateResult
    {
        public bool Success;
        public string ErrorMessage;
        public bool HasUpdate;
        public string LatestVersion;
        public int LatestBuild;
        public string Channel;
        public string DownloadUrl;
        public string Notes;
        public string ReleaseDate;
        public bool Mandatory;
    }

    // ---------------- 更新管理器 ----------------
    public static class UpdateManager
    {
        static UpdateManager()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        // 检查更新。url 支持 http(s)://、file:/// 以及本机文件路径（便于上线前自测）。
        public static UpdateResult Check(string url)
        {
            var res = new UpdateResult();
            try
            {
                string u = (url ?? "").Trim();
                if (u.Length == 0)
                {
                    res.ErrorMessage = "尚未填写更新接口地址。";
                    return res;
                }

                string json;
                if (u.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    try { json = File.ReadAllText(new Uri(u).LocalPath, Encoding.UTF8); }
                    catch (Exception ex) { res.ErrorMessage = "读取本地更新文件失败：" + ex.Message; return res; }
                }
                else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    json = HttpGetSmart(u, out res.ErrorMessage);
                    if (json == null) return res;
                }
                else
                {
                    if (!File.Exists(u)) { res.ErrorMessage = "更新接口地址无效：请填写 http(s) 链接或存在的本地 update.json 路径。"; return res; }
                    try { json = File.ReadAllText(u, Encoding.UTF8); }
                    catch (Exception ex) { res.ErrorMessage = "读取本地更新文件失败：" + ex.Message; return res; }
                }

                return ParseJson(json);
            }
            catch (Exception ex)
            {
                res.ErrorMessage = "检查更新时发生异常：" + ex.Message;
                return res;
            }
        }

        // 智能读取：部分个人网站会对非浏览器请求返回 JavaScript 验证页
        // （页面内嵌 AES 挑战）。遇到时自动求解 cookie 并重试一次。
        static string HttpGetSmart(string url, out string error)
        {
            string first = HttpGet(url, null, out error);
            if (first == null) return null;
            if (!IsJsChallenge(first)) return first;

            string cookie = SolveChallengeCookie(first);
            if (cookie == null)
            {
                error = "更新接口返回的是网站 JavaScript 验证页，程序无法读取 JSON。\n" +
                        "请在网站后台关闭该路径的“JS 访客防护/防盗链”，或让 update.json 对程序直接返回 JSON。";
                return null;
            }

            string json = HttpGet(WithChallengeQuery(url), cookie, out error);
            if (json == null) return null;
            if (IsJsChallenge(json))
            {
                error = "更新接口的 JavaScript 验证未通过，仍返回验证页。\n" +
                        "请在网站后台关闭该路径的“JS 访客防护/防盗链”，或让 update.json 对程序直接返回 JSON。";
                return null;
            }
            return json;
        }

        static string HttpGet(string url, string cookie, out string error)
        {
            error = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "YBH-LuckyPicker/" + AppVersion.Display + " (Win32; update-check)";
                req.Accept = "application/json, text/plain, */*";
                try
                {
                    var uri = new Uri(url);
                    req.Referer = uri.GetLeftPart(UriPartial.Authority) + "/";
                }
                catch { }

                if (!string.IsNullOrEmpty(cookie))
                {
                    try
                    {
                        var jar = new CookieContainer();
                        int eq = cookie.IndexOf('=');
                        string name = eq > 0 ? cookie.Substring(0, eq) : "__test";
                        string value = eq >= 0 ? cookie.Substring(eq + 1) : cookie;
                        var uri = new Uri(url);
                        jar.Add(new Cookie(name, value, "/", uri.Host));
                        req.CookieContainer = jar;
                    }
                    catch
                    {
                        try { req.Headers[HttpRequestHeader.Cookie] = cookie; } catch { }
                    }
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK)
                    {
                        error = "更新服务器返回 HTTP " + (int)resp.StatusCode + "。";
                        return null;
                    }
                    using (var s = resp.GetResponseStream())
                    using (var sr = new StreamReader(s, Encoding.UTF8))
                    {
                        string text = sr.ReadToEnd();
                        if (text == null || text.Trim().Length == 0)
                        {
                            error = "更新服务器返回了空内容。";
                            return null;
                        }
                        return text;
                    }
                }
            }
            catch (WebException wex)
            {
                var hr = wex.Response as HttpWebResponse;
                if (hr != null)
                    error = "无法访问更新服务器（HTTP " + (int)hr.StatusCode + "）。";
                else if (wex.Status == WebExceptionStatus.Timeout)
                    error = "连接更新服务器超时，请稍后重试。";
                else if (wex.Status == WebExceptionStatus.NameResolutionFailure)
                    error = "无法解析更新服务器域名，请检查更新地址。";
                else
                    error = "无法访问更新服务器：" + wex.Message;
                return null;
            }
            catch (Exception ex)
            {
                error = "无法访问更新服务器：" + ex.Message;
                return null;
            }
        }

        // 是否为网站 JS 访客验证页（包含 toNumbers / slowAES 挑战脚本）
        static bool IsJsChallenge(string text)
        {
            if (text == null) return false;
            string t = text.TrimStart();
            return t.StartsWith("<", StringComparison.OrdinalIgnoreCase) &&
                   t.IndexOf("toNumbers", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   t.IndexOf("slowAES", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 解析验证页的 AES 挑战并生成 __test cookie。
        // 页面算法：__test = toHex( slowAES.decrypt(c, 2, a, b) )
        //          其中 mode=2 即 AES-128-CBC（key=a, iv=b, 无填充）。
        public static string SolveChallengeCookie(string html)
        {
            try
            {
                if (html == null) return null;
                var nums = new List<string>();
                const string marker = "toNumbers(\"";
                int pos = 0;
                while (nums.Count < 3 && (pos = html.IndexOf(marker, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int start = pos + marker.Length;
                    int end = html.IndexOf('"', start);
                    if (end < 0) break;
                    string hex = html.Substring(start, end - start).Trim();
                    if (hex.Length > 0 && hex.Length % 2 == 0) nums.Add(hex);
                    pos = end + 1;
                }
                if (nums.Count < 3) return null;

                byte[] a = HexToBytes(nums[0]);
                byte[] b = HexToBytes(nums[1]);
                byte[] c = HexToBytes(nums[2]);
                if (a == null || b == null || c == null) return null;

                byte[] plain = AesCbcDecryptNoPad(c, a, b);
                if (plain == null || plain.Length == 0) return null;
                return "__test=" + BytesToHex(plain);
            }
            catch { return null; }
        }

        static byte[] AesCbcDecryptNoPad(byte[] data, byte[] key, byte[] iv)
        {
            try
            {
                using (var aes = new AesCryptoServiceProvider())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.None;
                    aes.KeySize = key.Length * 8;
                    aes.Key = key;
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                        return dec.TransformFinalBlock(data, 0, data.Length);
                }
            }
            catch { return null; }
        }

        static byte[] HexToBytes(string hex)
        {
            if (hex == null || hex.Length == 0 || hex.Length % 2 != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = HexVal(hex[i * 2]);
                int lo = HexVal(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                bytes[i] = (byte)((hi << 4) | lo);
            }
            return bytes;
        }

        static int HexVal(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
        }

        static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static string WithChallengeQuery(string url)
        {
            if (url.IndexOf("?i=1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("&i=1", StringComparison.OrdinalIgnoreCase) >= 0)
                return url;
            return url + (url.IndexOf('?') >= 0 ? "&" : "?") + "i=1";
        }

        // 解析更新接口返回的 JSON（公开以便单元测试）。
        public static UpdateResult ParseJson(string json)
        {
            var res = new UpdateResult();
            try
            {
                if (json == null || json.Trim().Length == 0)
                {
                    res.ErrorMessage = "更新接口返回了空内容。";
                    return res;
                }
                if (json.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase))
                {
                    res.ErrorMessage = "更新接口返回的是 HTML 网页而不是 JSON。\n" +
                                       "请确认更新地址指向 update.json，并在网站后台关闭该路径的 JS 访客防护/防盗链。";
                    return res;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                res.LatestVersion = GetString(root, "version", "latestVersion", "Version");
                if (string.IsNullOrWhiteSpace(res.LatestVersion))
                {
                    res.ErrorMessage = "更新接口 JSON 缺少 version 字段。";
                    return res;
                }

                res.LatestBuild = GetInt(root, "build", "buildNumber", "versionCode", "Build");
                if (res.LatestBuild <= 0) res.LatestBuild = ExtractLastInt(res.LatestVersion);
                res.Channel = GetString(root, "channel", "Channel");
                res.DownloadUrl = NormalizeHttpUrl(GetString(root, "url", "downloadUrl", "download_url"));
                res.Notes = GetString(root, "notes", "note", "releaseNotes", "release_notes");
                res.ReleaseDate = GetString(root, "releaseDate", "release_date", "date");
                res.Mandatory = GetBool(root, "mandatory", "required");

                res.HasUpdate = res.LatestBuild > AppVersion.Build;
                res.Success = true;
                return res;
            }
            catch (Exception ex)
            {
                res.ErrorMessage = "解析更新信息失败：" + ex.Message;
                return res;
            }
        }

        static string GetString(JsonElement el, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    string s = v.GetString()?.Trim() ?? "";
                    if (s.Length > 0) return s;
                }
            }
            return "";
        }

        static int GetInt(JsonElement el, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number)
                {
                    if (v.TryGetInt32(out int n)) return n;
                }
                else if (el.TryGetProperty(k, out v) && v.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(v.GetString()?.Trim(), out int n)) return n;
                }
            }
            return 0;
        }

        static bool GetBool(JsonElement el, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True) return true;
                if (el.TryGetProperty(k, out v) && v.ValueKind == JsonValueKind.String)
                {
                    string s = v.GetString()?.Trim().ToLowerInvariant() ?? "";
                    if (s == "1" || s == "yes" || s == "true") return true;
                }
            }
            return false;
        }

        // 合并 http(s) 地址路径中的连续斜杠（https://a//b//c.zip -> https://a/b/c.zip），
        // 兼容一些服务器生成下载地址时出现的双斜杠。
        public static string NormalizeHttpUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0) return url;
            string prefix = url.Substring(0, scheme + 3);
            string rest = url.Substring(scheme + 3);
            int queryAt = rest.IndexOf('?');
            string path = queryAt >= 0 ? rest.Substring(0, queryAt) : rest;
            string query = queryAt >= 0 ? rest.Substring(queryAt) : "";

            var sb = new StringBuilder(prefix.Length + path.Length + query.Length);
            sb.Append(prefix);
            bool lastSlash = false;
            foreach (char ch in path)
            {
                if (ch == '/' && lastSlash) continue;
                lastSlash = ch == '/';
                sb.Append(ch);
            }
            sb.Append(query);
            return sb.ToString();
        }

        // 从 "26H2 Build 13" 这类字符串中提取最后一个整数（Build 号）。
        public static int ExtractLastInt(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int last = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsDigit(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                int n;
                if (int.TryParse(text.Substring(start, i - start), out n)) last = n;
            }
            return last;
        }
    }
}
