// ================================================================
// Tts.cs — 自然语音引擎（三层降级）
//   1) Edge 在线 TTS（微软神经语音 晓晓/云希/云扬）
//      - ClientWebSocket 直连 Bing 语音服务，输出 WAV(PCM)
//      - 该端点在国内网络常被 403 拒绝（实测），失败后自动跳过并记住
//   2) 百度翻译在线语音（gettts，免密钥，国内可用，实测通过）
//      - 输出 MP3，用 Media Foundation (MFPlay, Windows 10 内置) 播放
//   3) 本地 SAPI5 (System.Speech) 兜底，零延迟
//   所有网络与播放均在后台线程，带超时，缓存复用，低配设备流畅
// ================================================================
using System;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace LuckyPickerWpf
{
    // ---------------- 应用配置（%LOCALAPPDATA%\LuckyPicker\config.json） ----------------
    public static class AppConfig
    {
        public static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuckyPicker");

        public static string ConfigPath { get { return Path.Combine(Dir, "config.json"); } }

        public static bool TtsOnline = true;
        public static string TtsVoice = "zh-CN-XiaoxiaoNeural";
        public static string TtsSource = "auto"; // auto | azure | edge | baidu | off
        public static bool EdgeBlocked = false;  // Edge WebSocket 端点被拒（如国内网络）时持久化（仅 edge 直连模式使用）
        public static string UpdateUrl = "https://lr.yibianhui.cn/update.json"; // 检查更新接口地址
        public static bool BallVisible = true;  // 桌面悬浮球显示状态
        public static int BallX = -1;           // 悬浮球位置（-1 表示使用默认位置）
        public static int BallY = -1;
        public static int BallDiameter = 96;    // 悬浮球直径（可在设置菜单调整）
        public static string DefaultClassId = ""; // 默认班级（非空则启动不再询问班级选择）
        public static bool BallPinned = true;     // 悬浮球固定（勾选后抽取不自动隐藏）
        public static string Theme = "sage";      // 配色主题：sage | mist | ink
        public static string LastUpdateCheckDate = ""; // 静默检查更新的日期（yyyy-MM-dd，每日至多提醒一次）

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                var dict = doc.RootElement;
                if (dict.TryGetProperty("ttsOnline", out var j1) && j1.ValueKind == JsonValueKind.True) TtsOnline = true;
                if (dict.TryGetProperty("ttsOnline", out j1) && j1.ValueKind == JsonValueKind.False) TtsOnline = false;
                if (dict.TryGetProperty("ttsVoice", out var j2) && j2.ValueKind == JsonValueKind.String) { var t = j2.GetString(); if (!string.IsNullOrEmpty(t)) TtsVoice = t; }
                if (dict.TryGetProperty("ttsSource", out var j3) && j3.ValueKind == JsonValueKind.String) { var t = j3.GetString(); if (!string.IsNullOrEmpty(t)) TtsSource = t; }
                if (dict.TryGetProperty("edgeBlocked", out var j4) && j4.ValueKind == JsonValueKind.True) EdgeBlocked = true;
                if (dict.TryGetProperty("updateUrl", out var j5) && j5.ValueKind == JsonValueKind.String) { var t = j5.GetString(); if (!string.IsNullOrEmpty(t)) UpdateUrl = t; }
                if (dict.TryGetProperty("ballVisible", out var j6) && j6.ValueKind == JsonValueKind.False) BallVisible = false;
                if (dict.TryGetProperty("ballDiameter", out var j7b) && j7b.ValueKind == JsonValueKind.Number)
                { var v = j7b.GetInt32(); if (v >= 64 && v <= 160) BallDiameter = v; }
                if (dict.TryGetProperty("defaultClassId", out var j7c) && j7c.ValueKind == JsonValueKind.String) DefaultClassId = j7c.GetString() ?? "";
                if (dict.TryGetProperty("ballPinned", out var j7d) && j7d.ValueKind == JsonValueKind.False) BallPinned = false;
                if (dict.TryGetProperty("theme", out var j7t) && j7t.ValueKind == JsonValueKind.String) Theme = ThemeManager.Normalize(j7t.GetString());
                if (dict.TryGetProperty("ballX", out var j7) && j7.ValueKind == JsonValueKind.Number) BallX = j7.GetInt32();
                if (dict.TryGetProperty("ballY", out var j8) && j8.ValueKind == JsonValueKind.Number) BallY = j8.GetInt32();
                if (dict.TryGetProperty("lastUpdateCheckDate", out var j9) && j9.ValueKind == JsonValueKind.String) LastUpdateCheckDate = j9.GetString() ?? "";
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var obj = new
                {
                    ttsOnline = TtsOnline, ttsVoice = TtsVoice, ttsSource = TtsSource,
                    edgeBlocked = EdgeBlocked, updateUrl = UpdateUrl, ballVisible = BallVisible,
                    ballX = BallX, ballY = BallY, ballDiameter = BallDiameter,
                    defaultClassId = DefaultClassId, ballPinned = BallPinned, theme = Theme,
                    lastUpdateCheckDate = LastUpdateCheckDate
                };
                JsonStorage.WriteAtomic(ConfigPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    // ---------------- Edge 在线 TTS 客户端 ----------------
    public static class EdgeTts
    {
        public const string DefaultVoice = "zh-CN-XiaoxiaoNeural";
        const string TrustedToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
        const string SecMsGecVersion = "1-130.0.2849.68";
        const string WsHost = "speech.platform.bing.com";
        const string OutputFormat = "riff-24khz-16bit-mono-pcm";
        public const int SampleRate = 24000;

        static EdgeTts()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public static string BuildUrl()
        {
            long ticks = DateTime.UtcNow.ToFileTime();
            string gec = "ticksTo100NanosecondTicks(" + ticks + ")";
            string conn = Guid.NewGuid().ToString("N");
            return "wss://" + WsHost + "/consumer/speech/synthesize/readaloud/edge/v1" +
                   "?TrustedClientToken=" + TrustedToken +
                   "&Sec-MS-GEC=" + Uri.EscapeDataString(gec) +
                   "&Sec-MS-GEC-Version=" + SecMsGecVersion +
                   "&ConnectionId=" + conn;
        }

        public static string BuildSsml(string text, string voice)
        {
            if (string.IsNullOrEmpty(voice)) voice = DefaultVoice;
            string esc = System.Security.SecurityElement.Escape(text ?? "");
            return "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='zh-CN'>" +
                   "<voice name='" + voice + "'><prosody pitch='+0Hz' rate='+0%' volume='+0%'>" +
                   esc + "</prosody></voice></speak>";
        }

        static string SpeechConfigMsg()
        {
            string date = DateTime.UtcNow.ToString(
                "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
                CultureInfo.InvariantCulture);
            string cfg = "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"true\"},\"outputFormat\":\"" + OutputFormat + "\"}}}}";
            return "X-Timestamp:" + date + "\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" + cfg;
        }

        static string SsmlMsg(string ssml)
        {
            return "X-RequestId:" + Guid.NewGuid().ToString("N") +
                   "\r\nContent-Type:application/json; charset=utf-8\r\nPath:ssml\r\n\r\n" + ssml;
        }

        // 合成一段语音，返回完整 WAV 字节；任何失败返回 null（含连接超时/403 拒绝）。
        public static byte[] Synthesize(string text, string voice, int connectTimeoutMs, int totalTimeoutMs)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                using (var ws = new ClientWebSocket())
                {
                    try { ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold"); } catch { }
                    try { ws.Options.SetRequestHeader("Pragma", "no-cache"); } catch { }
                    try { ws.Options.SetRequestHeader("Cache-Control", "no-cache"); } catch { }
                    try { ws.Options.SetRequestHeader("Accept-Language", "zh-CN,zh;q=0.9"); } catch { }
                    try { ws.Options.AddSubProtocol("chat"); } catch { }
                    try { ws.Options.AddSubProtocol("supergroup"); } catch { }
                    try { ws.Options.AddSubProtocol("binary"); } catch { }

                    using (var cts = new CancellationTokenSource(totalTimeoutMs))
                    using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                    {
                        connectCts.CancelAfter(connectTimeoutMs);
                        try { ws.ConnectAsync(new Uri(BuildUrl()), connectCts.Token).Wait(); }
                        catch { return null; }

                        try
                        {
                            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(SpeechConfigMsg())),
                                WebSocketMessageType.Text, true, cts.Token).Wait();
                            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(SsmlMsg(BuildSsml(text, voice)))),
                                WebSocketMessageType.Text, true, cts.Token).Wait();
                        }
                        catch { return null; }

                        var chunks = new List<byte[]>();
                        var buffer = new byte[65536];
                        while (true)
                        {
                            WebSocketReceiveResult res;
                            try { res = ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).Result; }
                            catch { break; }
                            if (res.MessageType == WebSocketMessageType.Close) break;
                            if (res.MessageType == WebSocketMessageType.Text) continue;
                            if (res.Count <= 0) continue;

                            byte[] frame = new byte[res.Count];
                            Array.Copy(buffer, frame, res.Count);
                            if (!res.EndOfMessage)
                            {
                                using (var ms = new MemoryStream())
                                {
                                    ms.Write(frame, 0, frame.Length);
                                    while (!res.EndOfMessage)
                                    {
                                        res = ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).Result;
                                        ms.Write(buffer, 0, res.Count);
                                    }
                                    frame = ms.ToArray();
                                }
                            }

                            string header = "";
                            int pStart = -1;
                            if (frame.Length >= 2)
                            {
                                int hlen = (frame[0] << 8) | frame[1];
                                if (2 + hlen <= frame.Length)
                                {
                                    header = Encoding.ASCII.GetString(frame, 2, hlen);
                                    pStart = 2 + hlen;
                                }
                            }
                            if (header.IndexOf("Path:turn.end", StringComparison.OrdinalIgnoreCase) >= 0) break;
                            if (header.IndexOf("Path:audio", StringComparison.OrdinalIgnoreCase) >= 0
                                && pStart >= 0 && pStart + 2 <= frame.Length)
                            {
                                int plen = (frame[pStart] << 8) | frame[pStart + 1];
                                if (pStart + 2 + plen > frame.Length) plen = frame.Length - pStart - 2;
                                if (plen > 0)
                                {
                                    var payload = new byte[plen];
                                    Array.Copy(frame, pStart + 2, payload, 0, plen);
                                    chunks.Add(payload);
                                }
                            }
                        }
                        return AssembleWav(chunks);
                    }
                }
            }
            catch { return null; }
        }

        public static byte[] AssembleWav(List<byte[]> chunks)
        {
            if (chunks == null || chunks.Count == 0) return null;
            int rate = SampleRate, bits = 16, channels = 1;
            var pcm = new List<byte>();
            for (int i = 0; i < chunks.Count; i++)
            {
                byte[] c = chunks[i];
                if (c == null || c.Length < 4) continue;
                bool riff = c.Length >= 44 && c[0] == (byte)'R' && c[1] == (byte)'I' && c[2] == (byte)'F' && c[3] == (byte)'F'
                            && c[8] == (byte)'W' && c[9] == (byte)'A' && c[10] == (byte)'V' && c[11] == (byte)'E';
                if (riff)
                {
                    int dataOff = FindDataOffset(c);
                    if (dataOff < 0) continue;
                    if (i == 0 && c.Length >= 36)
                    {
                        channels = ReadU16(c, 22);
                        rate = (int)ReadU32(c, 24);
                        bits = ReadU16(c, 34);
                    }
                    int payloadLen = (int)ReadU32(c, dataOff + 4);
                    int avail = c.Length - dataOff - 8;
                    if (payloadLen > avail) payloadLen = avail;
                    for (int k = 0; k < payloadLen; k++) pcm.Add(c[dataOff + 8 + k]);
                }
                else
                {
                    for (int k = 0; k < c.Length; k++) pcm.Add(c[k]);
                }
            }
            if (pcm.Count == 0) return null;
            if (rate <= 0) rate = SampleRate;
            if (bits <= 0) bits = 16;
            if (channels <= 0) channels = 1;
            int blockAlign = channels * bits / 8;
            int byteRate = rate * blockAlign;
            var wav = new byte[44 + pcm.Count];
            WriteAscii(wav, 0, "RIFF"); WriteU32(wav, 4, 36 + pcm.Count); WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt "); WriteU32(wav, 16, 16); WriteU16(wav, 20, 1);
            WriteU16(wav, 22, channels); WriteU32(wav, 24, rate); WriteU32(wav, 28, byteRate);
            WriteU16(wav, 32, blockAlign); WriteU16(wav, 34, bits);
            WriteAscii(wav, 36, "data"); WriteU32(wav, 40, pcm.Count);
            pcm.CopyTo(wav, 44);
            return wav;
        }

        static int FindDataOffset(byte[] c)
        {
            for (int i = 12; i + 4 <= c.Length; i++)
                if (c[i] == (byte)'d' && c[i + 1] == (byte)'a' && c[i + 2] == (byte)'t' && c[i + 3] == (byte)'a') return i;
            return -1;
        }

        static void WriteAscii(byte[] b, int off, string s)
        {
            for (int i = 0; i < s.Length; i++) b[off + i] = (byte)s[i];
        }
        static void WriteU16(byte[] b, int off, int v) { b[off] = (byte)(v & 0xFF); b[off + 1] = (byte)((v >> 8) & 0xFF); }
        static void WriteU32(byte[] b, int off, int v) { WriteU16(b, off, v & 0xFFFF); WriteU16(b, off + 2, (v >> 16) & 0xFFFF); }
        static ushort ReadU16(byte[] b, int off) { return (ushort)(b[off] | (b[off + 1] << 8)); }
        static uint ReadU32(byte[] b, int off) { return (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24)); }
    }

    // ---------------- 微软神经语音（Azure Speech REST，内置直连） ----------------
    // 通过微软翻译应用令牌接口（dev.microsofttranslator.com）免费换取 Azure Speech 令牌，
    // 再调用 {region}.tts.speech.microsoft.com 的 REST 合成接口 —— 与 Edge TTS 同源的
    // 神经语音（晓晓/云希/云扬等），本机直连可用（不受 Edge WebSocket 端点封锁影响）。
    public static class MsTts
    {
        const string KeyB64 = "oik6PdDdMnOXemTbwvMn9de/h9lFnfBaCWbGMMZqqoSaQaqUOqjVGm5NqsmjcBI1x+sS9ugjB55HEJWRiFXYFw==";
        const string EndpointUrl = "https://dev.microsofttranslator.com/apps/endpoint?api-version=1.0";
        static readonly object sync = new object();
        static string region;
        static string token;
        static long tokenExpiryUnix;

        static MsTts()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        static string DateFormat()
        {
            return DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture).ToLowerInvariant();
        }

        // 与微软翻译 App 相同的签名算法（HMAC-SHA256）
        static string BuildSignature(string urlStr, out string date, out string uid)
        {
            string encodedUrl = Uri.EscapeDataString(urlStr.Substring(urlStr.IndexOf("://") + 3));
            uid = Guid.NewGuid().ToString("N");
            date = DateFormat();
            string bytesToSign = ("MSTranslatorAndroidApp" + encodedUrl + date + uid).ToLowerInvariant();
            byte[] key = Convert.FromBase64String(KeyB64);
            byte[] sig;
            using (var h = new HMACSHA256(key)) sig = h.ComputeHash(Encoding.UTF8.GetBytes(bytesToSign));
            return "MSTranslatorAndroidApp::" + Convert.ToBase64String(sig) + "::" + date + "::" + uid;
        }

        // 获取 { region, token }，带内存缓存（过期前 3 分钟刷新）
        static bool GetEndpoint()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lock (sync)
            {
                if (token != null && now < tokenExpiryUnix - 180) return true;
                try
                {
                    string date, uid;
                    string sig = BuildSignature(EndpointUrl, out date, out uid);
                    var req = (HttpWebRequest)WebRequest.Create(EndpointUrl);
                    req.Method = "POST";
                    req.Timeout = 8000;
                    req.ReadWriteTimeout = 8000;
                    req.ContentLength = 0;
                    req.ContentType = "application/json; charset=utf-8";
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36 Edg/127.0.0.0";
                    req.Headers["Accept-Language"] = "zh-Hans";
                    req.Headers["X-ClientVersion"] = "4.0.530a 5fe1dc6c";
                    req.Headers["X-UserId"] = "0f04d16a175c411e";
                    req.Headers["X-HomeGeographicRegion"] = "zh-Hans-CN";
                    req.Headers["X-ClientTraceId"] = uid;
                    req.Headers["X-MT-Signature"] = sig;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    using (var sr = new StreamReader(s, Encoding.UTF8))
                    {
                        string json = sr.ReadToEnd();
                        string r = null, t = null;
                        using (var jdoc = JsonDocument.Parse(json))
                        {
                            var root = jdoc.RootElement;
                            if (root.TryGetProperty("r", out var jr) && jr.ValueKind == JsonValueKind.String) r = jr.GetString();
                            if (root.TryGetProperty("t", out var jt) && jt.ValueKind == JsonValueKind.String) t = jt.GetString();
                        }
                        if (string.IsNullOrEmpty(r) || string.IsNullOrEmpty(t)) return false;
                        region = r;
                        token = t;
                        tokenExpiryUnix = ParseJwtExp(t);
                        return true;
                    }
                }
                catch { return token != null; }
            }
        }

        static long ParseJwtExp(string jwt)
        {
            try
            {
                string[] parts = jwt.Split('.');
                if (parts.Length < 2) return 0;
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                while (payload.Length % 4 != 0) payload += "=";
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                try
                {
                    using var jdoc = JsonDocument.Parse(json);
                    if (jdoc.RootElement.TryGetProperty("exp", out var je) && je.ValueKind == JsonValueKind.Number)
                    {
                        if (je.TryGetInt64(out long e)) return e;
                    }
                }
                catch { }
            }
            catch { }
            return 0;
        }

        static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        static string BuildSsml(string text, string voice)
        {
            return "<speak xmlns=\"http://www.w3.org/2001/10/synthesis\" xmlns:mstts=\"http://www.w3.org/2001/mstts\" version=\"1.0\" xml:lang=\"zh-CN\"> " +
                   "<voice name=\"" + voice + "\"> <mstts:express-as style=\"general\" styledegree=\"2.0\" role=\"default\"> " +
                   "<prosody rate=\"+0%\" pitch=\"+0Hz\" volume=\"+0%\">" + EscapeXml(text) + "</prosody> </mstts:express-as> </voice> </speak>";
        }

        // 合成一段语音，返回 WAV(PCM) 字节；失败返回 null（429/5xx 自动重试，最多3次）
        public static byte[] Synthesize(string text, string voice)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (!GetEndpoint()) return null;
            string url = "https://" + region + ".tts.speech.microsoft.com/cognitiveservices/v1";
            for (int attempt = 0; attempt <= 3; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "POST";
                    req.Timeout = 10000;
                    req.ReadWriteTimeout = 10000;
                    req.ContentType = "application/ssml+xml";
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36 Edg/127.0.0.0";
                    req.Headers["Authorization"] = token;
                    req.Headers["X-Microsoft-OutputFormat"] = "riff-24khz-16bit-mono-pcm";
                    byte[] body = Encoding.UTF8.GetBytes(BuildSsml(text, voice));
                    req.ContentLength = body.Length;
                    using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        byte[] data = ms.ToArray();
                        bool looksWav = data.Length > 1000 && data[0] == (byte)'R' && data[1] == (byte)'I'
                            && data[2] == (byte)'F' && data[3] == (byte)'F'
                            && data[8] == (byte)'W' && data[9] == (byte)'A'
                            && data[10] == (byte)'V' && data[11] == (byte)'E';
                        if (resp.StatusCode == HttpStatusCode.OK && looksWav)
                            return data;
                        int code = (int)resp.StatusCode;
                        if ((code == 429 || code >= 500) && attempt < 3)
                        {
                            Thread.Sleep(500 * (attempt + 1));
                            continue;
                        }
                        return null;
                    }
                }
                catch (WebException wex)
                {
                    HttpWebResponse hr = wex.Response as HttpWebResponse;
                    int code = hr != null ? (int)hr.StatusCode : 0;
                    if ((code == 429 || code >= 500) && attempt < 3)
                    {
                        Thread.Sleep(500 * (attempt + 1));
                        continue;
                    }
                    return null;
                }
                catch { return null; }
            }
            return null;
        }
    }

    // ---------------- 百度翻译在线 TTS（免密钥，国内网络可用） ----------------
    public static class BaiduTts
    {
        static BaiduTts()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        // 返回 MP3 字节；失败返回 null
        public static byte[] Synthesize(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string url = "https://fanyi.baidu.com/gettts?lan=zh&text=" + Uri.EscapeDataString(text) + "&spd=5&source=web";
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.Referer = "https://fanyi.baidu.com/";
                req.Accept = "*/*";
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    using (var s = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        byte[] data = ms.ToArray();
                        bool looksMp3 = data.Length > 1000 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0;
                        if (resp.StatusCode == HttpStatusCode.OK && looksMp3)
                            return data;
                    }
                }
            }
            catch { }
            return null;
        }
    }

    // ---------------- Media Foundation 播放器（MP3，Windows 10 内置，无需解码器） ----------------
    public static class MfPlayer
    {
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A714590A-58AF-430a-85BF-44F5EC838D85")]
        interface IMFPMediaPlayer
        {
            [PreserveSig] int Play();
            [PreserveSig] int Pause();
            [PreserveSig] int Stop();
            [PreserveSig] int FrameStep();
            [PreserveSig] int SetPosition(Guid guidPositionType, IntPtr pvPosition);
            [PreserveSig] int GetPosition(Guid guidPositionType, IntPtr pvPosition);
            [PreserveSig] int GetDuration(Guid guidPositionType, IntPtr pvDuration);
            [PreserveSig] int SetRate([MarshalAs(UnmanagedType.Bool)] bool fIsForward, float flRate);
            [PreserveSig] int GetRate([MarshalAs(UnmanagedType.Bool)] out bool pfIsForward, out float pflRate);
            [PreserveSig] int GetSupportedRates([MarshalAs(UnmanagedType.Bool)] bool fForwardDirection, out float pflSlowestRate, out float pflFastestRate);
            [PreserveSig] int GetState(out int pState);
            [PreserveSig] int CreateMediaItemFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszURL, [MarshalAs(UnmanagedType.Bool)] bool fSync, out IntPtr ppIMediaItem);
            [PreserveSig] int CreateMediaItemFromObject(IntPtr pIUnknownObj, [MarshalAs(UnmanagedType.Bool)] bool fSync, out IntPtr ppIMediaItem);
            [PreserveSig] int SetMediaItem(IntPtr pIMediaItem);
            [PreserveSig] int ClearMediaItem();
            [PreserveSig] int GetMediaItem(out IntPtr ppIMediaItem);
            [PreserveSig] int GetVolume(out float pflVolume);
            [PreserveSig] int SetVolume(float flVolume);
            [PreserveSig] int GetBalance(out float pflBalance);
            [PreserveSig] int SetBalance(float flBalance);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pfMute);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool fMute);
            [PreserveSig] int GetNativeVideoSize(out long pszVideo, out long pszARVideo);
            [PreserveSig] int GetIdealVideoSize(out long pszVideo, out long pszARVideo);
            [PreserveSig] int SetVideoSourceRect(IntPtr pnrcSource);
            [PreserveSig] int GetVideoSourceRect(IntPtr pnrcSource);
            [PreserveSig] int SetAspectRatioMode(int dwAspectRatioMode);
            [PreserveSig] int GetAspectRatioMode(out int pdwAspectRatioMode);
            [PreserveSig] int GetVideoWindow(out IntPtr phwndVideo);
            [PreserveSig] int UpdateVideo();
            [PreserveSig] int SetBorderColor(int clr);
            [PreserveSig] int GetBorderColor(out int pclr);
            [PreserveSig] int InsertEffect(int dwFlags, IntPtr pEffect);
            [PreserveSig] int RemoveEffect(IntPtr pEffect);
            [PreserveSig] int RemoveAllEffects();
            [PreserveSig] int Shutdown();
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("766C8FFB-5FDB-4fea-A28D-B912996F51BD")]
        interface IMFPMediaPlayerCallback
        {
            void OnMediaPlayerEvent(IntPtr pEventHeader);
        }

        class Callback : IMFPMediaPlayerCallback
        {
            public void OnMediaPlayerEvent(IntPtr pEventHeader) { }
        }

        [DllImport("mfplay.dll", CharSet = CharSet.Unicode)]
        static extern int MFPCreateMediaPlayer(string pwszURL, [MarshalAs(UnmanagedType.Bool)] bool fStartPlayback,
            int creationOptions, IMFPMediaPlayerCallback pCallback, IntPtr hWnd, out IMFPMediaPlayer ppMediaPlayer);

        // 同步播放一个 MP3 文件直到结束或超时；成功返回 true
        public static bool Play(string file, int timeoutMs)
        {
            try
            {
                if (file == null || !File.Exists(file)) return false;
                var cb = new Callback();
                IMFPMediaPlayer player;
                int hr = MFPCreateMediaPlayer(file, false, 0x1, cb, IntPtr.Zero, out player);
                if (hr != 0) return false;
                try
                {
                    hr = player.Play();
                    if (hr != 0) return false;
                    DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < end)
                    {
                        int st;
                        if (player.GetState(out st) != 0) break;
                        if (st == 1 || st == 4) break; // STOPPED / SHUTDOWN
                        Thread.Sleep(50);
                    }
                    return true;
                }
                finally
                {
                    try { player.Shutdown(); } catch { }
                }
            }
            catch { return false; }
        }
    }

    static class AudioUnmute
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, uint classContext, IntPtr activationParams, out IntPtr endpoint);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr notify);
            int UnregisterControlChangeNotify(IntPtr notify);
            int GetChannelCount(out uint channelCount);
            int SetMasterVolumeLevel(float level, IntPtr eventContext);
            int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
            int GetMasterVolumeLevel(out float level);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channel, float level, IntPtr eventContext);
            int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
            int GetChannelVolumeLevel(uint channel, out float level);
            int GetChannelVolumeLevelScalar(uint channel, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            int GetVolumeStepInfo(out uint step, out uint stepCount);
            int VolumeStepUp(IntPtr eventContext);
            int VolumeStepDown(IntPtr eventContext);
            int QueryHardwareSupport(out uint hardwareSupportMask);
            int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
        }

        public static string LastError { get; private set; } = "";

        public static bool DefaultOutput()
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            IAudioEndpointVolume? volume = null;
            IntPtr volumePtr = IntPtr.Zero;
            LastError = "";
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                int hr = -1;
                for (int role = 0; role <= 2; role++)
                {
                    hr = enumerator.GetDefaultAudioEndpoint(0, role, out device);
                    if (hr == 0) break;
                }
                if (hr != 0 || device == null) { LastError = "GetDefaultAudioEndpoint=0x" + hr.ToString("X8"); return false; }
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out volumePtr);
                if (hr != 0 || volumePtr == IntPtr.Zero) { LastError = "Activate=0x" + hr.ToString("X8"); return false; }
                volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(volumePtr);
                hr = volume.SetMute(false, IntPtr.Zero);
                if (hr != 0) { LastError = "SetMute=0x" + hr.ToString("X8"); return false; }
                return true;
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return false; }
            finally
            {
                if (volume != null) Marshal.FinalReleaseComObject(volume);
                if (volumePtr != IntPtr.Zero) Marshal.Release(volumePtr);
                if (device != null) Marshal.FinalReleaseComObject(device);
                if (enumerator != null) Marshal.FinalReleaseComObject(enumerator);
            }
        }
    }

    // ---------------- WAV(PCM) 首尾静音裁剪与拼接 ----------------
    internal readonly record struct WavFormat(int SampleRate, int Channels, int Bits);

    internal static class WavAudio
    {
        public const int HeadPadMs = 30;
        public const int TailPadMs = 90;

        public static bool TryReadPcm(byte[] raw, out byte[] pcm, out WavFormat format)
        {
            pcm = Array.Empty<byte>();
            format = default;
            if (raw == null || raw.Length < 44) return false;
            if (raw[0] != 'R' || raw[1] != 'I' || raw[2] != 'F' || raw[3] != 'F'
                || raw[8] != 'W' || raw[9] != 'A' || raw[10] != 'V' || raw[11] != 'E') return false;

            int fmtOff = -1, dataOff = -1, dataLen = 0;
            int p = 12;
            while (p + 8 <= raw.Length)
            {
                int len = ReadI32(raw, p + 4);
                if (len < 0) return false;
                int body = p + 8;
                if (raw[p] == 'f' && raw[p + 1] == 'm' && raw[p + 2] == 't' && raw[p + 3] == ' ' && len >= 16)
                    fmtOff = body;
                else if (raw[p] == 'd' && raw[p + 1] == 'a' && raw[p + 2] == 't' && raw[p + 3] == 'a')
                {
                    dataOff = body;
                    dataLen = Math.Min(len, raw.Length - body);
                    break;
                }
                p = body + len + (len & 1);
            }
            if (fmtOff < 0 || fmtOff + 16 > raw.Length || dataOff < 0 || dataLen <= 0) return false;

            int tag = ReadU16(raw, fmtOff);
            int channels = ReadU16(raw, fmtOff + 2);
            int rate = ReadI32(raw, fmtOff + 4);
            int bits = ReadU16(raw, fmtOff + 14);
            if (tag != 1 || channels < 1 || channels > 2 || rate <= 0 || bits != 16) return false;

            int frame = channels * 2;
            dataLen -= dataLen % frame;
            if (dataLen <= 0) return false;
            pcm = new byte[dataLen];
            Buffer.BlockCopy(raw, dataOff, pcm, 0, dataLen);
            format = new WavFormat(rate, channels, bits);
            return true;
        }

        public static byte[] TrimSilence(byte[] pcm, WavFormat format)
        {
            int frame = format.Channels * 2;
            if (pcm == null || pcm.Length < frame * 4) return pcm ?? Array.Empty<byte>();
            int count = pcm.Length / frame;

            int peak = 0;
            for (int i = 0; i < pcm.Length; i += 2)
            {
                int v = Math.Abs((int)BitConverter.ToInt16(pcm, i));
                if (v > peak) peak = v;
            }
            if (peak == 0) return Array.Empty<byte>();
            int threshold = Math.Max(260, peak * 3 / 100);

            int first = -1, last = -1;
            for (int f = 0; f < count; f++)
                if (Level(pcm, f * frame, format.Channels) >= threshold) { first = f; break; }
            if (first < 0) return Array.Empty<byte>();
            for (int f = count - 1; f >= first; f--)
                if (Level(pcm, f * frame, format.Channels) >= threshold) { last = f; break; }
            if (last < first) return Array.Empty<byte>();

            int head = format.SampleRate * HeadPadMs / 1000;
            int tail = format.SampleRate * TailPadMs / 1000;
            first = Math.Max(0, first - head);
            last = Math.Min(count - 1, last + tail);
            int from = first * frame;
            int len = (last - first + 1) * frame;
            var trimmed = new byte[len];
            Buffer.BlockCopy(pcm, from, trimmed, 0, len);
            return trimmed;
        }

        static int Level(byte[] pcm, int offset, int channels)
        {
            int level = 0;
            for (int c = 0; c < channels; c++)
            {
                int v = Math.Abs((int)BitConverter.ToInt16(pcm, offset + c * 2));
                if (v > level) level = v;
            }
            return level;
        }

        public static byte[] BuildWav(IReadOnlyList<byte[]> parts, WavFormat format)
        {
            int total = 0;
            if (parts != null)
                foreach (var part in parts)
                    if (part != null) total += part.Length;
            int frame = format.Channels * 2;
            total -= total % frame;
            if (total <= 0) return Array.Empty<byte>();

            var wav = new byte[44 + total];
            WriteAscii(wav, 0, "RIFF"); WriteI32(wav, 4, 36 + total); WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt "); WriteI32(wav, 16, 16);
            WriteU16(wav, 20, 1); WriteU16(wav, 22, format.Channels);
            WriteI32(wav, 24, format.SampleRate); WriteI32(wav, 28, format.SampleRate * frame);
            WriteU16(wav, 32, frame); WriteU16(wav, 34, format.Bits);
            WriteAscii(wav, 36, "data"); WriteI32(wav, 40, total);

            int offset = 44;
            foreach (var part in parts)
            {
                if (part == null) continue;
                int n = Math.Min(part.Length, wav.Length - offset);
                if (n <= 0) break;
                Buffer.BlockCopy(part, 0, wav, offset, n);
                offset += n;
            }
            return wav;
        }

        public static bool SelfTest(out string detail)
        {
            int rate = 24000;
            var format = new WavFormat(rate, 1, 16);
            byte[] raw1 = MakeToneWav(rate, 200, 300, 500, format);
            byte[] raw2 = MakeToneWav(rate, 200, 450, 500, format);
            if (!TryReadPcm(raw1, out byte[] pcm1, out WavFormat f1)
                || !TryReadPcm(raw2, out byte[] pcm2, out WavFormat f2)
                || f1 != format || f2 != format)
            {
                detail = "WAV 解析失败";
                return false;
            }
            byte[] t1 = TrimSilence(pcm1, f1);
            byte[] t2 = TrimSilence(pcm2, f2);
            int expected1 = (300 + HeadPadMs + TailPadMs) * rate / 1000 * 2;
            bool trimOk = Math.Abs(t1.Length - expected1) <= rate / 1000 * 2 * 15;
            byte[] merged = BuildWav(new[] { t1, t2 }, f1);
            bool mergeOk = TryReadPcm(merged, out byte[] mp, out WavFormat mf)
                && mf == f1 && mp.Length == t1.Length + t2.Length;
            detail = $"原 {pcm1.Length / 2.0 / rate:F2}s → 裁剪 {t1.Length / 2.0 / rate:F2}s，两段拼接 {mp.Length / 2.0 / rate:F2}s";
            return trimOk && mergeOk;
        }

        static byte[] MakeToneWav(int rate, int leadMs, int toneMs, int tailMs, WavFormat format)
        {
            int frames = rate * (leadMs + toneMs + tailMs) / 1000;
            var pcm = new byte[frames * 2];
            int from = rate * leadMs / 1000;
            int to = rate * (leadMs + toneMs) / 1000;
            for (int i = from; i < to; i++)
            {
                short s = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return BuildWav(new[] { pcm }, format);
        }

        static void WriteAscii(byte[] b, int off, string s)
        {
            for (int i = 0; i < s.Length; i++) b[off + i] = (byte)s[i];
        }
        static void WriteU16(byte[] b, int off, int v) { b[off] = (byte)(v & 0xFF); b[off + 1] = (byte)((v >> 8) & 0xFF); }
        static void WriteI32(byte[] b, int off, int v) { WriteU16(b, off, v & 0xFFFF); WriteU16(b, off + 2, (v >> 16) & 0xFFFF); }
        static ushort ReadU16(byte[] b, int off) { return (ushort)(b[off] | (b[off + 1] << 8)); }
        static int ReadI32(byte[] b, int off) { return b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24); }
    }

    // ---------------- 语音引擎（在线优先 + 缓存 + 本地降级） ----------------
    public class TtsEngine : IDisposable
    {
        Action<string> onStatus;
        System.Speech.Synthesis.SpeechSynthesizer sapi;
        volatile bool onlineOk = true;
        volatile bool edgeBlocked;
        volatile bool stopped;
        volatile int gen;
        ConcurrentQueue<string> warmQueue = new ConcurrentQueue<string>();
        bool warmThreadStarted;
        readonly object warmLock = new object();

        public TtsEngine(Action<string> onStatus)
        {
            this.onStatus = onStatus;
            onlineOk = AppConfig.TtsOnline;
            edgeBlocked = AppConfig.EdgeBlocked;
        }

        public string InitStatus()
        {
            bool zh = false;
            try
            {
                sapi = new System.Speech.Synthesis.SpeechSynthesizer();
                foreach (var v in sapi.GetInstalledVoices())
                {
                    string c = v.VoiceInfo.Culture != null ? v.VoiceInfo.Culture.Name : "";
                    if (c.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    {
                        try { sapi.SelectVoice(v.VoiceInfo.Name); zh = true; } catch { }
                        break;
                    }
                }
                sapi.Rate = 1;
            }
            catch { sapi = null; }
            string local = sapi == null ? "本地语音不可用" : (zh ? "本地中文语音备用就绪" : "本地语音就绪（无中文音色）");
            string online;
            if (AppConfig.TtsSource == "edge") online = "Edge 直连语音";
            else if (AppConfig.TtsSource == "baidu") online = "百度在线语音";
            else if (AppConfig.TtsSource == "azure") online = "微软神经语音";
            else if (AppConfig.TtsSource == "off") online = "仅本地语音";
            else online = "微软神经语音（自动→百度）";
            return "♪ 语音：" + (AppConfig.TtsOnline ? online + "优先 · " : "本地语音模式 · ") + local;
        }

        public void SetOnline(bool on)
        {
            onlineOk = on;
            SetStatus(on ? "♪ 在线语音已开启" : "♪ 本地语音模式");
        }

        // 当前生效的在线源标签：azure / edge / baidu / off
        string EffectiveSource()
        {
            string src = AppConfig.TtsSource;
            if (src == "baidu") return "baidu";
            if (src == "off") return "off";
            if (src == "azure") return "azure";
            if (src == "edge") return edgeBlocked ? "off" : "edge";
            return "azure"; // auto：微软神经语音（Azure REST 直连）优先，失败回退百度
        }

        /// <summary>先准备全部音频再连续播放；WAV 裁掉首尾静音后拼成单段，避免逐条切换播放器的间隔。</summary>
        public void SpeakSequence(IEnumerable<string> texts)
        {
            var list = texts?.Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (list == null || list.Count == 0) return;
            int g = ++gen;
            Task.Run(() =>
            {
                var clips = new List<(string Text, string Tag, string File)>();
                bool allReady = true;
                foreach (var text in list)
                {
                    if (g != gen || stopped) return;
                    try
                    {
                        bool ready = EnsureCached(g, text, out string tag, out string file);
                        if (!ready) allReady = false;
                        clips.Add((text, ready ? tag : "", ready ? file : ""));
                    }
                    catch { allReady = false; clips.Add((text, "", "")); }
                }
                if (g != gen || stopped) return;

                // 有片段缺失时整句交给本地语音，避免在线片段之间夹入逐条播放的停顿
                if (!allReady && sapi != null) { SapiSequenceFallback(g, list); return; }

                string merged = TryBuildWavSequence(clips) ?? TryBuildMp3Sequence(clips);
                if (merged != null && g == gen && !stopped)
                {
                    SetStatus(PlaybackStatus(clips[0].Tag));
                    if (PlayFile(merged)) return;
                }

                foreach (var clip in clips)
                {
                    if (g != gen || stopped) return;
                    PlayPrepared(g, clip.Text, clip.Tag, clip.File, true);
                }
            });
        }

        /// <summary>把缓存里的同格式 WAV(PCM) 片段裁剪首尾静音后拼成单段；不可用时返回 null。</summary>
        string? TryBuildWavSequence(List<(string Text, string Tag, string File)> clips)
        {
            if (clips.Count < 2) return null;
            string tag = clips[0].Tag;
            if (tag != "edge" && tag != "azure") return null;

            WavFormat? common = null;
            var parts = new List<byte[]>(clips.Count);
            var fingerprint = new StringBuilder("wav2|").Append(tag).Append('|').Append(AppConfig.TtsVoice)
                .Append('|').Append(WavAudio.HeadPadMs).Append(':').Append(WavAudio.TailPadMs);
            foreach (var clip in clips)
            {
                if (clip.Tag != tag || clip.File.Length == 0) return null;
                try
                {
                    var info = new FileInfo(clip.File);
                    if (!info.Exists) return null;
                    byte[] raw = File.ReadAllBytes(clip.File);
                    if (!WavAudio.TryReadPcm(raw, out byte[] pcm, out WavFormat format)) return null;
                    if (common.HasValue && common.Value != format) return null;
                    common = format;
                    byte[] trimmed = WavAudio.TrimSilence(pcm, format);
                    if (trimmed.Length == 0) return null;
                    parts.Add(trimmed);
                    fingerprint.Append('|').Append(clip.Text).Append(':')
                        .Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
                }
                catch { return null; }
            }
            if (!common.HasValue) return null;

            byte[] wav = WavAudio.BuildWav(parts, common.Value);
            if (wav.Length <= 44) return null;
            string file = Path.Combine(CacheDir, "seqw-" + Md5Hex(fingerprint.ToString()) + ".wav");
            try
            {
                if (File.Exists(file) && new FileInfo(file).Length == wav.Length) return file;
                Directory.CreateDirectory(CacheDir);
                string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, wav);
                if (File.Exists(file)) File.Delete(file);
                File.Move(temp, file);
                return file;
            }
            catch { return null; }
        }

        static string PlaybackStatus(string tag)
        {
            if (tag == "azure") return "♪ 微软神经语音（" + VoiceDisplay() + "）";
            if (tag == "edge") return "♪ Edge 直连语音（" + VoiceDisplay() + "）";
            return "♪ 百度在线语音";
        }

        /// <summary>把缓存里的同格式 MP3 片段拼成单段；音源/采样格式不一致时返回 null，回退逐条播放。</summary>
        string? TryBuildMp3Sequence(List<(string Text, string Tag, string File)> clips)
        {
            if (clips.Count < 2) return null;
            string tag = clips[0].Tag;
            if (tag.Length == 0 || tag == "edge" || tag == "azure") return null;

            Mp3Format? common = null;
            var payloads = new List<byte[]>(clips.Count);
            var fingerprint = new StringBuilder(tag).Append('|').Append(AppConfig.TtsVoice);
            foreach (var clip in clips)
            {
                if (clip.Tag != tag || clip.File.Length == 0) return null;
                try
                {
                    var info = new FileInfo(clip.File);
                    if (!info.Exists || info.Length < 128) return null;
                    byte[] raw = File.ReadAllBytes(clip.File);
                    if (!TryGetMp3Payload(raw, out byte[] payload, out Mp3Format format)) return null;
                    if (common.HasValue && common.Value != format) return null;
                    common = format;
                    payloads.Add(payload);
                    fingerprint.Append('|').Append(clip.Text).Append(':')
                        .Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
                }
                catch { return null; }
            }
            if (!common.HasValue) return null;

            int total = payloads.Sum(p => p.Length);
            string file = Path.Combine(CacheDir, "seq-" + Md5Hex(fingerprint.ToString()) + ".mp3");
            try
            {
                if (File.Exists(file) && new FileInfo(file).Length == total) return file;
                Directory.CreateDirectory(CacheDir);
                string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    foreach (var payload in payloads) fs.Write(payload, 0, payload.Length);
                    fs.Flush(true);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(temp, file);
                return file;
            }
            catch { return null; }
        }

        readonly record struct Mp3Format(int Version, int Layer, int SampleRate, int Channels);

        static bool TryGetMp3Payload(byte[] raw, out byte[] payload, out Mp3Format format)
        {
            payload = raw;
            format = default;
            int start = 0, end = raw.Length;
            if (end >= 10 && raw[0] == (byte)'I' && raw[1] == (byte)'D' && raw[2] == (byte)'3')
            {
                int size = ((raw[6] & 0x7F) << 21) | ((raw[7] & 0x7F) << 14)
                         | ((raw[8] & 0x7F) << 7) | (raw[9] & 0x7F);
                start = 10 + size + ((raw[5] & 0x10) != 0 ? 10 : 0);
            }
            if (start < 0 || start >= end) return false;
            if (end - start >= 128 && raw[end - 128] == (byte)'T'
                && raw[end - 127] == (byte)'A' && raw[end - 126] == (byte)'G') end -= 128;
            if (!TryFindMp3Frame(raw, start, end, out int offset, out format)) return false;
            if (offset == 0 && end == raw.Length) return true;
            payload = new byte[end - offset];
            Buffer.BlockCopy(raw, offset, payload, 0, payload.Length);
            return true;
        }

        static readonly int[] Mp3RatesMpeg1 = { 44100, 48000, 32000 };
        static readonly int[] Mp3RatesMpeg2 = { 22050, 24000, 16000 };
        static readonly int[] Mp3RatesMpeg25 = { 11025, 12000, 8000 };
        static readonly int[] Mp3BitratesMpeg1 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
        static readonly int[] Mp3BitratesMpeg2 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

        static bool TryFindMp3Frame(byte[] data, int start, int end, out int offset, out Mp3Format format)
        {
            offset = 0;
            format = default;
            int limit = Math.Min(end - 4, start + 8192);
            for (int i = start; i <= limit; i++)
            {
                if (data[i] != 0xFF || (data[i + 1] & 0xE0) != 0xE0) continue;
                uint h = (uint)(data[i] << 24 | data[i + 1] << 16 | data[i + 2] << 8 | data[i + 3]);
                int version = (int)((h >> 19) & 0x3);
                int layer = (int)((h >> 17) & 0x3);
                int bitrateIndex = (int)((h >> 12) & 0xF);
                int rateIndex = (int)((h >> 10) & 0x3);
                int padding = (int)((h >> 9) & 0x1);
                int channelMode = (int)((h >> 6) & 0x3);
                if (version == 1 || layer != 1 || bitrateIndex == 0
                    || bitrateIndex == 15 || rateIndex == 3) continue;
                int sampleRate = version == 3 ? Mp3RatesMpeg1[rateIndex]
                    : version == 2 ? Mp3RatesMpeg2[rateIndex] : Mp3RatesMpeg25[rateIndex];
                int bitrate = (version == 3 ? Mp3BitratesMpeg1[bitrateIndex] : Mp3BitratesMpeg2[bitrateIndex]) * 1000;
                int frameLength = (version == 3 ? 144 : 72) * bitrate / sampleRate + padding;
                if (frameLength < 4 || i + frameLength > end) continue;
                offset = i;
                format = new Mp3Format(version, layer, sampleRate, channelMode == 3 ? 1 : 2);
                return true;
            }
            return false;
        }

        static string Md5Hex(string text)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>自检用：验证 WAV(PCM) 解析、首尾静音裁剪与无缝拼接。</summary>
        internal static bool WavSequenceSelfTest(out string detail) => WavAudio.SelfTest(out detail);

        /// <summary>自检用：构造 MPEG-1/2 Layer III 帧，验证帧长识别与 ID3v2/ID3v1 剥离。</summary>
        internal static bool Mp3ParserSelfTest()
            => Mp3CaseSelfTest(0xF3, 0x64, 0xC4, 144, 2, 24000, 1)
            && Mp3CaseSelfTest(0xFB, 0x90, 0x00, 417, 3, 44100, 2);

        static bool Mp3CaseSelfTest(byte b1, byte b2, byte b3, int frameLength,
            int version, int sampleRate, int channels)
        {
            byte[] plain = new byte[frameLength * 3];
            for (int f = 0; f < 3; f++)
            {
                int o = f * frameLength;
                plain[o] = 0xFF; plain[o + 1] = b1; plain[o + 2] = b2; plain[o + 3] = b3;
                for (int k = 4; k < frameLength; k++) plain[o + k] = (byte)(f * 31 + k);
            }
            if (!TryGetMp3Payload(plain, out byte[] payload, out Mp3Format format)) return false;
            if (payload.Length != plain.Length
                || format != new Mp3Format(version, 1, sampleRate, channels)) return false;

            byte[] tagged = new byte[26 + plain.Length + 128];
            tagged[0] = (byte)'I'; tagged[1] = (byte)'D'; tagged[2] = (byte)'3';
            tagged[3] = 4; tagged[9] = 16;                       // 16 字节 ID3v2
            Buffer.BlockCopy(plain, 0, tagged, 26, plain.Length);
            int tagAt = 26 + plain.Length;                       // 128 字节 ID3v1
            tagged[tagAt] = (byte)'T'; tagged[tagAt + 1] = (byte)'A'; tagged[tagAt + 2] = (byte)'G';

            return TryGetMp3Payload(tagged, out byte[] stripped, out Mp3Format taggedFormat)
                && stripped.Length == plain.Length && stripped[0] == 0xFF && taggedFormat == format;
        }

        public void Speak(string text)
        {
            if (string.IsNullOrEmpty(text) || stopped) return;
            int g = ++gen;
            string t = text;
            Task.Run(() => SpeakWorker(g, t));
        }

        bool EnsureCached(int g, string text, out string tag, out string file)
        {
            tag = EffectiveSource();
            file = "";
            if (!onlineOk || AppConfig.TtsSource == "off" || tag == "off") return false;
            file = CachePath(tag, text);
            if (File.Exists(file)) return true;
            if (g != gen || stopped) return false;

            byte[]? data = null;
            if (tag == "azure")
            {
                SetStatus("♪ 正在合成自然语音...");
                data = MsTts.Synthesize(text, AppConfig.TtsVoice);
                if (data == null && AppConfig.TtsSource == "auto")
                {
                    tag = "baidu";
                    file = CachePath(tag, text);
                }
            }
            else if (tag == "edge")
            {
                SetStatus("♪ 正在合成自然语音...");
                data = EdgeTts.Synthesize(text, AppConfig.TtsVoice, 2500, 8000);
                if (data == null)
                {
                    edgeBlocked = true;
                    AppConfig.EdgeBlocked = true;
                    AppConfig.Save();
                    return false;
                }
            }
            if (data == null && tag == "baidu")
            {
                SetStatus("♪ 正在合成语音...");
                data = BaiduTts.Synthesize(text);
                if (data == null) onlineOk = false;
            }
            if (data == null) return false;
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllBytes(file, data);
            }
            catch { }
            return File.Exists(file);
        }

        void SpeakWorker(int g, string text)
        {
            try
            {
                bool ready = EnsureCached(g, text, out string tag, out string file);
                PlayPrepared(g, text, ready ? tag : "", ready ? file : "");
            }
            catch { SapiFallback(g, text); }
        }

        void PlayPrepared(int g, string text, string tag, string file, bool waitForFallback = false)
        {
            if (g != gen || stopped) return;
            if (file.Length == 0) { SapiFallback(g, text, waitForFallback); return; }
            PlayFile(file);
            SetStatus(tag == "azure" ? "♪ 微软神经语音（" + VoiceDisplay() + "）"
                : tag == "edge" ? "♪ Edge 直连语音（" + VoiceDisplay() + "）"
                : "♪ 百度在线语音");
        }

        bool PlayFile(string file)
        {
            if (file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return PlayWavTrimmed(file);
            return MfPlayer.Play(file, 60000);
        }

        static bool PlayWavTrimmed(string file)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(file);
                if (WavAudio.TryReadPcm(raw, out byte[] pcm, out WavFormat format))
                {
                    byte[] trimmed = WavAudio.TrimSilence(pcm, format);
                    byte[] wav = trimmed.Length > 0 ? WavAudio.BuildWav(new[] { trimmed }, format) : raw;
                    using (var ms = new MemoryStream(wav, false))
                    using (var sp = new System.Media.SoundPlayer(ms))
                    {
                        sp.PlaySync();
                        return true;
                    }
                }
            }
            catch { }
            try
            {
                using (var sp = new System.Media.SoundPlayer(file)) sp.PlaySync();
                return true;
            }
            catch { return false; }
        }

        void SapiSequenceFallback(int g, List<string> texts)
        {
            if (sapi == null || g != gen || stopped) return;
            SetStatus("♪ 本地语音（离线/备用）");
            try { sapi.SpeakAsyncCancelAll(); sapi.Speak(string.Join("、", texts)); } catch { }
        }

        void SapiFallback(int g, string text, bool wait = false)
        {
            if (sapi == null || g != gen || stopped) return;
            if (wait)
            {
                try { sapi.Speak(text); } catch { }
                TryInvoke(delegate { if (onStatus != null) onStatus("♪ 本地语音（离线/备用）"); });
                return;
            }
            TryInvoke(delegate
            {
                if (g != gen || stopped) return;
                try { sapi.SpeakAsyncCancelAll(); sapi.SpeakAsync(text); } catch { }
                if (onStatus != null) onStatus("♪ 本地语音（离线/备用）");
            });
        }

        static string VoiceDisplay()
        {
            string v = AppConfig.TtsVoice;
            if (v.IndexOf("Yunxi", StringComparison.OrdinalIgnoreCase) >= 0) return "云希";
            if (v.IndexOf("Yunyang", StringComparison.OrdinalIgnoreCase) >= 0) return "云扬";
            return "晓晓";
        }

        // ---------------- 音频缓存 ----------------
        string CacheDir { get { return Path.Combine(AppConfig.Dir, "tts"); } }

        string CachePath(string tag, string text)
        {
            string key = tag + "|" + AppConfig.TtsVoice + "|" + text;
            byte[] h;
            using (var md5 = MD5.Create()) h = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder();
            foreach (var b in h) sb.Append(b.ToString("x2"));
            return Path.Combine(CacheDir, sb.ToString() + AudioExt(tag));
        }

        static string AudioExt(string tag)
        {
            return tag == "edge" || tag == "azure" ? ".wav" : ".mp3";
        }

        void PruneCache()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                var files = Directory.GetFiles(CacheDir, "*.*");
                if (files.Length <= 600) return;
                Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
                for (int i = 0; i < files.Length - 400; i++)
                    try { File.Delete(files[i]); } catch { }
            }
            catch { }
        }

        // ---------------- 批量预生成语音缓存（导入名单后调用，可取消、带进度） ----------------
        volatile bool prefetchCancelled;

        public void CancelPrefetch() => prefetchCancelled = true;

        /// <summary>后台逐条合成名单语音并写入缓存（命中缓存直接跳过）。不播放。</summary>
        public void PrefetchBatch(IEnumerable<string> names, Action<int, int> onProgress, Action onDone)
        {
            var list = names?.Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (list == null || list.Count == 0) { onDone?.Invoke(); return; }
            Task.Run(() =>
            {
                prefetchCancelled = false;
                if (!onlineOk || AppConfig.TtsSource == "off") { onDone?.Invoke(); return; }
                string tag = EffectiveSource();
                if (tag == "off") { onDone?.Invoke(); return; }
                int done = 0, fails = 0;
                foreach (var name in list)
                {
                    if (prefetchCancelled || stopped) break;
                    try
                    {
                        string file = CachePath(tag, name);
                        if (!File.Exists(file))
                        {
                            byte[] data = null;
                            if (tag == "azure")
                            {
                                data = MsTts.Synthesize(name, AppConfig.TtsVoice);
                                if (data == null && AppConfig.TtsSource == "auto")
                                {
                                    tag = "baidu";
                                    file = CachePath(tag, name);
                                }
                            }
                            else if (tag == "edge")
                            {
                                data = EdgeTts.Synthesize(name, AppConfig.TtsVoice, 2500, 8000);
                                if (data == null)
                                {
                                    edgeBlocked = true;
                                    AppConfig.EdgeBlocked = true;
                                    AppConfig.Save();
                                    fails++;
                                    done++;
                                    onProgress?.Invoke(done, list.Count);
                                    continue;
                                }
                            }
                            if (data == null && tag == "baidu")
                            {
                                data = BaiduTts.Synthesize(name);
                                if (data == null) { fails++; onlineOk = false; }
                            }
                            if (data != null && data.Length >= 100)
                            {
                                Directory.CreateDirectory(CacheDir);
                                File.WriteAllBytes(file, data);
                                fails = 0;
                                onlineOk = true;
                            }
                            else fails++;
                        }
                    }
                    catch { fails++; onlineOk = false; }
                    done++;
                    try { onProgress?.Invoke(done, list.Count); } catch { }
                }
                try { onDone?.Invoke(); } catch { }
            });
        }

        // ---------------- 后台预热（当前班级名单提前合成缓存） ----------------
        public void QueueWarmup(IEnumerable<string> names)
        {
            if (names == null) return;
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n)) warmQueue.Enqueue(n);
            lock (warmLock)
            {
                if (warmThreadStarted) return;
                warmThreadStarted = true;
                Task.Run((Action)WarmLoop);
            }
        }

        void WarmLoop()
        {
            PruneCache();
            int fails = 0;
            while (!stopped)
            {
                string name;
                if (!warmQueue.TryDequeue(out name)) { Thread.Sleep(250); continue; }
                if (!onlineOk || AppConfig.TtsSource == "off") { Thread.Sleep(300); continue; }
                string tag = EffectiveSource();
                if (tag == "off") { Thread.Sleep(300); continue; }
                string file = CachePath(tag, name);
                if (File.Exists(file)) continue;
                try
                {
                    byte[] data = null;
                    if (tag == "azure")
                    {
                        data = MsTts.Synthesize(name, AppConfig.TtsVoice);
                        if (data == null && AppConfig.TtsSource == "auto")
                        {
                            tag = "baidu";
                            file = CachePath(tag, name);
                        }
                    }
                    else if (tag == "edge")
                    {
                        data = EdgeTts.Synthesize(name, AppConfig.TtsVoice, 2500, 8000);
                        if (data == null)
                        {
                            edgeBlocked = true;
                            AppConfig.EdgeBlocked = true;
                            AppConfig.Save();
                            fails++;
                            Thread.Sleep(400);
                            continue;
                        }
                    }
                    if (data == null && tag == "baidu")
                    {
                        data = BaiduTts.Synthesize(name);
                        if (data == null) { fails++; onlineOk = false; Thread.Sleep(400); continue; }
                    }
                    if (data != null && data.Length >= 100)
                    {
                        try
                        {
                            Directory.CreateDirectory(CacheDir);
                            File.WriteAllBytes(file, data);
                        }
                        catch { }
                        fails = 0;
                        onlineOk = true;
                    }
                }
                catch { fails++; onlineOk = false; }
                if (fails >= 3) { Thread.Sleep(15000); fails = 0; }
                Thread.Sleep(350);
            }
        }

        void SetStatus(string s)
        {
            TryInvoke(delegate { if (onStatus != null) onStatus(s); });
        }

        void TryInvoke(Action a)
        {
            try { a(); }
            catch { }
        }

        public void Dispose()
        {
            stopped = true;
            try
            {
                if (sapi != null) { sapi.SpeakAsyncCancelAll(); sapi.Dispose(); }
            }
            catch { }
            sapi = null;
        }
    }
}
