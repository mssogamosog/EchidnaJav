using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
// Resolve ambiguity between AngleSharp.Dom.IElement and Microsoft.Maui.Controls.IElement
using IDomElement = AngleSharp.Dom.IElement;

namespace EchidnaJav.Scraper
{
    public sealed class MovieJavLibrary : MovieScraperBase
    {
        private readonly IMovieIdService _movieIdService;
        private string _targetPageLink = string.Empty;

        // Track local execution provenance securely per background thread
        private string _currentReferrer = string.Empty;

        // Ensure underlying Cloudflare checking buffers are armed natively
        protected override bool EnableCloudflareHandling => true;

        public MovieJavLibrary(
            ILogger<MovieScraperBase> logger,
            ISilentWebViewSandbox sandboxEngine,
            IMovieIdService movieIdService)
            : base(logger, sandboxEngine)
        {
            _movieIdService = movieIdService ;
        }

        public override async Task ScrapeAsync(string movieID, LanguageType language)
        {
            // 1. Reset volatile scraping parameters cleanly per queue execution
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata(movieID);
            m_language = language;
            _targetPageLink = string.Empty;
            _currentReferrer = string.Empty;

            if (!IsLanguageSupported())
                return;

            string langSegment = GetLanguageSegment();
            string searchUrl = $"https://www.javlibrary.com/{langSegment}/vl_searchbyid.php?keyword={Uri.EscapeDataString(movieID)}";

            // Establish Phase 1 navigation baseline
            _currentReferrer = $"https://www.javlibrary.com/{langSegment}/";
            await ScrapeWebsiteAsync(searchUrl);

            // Phase 2: If the query landed on a search result listing grid, route execution to the direct target link
            if (!string.IsNullOrEmpty(_targetPageLink))
            {
                m_parsingSuccessful = false;
                SearchNotFound = false;

                // Update localized origin parameters to simulate user traversal dynamics
                _currentReferrer = searchUrl;
                await ScrapeWebsiteAsync(_targetPageLink);
            }
        }

        // 🔥 LIFECYCLE HOOK: Inject optimal fetch headers specifically tailored for JAVLibrary profiles
        protected override void CustomizeHttpRequest(HttpRequestMessage request, string targetUrl)
        {
            request.Headers.Add("Sec-Fetch-Dest", "document");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");

            if (!string.IsNullOrEmpty(_currentReferrer))
            {
                request.Headers.Add("Referer", _currentReferrer);
            }

            // Apply explicit Accept-Language mapping properties matching our parsing target scope
            request.Headers.Remove("Accept-Language");
            request.Headers.Add("Accept-Language", m_language == LanguageType.Japanese ? "ja,en;q=0.9" : "en-US,en;q=0.9");
        }

        protected override bool IsLanguageSupported() =>
            m_language == LanguageType.English || m_language == LanguageType.Japanese;

        protected override bool IsValidDataParsed() =>
            !string.IsNullOrEmpty(Metadata.Title) || m_parsingSuccessful;

        protected override void ParseDocument(IHtmlDocument document)
        {
            // --- Phase 1: Verify Search Listing Results Layout ---
            if (CheckResultsPage(document))
                return;

            // Grab shared culture formatters safely for tag formatting pipelines
            var textInfo = System.Globalization.CultureInfo.InvariantCulture.TextInfo;

            // --- Phase 2: Deep Detail Page Extraction (Optimized AngleSharp Selectors) ---

            // 1. Primary Title Extraction
            var titleDiv = document.QuerySelector("div#video_title");
            if (titleDiv != null && titleDiv.FirstElementChild != null)
            {
                string fullTitle = titleDiv.FirstElementChild.TextContent ?? string.Empty;
                string cleanId = _movieIdService.ParseMovieID(fullTitle);

                if (!string.IsNullOrEmpty(cleanId))
                {
                    // Clean off primary identifier prefixes and raw suffix tags natively
                    int cutLength = fullTitle.Length - cleanId.Length - 1;
                    if (cutLength > 0)
                    {
                        fullTitle = fullTitle.Substring(cleanId.Length + 1, cutLength);
                    }
                }

                Metadata.Title = fullTitle.Trim();
            }

            // 2. High-Fidelity Cover Jacket Asset
            var coverImage = document.QuerySelector("img#video_jacket_img");
            if (coverImage != null)
            {
                string srcAttr = coverImage.GetAttribute("src") ?? string.Empty;
                if (!string.IsNullOrEmpty(srcAttr))
                {
                    ImageSource = srcAttr.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? srcAttr
                        : $"https://www.javlibrary.com/{GetLanguageSegment()}/" + srcAttr.TrimStart('/');
                }
            }

            // 3. Metadata Property Pairs (Release Date, Length, Director, Maker, Label)
            var infoRows = document.QuerySelectorAll("div#video_info div.item").OfType<IDomElement>();
            foreach (var row in infoRows)
            {
                string headerText = (row.QuerySelector("td.header")?.TextContent ?? string.Empty).Trim();
                var textCell = row.QuerySelector("td.text");
                if (textCell == null) continue;

                string cellValue = (textCell.TextContent ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(cellValue) || cellValue == "----") continue;

                // Check static locale-independent structural keywords OR localized string patterns
                if (headerText.Contains("Release Date") || headerText.Contains("発売日"))
                {
                    Metadata.Premiered = cellValue;
                    if (DateTime.TryParse(cellValue, out DateTime parsedDate))
                    {
                        Metadata.Year = parsedDate.Year;
                    }
                }
                else if (headerText.Contains("Length") || headerText.Contains("収録時間"))
                {
                    // Strip numeric metric elements out securely
                    var numericSpan = textCell.QuerySelector("span");
                    string durationStr = numericSpan?.TextContent?.Trim() ?? cellValue;

                    if (int.TryParse(new string(durationStr.Where(char.IsDigit).ToArray()), out int runtimeMinutes))
                    {
                        Metadata.Runtime = runtimeMinutes;
                    }
                }
                else if (headerText.Contains("Director") || headerText.Contains("監督"))
                {
                    Metadata.Director = ActorMatchingEngine.ReverseNames(cellValue);
                }
                else if (headerText.Contains("Maker") || headerText.Contains("メーカー"))
                {
                    Metadata.Studio = cellValue;
                }
                else if (headerText.Contains("Label") || headerText.Contains("レーベル"))
                {
                    Metadata.Label = cellValue;
                }
            }

            // 4. Tracked Genres Extraction
            var genreSpans = document.QuerySelectorAll("span.genre a").OfType<IDomElement>();
            foreach (var span in genreSpans)
            {
                string rawGenre = span.TextContent?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(rawGenre))
                {
                    // Apply global Title Case mappings smoothly
                    string formattedGenre = textInfo.ToTitleCase(rawGenre);
                    if (!Metadata.Genres.Contains(formattedGenre, StringComparer.OrdinalIgnoreCase))
                    {
                        Metadata.Genres.Add(formattedGenre);
                    }
                }
            }

            // 5. Materialized Cast / Actress Profile Objects
            var castBlocks = document.QuerySelectorAll("span.cast").OfType<IDomElement>();
            foreach (var castBlock in castBlocks)
            {
                var primaryStar = castBlock.QuerySelector("span.star a");
                if (primaryStar != null)
                {
                    string starName = primaryStar.TextContent?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(starName))
                    {
                        var actorData = new ActorData
                        {
                            Name = ActorMatchingEngine.ReverseNames(starName),
                            Order = Metadata.Actors.Count
                        };

                        // Scrape linked structural Alias blocks attached cleanly beneath the primary star entry
                        var aliasNodes = castBlock.QuerySelectorAll("span.alias").OfType<IDomElement>();
                        foreach (var aliasNode in aliasNodes)
                        {
                            string aliasName = aliasNode.TextContent?.Trim() ?? string.Empty;
                            if (!string.IsNullOrEmpty(aliasName))
                            {
                                actorData.Aliases.Add(ActorMatchingEngine.ReverseNames(aliasName));
                            }
                        }

                        Metadata.Actors.Add(actorData);
                    }
                }
            }

            // Guarantee task status updates securely
            if (!string.IsNullOrEmpty(Metadata.Title) || Metadata.Actors.Count > 0 || Metadata.Genres.Count > 0)
            {
                m_parsingSuccessful = true;
            }
        }

        private bool CheckResultsPage(IHtmlDocument document)
        {
            // Scenario A: Zero results returned explicitly
            var bodyText = document.Body?.TextContent ?? string.Empty;
            if (bodyText.Contains("Search returned no result.") || bodyText.Contains("検索結果はありません"))
            {
                m_parsingSuccessful = true;
                SearchNotFound = true;
                return true;
            }

            // Scenario B: Listing page grid triggered — locate direct identity map target
            var listingAnchors = document.QuerySelectorAll("div.videos div.video a[href]").OfType<IDomElement>();
            foreach (var anchor in listingAnchors)
            {
                var idDiv = anchor.QuerySelector("div.id");
                if (idDiv != null)
                {
                    string extractedId = idDiv.TextContent?.Trim() ?? string.Empty;

                    // Match precisely against incoming target identifier profiles
                    if (extractedId.Equals(Metadata.UniqueID.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        string hrefAttr = anchor.GetAttribute("href") ?? string.Empty;
                        if (!string.IsNullOrEmpty(hrefAttr))
                        {
                            _targetPageLink = hrefAttr.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                ? hrefAttr
                                : $"https://www.javlibrary.com/{GetLanguageSegment()}/" + hrefAttr.TrimStart('.', '/');

                            m_parsingSuccessful = true;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private string GetLanguageSegment() => m_language == LanguageType.Japanese ? "ja" : "en";
    }
}