using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Scraper.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EchidnaJav.Scraper
{
    public class ActressJavDatabase : ActressScraperBase
    {
        public ActressJavDatabase(ILogger<ActressScraperBase> logger) : base(logger)
        {
        }

        public override async Task ScrapeAsync(string actressName, LanguageType language)
        {
            // 1. Reset state (Critical for DI if this class is reused)
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Actress = new ActressData { Name = actressName };
            m_language = language;

            if (IsLanguageSupported() == false)
                return;

            string formattedName = Actress.Name.Replace(' ', '-').ToLower();
            await ScrapeWebsiteAsync("https://www.javdatabase.com/idols/" + formattedName + "/");
        }    

        protected override bool IsLanguageSupported()
        {
            return m_language == LanguageType.English;
        }

        // 3. Define what constitutes a successful parse to stop the retry loop
        protected override bool IsValidDataParsed()
        {
            return m_parsingSuccessful || SearchNotFound;
        }

        // 4. Your existing AngleSharp Logic
        protected override void ParseDocument(IHtmlDocument document)
        {
            foreach (var element in document.All)
            {
                if (element.TextContent.Contains("Page not found."))
                {
                    SearchNotFound = true;
                    m_parsingSuccessful = true;
                    return;
                }

                if (element.NodeName == "IMG")
                {
                    string srcAttr = element.GetAttribute("src");
                    if (!string.IsNullOrEmpty(srcAttr) && srcAttr.StartsWith("http"))
                    {
                        if (string.IsNullOrEmpty(ImageSource) && srcAttr.Contains("idolimages/full/"))
                        {
                            ImageSource = srcAttr.Trim();
                        }
                    }
                }

                if (element.NodeName == "H1" && element.TextContent.StartsWith(Actress.Name))
                {
                    if (element.Parent == null) continue;

                    string content = element.Parent.TextContent;
                    if (string.IsNullOrEmpty(content)) continue;

                    Actress.JapaneseName = Parse(content, "JP: ");

                    string dobText = Parse(content, "DOB: ");
                    ScraperParsingExtensions.StringToDateTime(dobText, out int year, out int month, out int day);
                    Actress.DobYear = year;
                    Actress.DobMonth = month;
                    Actress.DobDay = day;

                    string height = Parse(content, "Height: ");
                    Actress.Height = ScraperParsingExtensions.ParseInitialDigits(height);

                    string measurements = Parse(content, "Measurements: ");
                    var matches = Regex.Matches(measurements, @"\d+");

                    if (matches.Count >= 3)
                    {
                        Actress.Bust = int.Parse(matches[0].Value);
                        Actress.Waist = int.Parse(matches[1].Value);
                        Actress.Hips = int.Parse(matches[2].Value);
                    }

                    string cupText = Parse(content, "Cup: ");
                    string[] cups = cupText.Split(' ');
                    if (cups.Length > 0 && string.Compare(cups[0], "Unknown", true) != 0)
                        Actress.Cup = cups[0][0].ToString();

                    Actress.BloodType = Parse(content, "Blood: ");

                    // Mark success so the loop knows it can finish
                    m_parsingSuccessful = true;
                }
            }
        }

        // 5. Your existing string parser helper
        private string Parse(string text, string search)
        {
            string retVal = string.Empty;
            int index = text.IndexOf(search, StringComparison.Ordinal);
            if (index == -1) return retVal;

            string substr = text.Substring(index + search.Length);
            if (substr.Length == 0) return retVal;

            int wsIndex = substr.IndexOf(' ');
            int nlIndex = substr.IndexOf('\n');

            if (wsIndex == -1 && nlIndex == -1) index = substr.Length - 1;
            else if (wsIndex == -1) index = nlIndex;
            else if (nlIndex == -1) index = wsIndex;
            else index = Math.Min(wsIndex, nlIndex);

            return substr.Substring(0, index);
        }
    }
}