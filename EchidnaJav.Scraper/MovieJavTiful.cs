using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Alias to resolve the ambiguity between AngleSharp.Dom.IElement and Microsoft.Maui.Controls.IElement
using IDomElement = AngleSharp.Dom.IElement;

namespace EchidnaJav.Scraper
{
    public sealed class MovieJavTiful : MovieScraperBase
    {
        private readonly IMovieIdService _movieIdService;
        private string _targetDetailUrl = string.Empty;
        private string _queryDigits = string.Empty;

        // Tracks internal session traversal state exclusively for this background thread
        private string _currentReferrer = string.Empty;

        // Ensure the base orchestration pipeline handles underlying WAF checks natively
        protected override bool EnableCloudflareHandling => true;

        public MovieJavTiful(
            ILogger<MovieScraperBase> logger,
            ISilentWebViewSandbox sandboxEngine)
            : base(logger, sandboxEngine)
        {
           
        }

        public override async Task ScrapeAsync(string movieID, LanguageType language)
        {
            // 1. Reset transient scraper task states cleanly
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata(movieID);
            m_language = language;
            _targetDetailUrl = string.Empty;
            _currentReferrer = string.Empty;

            if (!IsLanguageSupported())
                return;

            _queryDigits = GetJavtifulQuery(movieID);
            string encodedQuery = Uri.EscapeDataString(_queryDigits);
            string searchUrl = $"https://javtiful.com/search?q={encodedQuery}";

            // Establish Phase 1 navigation state
            _currentReferrer = "https://javtiful.com/";

            // Note: Since legacy client-side scripts injected cookies to enforce English language rendering,
            // we inject specific Accept-Language configuration headers via the CustomizeHttpRequest hook instead.
            await ScrapeWebsiteAsync(searchUrl);

            // Phase 2 Extraction: A specific media target path was discovered inside the search view
            if (!string.IsNullOrEmpty(_targetDetailUrl))
            {
                m_parsingSuccessful = false;
                SearchNotFound = false;

                // Update localized Referer state to mimic standard browser traversal patterns
                _currentReferrer = searchUrl;
                await ScrapeWebsiteAsync(_targetDetailUrl);
            }
        }

        // 🔥 LIFECYCLE HOOK: Delegate platform request header modifications explicitly for Javtiful
        protected override void CustomizeHttpRequest(HttpRequestMessage request, string targetUrl)
        {
            request.Headers.Add("Sec-Fetch-Dest", "document");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");

            // Broadcast strict locale preference headers to force English payload returns natively
            request.Headers.Remove("Accept-Language");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            if (!string.IsNullOrEmpty(_currentReferrer))
            {
                request.Headers.Add("Referer", _currentReferrer);
            }

            // Attach secondary state verification cookie headers directly if necessary
            request.Headers.Add("Cookie", "language=en_US;");
        }

        protected override bool IsLanguageSupported() => m_language == LanguageType.English;

        protected override bool IsValidDataParsed() => !string.IsNullOrEmpty(Metadata.Title) || m_parsingSuccessful;

        protected override void ParseDocument(IHtmlDocument document)
        {
            var textInfo = System.Globalization.CultureInfo.InvariantCulture.TextInfo;

            // =========================================================================
            // PHASE 1: SEARCH RESULT PAGE TRAVERSAL
            // =========================================================================
            if (string.IsNullOrEmpty(_targetDetailUrl))
            {
                var videoCards = document.QuerySelectorAll("article.front-video-card").OfType<IDomElement>();

                foreach (var card in videoCards)
                {
                    var thumbAnchor = card.QuerySelector("a.front-video-thumb");
                    if (thumbAnchor == null) continue;

                    string rawHref = thumbAnchor.GetAttribute("href") ?? string.Empty;
                    if (string.IsNullOrEmpty(rawHref)) continue;

                    // 🔥 SANITIZATION: Strip localized routing directories to enforce Canonical English Root
                    string cleanHref = rawHref;
                    string[] localeDirs = { "/ja/", "/zh/", "/id/", "/kr/", "/vn/" };
                    foreach (string loc in localeDirs)
                    {
                        if (cleanHref.StartsWith(loc, StringComparison.OrdinalIgnoreCase))
                        {
                            cleanHref = cleanHref.Substring(loc.Length - 1); // Retain leading slash
                            break;
                        }
                    }

                    bool looksLikeVideo = cleanHref.IndexOf("/video/", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchesFc2Pattern = false;

                    if (!string.IsNullOrEmpty(_queryDigits))
                    {
                        try
                        {
                            string escapedDigits = Regex.Escape(_queryDigits);
                            matchesFc2Pattern = Regex.IsMatch(cleanHref, $"(?i)fc2[-_]?ppv[-_]?{escapedDigits}");
                        }
                        catch { matchesFc2Pattern = false; }
                    }

                    bool matchesDigitsInPath = !string.IsNullOrEmpty(_queryDigits) &&
                                               cleanHref.IndexOf(_queryDigits, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (looksLikeVideo && (matchesFc2Pattern || matchesDigitsInPath))
                    {
                        // Materialize sanitized root path guarantees
                        if (cleanHref.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            _targetDetailUrl = cleanHref;
                        else
                            _targetDetailUrl = "https://javtiful.com" + (cleanHref.StartsWith("/") ? cleanHref : "/" + cleanHref);

                        // Extract highest available thumbnail fidelity directly from framework buffers
                        var imgElement = thumbAnchor.QuerySelector("img");
                        if (imgElement != null)
                        {
                            string srcAttr = imgElement.GetAttribute("data-front-lazy-fallback-src") ??
                                             imgElement.GetAttribute("data-front-lazy-src") ??
                                             imgElement.GetAttribute("src") ?? string.Empty;

                            if (srcAttr.Contains('|')) srcAttr = srcAttr.Split('|')[0].Trim();

                            if (!string.IsNullOrEmpty(srcAttr) && !srcAttr.Contains("placeholder"))
                            {
                                ImageSource = srcAttr.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                    ? srcAttr
                                    : "https://javtiful.com" + srcAttr;
                            }
                        }

                        m_parsingSuccessful = true;
                        return;
                    }
                }

                SearchNotFound = true;
                m_parsingSuccessful = true;
                return;
            }

            // =========================================================================
            // PHASE 2: MATERIALIZED DETAIL EXTRACTION (MULTI-LOCALE RESILIENT)
            // =========================================================================

            // 1. Target Canonical Clean Title
            var titleElement = document.QuerySelector("div.front-watch-title > h1") as IDomElement;
            if (titleElement != null)
            {
                string rawTitle = titleElement.TextContent?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(rawTitle))
                {
                    string titleClean = rawTitle;
                    if (!string.IsNullOrEmpty(_queryDigits) && titleClean.IndexOf(_queryDigits, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            string escapedDigits = Regex.Escape(_queryDigits);
                            string patternFc2 = $"(?i)fc2[-_\\s]?ppv[-_\\s]?{escapedDigits}";

                            titleClean = Regex.Replace(titleClean, patternFc2, string.Empty, RegexOptions.IgnoreCase);
                            titleClean = Regex.Replace(titleClean, $"(?i){escapedDigits}", string.Empty, RegexOptions.IgnoreCase);
                        }
                        catch { }

                        titleClean = titleClean.TrimStart(':', '-', ' ').Trim();
                    }

                    Metadata.Title = string.IsNullOrEmpty(titleClean) ? rawTitle : titleClean;
                    m_parsingSuccessful = true;
                }
            }

            // 2. Cover Image Fallback Validation
            if (string.IsNullOrEmpty(ImageSource))
            {
                var playerVideo = document.QuerySelector("video#front-player");
                string posterPath = playerVideo?.GetAttribute("poster") ??
                                    playerVideo?.GetAttribute("data-poster") ?? string.Empty;

                if (!string.IsNullOrEmpty(posterPath))
                {
                    ImageSource = posterPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? posterPath
                        : "https://javtiful.com" + posterPath;
                }
            }

            // 3. Metadata Iteration (Bypasses UI Locale Traps natively)
            var detailContainers = document.QuerySelectorAll("div.front-watch-detail").OfType<IDomElement>();
            var genreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var detailBlock in detailContainers)
            {
                string labelText = (detailBlock.QuerySelector("strong")?.TextContent ?? string.Empty).Trim();

                // 🔥 RESILIENCE: Match both English and Japanese layout headers safely

                // Channel / Studio Mapping
                if (labelText.StartsWith("Channel", StringComparison.OrdinalIgnoreCase) ||
                    labelText.StartsWith("チャンネル"))
                {
                    var channelLink = detailBlock.QuerySelector("a[href*=\"/channel/\"]");
                    string channelName = channelLink?.TextContent?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(channelName)) Metadata.Studio = channelName;
                }
                // Actress Mapping
                else if (labelText.StartsWith("Actresses", StringComparison.OrdinalIgnoreCase) ||
                         labelText.StartsWith("Actress", StringComparison.OrdinalIgnoreCase) ||
                         labelText.StartsWith("女優"))
                {
                    var actorLinks = detailBlock.QuerySelectorAll("div.front-watch-actor-list a").OfType<IDomElement>();
                    foreach (var actorLink in actorLinks)
                    {
                        string rawActor = actorLink.TextContent?.Trim() ?? string.Empty;
                        if (!string.IsNullOrEmpty(rawActor) &&
                            !rawActor.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                            !rawActor.Equals("未登録")) // Handle localized unknown states
                        {
                            string cleanedActor = ActorMatchingEngine.ReverseNames(rawActor);
                            if (!Metadata.Actors.Any(a => a.Name.Equals(cleanedActor, StringComparison.OrdinalIgnoreCase)))
                            {
                                Metadata.Actors.Add(new ActorData
                                {
                                    Name = cleanedActor,
                                    Order = Metadata.Actors.Count
                                });
                            }
                        }
                    }
                }
                // Tags & Categories Mapping
                else if (labelText.StartsWith("Tags", StringComparison.OrdinalIgnoreCase) ||
                         labelText.StartsWith("Categories", StringComparison.OrdinalIgnoreCase) ||
                         labelText.StartsWith("タグ") ||
                         labelText.StartsWith("カテゴリー"))
                {
                    var chips = detailBlock.QuerySelectorAll("div.front-watch-link-list a.front-watch-link-chip").OfType<IDomElement>();
                    foreach (var chip in chips)
                    {
                        string rawTag = chip.TextContent?.Trim() ?? string.Empty;
                        if (!string.IsNullOrEmpty(rawTag))
                        {
                            genreSet.Add(textInfo.ToTitleCase(rawTag));
                        }
                    }
                }
            }

            // Dynamic Censorship Validation (Handles multi-language UI flags)
            var metaChips = document.QuerySelectorAll("div.front-watch-meta span.front-chip").OfType<IDomElement>();
            foreach (var flagChip in metaChips)
            {
                string flagText = flagChip.TextContent?.Trim() ?? string.Empty;

                if (flagText.Equals("Uncensored", StringComparison.OrdinalIgnoreCase) || flagText.Equals("無修正"))
                    genreSet.Add("Uncensored");
                else if (flagText.Equals("Censored", StringComparison.OrdinalIgnoreCase) || flagText.Equals("有修正"))
                    genreSet.Add("Censored");
            }

            // Materialize unique genres downstream
            foreach (string genre in genreSet)
            {
                if (!Metadata.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                {
                    Metadata.Genres.Add(genre);
                }
            }

            if (!string.IsNullOrEmpty(Metadata.Title) || Metadata.Actors.Count > 0 || Metadata.Genres.Count > 0)
            {
                m_parsingSuccessful = true;
            }
        }
        private string GetJavtifulQuery(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return rawId;

            // Isolate numeric tokens directly if matching canonical PPV naming profiles
            var match = Regex.Match(rawId, @"(?i)FC2[-_ \s]?PPV[-_ \s]?(\d+)");
            if (match.Success) return match.Groups[1].Value;

            match = Regex.Match(rawId, @"(?i)FC2PPV[-_ \s]?(\d+)");
            if (match.Success) return match.Groups[1].Value;

            try
            {
                string parsedId = _movieIdService.ParseMovieID(rawId);
                if (!string.IsNullOrEmpty(parsedId))
                {
                    var numericSub = Regex.Match(parsedId, @"\d{3,}");
                    if (numericSub.Success) return numericSub.Value;
                }
            }
            catch { }

            // General fallback target extraction profile
            var genericNumericMatch = Regex.Match(rawId, @"\d{3,}");
            if (genericNumericMatch.Success) return genericNumericMatch.Value;

            return rawId;
        }
        public override Task ScrapeFromUrlAsync(string url, LanguageType language, string movieId)
        {
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata(movieId);
            m_language = language;

            if (!IsLanguageSupported())
            {
                return Task.CompletedTask;
            }

            _queryDigits = GetJavtifulQuery(movieId);
            _currentReferrer = "https://javtiful.com/";
            _targetDetailUrl = url;

            return ScrapeWebsiteAsync(url);
        }
    }
}