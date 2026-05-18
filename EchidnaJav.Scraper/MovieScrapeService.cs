using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EchidnaJav.Scraper
{
    public class MovieScrapeService : IMovieScrapeService
    {
        private readonly ILogger<MovieScrapeService> _logger;
        private readonly IImageService _imageService;
        private readonly IAppPaths _appPaths;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMovieIdService _movieIdService;
        private readonly string _cacheFolder;

        public MovieScrapeService(
            ILogger<MovieScrapeService> logger,
            IImageService imageService,
            IAppPaths appPaths,
            IServiceProvider serviceProvider,
            IMovieIdService movieIdService)
        {
            _logger = logger;
            _imageService = imageService;
            _appPaths = appPaths;
            _serviceProvider = serviceProvider;
            _movieIdService = movieIdService;
            _cacheFolder = Path.Combine(_appPaths.AppDataDirectory, "image-cache");
        }

        public async Task<MovieMetadata> ScrapeMovieAsync(string movieID, string coverImagePath, LanguageType language)
        {
            _logger.LogInformation($"Attempting to scrape metadata for {movieID}");

            bool downloadCoverImage = !string.IsNullOrEmpty(coverImagePath);
            var canonicalId = _movieIdService.ParseMovieID(movieID);
            MovieMetadata? resultMetadata = null;

            // Pipeline Branching: FC2 vs Standard
            if (!string.IsNullOrEmpty(canonicalId) && canonicalId.StartsWith("FC2-PPV", StringComparison.OrdinalIgnoreCase))
            {
                resultMetadata = await ScrapeFC2PipelineAsync(canonicalId, coverImagePath, language, downloadCoverImage);
            }
            else
            {
                resultMetadata = await ScrapeStandardPipelineAsync(movieID, coverImagePath, language, downloadCoverImage);
            }

            // Final Validation
            if (resultMetadata == null || !IsMovieMetadataAcceptable(resultMetadata))
                return null;

            foreach (var actor in resultMetadata.Actors)
            {
                ScraperParsingExtensions.FilterActorName(actor);
            }

            _logger.LogInformation($"Metadata for {resultMetadata.UniqueID.Value} successfully finalized.");
            return resultMetadata;
        }
        public async Task<MovieMetadata> ScrapeMovieFromMultipleUrlsAsync(string movieId, List<ManualUrlScrapeRequest> requests, string targetCoverPath, LanguageType english)
        {
            _logger.LogInformation("Launching dynamic multi-URL execution pipeline for {MovieId} with {Count} sources", movieId, requests.Count);

            if (requests == null || !requests.Any()) return null;

            // 1. Prepare all scrapers and their tasks
            var scrapeTasks = requests.Select(req =>
            {
                var scraper = GetScraperInstance(req.Source);
                return new
                {
                    Scraper = scraper,
                    Task = scraper.ScrapeFromUrlAsync(req.Url, english)
                };
            }).ToList();

            // 2. Execute ALL of them concurrently
            await Task.WhenAll(scrapeTasks.Select(x => x.Task));

            MovieMetadata? mergedMetadata = null;
            string? bestImageSource = null;

            // 3. Merge results in sequential priority order (Index 0 is highest priority)
            foreach (var result in scrapeTasks)
            {
                var scraper = result.Scraper;
                if (scraper.Metadata != null && !scraper.SearchNotFound)
                {
                    if (mergedMetadata == null)
                    {
                        mergedMetadata = scraper.Metadata;
                        mergedMetadata.UniqueID.Value = movieId; 
                    }
                    else
                    {
                        mergedMetadata = MetadataMerger.MergePrimary(mergedMetadata, scraper.Metadata);
                    }

                    if (string.IsNullOrEmpty(bestImageSource) && !string.IsNullOrEmpty(scraper.ImageSource))
                    {
                        bestImageSource = scraper.ImageSource;
                    }
                }
            }

            if (mergedMetadata == null)
            {
                _logger.LogWarning("Multi-URL scrape failed to extract valid data from any provided source.");
                return null;
            }
            mergedMetadata.UniqueID?.Value = movieId;
            if (!string.IsNullOrEmpty(targetCoverPath) && !string.IsNullOrEmpty(bestImageSource))
            {
                await DownloadCoverAsync(targetCoverPath, bestImageSource);
            }

            foreach (var actor in mergedMetadata.Actors)
            {
                ScraperParsingExtensions.FilterActorName(actor);
            }

            return mergedMetadata;
        }
        private IMovieScraper GetScraperInstance(ScraperSourceType source) => source switch
        {
            ScraperSourceType.JavLibrary => GetScraper<MovieJavLibrary>(),
            ScraperSourceType.JavDatabase => GetScraper<MovieJavDatabase>(),
            ScraperSourceType.R18Dev => GetScraper<MovieR18Dev>(),
            ScraperSourceType.SupJav => GetScraper<MovieSupJav>(),
            ScraperSourceType.MissAv => GetScraper<MovieMissAv>(),
            ScraperSourceType.JavTiful => GetScraper<MovieJavTiful>(),
            _ => throw new ArgumentException("Unsupported scraper engine subtype.")
        };
        private async Task<MovieMetadata> ScrapeStandardPipelineAsync(string movieID, string coverPath, LanguageType language, bool downloadCover)
        {
            MovieMetadata mergedMetadata = new MovieMetadata(movieID);

            // =========================================================================
            // Tier 1: Core Priority Group (JavLibrary & JavDatabase Concurrently)
            // =========================================================================
            _logger.LogInformation($"Launching primary concurrent execution for core targets: {movieID}");

            var javLib = GetScraper<MovieJavLibrary>();
            var javDb = GetScraper<MovieJavDatabase>();

            // Execute both core engines in parallel to maximize IO thread efficiency
            await Task.WhenAll(
                javLib.ScrapeAsync(movieID, language),
                javDb.ScrapeAsync(movieID, language)
            );

            bool javLibSuccess = javLib.Metadata != null && !javLib.SearchNotFound;
            bool javDbSuccess = javDb.Metadata != null && !javDb.SearchNotFound;

            // --- Priority Data Merging (JavLibrary > JavDatabase) ---
            if (javLibSuccess)
            {
                mergedMetadata = javLib.Metadata;

                // If JavDatabase also found data, cleanly backfill any empty attributes left by JavLibrary
                if (javDbSuccess)
                {
                    mergedMetadata = MetadataMerger.MergePrimary(mergedMetadata, javDb.Metadata);
                }
            }
            else if (javDbSuccess)
            {
                // Fallback entirely to JavDatabase base graph if JavLibrary failed to resolve
                mergedMetadata = javDb.Metadata;
            }

            // --- Priority Cover Resolution ---
            if (downloadCover)
            {
                // Give direct preference to the JavLibrary jacket source, pivoting to JavDatabase if absent
                if (javLibSuccess && !string.IsNullOrEmpty(javLib.ImageSource))
                {
                    await DownloadCoverAsync(coverPath, javLib.ImageSource);
                }
                else if (javDbSuccess && !string.IsNullOrEmpty(javDb.ImageSource))
                {
                    await DownloadCoverAsync(coverPath, javDb.ImageSource);
                }
            }

            // =========================================================================
            // Tier 2: R18.dev API (Executes ONLY if Tier 1 left structural gaps)
            // =========================================================================
            if (!IsMetadataFullyPopulated(mergedMetadata, coverPath, downloadCover))
            {
                _logger.LogInformation($"Gaps remain for {movieID}. Invoking secondary priority: R18.dev API...");
                var r18 = GetScraper<MovieR18Dev>();
                await r18.ScrapeAsync(movieID, language);

                if (r18.Metadata != null && !r18.SearchNotFound)
                {
                    mergedMetadata = MetadataMerger.MergePrimary(mergedMetadata, r18.Metadata);

                    if (downloadCover && !File.Exists(coverPath) && !string.IsNullOrEmpty(r18.ImageSource))
                    {
                        await DownloadCoverAsync(coverPath, r18.ImageSource);
                    }
                }
            }

            // =========================================================================
            // Tier 3: Secondary Fallback Modules (Executes if data is still incomplete)
            // =========================================================================
            if (!IsMetadataFullyPopulated(mergedMetadata, coverPath, downloadCover))
            {
                _logger.LogInformation($"Core pipeline incomplete for {movieID}. Running secondary fallback enrichment engines...");

                mergedMetadata = await RunSecondaryEnrichmentAsync<MovieSupJav>(movieID, mergedMetadata, coverPath, language);

                if (!IsMetadataFullyPopulated(mergedMetadata, coverPath, downloadCover))
                {
                    mergedMetadata = await RunSecondaryEnrichmentAsync<MovieMissAv>(movieID, mergedMetadata, coverPath, language);
                }
            }

            return mergedMetadata;
        }

        private async Task<MovieMetadata> ScrapeFC2PipelineAsync(string id, string coverPath, LanguageType language, bool downloadCover)
        {
            var metadata = new MovieMetadata(id);

            var supJav = GetScraper<MovieSupJav>();
            await supJav.ScrapeAsync(id, language);
            metadata = supJav.Metadata;
            if (downloadCover && !string.IsNullOrEmpty(supJav.ImageSource)) await DownloadCoverAsync(coverPath, supJav.ImageSource);

            if (downloadCover && !File.Exists(coverPath))
            {
                var javTiful = GetScraper<MovieJavTiful>();
                await javTiful.ScrapeAsync(id, language);
                metadata = MetadataMerger.MergePrimary(javTiful.Metadata, metadata);
                if (!string.IsNullOrEmpty(javTiful.ImageSource)) await DownloadCoverAsync(coverPath, javTiful.ImageSource);
            }

            if (downloadCover && !File.Exists(coverPath))
            {
                var missAv = GetScraper<MovieMissAv>();
                await missAv.ScrapeAsync(id, language);
                metadata = MetadataMerger.MergePrimary(missAv.Metadata, metadata);
                if (!string.IsNullOrEmpty(missAv.ImageSource)) await DownloadCoverAsync(coverPath, missAv.ImageSource);
            }

            if (!metadata.Genres.Contains("FC2PPV"))
                metadata.Genres.Add("FC2PPV");

            return metadata;
        }

        private async Task<MovieMetadata> RunSecondaryEnrichmentAsync<T>(string id, MovieMetadata currentMetadata, string coverPath, LanguageType lang) where T : IMovieScraper
        {
            var scraper = GetScraper<T>();
            await scraper.ScrapeAsync(id, lang);

            if (scraper.Metadata != null && !scraper.SearchNotFound)
            {
                currentMetadata = MetadataMerger.MergePrimary(currentMetadata, scraper.Metadata);

                if (!File.Exists(coverPath) && !string.IsNullOrEmpty(scraper.ImageSource))
                {
                    await DownloadCoverAsync(coverPath, scraper.ImageSource);
                }
            }
            return currentMetadata;
        }

        private async Task DownloadCoverAsync(string targetPath, string url)
        {
            string downloadedPath = await _imageService.DownloadImageAsync(_cacheFolder, url);
            if (!string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath))
            {
                File.Copy(downloadedPath, targetPath, overwrite: true);
            }
        }

        private T GetScraper<T>() where T : IMovieScraper
        {
            return ServiceProviderServiceExtensions.GetRequiredService<T>(_serviceProvider);
        }

        private bool IsMovieMetadataAcceptable(MovieMetadata meta) => meta != null && !string.IsNullOrEmpty(meta.Title);

        private bool IsMetadataFullyPopulated(MovieMetadata? meta, string coverPath, bool requiresCover)
        {
            if (meta == null) return false;

            if (requiresCover && !File.Exists(coverPath)) return false;

            return !string.IsNullOrEmpty(meta.Title) &&
                   !string.IsNullOrEmpty(meta.Studio) &&
                   !string.IsNullOrEmpty(meta.Premiered) &&
                   meta.Actors.Any() &&
                   meta.Genres.Any();
        }
    }
}