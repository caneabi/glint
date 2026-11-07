using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class UiControl : Control
    {
        protected bool hot;
        public Action Changed;
        public UiControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card; TabStop = false; Cursor = Cursors.Hand;
        }
        protected float LW { get { return Width / Ui.F; } }
        protected float LH { get { return Height / Ui.F; } }
        protected Graphics Begin(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Prepare(g);
            g.Clear(BackColor);
            g.ScaleTransform(Ui.F, Ui.F);
            return g;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected void Fire() { Invalidate(); if (Changed != null) Changed(); }
    }
}
