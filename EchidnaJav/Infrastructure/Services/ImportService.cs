using EchidnaJav.Domain.DTOs;
using EchidnaJav.Domain.Entities;
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
        Task ImportFromFolderAsync(string rootPath, CancellationToken ct = default);
    }

    public class ImportService : IImportService
    {
        private readonly IMovieIdService _movieIdService;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<ImportService> _logger;

        public ImportService(
            IMovieIdService movieIdService,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<ImportService> logger)
        {
            _movieIdService = movieIdService;
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task ImportFromFolderAsync(string rootPath, CancellationToken ct = default)
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
                await foreach (var movie in reader.ReadAllAsync(ct))
                {
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

                        // 🔥 Rebuild clean EF entity (DO NOT reuse incoming object)
                        var dbMovie = new Movie
                        {
                            Id = movie.Id,
                            Title = movie.Title,
                            OriginalTitle = movie.OriginalTitle,
                            Premiered = movie.Premiered,
                            Year = movie.Year,
                            Director = movie.Director,
                            Studio = movie.Studio,
                            Label = movie.Label,
                            Plot = movie.Plot,
                            Runtime = movie.Runtime,
                            DateAdded = movie.DateAdded,

                            MovieGenres = new List<MovieGenre>(),
                            MovieActresses = new List<MovieActress>(),
                            Files = new List<FileEntry>()
                        };

                        // 🏷️ Genres
                        foreach (var mg in movie.MovieGenres)
                        {
                            var key = Normalize(mg.Genre.Name);

                            if (!genreCache.TryGetValue(key, out var genre))
                            {
                                genre = new Genre { Name = mg.Genre.Name };
                                db.Genres.Add(genre);
                                genreCache[key] = genre;
                            }

                            dbMovie.MovieGenres.Add(new MovieGenre
                            {
                                Movie = dbMovie,
                                Genre = genre
                            });
                        }

                        // 🎭 Actresses
                        foreach (var ma in movie.MovieActresses)
                        {
                            var key = Normalize(ma.Actress.Name);

                            if (!actressCache.TryGetValue(key, out var actress))
                            {
                                actress = new Actress { Name = ma.Actress.Name };
                                db.Actresses.Add(actress);
                                actressCache[key] = actress;
                            }
                            dbMovie.MovieActresses.Add(new MovieActress
                            {
                                Movie = dbMovie,
                                Actress = actress,
                                Order = ma.Order
                            });
                        }

                        // 📁 Files
                        foreach (var f in movie.Files)
                        {
                            dbMovie.Files.Add(new FileEntry
                            {
                                Movie = dbMovie,
                                FilePath = f.FilePath,
                                FileName = f.FileName,
                                SizeBytes = f.SizeBytes,
                                LastModified = f.LastModified,
                                Hash = f.Hash,
                                IsScanned = f.IsScanned
                            });
                        }

                        // ➕ Add and save
                        db.Movies.Add(dbMovie);

                        // 🔍 Debug (optional)
                        /*
                        foreach (var e in db.ChangeTracker.Entries())
                        {
                            Console.WriteLine($"{e.Entity.GetType().Name} - {e.State}");
                        }
                        */

                        await db.SaveChangesAsync(ct);

                        _logger.LogInformation($"✅ Imported: {dbMovie.Id}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation($"❌ Failed saving movie {movie?.Id}");
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