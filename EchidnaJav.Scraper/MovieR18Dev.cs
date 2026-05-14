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
using System.Text.Json;
using System.Threading.Tasks;

namespace EchidnaJav.Scraper
{
    public sealed class MovieR18Dev : MovieScraperBase
    {
        private readonly IMovieIdService _movieIdService;
        private readonly JsonSerializerOptions _jsonOptions;

        // Since this module extracts pure JSON payloads directly from an API endpoint,
        // complex visual Cloudflare checks are bypassed natively.
        protected override bool EnableCloudflareHandling => false;

        public MovieR18Dev(
            ILogger<MovieScraperBase> logger,
            ISilentWebViewSandbox sandboxEngine,
            IMovieIdService movieIdService)
            : base(logger, sandboxEngine)
        {
            _movieIdService = movieIdService;

            // Reusable, thread-safe JSON deserialization configuration
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public override async Task ScrapeAsync(string movieID, LanguageType language)
        {
            // 1. Reset transient task parameters cleanly
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata(movieID);
            m_language = language;

            if (!IsLanguageSupported())
                return;

            // 2. Generate standard identifier matching permutations natively
            var idVariants = GenerateIDVariants(movieID);

            foreach (string variantId in idVariants)
            {
                string apiUrl = $"https://r18.dev/videos/vod/movies/detail/-/dvd_id={Uri.EscapeDataString(variantId)}/json";

                // Execute high-speed network request lifecycle
                await ScrapeWebsiteAsync(apiUrl);

                // Exit immediately once metadata resolves successfully or absolute failure is confirmed
                if (m_parsingSuccessful || SearchNotFound)
                    return;
            }
        }

        // Apply standard API headers safely to guarantee raw text execution returns
        protected override void CustomizeHttpRequest(HttpRequestMessage request, string targetUrl)
        {
            request.Headers.Add("Accept", "application/json,text/plain,*/*");
        }

        // The JSON structural response provides localized keys independently of requested headers
        protected override bool IsLanguageSupported() => true;

        protected override bool IsValidDataParsed() =>
            !string.IsNullOrEmpty(Metadata.Title) || m_parsingSuccessful;

        protected override void ParseDocument(IHtmlDocument document)
        {
            // Grab the raw JSON string payload cleanly from the DOM root wrapper
            string rawContent = document.DocumentElement?.TextContent ?? string.Empty;

            // Verify API routing continuity checks
            if (IsNotFoundResponse(rawContent))
            {
                // Defer parsing failure status to permit concurrent array variants to execute sequentially
                m_parsingSuccessful = false;
                return;
            }

            try
            {
                var responseData = JsonSerializer.Deserialize<R18DevResponse>(rawContent, _jsonOptions);

                if (responseData == null)
                {
                    m_parsingSuccessful = false;
                    return;
                }

                ParseJsonPayload(responseData);
                m_parsingSuccessful = true;
            }
            catch (Exception ex)
            {
                m_parsingSuccessful = false;
            }
        }

        private void ParseJsonPayload(R18DevResponse data)
        {
            var textInfo = System.Globalization.CultureInfo.InvariantCulture.TextInfo;

            // --- Scalar Metadata Mapping ---
            Metadata.Title = data.Title ?? string.Empty;
            Metadata.Premiered = data.ReleaseDate ?? string.Empty;

            if (!string.IsNullOrEmpty(Metadata.Premiered))
            {
                if (DateTime.TryParse(Metadata.Premiered, out DateTime parsedDate))
                {
                    Metadata.Year = parsedDate.Year;
                }
            }

            Metadata.Runtime = data.RuntimeMinutes;

            if (!string.IsNullOrEmpty(data.Director))
            {
                Metadata.Director = ActorMatchingEngine.ReverseNames(data.Director);
            }

            if (data.Maker != null && !string.IsNullOrEmpty(data.Maker.Name))
            {
                Metadata.Studio = data.Maker.Name;
            }

            if (data.Label != null && !string.IsNullOrEmpty(data.Label.Name))
            {
                Metadata.Label = data.Label.Name;
            }

            // --- Tag & Category Array Mappings ---
            if (data.Categories != null)
            {
                foreach (var category in data.Categories)
                {
                    string catName = category?.Name?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(catName))
                    {
                        // Apply uniform Title Case transformations securely downstream
                        string formattedCategory = textInfo.ToTitleCase(catName);
                        if (!Metadata.Genres.Contains(formattedCategory, StringComparer.OrdinalIgnoreCase))
                        {
                            Metadata.Genres.Add(formattedCategory);
                        }
                    }
                }
            }

            // --- Entity Actresses Mapping ---
            if (data.Actresses != null)
            {
                foreach (var actress in data.Actresses)
                {
                    string rawName = actress?.Name?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(rawName))
                    {
                        string cleanName = ActorMatchingEngine.ReverseNames(rawName);
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

            // --- Cover Image Resolution ---
            if (data.Images?.JacketImage != null)
            {
                ImageSource = !string.IsNullOrEmpty(data.Images.JacketImage.Large2)
                    ? data.Images.JacketImage.Large2
                    : data.Images.JacketImage.Large ?? string.Empty;
            }
        }

        private string NormalizeID(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;

            string lowerId = id.Trim().ToLowerInvariant();
            var parts = lowerId.Split('-');

            if (parts.Length != 2) return lowerId;

            string prefix = parts[0];
            string number = parts[1].PadLeft(5, '0'); // e.g., converts "982" cleanly to "00982"

            return prefix + number;
        }

        private string[] GenerateIDVariants(string originalID)
        {
            string normalized = NormalizeID(originalID);

            return new[]
            {
                normalized,        // e.g., dsvr00982
                "13" + normalized, // e.g., 13dsvr00982
                originalID
            };
        }

        private bool IsNotFoundResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return true;

            string lowerContent = content.ToLowerInvariant();

            return lowerContent.Contains("404 not found") ||
                   lowerContent.Contains("page does not exist") ||
                   lowerContent.Contains("sorry, this page does not exist") ||
                   lowerContent.Contains("requested url was not found");
        }
    }
}