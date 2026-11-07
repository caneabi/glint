using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace Glint
{
    // Generated at runtime, so the exe ships no audio files.
    static class Sounds
    {
        public enum Kind { Screenshot, Upload, Copy }
        const int Rate = 44100;
        public static bool Muted; // test hook
        static readonly Dictionary<Kind, SoundPlayer> Players = new Dictionary<Kind, SoundPlayer>();

        static void Tone(List<short> o, double start, double freq, double length, double volume)
        {
            int from = (int)(start * Rate), n = (int)(length * Rate);
            while (o.Count < from + n) o.Add(0);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate, env = Math.Min(1, i / (Rate * .002)) * Math.Exp(-t / (length * .35));
                int v = o[from + i] + (int)(Math.Sin(2 * Math.PI * freq * t) * env * volume * short.MaxValue);
                o[from + i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
            }
        }

        static void Click(List<short> o, double start, double length, double volume, int seed)
        {
            var rnd = new Random(seed);
            int from = (int)(start * Rate), n = (int)(length * Rate);
            while (o.Count < from + n) o.Add(0);
            double last = 0;
            for (int i = 0; i < n; i++)
            {
                double env = Math.Exp(-(i / (double)Rate) / (length * .25));
                last = last * .55 + (rnd.NextDouble() * 2 - 1) * .45;
                o[from + i] = (short)(last * env * volume * short.MaxValue);
            }
        }

        public static byte[] Wav(Kind kind)
        {
            var s = new List<short>();
            switch (kind)
            {
                case Kind.Screenshot:
                    Click(s, 0, .03, .5, 3);
                    Click(s, .055, .05, .35, 7);
                    break;
                case Kind.Copy:
                    Tone(s, 0, 1046, .07, .16);
                    Tone(s, .055, 1568, .1, .16);
                    break;
                default:
                    Tone(s, 0, 659, .1, .15);
                    Tone(s, .08, 880, .1, .15);
                    Tone(s, .16, 1319, .2, .15);
                    break;
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int bytes = s.Count * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (var v in s) w.Write(v);
                w.Flush();
                return ms.ToArray();
            }
        }

        static bool Enabled(Kind kind)
        {
            var c = Settings.Current;
            return kind == Kind.Screenshot ? c.SoundScreenshot : kind == Kind.Upload ? c.SoundUpload : c.SoundCopy;
        }

        public static void Play(Kind kind) { if (Enabled(kind)) Preview(kind); }

        public static void Preview(Kind kind)
        {
            if (Muted) return;
            try
            {
                SoundPlayer p;
                if (!Players.TryGetValue(kind, out p))
                {
                    p = new SoundPlayer(new MemoryStream(Wav(kind)));
                    p.Load();
                    Players[kind] = p;
                }
                p.Play();
            }
            catch (InvalidOperationException ex) { Log.Error("Sound", ex); }
            catch (TimeoutException ex) { Log.Error("Sound", ex); }
        }
    }
}
