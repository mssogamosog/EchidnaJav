using AngleSharp.Html.Dom;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace EchidnaJav.Scraper
{
    public class MovieJavDatabase : MovieScraperBase
    {
        private readonly IMovieIdService _movieIdService;
        private static readonly List<(string Search, string Replace)> _censored = new()
        {
            // Add your censored replacements here, e.g.:
            // ("censored text", "clean text")
        };

        public MovieJavDatabase(ILogger<MovieScraperBase> logger, IMovieIdService movieIdService, ISilentWebViewSandbox sandbox)
        : base(logger, sandbox)
        {
            _movieIdService = movieIdService ?? throw new ArgumentNullException(nameof(movieIdService));
        }

        public override async Task ScrapeAsync(string movieID, LanguageType language)
        {
            // 1. State Reset (Critical for DI)
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata(movieID);
            m_language = language;

            if (!IsLanguageSupported())
                return;

            string cleanID = movieID.ToLower();
            string url = $"https://www.javdatabase.com/movies/{cleanID}/";

            // Attempt 1: Standard ID
            await ScrapeWebsiteAsync(url);

            // Attempt 2: If Page Not Found, try alternate ID formats
            if (SearchNotFound)
            {
                var candidateIds = new List<string>();
                string altID = GetAltMovieID(movieID);

                if (altID != cleanID)
                {
                    candidateIds.Add(altID);
                    candidateIds.Add("3" + cleanID);
                    candidateIds.Add("3" + altID);
                }

                foreach (var id in candidateIds)
                {
                    SearchNotFound = false;
                    m_parsingSuccessful = false;

                    await ScrapeWebsiteAsync($"https://www.javdatabase.com/movies/{id}/");

                    if (!SearchNotFound && m_parsingSuccessful)
                        break;
                }
            }
        }

        protected override bool IsLanguageSupported() => m_language == LanguageType.English;

        protected override bool IsValidDataParsed() => m_parsingSuccessful || SearchNotFound;

        protected override void ParseDocument(IHtmlDocument document)
        {
            // 1. Check 404
            if (document.Body?.TextContent.Contains("Page not found.") == true)
            {
                SearchNotFound = true;
                m_parsingSuccessful = true;
                return;
            }

            // 2. Extract Cover Image via CSS Selector
            var coverImg = document.QuerySelector($"img[alt^='{Metadata.UniqueID.Value}' i]");
            if (coverImg != null)
            {
                string srcAttr = coverImg.GetAttribute("src");
                if (!string.IsNullOrEmpty(srcAttr) && srcAttr.StartsWith("http"))
                {
                    ImageSource = srcAttr;
                }
            }

            // 3. Extract Validation ID (Ensure we landed on the right page)
            var dvdIdLabel = document.QuerySelectorAll("div").FirstOrDefault(e => e.TextContent.Trim() == "DVD ID:");
            if (dvdIdLabel?.NextElementSibling != null)
            {
                string scrapedId = dvdIdLabel.NextElementSibling.TextContent.Trim();
                if (!_movieIdService.MovieIDEquals(Metadata.UniqueID.Value, scrapedId))
                {
                    // Wrong page landed, abort
                    Metadata = new MovieMetadata(Metadata.UniqueID.Value);
                    ImageSource = string.Empty;
                    return;
                }
            }

            // 4. Extract Title
            var titleElem = document.QuerySelector(".mb-1");
            if (titleElem != null && titleElem.TextContent.StartsWith("Title:"))
            {
                string title = titleElem.TextContent.Substring(6).Trim();
                title = FixCensored(title);
                if (title.StartsWith(Metadata.UniqueID.Value, StringComparison.OrdinalIgnoreCase))
                {
                    title = title.Substring(Metadata.UniqueID.Value.Length).Trim();
                }
                Metadata.Title = title;
            }

            // 5. Extract Actors via clean hierarchy selector
            var actressLinks = document.QuerySelectorAll("h4:contains('Actress/Idols') + div.row a");
            foreach (var link in actressLinks)
            {
                string name = link.TextContent.Trim();
                if (!string.IsNullOrEmpty(name) && !Metadata.Actors.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    Metadata.Actors.Add(new ActorData { Name = name });
                }
            }

            // 6. Extract Metadata Paragraphs (Runtime, Release Date)
            var paragraphs = document.QuerySelectorAll("p");
            foreach (var p in paragraphs)
            {
                string text = p.TextContent.Trim();
                if (text.StartsWith("Release Date:"))
                {
                    Metadata.Premiered = text.Substring(13).Trim();
                    int year = ScraperParsingExtensions.ParseInitialDigits(Metadata.Premiered);
                    if (year != -1) Metadata.Year = year;
                }
                else if (text.StartsWith("Runtime:"))
                {
                    int runtime = ScraperParsingExtensions.ParseInitialDigits(text.Substring(8).Trim());
                    if (runtime != -1) Metadata.Runtime = runtime;
                }
            }

            // 7. Extract Tags (Genres, Studios, Labels)
            var tagLinks = document.QuerySelectorAll("a[rel='tag'][href^='https://www.javdatabase.com/']");
            foreach (var tag in tagLinks)
            {
                string href = tag.GetAttribute("href");
                string content = tag.TextContent.Trim();
                if (string.IsNullOrEmpty(content)) continue;

                if (href.Contains("/genres/"))
                {
                    string genreText = FixCensored(content);
                    if (!Metadata.Genres.Contains(genreText)) Metadata.Genres.Add(genreText);
                }
                else if (href.Contains("/studios/") && string.IsNullOrEmpty(Metadata.Studio))
                    Metadata.Studio = content;
                else if (href.Contains("/labels/") && string.IsNullOrEmpty(Metadata.Label))
                    Metadata.Label = content;
                else if (href.Contains("/directors/") && string.IsNullOrEmpty(Metadata.Director))
                    Metadata.Director = content;
                else if (href.Contains("/series/") && string.IsNullOrEmpty(Metadata.Series))
                    Metadata.Series = FixCensored(content);
            }

            m_parsingSuccessful = true;
        }

        private string GetAltMovieID(string movieID)
        {
            var match = Regex.Match(movieID.ToLower(), @"^([a-z0-9-]+?)([0-9]+)$");
            if (match.Success)
            {
                string alpha = match.Groups[1].Value.Replace("-", "").Trim();
                string numeric = match.Groups[2].Value;
                return $"{alpha}-0{numeric}"; // e.g. dsvr-01655
            }
            return movieID;
        }

        private string FixCensored(string original)
        {
            string clean = original;
            foreach (var pair in _censored)
                clean = clean.Replace(pair.Search, pair.Replace);
            return clean;
        }
    }
}
