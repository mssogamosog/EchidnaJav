using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EchidnaJav.Scraper
{
    public sealed class MovieSupJav : MovieScraperBase
    {
        private readonly IMovieIdService _movieIdService;
        private string _pageLink = string.Empty;
        private string _query = string.Empty;

        // Natively tracks localized Referer progression state strictly for this scraping task instance
        private string _currentReferrer = string.Empty;

        protected override bool EnableCloudflareHandling => true;

        public MovieSupJav(ILogger<MovieScraperBase> logger, IMovieIdService movieIdService, ISilentWebViewSandbox sandbox)
            : base(logger, sandbox)
        {
            _movieIdService = movieIdService ?? throw new ArgumentNullException(nameof(movieIdService));
        }

        public override async Task ScrapeAsync(string movieID, LanguageType language)
        {
            // 1. Reset transient states safely for runtime collection loops
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata(movieID);
            m_language = language;
            _pageLink = string.Empty;
            _currentReferrer = string.Empty;

            if (!IsLanguageSupported())
                return;

            _query = GetSupJavQuery(movieID);
            string searchUrl = $"https://supjav.com/?s={Uri.EscapeDataString(_query)}";

            // Establish Phase 1 origin state
            _currentReferrer = "https://supjav.com/";
            await ScrapeWebsiteAsync(searchUrl);

            // Phase 2 Extraction: Target detail path found inside Phase 1 execution
            if (!string.IsNullOrEmpty(_pageLink))
            {
                m_parsingSuccessful = false;
                SearchNotFound = false;

                // Provide direct referral provenance back to the site engine to bypass hotlink firewalls
                _currentReferrer = searchUrl;
                await ScrapeWebsiteAsync(_pageLink);
            }
        }

        // 🔥 NATIVE INTERCEPT: Executes specifically for SupJav queries to supply custom target headers
        protected override void CustomizeHttpRequest(HttpRequestMessage request, string targetUrl)
        {
            request.Headers.Add("Sec-Fetch-Dest", "document");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");

            if (!string.IsNullOrEmpty(_currentReferrer))
            {
                request.Headers.Add("Referer", _currentReferrer);
            }
        }

        protected override bool IsLanguageSupported() => true;

        protected override bool IsValidDataParsed() => m_parsingSuccessful || SearchNotFound;

        protected override void ParseDocument(IHtmlDocument document)
        {
            // Route Phase 1 Document handling
            if (CheckResultsPage(document))
                return;

            // Route Phase 2 Document handling
            var metaBlock = document.QuerySelector("div.post-meta");
            if (metaBlock == null)
            {
                SearchNotFound = true;
                m_parsingSuccessful = true;
                return;
            }

            // --- Cover Image Extraction ---
            var imgElem = metaBlock.QuerySelector("img.img");
            if (imgElem != null)
            {
                string srcAttr = imgElem.GetAttribute("src");
                if (!string.IsNullOrEmpty(srcAttr) && srcAttr.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    ImageSource = srcAttr.Trim();
                }
            }

            // --- Cleaned Title Extraction ---
            var titleElem = metaBlock.QuerySelector("h2");
            if (titleElem != null)
            {
                string title = titleElem.TextContent.Trim();

                string id = _movieIdService.ParseMovieID(title);
                if (!string.IsNullOrEmpty(id))
                {
                    title = title.Replace(id, string.Empty, StringComparison.OrdinalIgnoreCase).TrimStart('-', ' ');
                }

                Metadata.Title = title;
            }

            // --- Studio & Actor Data population ---
            var paragraphNodes = metaBlock.QuerySelectorAll("div.cats p");
            foreach (var p in paragraphNodes)
            {
                string label = p.QuerySelector("span")?.TextContent?.Trim() ?? string.Empty;
                var anchor = p.QuerySelector("a");

                if (anchor == null || string.IsNullOrEmpty(label))
                    continue;

                if (label.StartsWith("Maker", StringComparison.OrdinalIgnoreCase))
                {
                    Metadata.Studio = anchor.TextContent.Trim();
                }
                else if (label.StartsWith("Cast", StringComparison.OrdinalIgnoreCase))
                {
                    string rawName = anchor.TextContent.Trim();
                    if (!string.IsNullOrEmpty(rawName))
                    {
                        string cleanName = ReverseNames(rawName);
                        if (!Metadata.Actors.Any(a => a.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase)))
                        {
                            Metadata.Actors.Add(new ActorData
                            {
                                Name = cleanName,
                                Order = Metadata.Actors.Count
                            });
                        }
                    }
                }
            }

            // --- Metadata Genres ---
            var tagNodes = metaBlock.QuerySelectorAll("div.tags a");
            foreach (var tagNode in tagNodes)
            {
                string genreText = tagNode.TextContent.Trim();
                if (!string.IsNullOrEmpty(genreText) && !Metadata.Genres.Contains(genreText, StringComparer.OrdinalIgnoreCase))
                {
                    Metadata.Genres.Add(genreText);
                }
            }

            m_parsingSuccessful = true;
        }
        private string ReverseNames(string name)
        {
            var splitNames = name.Trim().Split(' ');
            if (splitNames.Count() == 2)
                return splitNames[1] + " " + splitNames[0];
            return name;
        }
        private bool CheckResultsPage(IHtmlDocument document)
        {
            var header = document.QuerySelector("div.archive-title > h1");
            if (header == null || !header.TextContent.StartsWith("Search Result", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var postContainers = document.QuerySelectorAll("div.posts div.post");
            foreach (var post in postContainers)
            {
                var anchor = post.QuerySelector("h3 a[rel='bookmark']") ??
                             post.QuerySelector("a.img[rel='bookmark']");

                if (anchor == null) continue;

                string titleAttribute = anchor.GetAttribute("title") ?? anchor.TextContent ?? string.Empty;

                if (titleAttribute.Contains(_query, StringComparison.OrdinalIgnoreCase))
                {
                    string href = anchor.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href))
                    {
                        _pageLink = href;
                        m_parsingSuccessful = true;
                        return true;
                    }
                }
            }

            SearchNotFound = true;
            m_parsingSuccessful = true;
            return true;
        }

        private string GetSupJavQuery(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId))
                return rawId;

            var fc2Match = Regex.Match(rawId, @"(?i)FC2[-_ \s]?PPV[-_ \s]?(\d+)");
            if (fc2Match.Success)
            {
                return fc2Match.Groups[1].Value;
            }

            var standardMatch = Regex.Match(rawId, @"([A-Z]{2,}-\d+)", RegexOptions.IgnoreCase);
            if (standardMatch.Success)
            {
                return standardMatch.Groups[1].Value.ToUpperInvariant();
            }

            try
            {
                string parsed = _movieIdService.ParseMovieID(rawId);
                if (!string.IsNullOrEmpty(parsed))
                    return parsed;
            }
            catch { }

            return rawId;
        }
    }
}