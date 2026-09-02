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
                    ballX = BallX, ballY = BallY, lastUpdateCheckDate = LastUpdateCheckDate
                };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
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

        // 合成一段语音，返回 MP3 字节；失败返回 null（429/5xx 自动重试，最多3次）
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
                    req.Headers["X-Microsoft-OutputFormat"] = "audio-24khz-48kbitrate-mono-mp3";
                    byte[] body = Encoding.UTF8.GetBytes(BuildSsml(text, voice));
                    req.ContentLength = body.Length;
                    using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        byte[] data = ms.ToArray();
                        if (resp.StatusCode == HttpStatusCode.OK && data.Length > 1000 &&
                            data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
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
                        Thread.Sleep(120);
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

        public void Speak(string text)
        {
            if (string.IsNullOrEmpty(text) || stopped) return;
            int g = ++gen;
            string t = text;
            Task.Run(() => SpeakWorker(g, t));
        }

        void SpeakWorker(int g, string text)
        {
            try
            {
                if (!onlineOk || AppConfig.TtsSource == "off") { SapiFallback(g, text); return; }
                string tag = EffectiveSource();
                if (tag == "off") { SapiFallback(g, text); return; }
                string ext = tag == "edge" ? "wav" : "mp3";
                string file = CachePath(tag, text);
                if (!File.Exists(file))
                {
                    if (g != gen || stopped) return;
                    byte[] data = null;
                    if (tag == "azure")
                    {
                        SetStatus("♪ 正在合成自然语音...");
                        data = MsTts.Synthesize(text, AppConfig.TtsVoice);
                        if (data == null && AppConfig.TtsSource == "auto")
                        {
                            tag = "baidu";
                            ext = "mp3";
                            file = CachePath(tag, text);
                        }
                    }
                    else if (tag == "edge")
                    {
                        SetStatus("♪ 正在合成自然语音...");
                        data = EdgeTts.Synthesize(text, AppConfig.TtsVoice, 2500, 8000);
                        if (data == null)
                        {
                            // Edge WebSocket 端点不可用（常见于国内网络 403）
                            edgeBlocked = true;
                            AppConfig.EdgeBlocked = true;
                            AppConfig.Save();
                            SapiFallback(g, text);
                            return;
                        }
                    }
                    if (data == null && tag == "baidu")
                    {
                        SetStatus("♪ 正在合成语音...");
                        data = BaiduTts.Synthesize(text);
                        if (data == null) { onlineOk = false; SapiFallback(g, text); return; }
                    }
                    if (data == null) { SapiFallback(g, text); return; }
                    try
                    {
                        Directory.CreateDirectory(CacheDir);
                        File.WriteAllBytes(file, data);
                    }
                    catch { }
                }
                if (g != gen || stopped) return;
                PlayFile(tag, file);
                SetStatus(tag == "azure" ? "♪ 微软神经语音（" + VoiceDisplay() + "）"
                    : tag == "edge" ? "♪ Edge 直连语音（" + VoiceDisplay() + "）"
                    : "♪ 百度在线语音");
            }
            catch
            {
                SapiFallback(g, text);
            }
        }

        void PlayFile(string tag, string file)
        {
            if (tag == "edge")
            {
                using (var sp = new System.Media.SoundPlayer(file)) sp.PlaySync();
            }
            else
            {
                MfPlayer.Play(file, 60000);
            }
        }

        void SapiFallback(int g, string text)
        {
            if (sapi == null) return;
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
            return Path.Combine(CacheDir, sb.ToString() + (tag == "edge" ? ".wav" : ".mp3"));
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
                        string ext = tag == "edge" ? "wav" : "mp3";
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
                                    ext = "mp3";
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
                string ext = tag == "edge" ? "wav" : "mp3";
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
                            ext = "mp3";
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
