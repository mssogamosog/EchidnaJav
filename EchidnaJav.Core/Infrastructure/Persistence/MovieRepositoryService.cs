using EchidnaJav.Core.Domain.Constants;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EchidnaJav.Core.Infrastructure.Persistence
{
    public interface IMovieRepositoryService
    {
        Task<MovieDetailsDto?> GetMovieDetailsAsync(string id);
        Task<List<MovieDto>> GetMoviesAsync(MovieQueryParameters queryParams);
        Task<int> GetTotalMovieCountAsync(MovieQueryParameters queryParams);
        Task<List<string>> GetAllMovieIdsAsync();
        Task<Movie> UpsertScrapedMovieAsync(MovieMetadata scrapedDto, string targetCoverPath, IReadOnlyList<string> files);
    }

    public class MovieRepositoryService : IMovieRepositoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IImageService _imageService;

        public MovieRepositoryService(IDbContextFactory<AppDbContext> dbFactory, IImageService imageService)
        {
            _dbFactory = dbFactory;
            _imageService = imageService;
        }

        #region Read Layer (Queries & Pagination)

        private IQueryable<Movie> BuildFilteredQuery(AppDbContext db, MovieQueryParameters queryParams)
        {
            var query = db.Movies.AsNoTracking().AsQueryable();

            // 1. Exact Actress Filter
            if (!string.IsNullOrWhiteSpace(queryParams.SearchActress))
            {
                query = query.Where(m => m.MovieActresses.Any(a => a.Actress.Name == queryParams.SearchActress));
            }

            // 2. Multi-term "Path-Aware" Search Text Logic
            if (!string.IsNullOrWhiteSpace(queryParams.SearchText))
            {
                var terms = queryParams.SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    if (term.StartsWith("-") && term.Length > 1)
                    {
                        var excl = $"%{term.Substring(1)}%";
                        query = query.Where(m =>
                            !EF.Functions.Like(m.Title, excl) &&
                            !EF.Functions.Like(m.Id, excl) &&
                            !(m.Studio != null && EF.Functions.Like(m.Studio, excl)) &&
                            !m.MovieActresses.Any(a => EF.Functions.Like(a.Actress.Name, excl)) &&
                            !m.Files.Any(f => f.FilePath != null && EF.Functions.Like(f.FilePath, excl)) &&
                            !m.MovieGenres.Any(mg => EF.Functions.Like(mg.Genre.Name, excl))
                        );
                    }
                    else
                    {
                        var incl = $"%{term}%";
                        query = query.Where(m =>
                            EF.Functions.Like(m.Title, incl) ||
                            EF.Functions.Like(m.Id, incl) ||
                            (m.Studio != null && EF.Functions.Like(m.Studio, incl)) ||
                            m.MovieActresses.Any(a => EF.Functions.Like(a.Actress.Name, incl)) ||
                            m.Files.Any(f => f.FilePath != null && EF.Functions.Like(f.FilePath, incl)) ||
                            m.MovieGenres.Any(mg => EF.Functions.Like(mg.Genre.Name, incl))
                        );
                    }
                }
            }

            // 3. Explicit SQL-Safe File Extension Filter
            query = query.Where(m => m.Files.Any(f =>
                MediaConstants.VideoExtensions.Any(ext => f.FileName.EndsWith(ext))));

            // 4. Image Coverage Filter
            if (queryParams.MissingImageOnly)
            {
                query = query.Where(m => string.IsNullOrEmpty(m.PrimaryImagePath));
            }

            return query;
        }

        public async Task<List<MovieDto>> GetMoviesAsync(MovieQueryParameters queryParams)
        {
            using var db = _dbFactory.CreateDbContext();
            var query = BuildFilteredQuery(db, queryParams);

            query = queryParams.SortBy switch
            {
                SortMoviesBy.DateNewest => query.OrderByDescending(m => m.Premiered).ThenBy(m => m.Title),
                SortMoviesBy.DateOldest => query.OrderBy(m => m.Premiered).ThenBy(m => m.Title),
                SortMoviesBy.RecentlyAdded => query.OrderByDescending(m => m.DateAdded),
                SortMoviesBy.ID => query.OrderBy(m => m.NormalizedId),
                SortMoviesBy.ActressName => query.OrderByDescending(m => m.MovieActresses.Any())
                    .ThenBy(m => m.MovieActresses.OrderBy(ma => ma.Order).Select(ma => ma.Actress.Name).FirstOrDefault())
                    .ThenBy(m => m.Title),
                SortMoviesBy.Random => query.OrderBy(m => EF.Functions.Random()),
                _ => query.OrderBy(m => m.Title)
            };

            return await query
                .Skip(queryParams.Skip)
                .Take(queryParams.Take)
                .Select(m => new MovieDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    ImagePath = m.PrimaryImagePath
                })
                .ToListAsync();
        }

        public async Task<int> GetTotalMovieCountAsync(MovieQueryParameters queryParams)
        {
            using var db = _dbFactory.CreateDbContext();
            return await BuildFilteredQuery(db, queryParams).CountAsync();
        }

        public async Task<List<string>> GetAllMovieIdsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Movies.Select(m => m.Id).ToListAsync();
        }

        public async Task<MovieDetailsDto?> GetMovieDetailsAsync(string id)
        {
            using var db = _dbFactory.CreateDbContext();

            var movie = await db.Movies
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
                        .OrderBy(ma => ma.Order)
                        .Select(ma => new MovieActorDto
                        {
                            Name = ma.Actress.Name,
                            ImagePath = ma.Actress.Images.OrderBy(i => i.Index).Select(i => i.Filepath).FirstOrDefault()
                        }).ToList(),
                    Genres = m.MovieGenres.Select(g => g.Genre.Name).ToList(),
                    Files = m.Files.OrderBy(f => f.FileName).Select(f => new FileDto
                    {
                        FileName = f.FileName,
                        FilePath = f.FilePath
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (movie != null)
            {
                // Materialize the collection in-memory first, then execute safely
                movie.Files = movie.Files
                    .Where(f => MediaConstants.VideoExtensions.Any(ext => f.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            return movie;
        }

        #endregion

        #region Write Layer (Scraper Persistence)

        public async Task<Movie> UpsertScrapedMovieAsync(MovieMetadata scrapedData, string primaryImagePath, IReadOnlyList<string> mediaFiles)
        {
            using var db = _dbFactory.CreateDbContext();
            string movieId = scrapedData.UniqueID.Value.ToUpper();

            // 1. Fetch complete tracking graph including nested relationship targets
            var movie = await db.Movies
                .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
                .Include(m => m.MovieActresses).ThenInclude(ma => ma.Actress)
                .Include(m => m.Files)
                .FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null)
            {
                movie = new Movie { Id = movieId };
                db.Movies.Add(movie);
            }

            // --- Map Scalar Properties ---
            movie.NormalizedId = movieId.Replace("-", "").Replace(" ", "");
            movie.Title = scrapedData.Title;
            movie.OriginalTitle = scrapedData.OriginalTitle;
            movie.Director = scrapedData.Director;
            movie.Studio = scrapedData.Studio;
            movie.Label = scrapedData.Label;
            movie.Series = scrapedData.Series;
            movie.Plot = scrapedData.Plot;

            if (scrapedData.Runtime > 0)
            {
                movie.Runtime = scrapedData.Runtime;
            }

            movie.PrimaryImagePath = primaryImagePath;
            movie.DateAdded ??= DateTime.UtcNow;

            if (DateTime.TryParse(scrapedData.Premiered, out DateTime premieredDate))
            {
                movie.Premiered = premieredDate;
                movie.Year = premieredDate.Year;
            }

            var primaryRating = scrapedData.Ratings.FirstOrDefault();
            if (primaryRating != null)
            {
                movie.Rating = primaryRating.Value;
            }

            // --- 2. Safely Synchronize Discovered Files ---
            foreach (string filePath in mediaFiles)
            {
                if (!movie.Files.Any(f => f.FilePath != null && f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        movie.Files.Add(new FileEntry
                        {
                            MovieId = movie.Id,
                            FileName = fileInfo.Name,
                            FilePath = filePath,
                            SizeBytes = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTimeUtc,
                            IsScanned = true,
                            Hash = string.Empty
                        });
                    }
                }
            }

            // --- 3. Differential Synchronization of Relationships ---
            await SyncGenresAsync(db, movie, scrapedData.Genres);
            await SyncActressesAsync(db, movie, scrapedData.Actors);

            // --- 4. Single Atomic Flush Commit ---
            // Guarantees all primary keys and temporary mapping references resolve natively
            await db.SaveChangesAsync();

            return movie;
        }

        private async Task SyncGenresAsync(AppDbContext db, Movie movie, List<string> scrapedGenres)
        {
            // Sanitize target inputs cleanly
            var targetGenres = scrapedGenres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 1. Remove existing join entities no longer represented in the scraped payload
            var orphansToRemove = movie.MovieGenres
                .Where(mg => mg.Genre != null && !targetGenres.Contains(mg.Genre.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var orphan in orphansToRemove)
            {
                movie.MovieGenres.Remove(orphan);
                db.Remove(orphan); // Force explicit database join table deletion
            }

            // 2. Map new active incoming connections
            foreach (string genreName in targetGenres)
            {
                // Skip execution if parent entity already holds an active bridge
                if (movie.MovieGenres.Any(mg => mg.Genre != null && mg.Genre.Name.Equals(genreName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Intercept pending untracked runtime allocations stored directly inside memory buffers
                var genre = db.Genres.Local.FirstOrDefault(g => g.Name.Equals(genreName, StringComparison.OrdinalIgnoreCase));

                if (genre == null)
                {
                    genre = await db.Genres.FirstOrDefaultAsync(g => g.Name.ToLower() == genreName.ToLower());
                }

                if (genre == null)
                {
                    genre = new Genre { Name = genreName };
                    db.Genres.Add(genre);
                    // Notice: Mid-stream SaveChanges entirely stripped out
                }

                movie.MovieGenres.Add(new MovieGenre
                {
                    MovieId = movie.Id,
                    Movie = movie,
                    Genre = genre
                });
            }
        }

        private async Task SyncActressesAsync(AppDbContext db, Movie movie, List<ActorData> scrapedActors)
        {
            var targetActors = scrapedActors
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .DistinctBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 1. Flush outdated mapping relationships safely
            var orphansToRemove = movie.MovieActresses
                .Where(ma => ma.Actress != null && !targetActors.Any(ta => ta.Name.Trim().Equals(ma.Actress.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var orphan in orphansToRemove)
            {
                movie.MovieActresses.Remove(orphan);
                db.Remove(orphan);
            }

            // 2. Synchronize active state structures sequentially
            for (int i = 0; i < targetActors.Count; i++)
            {
                var actorDto = targetActors[i];
                string cleanName = actorDto.Name.Trim();

                // If join assignment already resolves perfectly, simply refresh structural layout tracking index
                var existingJoin = movie.MovieActresses.FirstOrDefault(ma => ma.Actress != null && ma.Actress.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                if (existingJoin != null)
                {
                    existingJoin.Order = i;
                    continue;
                }

                // Verify active local thread allocations
                var actress = db.Actresses.Local.FirstOrDefault(a => a.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

                if (actress == null)
                {
                    actress = await db.Actresses.FirstOrDefaultAsync(a =>
                        a.Name.ToLower() == cleanName.ToLower() ||
                        a.AltNames.Any(alt => alt != null && alt.Name.ToLower() == cleanName.ToLower()));
                }

                if (actress == null)
                {
                    actress = new Actress { Name = cleanName };
                    db.Actresses.Add(actress);
                }

                movie.MovieActresses.Add(new MovieActress
                {
                    MovieId = movie.Id,
                    Movie = movie,
                    Actress = actress,
                    Order = i
                });
            }
        }

        #endregion
    }
}