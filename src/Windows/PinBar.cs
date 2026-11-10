using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glint
{
    class PinBar : Glass
    {
        readonly PinWindow pin;
        readonly string[] Tips = { L.T("In andere App ziehen"), L.T("Im Editor öffnen"), L.T("Kopieren"), L.T("Auf 100 % zurücksetzen"), L.T("Schließen") };
        readonly string[] KeysText = { null, "E", L.T("Strg+C"), "0", "Esc" };
        static readonly Color IconColor = Color.FromArgb(230, 230, 232), Grip = Color.FromArgb(125, 128, 134);
        const int W = 264, H = 55;
        readonly Rectangle panel = new Rectangle(S, S, W, H);
        readonly Rectangle[] rects = {
            new Rectangle(S + 10, S + 8, 52, 39), new Rectangle(S + 66, S + 8, 34, 39), new Rectangle(S + 102, S + 8, 34, 39),
            new Rectangle(S + 142, S + 8, 66, 39), new Rectangle(S + 216, S + 8, 38, 39) };
        int hover = -1;
        public const int Margin2 = S;

        public PinBar(PinWindow p)
        {
            pin = p;
            ClientSize = new Size(W + S * 2, H + S * 2);
        }

        protected override Rectangle PanelRect { get { return panel; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x08000000 | 0x80; return c; } }

        public override void PaintTo(Graphics g)
        {
            DrawPanel(g, panel, Ui.CardRadius);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < rects.Length; i++)
                if (i == hover)
                    using (var path = Renderer.Round(rects[i], Ui.ControlRadius))
                    using (var b = new SolidBrush(i == 4 ? Color.FromArgb(232, 17, 35) : Color.FromArgb(24, 255, 255, 255))) g.FillPath(b, path);
            float cy = S + 27.5f;
            Icons.DrawLucide(g, "grip-vertical", S + 20.5f, cy, 19, 1.7f, Grip);
            Icons.DrawLucide(g, "file", S + 36f, cy, 20, 1.6f, IconColor);
            Icons.DrawLucide(g, "grip-vertical", S + 51.5f, cy, 19, 1.7f, Grip);
            Icons.DrawLucide(g, "pencil", S + 83f, cy, 19, 1.6f, IconColor);
            Icons.DrawLucide(g, "copy", S + 119f, cy, 18, 1.6f, IconColor);
            using (var f = new Font("Segoe UI Semibold", 20f, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var c = new SolidBrush(IconColor))
                g.DrawString(pin.Percent + "%", f, c, rects[3], sf);
            Icons.DrawLucide(g, "x", S + 235f, cy, 20, 1.6f, hover == 4 ? Color.White : IconColor);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(e.Location)) h = i;
            if (h != hover) { hover = h; if (h >= 0) pin.SetTip(Tips[h], KeysText[h]); else pin.SetTip(null, null); Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; pin.SetTip(null, null); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (rects[0].Contains(e.Location)) pin.DragFile(this);
            else if (rects[1].Contains(e.Location)) pin.OpenEditor();
            else if (rects[2].Contains(e.Location)) pin.CopyImage();
            else if (rects[3].Contains(e.Location)) pin.ResetZoom();
            else if (rects[4].Contains(e.Location)) pin.Close();
        }
    }
}
