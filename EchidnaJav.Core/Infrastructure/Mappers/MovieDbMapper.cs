using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EchidnaJav.Core.Infrastructure.Mappers
{
    public interface IMovieDbMapper
    {
        Movie MapToDbMovie(Movie source, AppDbContext db, Dictionary<string, Genre> genreCache, Dictionary<string, Actress> actressCache);
    }

    public class MovieDbMapper : IMovieDbMapper
    {
        public Movie MapToDbMovie(
            Movie source,
            AppDbContext db,
            Dictionary<string, Genre> genreCache,
            Dictionary<string, Actress> actressCache)
        {
            // 🔥 Rebuild clean EF entity (DO NOT reuse incoming object)
            var dbMovie = new Movie
            {
                Id = source.Id,
                Title = source.Title,
                OriginalTitle = source.OriginalTitle,
                NormalizedId = source.NormalizedId,
                Premiered = source.Premiered,
                Year = source.Year,
                Director = source.Director,
                Studio = source.Studio,
                Label = source.Label,
                Plot = source.Plot,
                Runtime = source.Runtime,
                DateAdded = source.DateAdded,
                MovieGenres = new List<MovieGenre>(),
                MovieActresses = new List<MovieActress>(),
                Files = new List<FileEntry>()
            };

            // 🏷️ Genres
            foreach (var mg in source.MovieGenres)
            {
                var key = Normalize(mg.Genre.Name);

                if (!genreCache.TryGetValue(key, out var genre))
                {
                    // 1. Not in cache at all. Create and INSERT.
                    genre = new Genre { Name = mg.Genre.Name };
                    db.Genres.Add(genre);
                    genreCache[key] = genre;
                }
                else
                {
                    // 2. Found in cache. Check if EF is already tracking it
                    if (db.Entry(genre).State == EntityState.Detached)
                    {
                        // 🔥 THE FIX: Check if it's a brand new (unsaved) object or an existing DB record
                        if (genre.Id == 0)
                        {
                            db.Genres.Add(genre); // It has no DB ID yet. Tell EF to insert it.
                        }
                        else
                        {
                            db.Attach(genre); // It has an ID. Tell EF it already exists.
                        }
                    }
                }

                dbMovie.MovieGenres.Add(new MovieGenre
                {
                    Movie = dbMovie,
                    Genre = genre
                });
            }

            // 🎭 Actresses
            foreach (var ma in source.MovieActresses)
            {
                var key = Normalize(ma.Actress.Name);

                if (!actressCache.TryGetValue(key, out var actress))
                {
                    // 1. Not in cache at all. Create and INSERT.
                    actress = new Actress { Name = ma.Actress.Name };
                    db.Actresses.Add(actress);
                    actressCache[key] = actress;
                }
                else
                {
                    // 2. Found in cache. Check if EF is already tracking it
                    if (db.Entry(actress).State == EntityState.Detached)
                    {
                        // 🔥 THE FIX: Did this come from the scraper or the database?
                        if (actress.Id == 0)
                        {
                            db.Actresses.Add(actress); // Came from scraper (unsaved). Insert it.
                        }
                        else
                        {
                            db.Attach(actress); // Came from initial DB load. Attach it.
                        }
                    }
                }

                dbMovie.MovieActresses.Add(new MovieActress
                {
                    Movie = dbMovie,
                    Actress = actress,
                    Order = ma.Order
                });
            }

            // 📁 Files
            foreach (var f in source.Files)
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

            return dbMovie;
        }

        private string Normalize(string s)
            => s.Trim().ToLowerInvariant();
    }
}
