using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Glint
{
    class KeyRecorder : Button
    {
        static readonly Color Idle = Color.FromArgb(55, 55, 60), Active = Color.FromArgb(255, 94, 91);
        string value;
        bool recording;
        public Action Recorded;
        public bool IsRecording { get { return recording; } }
        public bool Taken; // another app or Windows holds this combination
        bool clearDown, clearHot;

        Rectangle ClearRect { get { int s = Ui.S(24); return new Rectangle(Width - s - Ui.S(2), (Height - s) / 2, s, s); } }
        bool CanClear { get { return !recording && !string.IsNullOrEmpty(value); } }

        protected override void OnMouseDown(MouseEventArgs e) { clearDown = CanClear && ClearRect.Contains(e.Location); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hot = CanClear && ClearRect.Contains(e.Location);
            if (hot != clearHot) { clearHot = hot; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { clearHot = false; Invalidate(); base.OnMouseLeave(e); }

        public KeyRecorder(string v)
        {
            value = v;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ForeColor = Color.White; BackColor = Idle; TabStop = false; Cursor = Cursors.Hand;
            Text = Settings.Pretty(v);
        }

        public string Value
        {
            get { return value; }
            set { this.value = value; Stop(); }
        }

        void Stop()
        {
            if (recording && Tray.Instance != null) Tray.Instance.RegisterAll();
            recording = false;
            if (KeyHook.Recorder == (Action<Keys>)Capture) KeyHook.Recorder = null;
            BackColor = Idle;
            Text = Settings.Pretty(value);
        }

        void Capture(Keys k)
        {
            value = Settings.KeyName(k);
            Stop();
            if (Recorded != null) Recorded();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g); g.Clear(Ui.Card);
            g.ScaleTransform(Ui.F, Ui.F);
            float w = Width / Ui.F, h = Height / Ui.F;
            Ui.Pill(g, new RectangleF(0, 0, w, h), recording ? Ui.Accent : Ui.Well, Ui.ControlRadius);
            Ui.Center(g, Text, Ui.Small, recording ? Brand.Dark : !string.IsNullOrEmpty(value) ? Ui.Text : Ui.Dim, CanClear ? new RectangleF(24, 0, w - 48, h) : new RectangleF(6, 0, w - 12, h));
            if (CanClear) Icons.DrawLucide(g, "x", w - 14, h / 2, 12, 1.4f, clearHot ? Ui.Text : Ui.Dim);
            if (Taken && !recording) using (var path = Renderer.Round(new RectangleF(.75f, .75f, w - 1.5f, h - 1.5f), Ui.ControlRadius)) using (var p = new Pen(Ui.Accent, 1.5f)) g.DrawPath(p, path);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (clearDown)
            {
                clearDown = false; clearHot = false;
                Value = "";
                if (Recorded != null) Recorded();
                return;
            }
            if (recording) { Stop(); return; }
            if (Tray.Instance != null) Tray.Instance.SuspendHotkeys();
            recording = true;
            var form = FindForm();
            if (form != null) form.ActiveControl = this;
            KeyHook.Recorder = Capture;
            BackColor = Active;
            Text = L.T("Taste drücken…");
            Focus();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (!recording || !IsHandleCreated) return;
            // Activating the window can move focus for a moment, only stop if it really went elsewhere.
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || !recording || Focused) return;
                var f = FindForm();
                if (f == null || Form.ActiveForm != f) return;
                if (f.ActiveControl == this) Focus(); else Stop();
            }));
        }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (!recording) return base.ProcessCmdKey(ref m, k);
            var code = k & Keys.KeyCode;
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin)
            {
                Text = Settings.Pretty(Settings.KeyName(k & Keys.Modifiers).TrimEnd('e', 'N', 'o', 'n').TrimEnd('+')) + " + …";
                return true;
            }
            Capture(k);
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            // Fallback without the hook, Print Screen only reports key up.
            if (recording && e.KeyCode == Keys.PrintScreen) { Capture(e.KeyData); return; }
            base.OnKeyUp(e);
        }

        protected override void Dispose(bool d) { if (d && recording) Stop(); base.Dispose(d); }
    }
}
