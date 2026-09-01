// WinRT MediaPlayer 播放探针
using System;
using System.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;

class WinrtProbe
{
    static void Main()
    {
        try
        {
            var player = new MediaPlayer();
            player.Volume = 1.0;
            player.Source = MediaSource.CreateFromUri(new Uri("file:///E:/dsh/LuckyPicker/sample.mp3"));
            player.Play();
            DateTime end = DateTime.UtcNow.AddSeconds(20);
            int last = -1;
            while (DateTime.UtcNow < end)
            {
                try
                {
                    int s = (int)player.PlaybackSession.PlaybackState;
                    if (s != last) { Console.WriteLine("state=" + s); last = s; }
                    if (s == 5 || s == 6) break;
                }
                catch { }
                Thread.Sleep(200);
            }
            Console.WriteLine("final state=" + (int)player.PlaybackSession.PlaybackState);
            player.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("WINRT EX: " + ex.GetType().Name + ": " + ex.Message);
            if (ex.InnerException != null) Console.WriteLine("INNER: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
        }
        Console.WriteLine("done");
    }
}
