using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

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

            MovieMetadata mergedMetadata ;

            // 3. Merge Logic
            //if (r18Success && javDbSuccess)
            //    mergedMetadata = MergeSecondary(javDb.Metadata, r18.Metadata);
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

                //mergedMetadata = MergePrimary(javLib.Metadata, mergedMetadata);
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

            // Execute Sequentially: SupJav -> JavTiful -> MissAv until cover/data filled
            var supJav = GetScraper<MovieSupJav>();
            await supJav.ScrapeAsync(id, language);
            metadata = supJav.Metadata;
            if (downloadCover && !string.IsNullOrEmpty(supJav.ImageSource)) await DownloadCoverAsync(coverPath, supJav.ImageSource);

            //if (downloadCover && !File.Exists(coverPath))
            //{
            //    var javTiful = GetScraper<MovieJavTiful>();
            //    await javTiful.ScrapeAsync(id, language);
            //    metadata = MergePrimary(javTiful.Metadata, metadata);
            //    if (!string.IsNullOrEmpty(javTiful.ImageSource)) await DownloadCoverAsync(coverPath, javTiful.ImageSource);
            //}

            //if (downloadCover && !File.Exists(coverPath))
            //{
            //    var missAv = GetScraper<MovieMissAv>();
            //    await missAv.ScrapeAsync(id, language);
            //    metadata = MergePrimary(missAv.Metadata, metadata);
            //    if (!string.IsNullOrEmpty(missAv.ImageSource)) await DownloadCoverAsync(coverPath, missAv.ImageSource);
            //}

            if (!metadata.Genres.Contains("FC2PPV"))
                metadata.Genres.Add("FC2PPV");

            return metadata;
        }

        private async Task RunSecondaryEnrichmentAsync<T>(string id, MovieMetadata target, string coverPath, LanguageType lang) where T : IMovieScraper
        {
            var scraper = GetScraper<T>();
            await scraper.ScrapeAsync(id, lang);
            MergeSecondary(target, scraper.Metadata);

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

        private MovieMetadata MergePrimary(MovieMetadata javLibrary, MovieMetadata javDatabase)
        {
            var combined = new MovieMetadata();
            combined.UniqueID = javDatabase.UniqueID;
            // Prefer JavDatabase for titles
            combined.Title = MergeStrings(javDatabase.Title, javLibrary.Title);
            // For all other info, prefer JavLibrary
            combined.OriginalTitle = MergeStrings(javLibrary.OriginalTitle, javDatabase.OriginalTitle);
            combined.Premiered = MergeStrings(javLibrary.Premiered, javDatabase.Premiered);
            combined.Year = MergeNumbers(javLibrary.Year, javDatabase.Year);
            combined.Studio = MergeStrings(javLibrary.Studio, javDatabase.Studio);
            combined.Label = MergeStrings(javLibrary.Label, javDatabase.Label);
            combined.Runtime = MergeNumbers(javLibrary.Runtime, javDatabase.Runtime);
            combined.Director = MergeStrings(javLibrary.Director, javDatabase.Director);
            combined.Series = MergeStrings(javLibrary.Series, javDatabase.Series);
            combined.Genres = MergeStringLists(javLibrary.Genres, javDatabase.Genres);

            // JavLibrary gives us alternate names, which is really handy, but since we're scraping
            // actress data from JavDatabase first, we'll initially get actresses from them and 
            // try merging in other actresses later.  
            combined.Actors = MergeActors(javDatabase.Actors, javLibrary.Actors);
            return combined;
        }
        private MovieMetadata MergeSecondary(MovieMetadata primary, MovieMetadata secondary)
        {
            primary.Title = MergeStrings(primary.Title, secondary.Title);
            primary.OriginalTitle = MergeStrings(primary.OriginalTitle, secondary.OriginalTitle);
            primary.Premiered = MergeStrings(primary.Premiered, secondary.Premiered);
            primary.Year = MergeNumbers(primary.Year, secondary.Year);
            primary.Studio = MergeStrings(primary.Studio, secondary.Studio);
            primary.Label = MergeStrings(primary.Label, secondary.Label);
            primary.Runtime = MergeNumbers(primary.Runtime, secondary.Runtime);
            primary.Director = MergeStrings(primary.Director, secondary.Director);
            primary.Series = MergeStrings(primary.Series, secondary.Series);
            primary.Genres = MergeStringLists(primary.Genres, secondary.Genres);
            primary.Actors = MergeActors(primary.Actors, secondary.Actors);

            return primary;
        }
        private List<string> MergeStringLists(List<string> a, List<string> b)
        {
            return a.Union(b, StringComparer.OrdinalIgnoreCase).ToList();
        }
        private string MergeStrings(string a, string b)
        {
            if (String.IsNullOrEmpty(a))
                return b;
            return a;
        }
        private int MergeNumbers(int a, int b)
        {
            return (a == 0) ? b : a;
        }
        private List<ActorData> MergeActors(List<ActorData> a, List<ActorData> b)
        {
            if (a == null || a.Count == 0)
                return b ?? new List<ActorData>();

            if (b == null || b.Count == 0)
                return a;

            foreach (var actorB in b)
            {
                bool merged = false;

                foreach (var actorA in a)
                {
                    if (AreActorsEquivalent(actorA, actorB))
                    {
                        MergeActors(actorA, actorB);
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                    a.Add(actorB);
            }

            return a;
        }
        private void MergeActors(ActorData target, ActorData source)
        {
            if (target == null || source == null)
                return;

            // 🔥 Keep better name
            if (IsBetterName(source.Name, target.Name))
            {
                // Move old name to aliases before replacing
                AddAliasIfMissing(target, target.Name);
                target.Name = source.Name;
            }
            else
            {
                AddAliasIfMissing(target, source.Name);
            }

            // Merge aliases
            foreach (var alias in source.Aliases ?? Enumerable.Empty<string>())
            {
                AddAliasIfMissing(target, alias);
            }
        }
        private static void AddAliasIfMissing(ActorData actor, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (actor.Aliases == null)
                actor.Aliases = new List<string>();

            if (!actor.Aliases.Any(a =>
                string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
            {
                actor.Aliases.Add(name);
            }
        }
        private static bool IsBetterName(string candidate, string current)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            if (string.IsNullOrWhiteSpace(current))
                return true;

            // Prefer names with spaces (Hinano Minami > Minamihinano)
            bool candidateHasSpace = candidate.Contains(" ");
            bool currentHasSpace = current.Contains(" ");

            if (candidateHasSpace && !currentHasSpace)
                return true;

            if (!candidateHasSpace && currentHasSpace)
                return false;

            // Otherwise prefer longer (more complete)
            return candidate.Length > current.Length;
        }
        public static bool AreActorsEquivalent(ActorData a, ActorData b)
        {
            if (a == null || b == null)
                return false;

            // Exact match
            if (string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
                return true;

            // Direct alias match
            if (Equals(b.Name, a.Aliases, StringComparison.OrdinalIgnoreCase))
                return true;

            if (Equals(a.Name, b.Aliases, StringComparison.OrdinalIgnoreCase))
                return true;

            // Alias ↔ alias
            foreach (var name in a.Aliases ?? Enumerable.Empty<string>())
            {
                if (Equals(name, b.Aliases, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // 🔥 Structural + fuzzy fallback
            return AreActorsNearlyEquivalent(a, b);
        }
        private static bool AreActorsNearlyEquivalent(ActorData a, ActorData b)
        {
            if (a == null || b == null)
                return false;

            // 🔥 Strong structural match first (handles reversed + concatenated)
            if (AreNamesStructurallyEquivalent(a.Name, b.Name))
                return true;

            const float SimilarityThreshold = 0.75f;

            if (GetSimilarity(a.Name, b.Name) > SimilarityThreshold)
                return true;

            if (GetSimilarityMatches(b.Name, a.Aliases, SimilarityThreshold))
                return true;

            if (GetSimilarityMatches(a.Name, b.Aliases, SimilarityThreshold))
                return true;

            foreach (var name in a.Aliases ?? Enumerable.Empty<string>())
            {
                if (GetSimilarityMatches(name, b.Aliases, SimilarityThreshold))
                    return true;
            }

            return false;
        }
        private static bool GetSimilarityMatches(string s, List<string> strings, float threshold)
        {
            foreach (var str in strings)
            {
                if (GetSimilarity(s, str) > threshold)
                    return true;
            }
            return false;
        }
        public static float GetSimilarity(string left, string right)
        {
            if (String.IsNullOrEmpty(left) && String.IsNullOrEmpty(right))
                return 1.0f;
            if (String.IsNullOrEmpty(left) || String.IsNullOrEmpty(right))
                return 0.0f;
            if (left == right)
                return 1.0f;

            int leftSize = left.Length;
            int rightSize = right.Length;
            int leftIdx = 0;
            int rightIdx = 0;
            float matchVal = 0.0f;
            int maxSize = Math.Max(leftSize, rightSize);

            while (leftIdx < leftSize && rightIdx < rightSize)
            {
                if (left[leftIdx] == right[rightIdx])
                {
                    matchVal += 1.0f / maxSize;
                    ++leftIdx;
                    ++rightIdx;
                }
                else if (char.ToLowerInvariant(left[leftIdx]) == char.ToLowerInvariant(right[rightIdx]))
                {
                    matchVal += 0.9f / maxSize;
                    ++leftIdx;
                    ++rightIdx;
                }
                else
                {
                    int lidxbest = leftSize;
                    int ridxbest = rightSize;
                    int totalCount = 0;
                    int bestCount = int.MaxValue;
                    int leftCount = 0;
                    for (int lidx = leftIdx; lidx != leftSize; ++lidx)
                    {
                        int rightCount = 0;
                        for (int ridx = rightIdx; ridx != rightSize; ++ridx)
                        {
                            if (char.ToLowerInvariant(left[lidx]) == char.ToLowerInvariant(right[ridx]))
                            {
                                totalCount = leftCount + rightCount;
                                if (totalCount < bestCount)
                                {
                                    bestCount = totalCount;
                                    lidxbest = lidx;
                                    ridxbest = ridx;
                                }
                            }
                            ++rightCount;
                        }
                        ++leftCount;
                    }
                    leftIdx = lidxbest;
                    rightIdx = ridxbest;
                }
            }
            return Math.Max(Math.Min(matchVal, 1.0f), 0.0f);
        }
        private static List<string> Tokenize(string name)
        {
            return name
                .ToLowerInvariant()
                .Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static string Normalize(string name)
        {
            return name
                .ToLowerInvariant()
                .Replace(" ", "")
                .Replace("　", "");
        }
        private static bool AreNamesStructurallyEquivalent(string nameA, string nameB)
        {
            if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB))
                return false;

            var tokensA = Tokenize(nameA);
            var tokensB = Tokenize(nameB);

            string normA = Normalize(nameA);
            string normB = Normalize(nameB);

            // Case 1: same tokens, different order
            if (tokensA.Count > 1 && tokensB.Count > 1)
            {
                if (new HashSet<string>(tokensA).SetEquals(tokensB))
                    return true;
            }

            // Case 2: one is concatenated
            if (tokensA.Count > 1 && tokensA.All(t => normB.Contains(t)))
                return true;

            if (tokensB.Count > 1 && tokensB.All(t => normA.Contains(t)))
                return true;

            return false;
        }
        public static bool Equals(string str, List<string> strings, StringComparison comparison)
        {
            foreach (string s in strings)
            {
                if (String.Equals(str, s, comparison))
                    return true;
            }
            return false;
        }
        private bool IsMovieMetadataAcceptable(MovieMetadata meta) => meta != null && !string.IsNullOrEmpty(meta.Title);
    }
}
