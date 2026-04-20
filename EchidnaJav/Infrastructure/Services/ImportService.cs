using EchidnaJav.Domain.DTOs;
using EchidnaJav.Domain.Entities;
using EchidnaJav.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

        public ImportService(IMovieIdService movieIdService, IDbContextFactory<AppDbContext> dbFactory)
        {
            _movieIdService = movieIdService;
            _dbFactory = dbFactory;
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
                using var db = _dbFactory.CreateDbContext();

                // Cache existing data
                var genreCache = await db.Genres.ToDictionaryAsync(g => g.Name.ToLower(), ct);
                var actressCache = await db.Actresses.ToDictionaryAsync(a => a.Name.ToLower(), ct);
                var existingMovies = await db.Movies.Select(m => m.Id).ToHashSetAsync(ct);

                await foreach (var movie in reader.ReadAllAsync(ct))
                {
                    try
                    {
                        if (existingMovies.Contains(movie.Id))
                            continue;

                        // 🔧 Resolve Genres
                        foreach (var mg in movie.MovieGenres)
                        {
                            var key = Normalize(mg.Genre.Name);

                            if (!genreCache.TryGetValue(key, out var genre))
                            {
                                genre = new Genre { Name = mg.Genre.Name };
                                db.Genres.Add(genre);
                                genreCache[key] = genre;
                            }

                            mg.Genre = genre;
                        }

                        // 🔧 Resolve Actresses
                        foreach (var ma in movie.MovieActresses)
                        {
                            var key = Normalize(ma.Actress.Name);

                            if (!actressCache.TryGetValue(key, out var actress))
                            {
                                actress = new Actress { Name = ma.Actress.Name };
                                db.Actresses.Add(actress);
                                actressCache[key] = actress;
                            }

                            ma.Actress = actress;
                        }

                        db.Movies.Add(movie);
                        await db.SaveChangesAsync(ct);

                        // 👉 Hook for UI progress
                        Console.WriteLine($"Imported: {movie.Id}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Failed {movie.Id}: {ex.Message}");
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
                    Console.WriteLine($"❌ Failed processing group: {ex.Message}");
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
            Console.WriteLine($"Exists: {File.Exists(path)} - {path}");

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
                Console.WriteLine($"❌ Failed to deserialize: {path}");
                Console.WriteLine(ex.InnerException?.Message ?? ex.Message);
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

            // 🎭 Actresses
            if (nfo.Actors != null)
            {
                foreach (var actor in nfo.Actors)
                {
                    if (string.IsNullOrWhiteSpace(actor.Name))
                        continue;

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