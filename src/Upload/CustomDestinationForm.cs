using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Glint
{
    // Name/value table, a blank row is always ready at the end.
    class KeyValueEditor : Panel
    {
        const int RowH = 34, HeadH = 26;
        class Row { public UiInput Key, Value; public IconButton Remove; }
        readonly List<Row> rows = new List<Row>();
        readonly Panel list = new PassPanel { AutoScroll = true, BackColor = Ui.Card };

        public KeyValueEditor(IEnumerable<KeyValuePair<string, string>> items)
        {
            BackColor = Ui.Bg; DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Controls.Add(list);
            foreach (var i in items) AddRow(i.Key, i.Value);
            AddRow("", "");
        }

        public List<KeyValuePair<string, string>> Items
        {
            get { return rows.Select(r => new KeyValuePair<string, string>(r.Key.Value, r.Value.Value)).ToList(); }
        }

        void AddRow(string key, string value)
        {
            var r = new Row { Key = new UiInput(key, false, 100), Value = new UiInput(value, false, 100), Remove = new IconButton("x", 24) };
            r.Key.Changed = () => { if (r == rows[rows.Count - 1] && r.Key.Value != "") { AddRow("", ""); LayoutRows(); } };
            r.Remove.Changed = () =>
            {
                if (rows.Count == 1) { r.Key.Value = ""; r.Value.Value = ""; return; }
                rows.Remove(r);
                list.Controls.Remove(r.Key); list.Controls.Remove(r.Value); list.Controls.Remove(r.Remove);
                BeginInvoke(new Action(() => { r.Key.Dispose(); r.Value.Dispose(); r.Remove.Dispose(); }));
                LayoutRows();
            };
            rows.Add(r);
            list.Controls.Add(r.Key); list.Controls.Add(r.Value); list.Controls.Add(r.Remove);
        }

        void LayoutRows()
        {
            int pad = Ui.S(8), head = Ui.S(HeadH);
            list.SetBounds(pad, head, Width - pad * 2, Math.Max(1, Height - head - pad));
            int rowH = Ui.S(RowH), usable = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, btn = Ui.S(24), w = (usable - btn - pad * 2) / 2;
            int scroll = list.AutoScrollPosition.Y;
            for (int i = 0; i < rows.Count; i++)
            {
                int y = scroll + i * rowH + (rowH - rows[i].Key.Height) / 2;
                rows[i].Key.SetBounds(0, y, w, rows[i].Key.Height);
                rows[i].Value.SetBounds(w + pad, y, w, rows[i].Value.Height);
                rows[i].Remove.Location = new Point(w * 2 + pad * 2, y + (rows[i].Key.Height - btn) / 2);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutRows(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            float w = Width / Ui.F, h = Height / Ui.F;
            Ui.Pill(g, new RectangleF(0, 0, w, h), Ui.Card, Ui.CardRadius);
            using (var b = new SolidBrush(Ui.Dim))
            {
                g.DrawString("Name", Ui.Small, b, 10, 6);
                g.DrawString(L.T("Wert"), Ui.Small, b, w / 2, 6);
            }
        }
    }

    // Full request builder for the custom target.
    class CustomDestinationForm : ResizableForm
    {
        const int Margin0 = Ui.Pad, Gap = 20, BarH = 82;
        static readonly string[] BodyValues = { "none", "form", "urlencoded", "json", "xml", "binary" };
        readonly Destination target;
        readonly UiInput name, url, fileField, bodyText, urlTemplate, thumb, deletion, error;
        readonly Segmented method, bodyType;
        readonly KeyValueEditor urlParams, headers, bodyArgs;
        readonly Toggle allowHttp;
        readonly PillButton testBtn, cancel;
        readonly PrimaryButton ok;
        readonly List<VarChip> chips = new List<VarChip>();
        readonly List<Tuple<string, RectangleF>> labels = new List<Tuple<string, RectangleF>>();
        string status = "", hint;
        bool statusBad;

        public bool Testing { get; private set; }
        public string Status { get { return status; } }
        public void TestStart() { RunTest(); }
        public void ShowNotice(string text) { status = text; Invalidate(); }
        public void TestSave() { Apply(target); DialogResult = DialogResult.OK; Close(); }

        public CustomDestinationForm(Destination d)
        {
            target = d;
            Text = L.T("Glint – Upload-Ziel");
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Font = Ui.Font;
            FitModal();

            name = Add(new UiInput(d.Name, false, 100));
            string current = string.IsNullOrWhiteSpace(d.Method) ? "POST" : d.Method.ToUpperInvariant();
            method = new Segmented(new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }, new[] { "GET", "POST", "PUT", "PATCH", "DEL" }, current, 280) { BackColor = Ui.Bg };
            Controls.Add(method);
            url = Add(new UiInput(d.Url, false, 100));
            allowHttp = new Toggle(d.AllowHttp == "1") { BackColor = Ui.Bg };
            Controls.Add(allowHttp);
            urlParams = new KeyValueEditor(CustomProvider.ParseList(d.UrlParams));
            headers = new KeyValueEditor(CustomProvider.ParseList(d.Headers));
            Controls.Add(urlParams); Controls.Add(headers);
            bodyType = new Segmented(BodyValues, new[] { L.T("Keiner"), "Form", "URL", "JSON", "XML", L.T("Binär") }, string.IsNullOrWhiteSpace(d.BodyType) ? "form" : d.BodyType, 300) { BackColor = Ui.Bg };
            bodyType.Changed = () => { ApplyBodyMode(); Reflow(); };
            Controls.Add(bodyType);
            bodyArgs = new KeyValueEditor(CustomProvider.ParseList(d.BodyArgs));
            Controls.Add(bodyArgs);
            bodyText = Add(new UiInput(d.BodyText, false, 100));
            bodyText.MakeMultiline();
            fileField = Add(new UiInput(string.IsNullOrWhiteSpace(d.Field) ? "file" : d.Field, false, 100));
            string legacy = string.IsNullOrWhiteSpace(d.JsonPath) ? "" : "{json:" + d.JsonPath.Trim() + "}";
            urlTemplate = Add(new UiInput(string.IsNullOrWhiteSpace(d.UrlTemplate) ? legacy : d.UrlTemplate, false, 100));
            thumb = Add(new UiInput(d.ThumbUrl, false, 100));
            deletion = Add(new UiInput(d.DeleteUrl, false, 100));
            error = Add(new UiInput(d.ErrorMsg, false, 100));

            var vars = new[] {
                Tuple.Create("{filename}", L.T("Dateiname")), Tuple.Create("{ext}", L.T("Dateiendung")), Tuple.Create("{mime}", L.T("Dateityp (MIME)")),
                Tuple.Create("{size}", L.T("Dateigröße in Bytes")), Tuple.Create("{base64}", L.T("Bild als Base64, für JSON oder XML")),
                Tuple.Create("{response}", L.T("Ganze Antwort")), Tuple.Create("{json:data.url}", L.T("Wert aus JSON, auch mit [0]")), Tuple.Create("{xml:/root/url}", L.T("Wert aus XML per XPath")),
                Tuple.Create("{regex:url=(.+)|1}", L.T("Regex auf die Antwort, mit Gruppennummer")), Tuple.Create("{header:Location}", L.T("Antwort-Header")) };
            foreach (var v in vars)
            {
                var chip = new VarChip(v.Item1, v.Item2);
                chip.Hovered = text => { hint = text; Invalidate(); };
                chips.Add(chip); Controls.Add(chip);
            }

            testBtn = new PillButton(L.T("Test-Upload"), 120, 36) { BackColor = Ui.Bg };
            testBtn.Changed = RunTest;
            cancel = new PillButton(L.T("Abbrechen"), 100, 36) { BackColor = Ui.Bg };
            cancel.Changed = () => { DialogResult = DialogResult.Cancel; Close(); };
            ok = new PrimaryButton(L.T("Speichern"), Ui.S(110, 36));
            ok.Changed = () => { Apply(target); if (string.IsNullOrWhiteSpace(target.Name)) target.Name = target.TypeName; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(testBtn); Controls.Add(cancel); Controls.Add(ok);

            ApplyBodyMode();
            Reflow();
        }

        UiInput Add(UiInput input) { input.BackColor = Ui.Bg; Controls.Add(input); return input; }

        void ApplyBodyMode()
        {
            string t = bodyType.Value;
            bodyArgs.Visible = t == "form" || t == "urlencoded";
            bodyText.Visible = t == "json" || t == "xml";
            fileField.Visible = t == "form";
        }

        static void Put(Control c, float x, float y, float w, float h = 28) { c.SetBounds(Ui.S(x), Ui.S(y), Ui.S(w), Ui.S(h)); }

        void Label(string text, float x, float y) { labels.Add(Tuple.Create(text, new RectangleF(x, y, 400, 18))); }

        void Reflow()
        {
            float W = ClientSize.Width / Ui.F, H = ClientSize.Height / Ui.F, L0 = Margin0, full = W - Margin0 * 2, col = (full - Gap) / 2, R0 = L0 + col + Gap;
            labels.Clear();

            Label("Name", L0, 54); Put(name, L0, 72, col);
            Label(L.T("Unverschlüsseltes HTTP erlauben"), R0, 54);
            allowHttp.Location = new Point(Ui.S(R0), Ui.S(75));

            Label(L.T("Methode"), L0, 106); method.Location = new Point(Ui.S(L0), Ui.S(124));
            Label(L.T("Anfrage-URL"), L0 + 292, 106); Put(url, L0 + 292, 124, full - 292);

            // bottom up: buttons, status, response templates, variables
            float buttons = H - 54, templates = buttons - 28 - 104, chipTop = templates - 62;
            float top = 158, tables = chipTop - top - 12, half = (tables - 22 - 8) / 2;

            Label(L.T("URL-Parameter"), L0, top); Put(urlParams, L0, top + 18, col, half - 4);
            Label("Header", L0, top + 18 + half); Put(headers, L0, top + 36 + half, col, half - 4);

            Label("Body", R0, top);
            bodyType.Location = new Point(Ui.S(R0), Ui.S(top + 18));
            bodyType.Width = Ui.S(col);
            bool hasFile = bodyType.Value == "form";
            float bodyTop = top + 54, bodyH = tables - 54 - (hasFile ? 52 : 0);
            Put(bodyArgs, R0, bodyTop, col, bodyH);
            Put(bodyText, R0, bodyTop, col, bodyH);
            if (hasFile) { Label(L.T("Dateifeld"), R0, bodyTop + bodyH + 4); Put(fileField, R0, bodyTop + bodyH + 22, col); }

            float x = L0, y = chipTop;
            foreach (var c in chips)
            {
                float cw = c.Width / Ui.F;
                if (x + cw > L0 + full) { x = L0; y += 28; }
                c.Location = new Point(Ui.S(x), Ui.S(y));
                x += cw + 4;
            }

            Label("URL", L0, templates); Put(urlTemplate, L0, templates + 18, col);
            Label("Thumbnail-URL", R0, templates); Put(thumb, R0, templates + 18, col);
            Label("Deletion-URL", L0, templates + 52); Put(deletion, L0, templates + 70, col);
            Label(L.T("Fehlermeldung"), R0, templates + 52); Put(error, R0, templates + 70, col);

            testBtn.Location = new Point(Ui.S(L0), Ui.S(buttons));
            ok.Location = new Point(ClientSize.Width - Ui.S(Margin0 + 110), Ui.S(buttons));
            cancel.Location = new Point(ok.Left - Ui.S(8 + 100), Ui.S(buttons));
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (ok != null) Reflow(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            using (var b = new SolidBrush(Ui.Text)) g.DrawString(L.T("Eigene"), Ui.Title, b, Margin0 - 2, 4);
            using (var b = new SolidBrush(Ui.Dim))
            {
                foreach (var l in labels) g.DrawString(l.Item1, Ui.Small, b, l.Item2);
                if (bodyType.Value == "none" || bodyType.Value == "binary")
                {
                    float x = bodyType.Left / Ui.F, y = (bodyType.Bottom / Ui.F) + 14;
                    g.DrawString(bodyType.Value == "none" ? L.T("Die Anfrage hat keinen Body.") : L.T("Das Bild wird direkt als Body gesendet."), Ui.Small, b, x, y);
                }
            }
            string text = hint ?? status;
            float sy = ClientSize.Height / Ui.F - 54 - 24;
            using (var b = new SolidBrush(hint == null && statusBad ? Ui.Accent : Ui.Dim))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, Ui.Small, b, new RectangleF(Margin0, sy, ClientSize.Width / Ui.F - Margin0 * 2, 20), sf);
        }

        void Apply(Destination d)
        {
            d.Name = name.Value; d.Method = method.Value; d.Url = url.Value;
            d.AllowHttp = allowHttp.On ? "1" : "";
            d.UrlParams = CustomProvider.StoreList(urlParams.Items);
            d.Headers = CustomProvider.StoreList(headers.Items);
            d.BodyType = bodyType.Value;
            d.BodyArgs = CustomProvider.StoreList(bodyArgs.Items);
            d.BodyText = bodyText.Value;
            d.Field = fileField.Value;
            d.UrlTemplate = urlTemplate.Value; d.JsonPath = "";
            d.ThumbUrl = thumb.Value; d.DeleteUrl = deletion.Value; d.ErrorMsg = error.Value;
        }

        async void RunTest()
        {
            if (Testing) return;
            Testing = true;
            var probe = target.Clone();
            Apply(probe);
            Log.Mask(probe);
            statusBad = false;
            status = L.T("Lädt ein 1×1-px-Testbild hoch…"); Invalidate();
            try
            {
                string link = await Task.Run(() =>
                {
                    using (var ms = new MemoryStream())
                    using (var b = new Bitmap(1, 1))
                    {
                        b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        return Uploader.Send(probe, ms.ToArray(), ".png", "image/png");
                    }
                });
                status = L.T("Erfolgreich: ") + link;
                if (!string.IsNullOrEmpty(CustomProvider.LastDeletion)) status += "  ·  Deletion: " + CustomProvider.LastDeletion;
                Log.Info("Test-Upload nach " + probe.Name + ": " + link);
            }
            catch (Exception ex)
            {
                statusBad = true;
                status = L.T("Fehlgeschlagen, Details stehen im Log");
                Log.Error("Test-Upload nach " + probe.Name + " (" + probe.TypeName + ")", ex);
            }
            Testing = false;
            if (!IsDisposed) Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref m, k);
        }
    }
}
