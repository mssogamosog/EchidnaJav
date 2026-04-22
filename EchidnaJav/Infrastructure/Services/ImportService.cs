using EchidnaJav.Domain.DTOs;
using EchidnaJav.Domain.Entities;
using EchidnaJav.Infrastructure.Mappers;
using EchidnaJav.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Xml.Serialization;

namespace EchidnaJav.Infrastructure.Services
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

        public ImportService(
            IMovieIdService movieIdService,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<ImportService> logger,
            IMovieDbMapper movieDbMapper)
        {
            _movieIdService = movieIdService;
            _dbFactory = dbFactory;
            _logger = logger;
            _movieDbMapper = movieDbMapper;
        }

        public async Task ImportFromFolderAsync(string rootPath, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
        {
            var groups = await GroupFilesByMovieAsync(rootPath);

            var channel = Channel.CreateBounded<Movie>(new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var writer = channel.Writer;
            var reader = channel.Reader;

            // 🧵 Consumer (DB writer)
            var consumerTask = Task.Run(async () =>
            {
                var total = groups.Count;
                int processed = 0;
                await foreach (var movie in reader.ReadAllAsync(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var db = _dbFactory.CreateDbContext();

                        // 🔹 Load caches per context
                        var genreCache = await db.Genres
                            .ToDictionaryAsync(g => g.Name.ToLower(), ct);

                        var actressCache = await db.Actresses
                            .ToDictionaryAsync(a => a.Name.ToLower(), ct);

                        var exists = await db.Movies
                            .AnyAsync(m => m.Id == movie.Id, ct);

                        if (exists)
                        {
                            Console.WriteLine($"⏩ Skipped (exists): {movie.Id}");
                            continue;
                        }

                        var dbMovie = _movieDbMapper.MapToDbMovie(movie, db, genreCache, actressCache);

                        db.Movies.Add(dbMovie);
                        /*
                        foreach (var e in db.ChangeTracker.Entries())
                        {
                            Console.WriteLine($"{e.Entity.GetType().Name} - {e.State}");
                        }
                        */
                        ct.ThrowIfCancellationRequested();
                        await db.SaveChangesAsync(ct);

                        processed++;

                        progress?.Report(new ImportProgress
                        {
                            Processed = processed,
                            Total = total,
                            CurrentMovieId = dbMovie.Id,
                            Status = "Imported"
                        });

                        _logger.LogInformation("✅ Imported: {MovieId}", dbMovie.Id);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation($"⏹️ Cancelled: {movie?.Id}");
                    }
                    catch (Exception ex)
                    {
                        processed++;

                        progress?.Report(new ImportProgress
                        {
                            Processed = processed,
                            Total = total,
                            CurrentMovieId = movie?.Id,
                            Status = "Failed"
                        });
                        _logger.LogInformation($"❌ Failed saving movie {movie?.Id} {movie?.Files?.FirstOrDefault()?.FilePath}");
                        _logger.LogError(ex.ToString());
                    }
                }
            }, ct);

            // 🧵 Producers (parallel)
            await Parallel.ForEachAsync(groups, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            },
            async (group, token) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var movieId = group.Key;
                    var files = group.Value;

                    var nfoFile = files.FirstOrDefault(f =>
                        f.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase));

                    Movie movie;

                    if (nfoFile != null)
                    {
                        movie = await ParseNfoAsync_NoDb(nfoFile);
                        movie?.Files = files.Select(file =>
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
                        if (token.IsCancellationRequested)
                            return;
                        await writer.WriteAsync(movie, token);
                    }
                    else
                    {
                        // scrape path TODO
                        //movie = new Movie
                        //{
                        //    Id = movieId,
                        //    Title = movieId,
                        //    DateAdded = DateTime.Now,
                        //    MovieGenres = new(),
                        //    MovieActresses = new()
                        //};
                    }

                    
                }
                catch (OperationCanceledException)
                {
                    writer.TryComplete();
                    _logger.LogInformation("⏹️ Import cancelled during processing");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed processing group {MovieId}", group.Key);
                }
            });

            writer.Complete();
            await consumerTask;
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