using AngleSharp.Html.Dom;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Interfaces;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace EchidnaJav.Scraper;

public class ActressJavModel : ActressScraperBase
{
    public ActressJavModel( ILogger<ActressScraperBase> logger, ISilentWebViewSandbox sandbox)
        : base(logger, sandbox)
    {
    }

    public override async Task ScrapeAsync(string actressName, LanguageType language)
    {
        Actress = new ActressData { Name = actressName };
        m_language = language;
        m_parsingSuccessful = false;
        SearchNotFound = false;
        ImageSource = string.Empty;
        if (!IsLanguageSupported()) return;

        // 1. Generate name variations (Name-Surname and Surname-Name)
        var slugs = GetNameVariations(Actress.Name);

        foreach (var slug in slugs)
        {
            string url = $"https://javmodel.com/jav/{slug}/";

            await ScrapeWebsiteAsync(url);

            // If we found the page and parsing worked, we are done
            if (m_parsingSuccessful && !SearchNotFound)
            {
                break;
            }

            // Reset flags for the next attempt if the first variation 404'd
            m_parsingSuccessful = false;
            SearchNotFound = false;
        }

    }
    protected override bool IsValidDataParsed()
    {
        return m_parsingSuccessful || SearchNotFound;
    }

    private List<string> GetNameVariations(string fullName)
    {
        var variations = new List<string>();
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            // Variation 1: name-surname
            variations.Add($"{parts[0].ToLower()}-{parts[1].ToLower()}");
            // Variation 2: surname-name
            variations.Add($"{parts[1].ToLower()}-{parts[0].ToLower()}");
        }
        else
        {
            variations.Add(fullName.Replace(" ", "-").ToLower());
        }

        return variations;
    }

    protected override void ParseDocument(IHtmlDocument document)
    {
        // 1. Check for "Page not found"
        if (document.Title?.Contains("Page not found", StringComparison.OrdinalIgnoreCase) == true ||
            document.Body?.TextContent.Contains("Page not found") == true)
        {
            SearchNotFound = true;
            return;
        }

        // 2. Scrape Image (using CSS selector for accuracy)
        var imgElement = document.QuerySelector(".flq-card-image img");

        if (imgElement != null)
        {
            // Some sites use data-src for lazy loading; we check both just in case
            string? src = imgElement.GetAttribute("src");
            string? dataSrc = imgElement.GetAttribute("data-src");

            ImageSource = (dataSrc ?? src)?.Trim();

        }

        // 3. Japanese Name
        var jpNameElement = document.QuerySelector("h2.h5.mb-4");
        if (jpNameElement != null)
        {
            Actress.JapaneseName = jpNameElement.TextContent.Trim();
        }

        // 4. Table Metadata Parsing (More robust than NextSibling)
        var metaRows = document.QuerySelectorAll("td.flq-color-meta");
        foreach (var labelCell in metaRows)
        {
            var label = labelCell.TextContent.Trim();
            var valueCell = labelCell.NextElementSibling; // AngleSharp helper to skip text nodes
            if (valueCell == null) continue;

            string value = valueCell.TextContent.Trim();
            if (string.IsNullOrEmpty(value) || value == "Unknown") continue;

            switch (label)
            {
                case "Birthday":
                    if (DateTime.TryParse(value, out var birthday))
                    {
                        Actress.DobDay = birthday.Day;
                        Actress.DobMonth = birthday.Month;
                        Actress.DobYear = birthday.Year;
                    }
                    break;

                case "Blood Type":
                    Actress.BloodType = value;
                    break;

                case "Breast":
                    Actress.Bust = ScraperParsingExtensions.ParseInitialDigits(value);
                    break;

                case "Waist":
                    Actress.Waist = ScraperParsingExtensions.ParseInitialDigits(value);
                    break;

                case "Hips":
                    Actress.Hips = ScraperParsingExtensions.ParseInitialDigits(value);
                    break;

                case "Height":
                    Actress.Height = ScraperParsingExtensions.ParseInitialDigits(value);
                    break;
            }
        }

        m_parsingSuccessful = true;
    }

    

    protected override bool IsLanguageSupported() => m_language == LanguageType.English;
}