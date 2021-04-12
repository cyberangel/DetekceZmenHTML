using DetekceZmenHTML.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Xml;

namespace DetekceZmenHTML
{
    class Program
    {
        static void Main()
        {
            using (var webClient = new WebClient())
            {
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
                        var odkazySDatumyNove = VytahniDatumyZmenSledovanychOdkazu(obsahNoveStranky);
                        var odkazySDatumyPuvodni = VytahniDatumyZmenSledovanychOdkazu(obsahPuvodniStranky);

                        // pokud se datumy liší, pošle e-mail
                        if (odkazySDatumyNove != odkazySDatumyPuvodni)
                        {
                            File.WriteAllText(Settings.Default.NazevSouboruHTMLkUlozeni, obsahNoveStranky);
                            PosliEmail();
                        }
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
                    message.Body = Settings.Default.EmailObsah;

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
            sledovaneOdkazy.Add(new OdkazSDatumem() { Odkaz = "?q=katalog/databaze-lecivych-pripravku-dlp" });
            sledovaneOdkazy.Add(new OdkazSDatumem() { Odkaz = "?q=katalog/zmeny-ve-zverejnovanych-prehledech" });
            sledovaneOdkazy.Add(new OdkazSDatumem() { Odkaz = "?q=katalog/datove-rozhrani-k-databazi-lecivych-pripravku-dlp" });

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(htmlZdroj);

            var root = doc.DocumentNode;
            var tagyDiv = root.SelectNodes("/div");
            foreach (var tag in tagyDiv)
            {
                var attAbout = tag.Attributes["about"];
                if (attAbout != null
                    && !string.IsNullOrEmpty(attAbout.Value))
                {
                    if (sledovaneOdkazy.Any(o => o.Odkaz == attAbout.Value))
                    {
                        var tagySpan = tag.SelectNodes("//span");
                        foreach (var tagSpan in tagySpan)
                        {
                            if (tagSpan.HasAttributes)
                            {
                                if (tagSpan.Attributes["property"] != null
                                    && tagSpan.Attributes["property"].Value == "dc:date"
                                    && tagSpan.Attributes["datatype"].Value == "xsd:dateTime")
                                {
                                    var datum = XmlConvert.ToDateTime(tagSpan.Attributes["content"].Value, XmlDateTimeSerializationMode.Unspecified);
                                    sledovaneOdkazy.First(o => o.Odkaz == attAbout.Value).Datum = datum;
                                }
                            }
                        }
                    }
                }
            }

            return sledovaneOdkazy;
        }
    }

    class OdkazSDatumem : IComparable<OdkazSDatumem>
    {
        public string Odkaz { get; set; }
        public DateTime Datum { get; set; }

        public int CompareTo(OdkazSDatumem other)
        {
            if (other == null) return 1;

            int result = Odkaz.CompareTo(other.Odkaz);
            if (result == 0)
                result = Datum.CompareTo(other.Datum);
            return result;
        }
    }
}
