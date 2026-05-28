using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Xml.Serialization;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface INfoParserService
    {
        Task<Movie?> ParseNfoAsync(string path);
    }

    public class NfoParserService : INfoParserService
    {
        private readonly IMovieIdService _movieIdService;
        private readonly ILogger<NfoParserService> _logger;

        public NfoParserService(IMovieIdService movieIdService, ILogger<NfoParserService> logger)
        {
            _movieIdService = movieIdService;
            _logger = logger;
        }

        // 📄 Parse NFO (NO DB access)
        public async Task<Movie?> ParseNfoAsync(string path)
        {
            //_logger.LogInformation("Reading NFO: {Path} (Exists: {Exists})", path, File.Exists(path));

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


            int? ParseInt(string? s)
                => int.TryParse(s, out var i) ? i : null;
            //_logger.LogInformation("Parsed Date: {DateAdded} , Nfo Value : {NfoValue} Movie Id : {Id}", ParseDate(nfo.DateAdded), nfo.DateAdded, nfo.UniqueId?.Value);
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
        private DateTime? ParseDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            // 🔥 THE FIX: Uppercase the string to ensure "PM" matches "TT"
            // and trim to remove any invisible whitespace.
            string cleaned = s.Trim().ToUpperInvariant();

            // Define the specific patterns found in your NFOs
            string[] formats = {
                "yyyy-MM-dd:htt",   // Handles 1PM
                "yyyy-MM-dd:hhtt",  // Handles 12PM
                "yyyy-MM-dd:h:mmtt",
                "yyyy-MM-dd",
                "yyyy-MM-dd HH:mm:ss"
            };

            // Use AllowWhiteSpaces to be extra safe with the NFO formatting
            if (DateTime.TryParseExact(cleaned, formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var d))
            {
                return d;
            }

            // Final loose fallback
            return DateTime.TryParse(s, out var fallback) ? fallback : null;
        }
        private string Normalize(string s) => s.Trim().ToLowerInvariant();
    }
}
