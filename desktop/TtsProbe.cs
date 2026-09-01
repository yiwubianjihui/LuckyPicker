// TTS 引擎探针：微软神经语音(Azure REST) → 百度 → Edge → MF 播放
using System;
using System.IO;
using LuckyPicker;

class TtsProbe
{
    static void Main()
    {
        Console.WriteLine("== TTS probe (MsTts / Baidu / Edge / MFPlay) ==");
        // 1) MsTts（微软翻译令牌 + Azure Speech REST）
        try
        {
            byte[] mp3 = MsTts.Synthesize("测试语音，今天天气不错", "zh-CN-XiaoxiaoNeural");
            Console.WriteLine("MSTTS: " + (mp3 == null ? "null（失败）" : "OK len=" + mp3.Length + " magic=" + mp3[0].ToString("X2") + mp3[1].ToString("X2")));
            if (mp3 != null) File.WriteAllBytes("E:\\dsh\\LuckyPicker\\mstts_sample.mp3", mp3);
        }
        catch (Exception ex) { Console.WriteLine("MSTTS EX: " + ex.GetType().Name + ": " + ex.Message); }
        // 2) 男声
        try
        {
            byte[] mp3 = MsTts.Synthesize("云希测试", "zh-CN-YunxiNeural");
            Console.WriteLine("MSTTS(Yunxi): " + (mp3 == null ? "null" : "OK len=" + mp3.Length));
        }
        catch (Exception ex) { Console.WriteLine("YUNXI EX: " + ex.Message); }
        // 3) 播放验证
        if (File.Exists("E:\\dsh\\LuckyPicker\\mstts_sample.mp3"))
        {
            bool ok = MfPlayer.Play("E:\\dsh\\LuckyPicker\\mstts_sample.mp3", 30000);
            Console.WriteLine("MFPLAY: " + (ok ? "OK（播放完成）" : "失败"));
        }
        // 4) 百度（仍可用）
        try
        {
            byte[] mp3 = BaiduTts.Synthesize("测试语音");
            Console.WriteLine("BAIDU: " + (mp3 == null ? "null" : "OK len=" + mp3.Length));
        }
        catch (Exception ex) { Console.WriteLine("BAIDU EX: " + ex.Message); }
        // 5) Edge WS（本网络预期失败）
        try
        {
            byte[] wav = EdgeTts.Synthesize("测试语音", "zh-CN-XiaoxiaoNeural", 5000, 15000);
            Console.WriteLine("EDGE: " + (wav == null ? "null（本网络被拒，预期）" : "OK len=" + wav.Length));
        }
        catch (Exception ex) { Console.WriteLine("EDGE EX: " + ex.Message); }
        Console.WriteLine("done");
    }
}
