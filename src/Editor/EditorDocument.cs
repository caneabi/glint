using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace Glint
{
    // The image being edited: base bitmap, annotations and their history. Owns every bitmap it holds.
    class EditorDocument : IDisposable
    {
        public Bitmap Base { get; private set; }
        public List<Obj> Objs { get; private set; }
        readonly Stack<Tuple<Bitmap, List<Obj>>> undo = new Stack<Tuple<Bitmap, List<Obj>>>(), redo = new Stack<Tuple<Bitmap, List<Obj>>>();

        public EditorDocument(Bitmap image) { Base = image; Objs = new List<Obj>(); }

        // Call before every change. Objects are copied, their bitmaps are shared.
        public void Push()
        {
            undo.Push(Tuple.Create(Base, Objs.Select(o => o.Clone()).ToList()));
            var dropped = redo.ToList();
            redo.Clear();
            Release(dropped);
        }

        public bool Undo() { return Move(undo, redo); }
        public bool Redo() { return Move(redo, undo); }

        bool Move(Stack<Tuple<Bitmap, List<Obj>>> from, Stack<Tuple<Bitmap, List<Obj>>> to)
        {
            if (from.Count == 0) return false;
            to.Push(Tuple.Create(Base, Objs));
            var s = from.Pop();
            Base = s.Item1; Objs = s.Item2;
            return true;
        }

        public void Crop(Rectangle r)
        {
            Push();
            Base = Imaging.Crop(Base, r);
            foreach (var o in Objs) o.Move(-r.X, -r.Y);
        }

        // Flattens the annotations, they can't be scaled separately.
        public void Resize(int width)
        {
            int height = Math.Max(1, (int)(Base.Height * ((float)width / Base.Width)));
            Push();
            using (var flat = Renderer.Compose(Base, Objs))
            {
                var res = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(res))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(flat, 0, 0, res.Width, res.Height);
                }
                Base = res;
                Objs = new List<Obj>();
            }
        }

        public Bitmap Export(int backdrop, int pad)
        {
            using (var c = Renderer.Compose(Base, Objs))
                return backdrop >= 0 ? Renderer.Backdrop(c, backdrop, pad) : Imaging.Crop(c, new Rectangle(0, 0, c.Width, c.Height));
        }

        HashSet<Bitmap> InUse()
        {
            var used = new HashSet<Bitmap> { Base };
            foreach (var o in Objs) used.Add(o.Img);
            foreach (var s in undo.Concat(redo))
            {
                used.Add(s.Item1);
                foreach (var o in s.Item2) used.Add(o.Img);
            }
            return used;
        }

        // Frees bitmaps of dropped states that nothing else points to.
        void Release(List<Tuple<Bitmap, List<Obj>>> states)
        {
            if (states.Count == 0) return;
            var used = InUse();
            var free = new HashSet<Bitmap>();
            foreach (var s in states)
            {
                free.Add(s.Item1);
                foreach (var o in s.Item2) free.Add(o.Img);
            }
            foreach (var b in free) if (b != null && !used.Contains(b)) b.Dispose();
        }

        public void Dispose()
        {
            foreach (var b in InUse()) if (b != null) b.Dispose();
            undo.Clear(); redo.Clear();
        }
    }
}
