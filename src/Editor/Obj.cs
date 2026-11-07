using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Glint
{
    public class Obj
    {
        public Kind Kind; public PointF A, B; public PointF? Ctrl;
        public Color Color = Color.FromArgb(255, 94, 91); public float Width = 4; public bool Fill, Hand; public int Opacity = 100;
        public string Text = ""; public int Num; public List<PointF> Pts; public Bitmap Img; public int Seed = 7;
        public RectangleF Box { get { return RectangleF.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y)); } }
        public bool IsLine { get { return Kind == Kind.Arrow || Kind == Kind.Line || Kind == Kind.Measure; } }
        public Obj Clone()
        {
            var o = (Obj)MemberwiseClone();
            if (Pts != null) o.Pts = new List<PointF>(Pts);
            return o;
        }
        public void Move(float dx, float dy)
        {
            A = new PointF(A.X + dx, A.Y + dy); B = new PointF(B.X + dx, B.Y + dy);
            if (Ctrl.HasValue) Ctrl = new PointF(Ctrl.Value.X + dx, Ctrl.Value.Y + dy);
            if (Pts != null) for (int i = 0; i < Pts.Count; i++) Pts[i] = new PointF(Pts[i].X + dx, Pts[i].Y + dy);
        }
    }
}
