using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface INfoGeneratorService
    {
        Task GenerateNfoAsync(string movieId, string targetNfoDirectory);
    }

    public class NfoGeneratorService : INfoGeneratorService
    {
        private readonly AppDbContext _dbContext;

        public NfoGeneratorService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task GenerateNfoAsync(string movieId, string targetNfoDirectory)
        {
            // 1. Pull the complete graph from the Database
            var movieGraph = await _dbContext.Movies
                .AsNoTracking()
                .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
                .Include(m => m.MovieActresses).ThenInclude(ma => ma.Actress).ThenInclude(a => a.Images)
                .FirstOrDefaultAsync(m => m.Id == movieId);

            if (movieGraph == null)
                throw new FileNotFoundException($"Movie {movieId} not found in database.");

            // 2. Map EF Entity back to XML DTO
            var nfoData = new MovieMetadata(movieGraph.Id)
            {
                Title = movieGraph.Title ?? string.Empty,
                OriginalTitle = movieGraph.OriginalTitle ?? string.Empty,
                Director = movieGraph.Director ?? string.Empty,
                Studio = movieGraph.Studio ?? string.Empty,
                Label = movieGraph.Label ?? string.Empty,
                Series = movieGraph.Series ?? string.Empty,
                Plot = movieGraph.Plot ?? string.Empty,
                Runtime = movieGraph.Runtime ?? 0,
                Premiered = movieGraph.Premiered?.ToString("yyyy-MM-dd") ?? string.Empty,
                Year = movieGraph.Year ?? 0,
                DateAdded = movieGraph.DateAdded?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                Thumb = movieGraph.PrimaryImagePath ?? string.Empty
            };

            if (movieGraph.Rating.HasValue)
            {
                nfoData.Ratings.Add(new RatingData
                {
                    Name = "javdb",
                    Max = 10,
                    Value = (float)movieGraph.Rating.Value
                });
            }

            // Map Genres
            nfoData.Genres = movieGraph.MovieGenres
                .Select(mg => mg.Genre.Name)
                .ToList();

            // Map Actresses (Preserving order and fetching cached thumbnails)
            nfoData.Actors = movieGraph.MovieActresses
                .OrderBy(ma => ma.Order)
                .Select(ma => new ActorData
                {
                    Name = ma.Actress.Name ?? string.Empty,
                    Role = "Actress",
                    Order = ma.Order ?? 0,
                    Thumbnail = ma.Actress.Images.OrderBy(i => i.Index).FirstOrDefault()?.Filepath ?? string.Empty
                }).ToList();

            // 3. Serialize strictly as XML to the .nfo file
            string nfoFileName = $"{movieGraph.Id}.nfo";
            string fullPath = Path.Combine(targetNfoDirectory, nfoFileName);

            var serializer = new XmlSerializer(typeof(MovieMetadata));

            // Clean up namespaces to prevent messy XML declarations
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", "");

            var xmlSettings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = true // Optional: Many media players prefer omitted headers
            };

            using (var streamWriter = new StreamWriter(fullPath, append: false, Encoding.UTF8))
            using (var xmlWriter = XmlWriter.Create(streamWriter, xmlSettings))
            {
                serializer.Serialize(xmlWriter, nfoData, namespaces);
            }
        }
    }
}
