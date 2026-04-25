using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchidnaJav.Core.Infrastructure.Persistence
{
    public interface IMovieRepositoryService
    {
        Task<MovieDetailsDto?> GetMovieDetailsAsync(string id);
        Task<List<MovieDto>> GetMoviesAsync(MovieQueryParameters queryParams);
    }

    public class MovieRepositoryService : IMovieRepositoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IImageService _imageService;

        public MovieRepositoryService(
            IDbContextFactory<AppDbContext> dbFactory,
            IImageService imageService)
        {
            _dbFactory = dbFactory;
            _imageService = imageService;
        }

        public async Task<List<MovieDto>> GetMoviesAsync(MovieQueryParameters queryParams)
        {
            using var db = _dbFactory.CreateDbContext();

            // 1. Start with the base query
            var query = db.Movies.AsNoTracking().AsQueryable();

            // 2. Exact Actress Filter
            if (!string.IsNullOrWhiteSpace(queryParams.SearchActress))
            {
                query = query.Where(m => m.MovieActresses.Any(a => a.Actress.Name == queryParams.SearchActress));
            }

            // 3. Multi-term "Path-Aware" Search Text Logic using native SQL LIKE
            if (!string.IsNullOrWhiteSpace(queryParams.SearchText))
            {
                var terms = queryParams.SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var term in terms)
                {
                    if (term.StartsWith("-") && term.Length > 1)
                    {
                        // EXCLUSION: Wrap the term in % wildcards for SQL LIKE
                        var excl = $"%{term.Substring(1)}%";

                        query = query.Where(m =>
                            !EF.Functions.Like(m.Title, excl) &&
                            !EF.Functions.Like(m.Id, excl) &&
                            !(m.Studio != null && EF.Functions.Like(m.Studio, excl)) &&
                            !m.MovieActresses.Any(a => EF.Functions.Like(a.Actress.Name, excl)) &&
                            !m.Files.Any(f => f.FilePath != null && EF.Functions.Like(f.FilePath, excl))
                        );
                    }
                    else
                    {
                        // INCLUSION: Wrap the term in % wildcards for SQL LIKE
                        var cleanTerm = term.StartsWith("-") ? term : term; // Catch isolated "-"
                        var incl = $"%{cleanTerm}%";

                        query = query.Where(m =>
                            EF.Functions.Like(m.Title, incl) ||
                            EF.Functions.Like(m.Id, incl) ||
                            (m.Studio != null && EF.Functions.Like(m.Studio, incl)) ||
                            m.MovieActresses.Any(a => EF.Functions.Like(a.Actress.Name, incl)) ||
                            m.Files.Any(f => f.FilePath != null && EF.Functions.Like(f.FilePath, incl))
                        );
                    }
                }
            }          

            
            query = query.Where(m => m.Files.Any(f =>
                f.FileName.EndsWith(".mp4") ||
                f.FileName.EndsWith(".mkv") ||
                f.FileName.EndsWith(".avi") ||
                f.FileName.EndsWith(".wmv") ||
                f.FileName.EndsWith(".ts") ||
                f.FileName.EndsWith(".iso")
            ));

            if (queryParams.MissingImageOnly)
            {
                query = query.Where(m => string.IsNullOrEmpty(m.PrimaryImagePath));
            }
            // 6. Apply Sorting
            query = queryParams.SortBy switch
            {
                SortMoviesBy.DateNewest => query.OrderByDescending(m => m.Premiered).ThenBy(m => m.Title),
                SortMoviesBy.DateOldest => query.OrderBy(m => m.Premiered).ThenBy(m => m.Title),
                SortMoviesBy.ActressName => query.OrderBy(m => m.MovieActresses.FirstOrDefault().Actress.Name),
                SortMoviesBy.RecentlyAdded => query.OrderByDescending(m => m.DateAdded),
                SortMoviesBy.ID => query.OrderBy(m => m.NormalizedId),
                SortMoviesBy.Random => query.OrderBy(m => Guid.NewGuid()), // EF Core standard for random sorting
                _ => query.OrderBy(m => m.Title) // Default fallback
            };

            // 7. Project and Paginate
            var movies = await query
                .Skip(queryParams.Skip)
                .Take(queryParams.Take)
                .Select(m => new MovieDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    ImagePath = m.PrimaryImagePath
                })
                .ToListAsync();

            return movies;
        }

        public async Task<MovieDetailsDto?> GetMovieDetailsAsync(string id)
        {
            using var db = _dbFactory.CreateDbContext();

            return await db.Movies
                .AsNoTracking()
                .Where(m => m.Id == id)
                .Select(m => new MovieDetailsDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    ImagePath = m.PrimaryImagePath,

                    Premiered = m.Premiered,
                    Runtime = m.Runtime,
                    Studio = m.Studio,
                    Director = m.Director,
                    Plot = m.Plot,

                    Cast = m.MovieActresses
                        .Select(a => a.Actress.Name)
                        .ToList(),

                    Genres = m.MovieGenres
                        .Select(g => g.Genre.Name)
                        .ToList(),

                    Files = m.Files.Select(f => new FileDto
                    {
                        FileName = f.FileName
                    })
                    .ToList()
                })
                .FirstOrDefaultAsync();
        }
    }
}