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

            // 1. Fetch existing movie graph including links for synchronization
            var movie = await db.Movies
                .Include(m => m.MovieGenres)
                .Include(m => m.MovieActresses)
                .Include(m => m.Files)
                .FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null)
            {
                movie = new Movie { Id = movieId };
                db.Movies.Add(movie);
            }

            // 2. Map primitive fields safely
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
            foreach (string filePath in mediaFiles)
            {
                if (!movie.Files.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
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
                            Hash = string.Empty // Can be populated later by a background hashing queue
                        });
                    }
                }
            }

            // 3. Synchronize Relationships
            await SyncGenresAsync(db, movie, scrapedData.Genres);
            await SyncActressesAsync(db, movie, scrapedData.Actors);

            // 4. Commit transaction
            await db.SaveChangesAsync();

            return movie;
        }

        private async Task SyncGenresAsync(AppDbContext db, Movie movie, List<string> scrapedGenres)
        {
            movie.MovieGenres.Clear();

            foreach (string genreName in scrapedGenres.Distinct())
            {
                string cleanName = genreName.Trim();
                if (string.IsNullOrEmpty(cleanName)) continue;

                var genre = await db.Genres
                    .FirstOrDefaultAsync(g => g.Name.ToLower() == cleanName.ToLower());

                if (genre == null)
                {
                    genre = new Genre { Name = cleanName };
                    db.Genres.Add(genre);
                    await db.SaveChangesAsync();
                }

                movie.MovieGenres.Add(new MovieGenre
                {
                    MovieId = movie.Id,
                    GenreId = genre.Id
                });
            }
        }

        private async Task SyncActressesAsync(AppDbContext db, Movie movie, List<ActorData> scrapedActors)
        {
            movie.MovieActresses.Clear();

            for (int i = 0; i < scrapedActors.Count; i++)
            {
                var actorDto = scrapedActors[i];
                string cleanName = actorDto.Name.Trim();
                if (string.IsNullOrEmpty(cleanName)) continue;

                var actress = await db.Actresses
                    .FirstOrDefaultAsync(a => a.Name.ToLower() == cleanName.ToLower() ||
                                              a.AltNames.Any(alt => alt.Name.ToLower() == cleanName.ToLower()));

                if (actress == null)
                {
                    actress = new Actress { Name = cleanName };
                    db.Actresses.Add(actress);
                    await db.SaveChangesAsync();
                }

                movie.MovieActresses.Add(new MovieActress
                {
                    MovieId = movie.Id,
                    ActressId = actress.Id,
                    Order = i
                });
            }
        }

        #endregion
    }
}