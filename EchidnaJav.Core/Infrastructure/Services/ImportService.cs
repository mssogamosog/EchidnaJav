using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
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
        private readonly IMovieIdService _movieIdService;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<ImportService> _logger;
        private readonly IMovieDbMapper _movieDbMapper;
        private readonly IImageService _imageService;
        private readonly ConcurrentDictionary<string, bool> _queuedImages = new();

        public ImportService(
            IMovieIdService movieIdService,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<ImportService> logger,
            IMovieDbMapper movieDbMapper,
            IImageService imageService)
        {
            _movieIdService = movieIdService;
            _dbFactory = dbFactory;
            _logger = logger;
            _movieDbMapper = movieDbMapper;
            _imageService = imageService;
        }

        public async Task ImportFromFolderAsync(string rootPath, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
        {
            var groups = await GroupFilesByMovieAsync(rootPath);

            int total = groups.Count;
            int processed = 0; // Shared counter

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
                    var genreCache = await db.Genres.ToDictionaryAsync(g => g.Name.ToLower(), ct);
                    var actressCache = await db.Actresses.ToDictionaryAsync(a => a.Name.ToLower(), ct);
                    await foreach (var movie in channel.Reader.ReadAllAsync(ct))
                    {
                        try
                        {                          

                            var exists = await db.Movies.AnyAsync(m => m.Id == movie.Id, ct);

                            if (exists)
                            {
                                _logger.LogInformation($"⏩ Skipped (exists): {movie.Id}");

                                // 🔥 Thread-safe increment
                                var currentProcessed = Interlocked.Increment(ref processed);
                                progress?.Report(new ImportProgress
                                {
                                    Processed = currentProcessed,
                                    Total = total,
                                    CurrentMovieId = movie.Id,
                                    Status = "Skipped"
                                });
                                continue;
                            }

                            var dbMovie = _movieDbMapper.MapToDbMovie(movie, db, genreCache, actressCache);
                            var bestImage = _imageService.GetBestImage(movie);
                            dbMovie.PrimaryImagePath = bestImage;

                            db.Movies.Add(dbMovie);
                            await db.SaveChangesAsync(ct);

                            if (bestImage != null && _queuedImages.TryAdd(bestImage, true))
                            {
                                await imageWriter.WriteAsync(bestImage, ct);
                            }

                            // 🔥 Thread-safe increment
                            var curProcessed = Interlocked.Increment(ref processed);
                            progress?.Report(new ImportProgress
                            {
                                Processed = curProcessed,
                                Total = total,
                                CurrentMovieId = dbMovie.Id,
                                Status = "Imported"
                            });

                            _logger.LogInformation("✅ Imported: {MovieId}", dbMovie.Id);
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // Let the outer block catch the cancellation
                        }
                        catch (Exception ex)
                        {
                            var curProcessed = Interlocked.Increment(ref processed);
                            progress?.Report(new ImportProgress
                            {
                                Processed = curProcessed,
                                Total = total,
                                CurrentMovieId = movie?.Id,
                                Status = "Failed"
                            });
                            _logger.LogError(ex, $"❌ Failed saving movie {movie?.Id}");
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
                            // Passing movieId as fallback to prevent null IDs!
                            var movie = await ParseNfoAsync_NoDb(nfoFile);

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
                                        Hash = ComputeMetadataHash(file),
                                        IsScanned = true
                                    };
                                }).ToList();

                                await channel.Writer.WriteAsync(movie, token);
                            }
                        }
                        else
                        {
                            // 🔥 THE FIX FOR THE 70% FREEZE: 
                            // If there is no NFO, we MUST count it as processed so the math adds up!
                            var curProcessed = Interlocked.Increment(ref processed);
                            progress?.Report(new ImportProgress
                            {
                                Processed = curProcessed,
                                Total = total,
                                CurrentMovieId = movieId,
                                Status = "Skipped (No NFO)"
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Handled by Parallel.ForEachAsync
                    }
                    catch (Exception ex)
                    {
                        var curProcessed = Interlocked.Increment(ref processed);
                        progress?.Report(new ImportProgress { Processed = curProcessed, Total = total, CurrentMovieId = group.Key, Status = "Failed" });
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
                await consumerTask;

                imageWriter.TryComplete();
                await Task.WhenAll(imageWorkers); 
            }
        }
        // 📦 Group files (parallel)
        public async Task<Dictionary<string, List<string>>> GroupFilesByMovieAsync(string rootPath)
        {
            var allFiles = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories);

            var groups = new ConcurrentDictionary<string, ConcurrentBag<string>>();

            await Parallel.ForEachAsync(allFiles, (file, _) =>
            {
                var id = _movieIdService.ParseMovieID(file);

                if (string.IsNullOrEmpty(id))
                    return ValueTask.CompletedTask;

                var bag = groups.GetOrAdd(id, _ => new ConcurrentBag<string>());
                bag.Add(file);

                return ValueTask.CompletedTask;
            });

            return groups.ToDictionary(k => k.Key, v => v.Value.ToList());
        }

        // 📄 Parse NFO (NO DB access)
        private async Task<Movie?> ParseNfoAsync_NoDb(string path)
        {
            _logger.LogInformation("Reading NFO: {Path} (Exists: {Exists})", path, File.Exists(path));

            if (!File.Exists(path))
                return null;

            NfoMovie? nfo;

            try
            {
                var serializer = new XmlSerializer(typeof(NfoMovie));

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite // 🔥 important for cloud/downloads
                );

                nfo = (NfoMovie?)serializer.Deserialize(stream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to deserialize NFO: {Path}", path);
                return null;
            }

            if (nfo == null)
                return null;

            // 🔥 Safe parsing helpers
            DateTime? ParseDate(string? s)
                => DateTime.TryParse(s, out var d) ? d : null;

            int? ParseInt(string? s)
                => int.TryParse(s, out var i) ? i : null;

            var movie = new Movie
            {
                Id = nfo.UniqueId?.Value,
                
                Title = nfo.Title ?? "",
                OriginalTitle = string.IsNullOrWhiteSpace(nfo.OriginalTitle) ? null : nfo.OriginalTitle,
                Premiered = ParseDate(nfo.Premiered),
                Year = ParseInt(nfo.Year),
                Runtime = ParseInt(nfo.Runtime),
                Director = nfo.Director,
                Studio = nfo.Studio,
                Label = nfo.Label,
                Plot = nfo.Plot,
                DateAdded = ParseDate(nfo.DateAdded) ?? DateTime.Now,
                MovieGenres = new(),
                MovieActresses = new()
            };
            movie.NormalizedId = _movieIdService.GenerateNormalizedID(movie.Id);
            // 🎭 Actresses (DEDUPED)
            if (nfo.Actors != null)
            {
                var distinctActors = nfo.Actors
                    .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                    .GroupBy(a => Normalize(a.Name)) 
                    .Select(g => g.First());         

                foreach (var actor in distinctActors)
                {
                    movie.MovieActresses.Add(new MovieActress
                    {
                        Actress = new Actress { Name = actor.Name },
                        Order = actor.Order ?? 0
                    });
                }
            }
            // 🏷️ Genres
            if (nfo.Genres != null)
            {
                var distinct = nfo.Genres
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .Select(g => g!.Trim())
                    .GroupBy(g => Normalize(g))
                    .Select(g => g.First());

                foreach (var g in distinct)
                {
                    movie.MovieGenres.Add(new MovieGenre
                    {
                        Genre = new Genre { Name = g }
                    });
                }
            }

            return movie;
        }

        private string ComputeMetadataHash(string path)
        {
            var fi = new FileInfo(path);
            return $"{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
        }

        private string Normalize(string s)
            => s.Trim().ToLowerInvariant();
    }
}