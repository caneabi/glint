using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Glint
{
    class DestinationForm : ResizableForm
    {
        readonly Destination target;
        readonly Dictionary<string, UiInput> inputs = new Dictionary<string, UiInput>();
        readonly SettingsPage page;
        readonly PillButton testBtn;
        string status = "";
        bool statusBad;
        Toggle allowHttp;

        public static Form Open(Destination d) { return d.Type == "custom" ? (Form)new CustomDestinationForm(d) : new DestinationForm(d); }

        public DestinationForm(Destination d)
        {
            target = d;
            Text = L.T("Glint – Upload-Ziel");
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Font = Ui.Font;
            var rows = new List<Tuple<string, string, bool>> { Tuple.Create("name", "Name", false) };
            if (d.Type == "zipline")
            {
                rows.Add(Tuple.Create("url", L.T("Zipline-URL"), false));
                rows.Add(Tuple.Create("token", "Token", true));
                rows.Add(Tuple.Create("zDomain", L.T("Domain überschreiben"), false));
                rows.Add(Tuple.Create("zMaxViews", L.T("Max. Aufrufe"), false));
                rows.Add(Tuple.Create("zExpiry", L.T("Löschen nach (z. B. 7d)"), false));
                rows.Add(Tuple.Create("zPassword", L.T("Passwort"), true));
                rows.Add(Tuple.Create("zFolder", L.T("Ordner-ID"), false));
                rows.Add(Tuple.Create("zCompressQuality", L.T("Qualität (0–100)"), false));
            }
            else if (d.Type == "discord")
            {
                rows.Add(Tuple.Create("token", L.T("Webhook-URL"), true));
                if (d.Color == "") { d.Color = DiscordProvider.DefaultColor; d.Footer = DiscordProvider.DefaultFooter; d.FooterIcon = DiscordProvider.DefaultFooterIcon; }
                rows.Add(Tuple.Create("title", L.T("Titel"), false));
                rows.Add(Tuple.Create("description", L.T("Beschreibung"), false));
                rows.Add(Tuple.Create("color", L.T("Farbe"), false));
                rows.Add(Tuple.Create("footer", "Footer", false));
                rows.Add(Tuple.Create("footerIcon", L.T("Footer-Icon (URL)"), false));
            }
            else if (d.Type == "r2" || d.Type == "s3")
            {
                if (d.Type == "r2") rows.Add(Tuple.Create("account", "Account-ID", false));
                else { rows.Add(Tuple.Create("url", "Endpoint", false)); rows.Add(Tuple.Create("region", "Region", false)); }
                rows.Add(Tuple.Create("accessKey", "Access Key", false));
                rows.Add(Tuple.Create("secretKey", "Secret Key", true));
                rows.Add(Tuple.Create("bucket", "Bucket", false));
                rows.Add(Tuple.Create("publicUrl", L.T("Öffentliche URL"), false));
                rows.Add(Tuple.Create("prefix", L.T("Ordner (optional)"), false));
            }
            int extra = d.Type == "zipline" ? 5 : d.Type == "r2" || d.Type == "discord" ? 0 : 1;
            FitModal();
            // Rows scroll, the buttons stay put.
            footer = new PassPanel { Dock = DockStyle.Bottom, Height = Ui.S(FooterH), Width = ClientSize.Width };
            var body = new PassPanel { Dock = DockStyle.Fill, AutoScroll = true };
            page = new SettingsPage(d.TypeName, ClientSize) { Location = Point.Empty };
            body.Controls.Add(page);
            Controls.Add(body); Controls.Add(footer);
            body.Resize += delegate { FitPage(body); };
            if (d.Type == "discord") AddVariables();
            foreach (var r in rows)
            {
                var input = new UiInput(d.Get(r.Item1), r.Item3, 300);
                inputs[r.Item1] = input;
                if (r.Item1 == "color")
                {
                    var swatch = new ColorSwatch();
                    swatch.Current = () =>
                    {
                        Color c;
                        return ColorPicker.TryParse(input.Value, out c) ? c : Ui.Well;
                    };
                    input.Changed = swatch.Invalidate;
                    swatch.Changed = () =>
                    {
                        Color c;
                        if (!ColorPicker.TryParse(input.Value, out c)) c = Brand.Pixel;
                        using (var picker = new ColorPicker(c))
                            if (picker.ShowDialog(this) == DialogResult.OK) input.Value = Colors.Hex(picker.Value);
                    };
                    page.AddStretch(r.Item2, input, swatch);
                }
                else page.AddStretch(r.Item2, input);
            }
            if (d.Type == "zipline")
            {
                var type = new Segmented(new[] { "", "jpg", "png", "webp", "jxl" }, new[] { "Server", "JPG", "PNG", "WebP", "JXL" }, d.ZCompressType, 300);
                page.AddStretch(L.T("Kompression: Format"), type);
                extras.Add(t => t.ZCompressType = type.Value);
                var format = new Segmented(new[] { "", "random", "uuid", "date", "name", "gfycat", "random-words" }, new[] { "Server", "Random", "UUID", L.T("Datum"), "Name", "Gfycat", L.T("Wörter") }, d.ZFormat, 300);
                page.AddStretch(L.T("Namensformat"), format);
                extras.Add(t => t.ZFormat = format.Value);
                var noExt = new Toggle(d.ZExtensionless == "1");
                page.AddRow(L.T("URL ohne Dateiendung"), noExt);
                extras.Add(t => t.ZExtensionless = noExt.On ? "1" : "");
                var original = new Toggle(d.ZOriginalName == "1");
                page.AddRow(L.T("Originalnamen beibehalten"), original);
                extras.Add(t => t.ZOriginalName = original.On ? "1" : "");
            }
            if (extra >= 1)
            {
                allowHttp = new Toggle(d.AllowHttp == "1");
                page.AddRow(L.T("Unverschlüsseltes HTTP erlauben"), allowHttp);
            }
            FitPage(body);
            int by = Ui.S(FooterH - 54);
            testBtn = new PillButton(L.T("Test-Upload"), 120, 36) { BackColor = Ui.Bg, Location = new Point(Ui.S(Ui.Pad), by), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            testBtn.Changed = RunTest;
            var cancel = new PillButton(L.T("Abbrechen"), 100, 36) { BackColor = Ui.Bg, Location = new Point(ClientSize.Width - Ui.S(Ui.Pad + 110 + 8 + 100), by), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            cancel.Changed = () => { DialogResult = DialogResult.Cancel; Close(); };
            var ok = new PrimaryButton(L.T("Speichern"), Ui.S(110, 36)) { Location = new Point(ClientSize.Width - Ui.S(Ui.Pad + 110), by), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            ok.Changed = () => { Apply(target); if (string.IsNullOrWhiteSpace(target.Name)) target.Name = target.TypeName; DialogResult = DialogResult.OK; Close(); };
            footer.Controls.Add(testBtn); footer.Controls.Add(cancel); footer.Controls.Add(ok);
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                string text = hint ?? status;
                Ui.Prepare(e.Graphics);
                e.Graphics.ScaleTransform(Ui.F, Ui.F);
                using (var b = new SolidBrush(hint == null && statusBad ? Ui.Accent : Ui.Dim))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap })
                    e.Graphics.DrawString(text, Ui.Small, b, new RectangleF(Ui.Pad, 4, footer.Width / Ui.F - Ui.Pad * 2, 22), sf);
            };
        }

        const int FooterH = 82;
        readonly PassPanel footer;
        string hint;

        void FitPage(Panel body)
        {
            page.Height = Math.Max(page.ContentHeight, body.ClientSize.Height);
            page.Width = body.ClientSize.Width;
        }
        readonly List<Action<Destination>> extras = new List<Action<Destination>>();

        void AddVariables()
        {
            var vars = new[] {
                Tuple.Create("{filename}", L.T("Dateiname")), Tuple.Create("{ext}", L.T("Dateiendung")), Tuple.Create("{mime}", L.T("Dateityp (MIME)")),
                Tuple.Create("{size}", L.T("Dateigröße")), Tuple.Create("{created}", L.T("Erstellt: Discord-Zeitstempel")), Tuple.Create("{created:R}", L.T("Erstellt: relativ, z. B. vor 2 Minuten")) };
            int x = Ui.S(Ui.Pad - 1);
            foreach (var v in vars)
            {
                var chip = new VarChip(v.Item1, v.Item2) { Location = new Point(x, Ui.S(38)) };
                chip.Hovered = text => { hint = text; footer.Invalidate(); };
                page.Controls.Add(chip);
                x += chip.Width + Ui.S(4);
            }
        }

        void Apply(Destination d)
        {
            foreach (var i in inputs) d.Set(i.Key, i.Value.Value);
            foreach (var a in extras) a(d);
            d.AllowHttp = allowHttp != null && allowHttp.On ? "1" : "";
        }

        public string Status { get { return status; } }
        public bool Testing { get; private set; }
        public void TestFill(string field, string value) { inputs[field].Value = value; }
        public void TestStart() { RunTest(); }
        public void TestSave() { Apply(target); DialogResult = DialogResult.OK; Close(); }

        async void RunTest()
        {
            if (Testing) return;
            Testing = true;
            var probe = target.Clone();
            Apply(probe);
            Log.Mask(probe);
            statusBad = false;
            status = L.T("Lädt ein 1×1-px-Testbild hoch…"); footer.Invalidate();
            try
            {
                string url = await Task.Run(() =>
                {
                    using (var ms = new MemoryStream())
                    using (var b = new Bitmap(1, 1))
                    {
                        b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        return Uploader.Send(probe, ms.ToArray(), ".png", "image/png");
                    }
                });
                status = L.T("Erfolgreich: ") + url;
                Log.Info("Test-Upload nach " + probe.Name + ": " + url);
            }
            catch (Exception ex)
            {
                statusBad = true;
                status = L.T("Fehlgeschlagen, Details stehen im Log");
                Log.Error("Test-Upload nach " + probe.Name + " (" + probe.TypeName + ")", ex);
            }
            Testing = false;
            if (!IsDisposed) footer.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref m, k);
        }
    }
}
