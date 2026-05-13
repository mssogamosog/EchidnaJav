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

        private async Task<MovieMetadata> ScrapeStandardPipelineAsync(string movieID, string coverPath, LanguageType language, bool downloadCover)
        {
            // Resolve modules via DI
            //  var r18 = GetScraper<MovieR18Dev>();
            var javDb = GetScraper<MovieJavDatabase>();

            // 1. Concurrently fetch top priority scrapers to save time
            await Task.WhenAll(
                //r18.ScrapeAsync(movieID, language),
                javDb.ScrapeAsync(movieID, language)
            );

            //bool r18Success = r18.Metadata != null && !r18.SearchNotFound;
            bool javDbSuccess = javDb.Metadata != null && !javDb.SearchNotFound;

            // 2. Download Covers based on priority
            if (downloadCover)
            {
                //if (r18Success && !string.IsNullOrEmpty(r18.ImageSource))
                //    await DownloadCoverAsync(coverPath, r18.ImageSource);
                if (javDbSuccess && !string.IsNullOrEmpty(javDb.ImageSource))
                    await DownloadCoverAsync(coverPath, javDb.ImageSource);
            }

            MovieMetadata mergedMetadata;

            // 3. Merge Logic
            //if (r18Success && javDbSuccess)
            //    mergedMetadata = MetadataMerger.MergeSecondary(javDb.Metadata, r18.Metadata);
            //else if (r18Success)
            //    mergedMetadata = r18.Metadata;
            //else if (javDbSuccess)
            mergedMetadata = javDb.Metadata;
            //else
            //{
            // 4. FALLBACK: JavLibrary only if both failed
            //  var javLib = GetScraper<MovieJavLibrary>();
            //await javLib.ScrapeAsync(movieID, language);

            //if (downloadCover && !string.IsNullOrEmpty(javLib.ImageSource))
            //    await DownloadCoverAsync(coverPath, javLib.ImageSource);

            //mergedMetadata = MetadataMerger.MergePrimary(javLib.Metadata, mergedMetadata);
            // }

            // 5. Secondary Scrapers (Enrichment)
            if (mergedMetadata != null)
            {
                //await RunSecondaryEnrichmentAsync<MovieSupJav>(movieID, mergedMetadata, coverPath, language);
                //await RunSecondaryEnrichmentAsync<MovieJavSeenTv>(movieID, mergedMetadata, coverPath, language);
            }

            // 6. Emergency Cover Fallback
            //if (downloadCover && !File.Exists(coverPath) && mergedMetadata != null)
            //{
            //    _logger.LogInformation("Cover missing. Attempting MissAV emergency fallback...");
            //    var missAv = GetScraper<MovieMissAv>();
            //    await missAv.ScrapeAsync(mergedMetadata.UniqueID.Value, language);

            //    if (!string.IsNullOrEmpty(missAv.ImageSource))
            //        await DownloadCoverAsync(coverPath, missAv.ImageSource);
            //}

            return mergedMetadata;
        }

        private async Task<MovieMetadata> ScrapeFC2PipelineAsync(string id, string coverPath, LanguageType language, bool downloadCover)
        {
            var metadata = new MovieMetadata(id);

            //Execute Sequentially: SupJav->JavTiful->MissAv until cover/ data filled
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

        private async Task RunSecondaryEnrichmentAsync<T>(string id, MovieMetadata target, string coverPath, LanguageType lang) where T : IMovieScraper
        {
            var scraper = GetScraper<T>();
            await scraper.ScrapeAsync(id, lang);
            MetadataMerger.MergeSecondary(target, scraper.Metadata);

            // Fill cover if still missing
            if (!File.Exists(coverPath) && !string.IsNullOrEmpty(scraper.ImageSource))
                await DownloadCoverAsync(coverPath, scraper.ImageSource);
        }

        private async Task DownloadCoverAsync(string targetPath, string url)
        {
            string downloadedPath = await _imageService.DownloadImageAsync(_cacheFolder, url);
            if (!string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath))
            {
                // Move from cache to final destination requested by the pipeline
                File.Copy(downloadedPath, targetPath, overwrite: true);
            }
        }

        // Helper to grab transient scraper instances via DI
        private T GetScraper<T>() where T : IMovieScraper
        {
            return ServiceProviderServiceExtensions.GetRequiredService<T>(_serviceProvider);
        }

        private bool IsMovieMetadataAcceptable(MovieMetadata meta) => meta != null && !string.IsNullOrEmpty(meta.Title);
    }
}