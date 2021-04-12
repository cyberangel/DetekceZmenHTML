using DetekceZmenHTML.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Xml;

namespace DetekceZmenHTML
{
    class Program
    {
        static List<OdkazSDatumem> _odkazySDatumyNove;
        static List<OdkazSDatumem> _odkazySDatumyPuvodni;

        /// <summary>
        /// Pro parsování HTML používá HtmlAgilityPack (nuget)
        /// </summary>
        static void Main()
        {
            using (var webClient = new WebClient())
            {
                webClient.Encoding = Encoding.UTF8;

                // stáhne aktuální obsah stránky
                var obsahNoveStranky = webClient.DownloadString(Settings.Default.UrlStranky);
                if (obsahNoveStranky != null)
                {
                    if (obsahNoveStranky.Length > 200)
                    {
                        string obsahPuvodniStranky = string.Empty;
                        // pokud existuje soubor s obsahem původní stránky, načte jej
                        if (File.Exists(Settings.Default.NazevSouboruHTMLkUlozeni))
                        {
                            obsahPuvodniStranky = File.ReadAllText(Settings.Default.NazevSouboruHTMLkUlozeni);
                        }
                        else
                        {
                            obsahPuvodniStranky = obsahNoveStranky;
                        }

                        // vytáhne sledované odkazy s datumy
                        _odkazySDatumyNove = VytahniDatumyZmenSledovanychOdkazu(obsahNoveStranky);
                        _odkazySDatumyPuvodni = VytahniDatumyZmenSledovanychOdkazu(obsahPuvodniStranky);

                        // pokud se nějaký datum liší, pošle e-mail
                        if (_odkazySDatumyNove.Any(n => !n.Equals(_odkazySDatumyPuvodni.First(p => p.Odkaz == n.Odkaz))))
                        {
                            PosliEmail();
                        }
                        File.WriteAllText(Settings.Default.NazevSouboruHTMLkUlozeni, obsahNoveStranky);
                    }
                }
            }
        }

        /// <summary>
        /// Pošle e-mail podle konfigurace
        /// </summary>
        static void PosliEmail()
        {
            using (var smtp = new SmtpClient(Settings.Default.SmtpServer))
            {
                if (Settings.Default.SmtpUseSSL)
                    smtp.EnableSsl = true;
                smtp.Credentials = new NetworkCredential(Settings.Default.SmtpUsername, Settings.Default.SmtpPassword);
                if (Settings.Default.SmtpPort != 0)
                    smtp.Port = Settings.Default.SmtpPort;

                using (var message = new MailMessage(Settings.Default.SmtpUsername, Settings.Default.EmailAdresa))
                {
                    message.Subject = Settings.Default.EmailPredmet;
                    message.IsBodyHtml = true;
                    var body = new StringBuilder("<html><head></head><body>");
                    body.AppendLine("<div>");
                    body.AppendLine(Settings.Default.EmailObsah);
                    body.AppendLine("</div>");

                    body.AppendLine("<div>Změny:</div>");
                    foreach (var odkaz in _odkazySDatumyNove)
                    {
                        var puvodniOdkaz = _odkazySDatumyPuvodni.First(o => o.Odkaz == odkaz.Odkaz);
                        if (puvodniOdkaz.Datum != odkaz.Datum)
                        {
                            body.AppendFormat("<div>Odkaz: {0} Datum: {1:d} -&gt; {2:d}", odkaz.Odkaz, puvodniOdkaz.Datum, odkaz.Datum);
                            body.AppendLine("</div>");
                        }
                    }
                    body.AppendLine("</body></html>");
                    message.Body = body.ToString();

                    smtp.Send(message);
                }
            }
        }

        /// <summary>
        /// Hledá sledované odkazy a vytahuje z příslušného tagu datum
        /// </summary>
        /// <param name="htmlZdroj">HTML zdroj</param>
        /// <returns></returns>
        static List<OdkazSDatumem> VytahniDatumyZmenSledovanychOdkazu(string htmlZdroj)
        {
            List<OdkazSDatumem> sledovaneOdkazy = new List<OdkazSDatumem>();
            sledovaneOdkazy.Add(new OdkazSDatumem() { Odkaz = "/?q=katalog/databaze-lecivych-pripravku-dlp" });
            sledovaneOdkazy.Add(new OdkazSDatumem() { Odkaz = "/?q=katalog/zmeny-ve-zverejnovanych-prehledech" });
            sledovaneOdkazy.Add(new OdkazSDatumem() { Odkaz = "/?q=katalog/datove-rozhrani-k-databazi-lecivych-pripravku-dlp" });

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(htmlZdroj);

            var root = doc.DocumentNode;

            // hledá tagy DIV s atributem about, který obsahuje sledované odkazy
            var tagyDiv = root.SelectNodes("//div[@about]");
            foreach (var tag in tagyDiv)
            {
                var attAbout = tag.Attributes["about"];
                if (attAbout != null
                    && !string.IsNullOrEmpty(attAbout.Value))
                {
                    if (sledovaneOdkazy.Any(o => o.Odkaz == attAbout.Value))
                    {
                        // v tagu DIV hledá tag SPAN, který obsahuje atributy property="dc:date" a datatype="xsd:dateTime"
                        // a pomocí XmlConvert vytáhne datum z atributu "content"
                        var tagySpan = tag.SelectNodes("div//span[@property='dc:date' and @datatype='xsd:dateTime']");
                        foreach (var tagSpan in tagySpan)
                        {
                            var datum = XmlConvert.ToDateTime(tagSpan.Attributes["content"].Value, XmlDateTimeSerializationMode.Unspecified);
                            sledovaneOdkazy.First(o => o.Odkaz == attAbout.Value).Datum = datum;
                        }
                    }
                }
            }

            return sledovaneOdkazy;
        }
    }

    class OdkazSDatumem : IEquatable<OdkazSDatumem>
    {
        public string Odkaz { get; set; }
        public DateTime Datum { get; set; }

        public bool Equals(OdkazSDatumem other)
        {
            if (other == null) return false;
            return Datum == other.Datum &&
                string.Equals(Odkaz, other.Odkaz);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals(obj as OdkazSDatumem);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 13;
                var odkazHashCode = !string.IsNullOrEmpty(Odkaz) ? Odkaz.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ odkazHashCode;
                hashCode = (hashCode * 397) ^ Datum.GetHashCode();
                return hashCode;
            }
        }
    }
}
