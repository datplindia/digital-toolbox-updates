using System;
using System.IO;
using System.Media;

namespace DigitalArts.BatteryGuard
{
    enum AlertKind { None, High, Low }

    /// <summary>अलार्म की आवाज़ प्रोग्राम में ही बनती है (कोई अलग .wav फ़ाइल नहीं)।</summary>
    sealed class AlarmSound : IDisposable
    {
        SoundPlayer player;
        MemoryStream stream;

        public void PlayLooping(AlertKind kind)
        {
            Stop();
            try
            {
                stream = new MemoryStream(BuildWav(kind));
                player = new SoundPlayer(stream);
                player.Load();
                player.PlayLooping();
            }
            catch { Stop(); }
        }

        public void Stop()
        {
            try { player?.Stop(); } catch { }
            player?.Dispose(); player = null;
            stream?.Dispose(); stream = null;
        }

        public void Dispose() => Stop();

        // High: ऊपर जाती धुन (चार्जर निकालें), Low: नीचे जाती धुन (चार्जर लगाएँ)
        static byte[] BuildWav(AlertKind kind)
        {
            const int rate = 22050;
            (double hz, int ms)[] seq = kind == AlertKind.High
                ? new[] { (880.0, 140), (0.0, 60), (1174.7, 140), (0.0, 60), (1568.0, 260), (0.0, 1100) }
                : new[] { (1568.0, 140), (0.0, 60), (1174.7, 140), (0.0, 60), (880.0, 260), (0.0, 1100) };

            int total = 0;
            foreach (var t in seq) total += rate * t.ms / 1000;

            var pcm = new short[total];
            int pos = 0;
            foreach (var (hz, ms) in seq)
            {
                int n = rate * ms / 1000;
                int fade = Math.Min(n / 4, rate / 200); // ~5 ms, क्लिक की आवाज़ से बचने के लिए
                for (int i = 0; i < n; i++, pos++)
                {
                    if (hz <= 0) { pcm[pos] = 0; continue; }
                    double env = 1.0;
                    if (i < fade) env = i / (double)fade;
                    else if (i > n - fade) env = (n - i) / (double)fade;
                    double t = i / (double)rate;
                    double v = Math.Sin(2 * Math.PI * hz * t) * 0.75 + Math.Sin(4 * Math.PI * hz * t) * 0.15;
                    pcm[pos] = (short)(v * env * 0.6 * short.MaxValue);
                }
            }

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = pcm.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)1);       // PCM, mono
                w.Write(rate); w.Write(rate * 2);           // sample rate, byte rate
                w.Write((short)2); w.Write((short)16);      // block align, bits
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
                foreach (var s in pcm) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
