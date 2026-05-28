using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;
using IDomElement = AngleSharp.Dom.IElement;

namespace EchidnaJav.Scraper
{
    public sealed class MovieMissAv : MovieScraperBase
    {
        private readonly IMovieIdService _movieIdService;
        private string _targetDetailUrl = string.Empty;
        private string _queryToken = string.Empty;

        // Tracks internal session traversal provenance strictly for this execution thread
        private string _currentReferrer = string.Empty;

        // Ensure the base orchestration pipeline handles underlying WAF intercepts natively
        protected override bool EnableCloudflareHandling => true;

        public MovieMissAv(
            ILogger<MovieScraperBase> logger,
            ISilentWebViewSandbox sandboxEngine,
            IMovieIdService movieIdService)
            : base(logger, sandboxEngine)
        {
            _movieIdService = movieIdService ?? throw new ArgumentNullException(nameof(movieIdService));
        }

        public override async Task ScrapeAsync(string movieID, LanguageType language)
        {
            // 1. Clear transient task configurations securely
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata(movieID);
            m_language = language;
            _targetDetailUrl = string.Empty;
            _currentReferrer = string.Empty;

            if (!IsLanguageSupported())
                return;

            _queryToken = GetMissAvQuery(movieID);
            string searchUrl = $"https://missav.ws/en/search/{Uri.EscapeDataString(_queryToken)}";

            // Establish Phase 1 navigation state
            _currentReferrer = "https://missav.ws/en/";
            await ScrapeWebsiteAsync(searchUrl);

            // Phase 2 Extraction: A specific media target path was discovered inside the search view
            if (!string.IsNullOrEmpty(_targetDetailUrl))
            {
                m_parsingSuccessful = false;
                SearchNotFound = false;

                // Update localized Referer state to mimic organic UI traversal to deep-page components
                _currentReferrer = searchUrl;
                await ScrapeWebsiteAsync(_targetDetailUrl);
            }
        }

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

        protected override bool IsLanguageSupported() => m_language == LanguageType.English;

        protected override bool IsValidDataParsed() => !string.IsNullOrEmpty(Metadata.Title) || m_parsingSuccessful;

        protected override void ParseDocument(IHtmlDocument document)
        {
            // --- Phase 1: Search Result Traversal ---
            if (string.IsNullOrEmpty(_targetDetailUrl))
            {
                if (string.IsNullOrEmpty(_queryToken)) return;

                string tokenLower = _queryToken.ToLowerInvariant();
                string tokenNoDash = tokenLower.Replace("-", "");

                var anchorElements = document.QuerySelectorAll("div.thumbnail a[href]").OfType<IDomElement>();

                foreach (var anchor in anchorElements)
                {
                    string href = anchor.GetAttribute("href") ?? string.Empty;
                    if (string.IsNullOrEmpty(href)) continue;

                    string hrefLower = href.ToLowerInvariant();

                    // Ensure target string resolves strictly inside standard English content directories
                    if (!hrefLower.Contains("/en/")) continue;

                    // Match precise identifier configurations within the routing structure
                    if (!hrefLower.Contains(tokenLower) && !hrefLower.Contains(tokenNoDash)) continue;

                    if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        href = "https://missav.ws" + href;
                    }

                    _targetDetailUrl = href;
                    return; // Interrupt processing safely to permit Phase 2 execution lifecycle
                }

                SearchNotFound = true;
                m_parsingSuccessful = true;
                return;
            }

            // --- Phase 2: Materialized Detail Extraction ---

            // Extract Primary Cover Assets
            if (string.IsNullOrEmpty(ImageSource))
            {
                string? coverCandidate = null;

                // Candidate A: Dedicated media element poster attribute
                var videoNode = document.QuerySelector("video[data-poster]");
                if (videoNode != null)
                {
                    coverCandidate = videoNode.GetAttribute("data-poster");
                }

                // Candidate B: Embedded framework container background properties
                if (string.IsNullOrEmpty(coverCandidate))
                {
                    var posterDiv = document.QuerySelector(".plyr__poster");
                    string styleAttr = posterDiv?.GetAttribute("style") ?? string.Empty;

                    if (!string.IsNullOrEmpty(styleAttr))
                    {
                        var match = Regex.Match(styleAttr, @"url\(['""]?(?<url>[^'"")]+)");
                        if (match.Success) coverCandidate = match.Groups["url"].Value;
                    }
                }

                if (!string.IsNullOrEmpty(coverCandidate))
                {
                    ImageSource = coverCandidate.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? coverCandidate
                        : "https://missav.ws" + coverCandidate;
                }
            }

            // Extract Structural Metadata Strings
            foreach (var element in document.All)
            {
                if (element.NodeName != "SPAN" || string.IsNullOrEmpty(element.TextContent)) continue;
                string spanLabel = element.TextContent.Trim();

                // Release Dates
                if (spanLabel.Equals("Release date:", StringComparison.OrdinalIgnoreCase))
                {
                    if (element.NextElementSibling is IHtmlTimeElement timeElement)
                    {
                        string dateString = !string.IsNullOrEmpty(timeElement.DateTime)
                            ? timeElement.DateTime
                            : timeElement.TextContent.Trim();

                        if (!string.IsNullOrEmpty(dateString) && dateString.Length >= 10)
                        {
                            Metadata.Premiered = dateString.Substring(0, 10);
                            if (DateTime.TryParse(Metadata.Premiered, out DateTime parsedDate))
                            {
                                Metadata.Year = parsedDate.Year;
                            }
                        }
                    }
                }
                // Actress Mapping
                else if (spanLabel.Equals("Actress:", StringComparison.OrdinalIgnoreCase))
                {
                    var parentNode = element.ParentElement;
                    if (parentNode != null)
                    {
                        foreach (var anchor in parentNode.QuerySelectorAll("a"))
                        {
                            string rawName = anchor.TextContent.Trim();
                            if (string.IsNullOrEmpty(rawName)) continue;

                            string cleanedName = ActorMatchingEngine.ReverseNames(rawName);
                            if (!Metadata.Actors.Any(a => a.Name.Equals(cleanedName, StringComparison.OrdinalIgnoreCase)))
                            {
                                Metadata.Actors.Add(new ActorData
                                {
                                    Name = cleanedName,
                                    Order = Metadata.Actors.Count
                                });
                            }
                        }
                    }
                }
                // Genre Mapping
                else if (spanLabel.Equals("Genre:", StringComparison.OrdinalIgnoreCase) || spanLabel.Equals("Tag:", StringComparison.OrdinalIgnoreCase))
                {
                    var parentNode = element.ParentElement;
                    if (parentNode != null)
                    {
                        var textInfo = CultureInfo.InvariantCulture.TextInfo;
                        foreach (var anchor in parentNode.QuerySelectorAll("a"))
                        {
                            string genreStr = anchor.TextContent.Trim();
                            string formattedGenre = textInfo.ToTitleCase(genreStr);
                            if (!string.IsNullOrEmpty(formattedGenre) && !Metadata.Genres.Contains(formattedGenre, StringComparer.OrdinalIgnoreCase))
                            {
                                Metadata.Genres.Add(formattedGenre);
                            }
                        }
                    }
                }
            }

            // Target Canonical Clean Title
            var titleHeader = document.QuerySelector(".mt-4 > h1");
            if (titleHeader != null && !string.IsNullOrEmpty(titleHeader.TextContent))
            {
                string rawTitle = titleHeader.TextContent.Trim();
                string tokenNoDash = _queryToken.Replace("-", "");

                bool verificationMatch = rawTitle.IndexOf(_queryToken, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         rawTitle.IndexOf(tokenNoDash, StringComparison.OrdinalIgnoreCase) >= 0;

                if (verificationMatch)
                {
                    try
                    {
                        string cleanTitle = Regex.Replace(rawTitle, "(?i)" + Regex.Escape(_queryToken), string.Empty);
                        cleanTitle = Regex.Replace(cleanTitle, "(?i)" + Regex.Escape(tokenNoDash), string.Empty);
                        Metadata.Title = cleanTitle.TrimStart(':', '-', ' ').Trim();
                    }
                    catch
                    {
                        Metadata.Title = rawTitle;
                    }
                }
                else
                {
                    Metadata.Title = rawTitle;
                }
            }

            m_parsingSuccessful = true;
        }

        private string GetMissAvQuery(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return rawId;

            // Route pure numeric payload targets for specific channel mapping profiles
            var fc2Match = Regex.Match(rawId, @"(?i)FC2[-_ \s]?PPV[-_ \s]?(\d+)");
            if (fc2Match.Success) return fc2Match.Groups[1].Value;

            // Prefer canonical configurations complete with center delimiters
            var standardMatch = Regex.Match(rawId, @"([A-Z]{2,}-\d+)", RegexOptions.IgnoreCase);
            if (standardMatch.Success) return standardMatch.Groups[1].Value.ToUpperInvariant();

            try
            {
                string parsedId = _movieIdService.ParseMovieID(rawId);
                if (!string.IsNullOrEmpty(parsedId)) return parsedId;
            }
            catch { }

            return rawId;
        }
    }
}