using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Glint
{
    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public static readonly Color Bg = Ui.Side, Hi = Ui.Well;

        public DarkRenderer() { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Bg); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.Pill(e.Graphics, new RectangleF(2, 0, e.Item.Width - 4, e.Item.Height - 1), Hi, 12);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool shortcut = e.Text != e.Item.Text;
            e.TextColor = !e.Item.Enabled || shortcut ? Ui.Dim : Ui.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Ui.Dim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            Icons.DrawLucide(e.Graphics, "check", r.X + r.Width / 2f, r.Y + r.Height / 2f, 14, 1.8f, Ui.Accent);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }
    }

    static class MenuKit
    {
        public static void Style(ToolStripDropDownMenu m, bool checks)
        {
            m.Renderer = new DarkRenderer();
            m.ShowImageMargin = true;
            m.ShowCheckMargin = false;
            m.ImageScalingSize = new Size(16, 16);
            m.BackColor = DarkRenderer.Bg;
            m.ForeColor = Ui.Text;
            m.Font = new Font("Segoe UI", 9f);
            m.Padding = new Padding(2, 3, 2, 3);
            m.HandleCreated += delegate { Native.Rounded(m.Handle); };
        }

        public static Bitmap IconImage(string name)
        {
            var b = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(b)) Icons.DrawLucide(g, name, 8, 8, 15, 1.3f, Color.FromArgb(205, 205, 210));
            return b;
        }

        public static Bitmap MarkImage()
        {
            var b = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(b)) Brand.DrawMark(g, new Rectangle(1, 1, 14, 14), Ui.Text, Brand.Pixel);
            return b;
        }

        public static ToolStripMenuItem Item(string text, Action click, string icon = null)
        {
            var it = new ToolStripMenuItem(text) { Padding = new Padding(4, 2, 4, 2), ForeColor = Ui.Text };
            if (icon != null) it.Image = IconImage(icon);
            if (click != null) it.Click += delegate { click(); };
            return it;
        }

        public static ToolStripMenuItem Sub(string text, bool checks)
        {
            var it = Item(text, null);
            Style((ToolStripDropDownMenu)it.DropDown, checks);
            return it;
        }
    }
}
