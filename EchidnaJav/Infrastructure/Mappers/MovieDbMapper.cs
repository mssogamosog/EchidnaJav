using EchidnaJav.Domain.Entities;
using EchidnaJav.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Infrastructure.Mappers
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
            foreach (var ma in source.MovieActresses)
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
