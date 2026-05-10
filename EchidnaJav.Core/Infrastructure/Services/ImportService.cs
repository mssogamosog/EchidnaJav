using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.FileSystem;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Mappers;
using EchidnaJav.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Xml.Serialization;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface IImportService
    {
        Task ImportFromFolderAsync(
            string rootPath,
            IProgress<ImportProgress>? progress = null,
            CancellationToken ct = default);
    }

    public class ImportService : IImportService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<ImportService> _logger;
        private readonly IMovieDbMapper _movieDbMapper;
        private readonly IImageService _imageService;
        private readonly ILocalMediaScanner _localMediaScanner;
        private readonly INfoParserService _nfoParserService;
        private readonly IScrapeService _scrapeService;
        private readonly IActressScrapeQueue _actressQueue;
        private readonly ConcurrentDictionary<string, bool> _queuedImages = new();

        public ImportService(
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<ImportService> logger,
            IMovieDbMapper movieDbMapper,
            IImageService imageService,
            ILocalMediaScanner localMediaScanner,
            INfoParserService nfoParserService,
            IScrapeService scrapeService,
            IActressScrapeQueue actressQueue)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _movieDbMapper = movieDbMapper;
            _scrapeService = scrapeService;
            _actressQueue = actressQueue;
            _nfoParserService = nfoParserService;
            _localMediaScanner = localMediaScanner;
            _imageService = imageService;
        }

        public async Task ImportFromFolderAsync(string rootPath, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
        {
            var newlyScrapedActresses = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var groups = await _localMediaScanner.GroupFilesByMovieAsync(rootPath);

            int total = groups.Count;
            int processed = 0; // Shared counter
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            void Report(string status, string? movieId)
            {
                var current = Interlocked.Increment(ref processed);

                if (stopwatch.ElapsedMilliseconds > 250 || current == total || status == "Failed")
                {
                    progress?.Report(new ImportProgress
                    {
                        Processed = current,
                        Total = total,
                        CurrentMovieId = movieId,
                        Status = status
                    });

                    // Reset the clock
                    stopwatch.Restart();
                }
            }

            var channel = Channel.CreateBounded<Movie>(new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            // 🔥 IMAGE PIPELINE
            var imageChannel = Channel.CreateUnbounded<string>();
            var imageWriter = imageChannel.Writer;
            var imageReader = imageChannel.Reader;

            // 🔥 Start image workers (Added cancellation token to ReadAllAsync)
            var imageWorkers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await foreach (var path in imageReader.ReadAllAsync(ct))
                    {
                        try
                        {
                            await _imageService.GenerateImagesAsync(path);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Image generation failed: {Path}", path);
                        }
                    }
                }
                catch (OperationCanceledException) { /* Graceful exit on cancel */ }
            })).ToList();

            // 🧵 Consumer (DB writer)
            var consumerTask = Task.Run(async () =>
            {
                try
                {
                    using var db = _dbFactory.CreateDbContext();

                    var genreCache = await db.Genres.AsNoTracking().ToDictionaryAsync(g => g.Name.ToLower(), ct);
                    var actressCache = await db.Actresses.AsNoTracking().ToDictionaryAsync(a => a.Name.ToLower(), ct);
                    await foreach (var movie in channel.Reader.ReadAllAsync(ct))
                    {
                        
                        try
                        {                          

                            var exists = await db.Movies.AnyAsync(m => m.Id == movie.Id, ct);

                            if (exists)
                            {
                                //_logger.LogInformation($"⏩ Skipped (exists): {movie.Id}");
                                Report("Skipped (Exists)", movie.Id);
                                continue;
                            }
                            var actressesToQueue = new List<string>();
                            if (movie.MovieActresses != null && movie.MovieActresses.Any())
                            {
                                foreach (var ma in movie.MovieActresses)
                                {
                                    string actorName = ma.Actress?.Name;
                                    if (string.IsNullOrWhiteSpace(actorName)) continue;

                                    string actorNameLower = actorName.ToLower();

                                    if (actressCache.ContainsKey(actorNameLower) || newlyScrapedActresses.ContainsKey(actorName))
                                        continue;

                                    // 1. Create a blank "shell" actress to satisfy the database relationship
                                    var shellActress = new Actress { Name = actorName };
                                    actressCache[actorNameLower] = shellActress;
                                    newlyScrapedActresses.Add(actorName, true);

                                    // 2. Toss the name over the wall to the background worker to deal with later!
                                    actressesToQueue.Add(actorName);
                                }
                            }
                            var dbMovie = _movieDbMapper.MapToDbMovie(movie, db, genreCache, actressCache);
                            var bestImage = _imageService.GetBestImage(movie);
                            dbMovie.PrimaryImagePath = bestImage;

                            db.Movies.Add(dbMovie);
                            await db.SaveChangesAsync(ct);
                            foreach (var actorName in actressesToQueue)
                            {
                                await _actressQueue.QueueActressAsync(actorName);
                            }

                            if (bestImage != null && _queuedImages.TryAdd(bestImage, true))
                            {
                                await imageWriter.WriteAsync(bestImage, ct);
                            }

                            // 🔥 Thread-safe increment
                            Report("Imported", dbMovie.Id);

                            //_logger.LogInformation("✅ Imported: {MovieId}", dbMovie.Id);
                        }
                        catch (Exception ex)
                        {
                            Report("Failed", movie?.Id);
                            _logger.LogError(ex, $"❌ Failed saving movie {movie?.Id}");
                        }
                        finally
                        {
                            
                            db.ChangeTracker.Clear();
                        }
                    }
                }
                catch (OperationCanceledException) { /* Graceful exit */ }
            });

            // 🧵 Producers (parallel)
            try
            {
                await Parallel.ForEachAsync(groups, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = ct
                },
                async (group, token) =>
                {
                    try
                    {
                        var movieId = group.Key;
                        var files = group.Value;

                        var nfoFile = files.FirstOrDefault(f => f.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase));

                        if (nfoFile != null)
                        {

                            var movie = await _nfoParserService.ParseNfoAsync(nfoFile);

                            if (movie != null)
                            {
                                movie.Files = files.Select(file =>
                                {
                                    var fi = new FileInfo(file);
                                    return new FileEntry
                                    {
                                        MovieId = movie.Id,
                                        FilePath = file,
                                        FileName = fi.Name,
                                        SizeBytes = fi.Length,
                                        LastModified = fi.LastWriteTime,
                                        Hash = _localMediaScanner.ComputeMetadataHash(file),
                                        IsScanned = true
                                    };
                                }).ToList();

                                await channel.Writer.WriteAsync(movie, token);
                            }
                        }
                        else
                        {
                            // TODO : scrape metadata from online sources using movieId as query (if enabled in settings)
                            Report("Skipped (No NFO)", movieId);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Handled by Parallel.ForEachAsync
                    }
                    catch (Exception ex)
                    {
                        Report("Failed", group.Key);
                        _logger.LogError(ex, "❌ Failed processing group {MovieId}", group.Key);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("⏹️ Import cancelled by user.");
            }
            finally
            {
               

                channel.Writer.TryComplete();
                try
                {
                    await consumerTask;
                }
                catch{}
                finally
                {
                    imageWriter.TryComplete();
                    await Task.WhenAll(imageWorkers);
                }
            }
        }
        

    }
}