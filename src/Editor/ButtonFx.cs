using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Glint
{
    // Result of a toolbar action, drawn right on its button.
    class ButtonFx
    {
        public enum State { Busy, Done, Fail }
        const int Length = 1100, FadeAt = 950;
        static readonly Color Green = Color.FromArgb(52, 199, 89), Red = Color.FromArgb(253, 83, 76);

        public readonly State Kind;
        readonly Stopwatch clock = Stopwatch.StartNew();

        public ButtonFx(State kind) { Kind = kind; }
        public bool Finished { get { return Kind != State.Busy && clock.ElapsedMilliseconds > Length; } }
        public bool HidesIcon { get { return Kind != State.Busy; } }
        public void Draw(Graphics g, float cx, float cy) { DrawAt(g, cx, cy, Kind, clock.ElapsedMilliseconds); }

        static float Clamp(float v) { return Math.Max(0, Math.Min(1, v)); }
        static float EaseOut(float t) { return 1 - (1 - t) * (1 - t) * (1 - t); }

        // Springs slightly past its final size.
        static float Pop(float ms)
        {
            float t = Clamp(ms / 240f) - 1;
            float s = 1 + 2.7f * t * t * t + 1.7f * t * t;
            return s * (1 - Clamp((ms - FadeAt) / (Length - FadeAt)));
        }

        public static void DrawAt(Graphics g, float cx, float cy, State kind, float ms)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (kind == State.Busy) Spinner(g, cx, cy, ms);
            else if (kind == State.Done) Success(g, cx, cy, ms);
            else Failure(g, cx, cy, ms);
            g.Restore(state);
        }

        static void Spinner(Graphics g, float cx, float cy, float ms)
        {
            using (var p = new Pen(Color.FromArgb(235, 235, 238), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(p, cx - 13, cy - 13, 26, 26, ms * .45f % 360, 100);
        }

        static void Success(Graphics g, float cx, float cy, float ms)
        {
            float ring = Clamp(ms / 420f);
            if (ring < 1)
                using (var p = new Pen(Color.FromArgb((int)(190 * (1 - ring)), Green), .5f + 2.5f * (1 - ring)))
                {
                    float r = 12 + 16 * EaseOut(ring);
                    g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
                }

            float burst = Clamp((ms - 60) / 420f);
            if (burst > 0 && burst < 1)
                for (int i = 0; i < 8; i++)
                {
                    double angle = (i * 45 + 22.5) * Math.PI / 180;
                    float dist = (i % 2 == 0 ? 15 : 12) + 15 * EaseOut(burst), size = 3.4f * (1 - burst);
                    using (var b = new SolidBrush(i % 2 == 0 ? Green : Color.FromArgb(160, 235, 180)))
                        g.FillEllipse(b, cx + (float)Math.Cos(angle) * dist - size / 2, cy + (float)Math.Sin(angle) * dist - size / 2, size, size);
                }

            float s = Pop(ms), radius = 12 * s;
            if (radius <= 0) return;
            using (var b = new SolidBrush(Green)) g.FillEllipse(b, cx - radius, cy - radius, radius * 2, radius * 2);

            // The check draws itself in two strokes.
            float q = Clamp((ms - 120) / 180f);
            if (q <= 0) return;
            PointF p0 = new PointF(cx - 5 * s, cy + .3f * s), p1 = new PointF(cx - 1.5f * s, cy + 3.8f * s), p2 = new PointF(cx + 5.5f * s, cy - 3.6f * s);
            using (var pen = new Pen(Color.White, 2.2f * Math.Max(.4f, s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (q < .35f) g.DrawLine(pen, p0, Along(p0, p1, q / .35f));
                else g.DrawLines(pen, new[] { p0, p1, Along(p1, p2, (q - .35f) / .65f) });
            }
        }

        static PointF Along(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

        static void Failure(Graphics g, float cx, float cy, float ms)
        {
            cx += (float)Math.Sin(ms / 25f) * 3 * (1 - Clamp(ms / 320f));
            float s = Pop(ms), radius = 12 * s, arm = 4.2f * s;
            if (radius <= 0) return;
            using (var b = new SolidBrush(Red)) g.FillEllipse(b, cx - radius, cy - radius, radius * 2, radius * 2);
            using (var pen = new Pen(Color.White, 2.2f * Math.Max(.4f, s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm);
                g.DrawLine(pen, cx - arm, cy + arm, cx + arm, cy - arm);
            }
        }
    }
}
