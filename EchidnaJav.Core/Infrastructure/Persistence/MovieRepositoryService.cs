using EchidnaJav.Core.Domain.Constants;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Persistence;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
public interface IMovieRepositoryService
{
    Task<MovieDetailsDto?> GetMovieDetailsAsync(string id);
    Task<List<MovieDto>> GetMoviesAsync(MovieQueryParameters queryParams);
    Task<int> GetTotalMovieCountAsync(MovieQueryParameters queryParams);
    Task<List<string>> GetAllMovieIdsAsync();
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

        // 3. Video Extension Filter (Single source of truth)
        query = query.Where(m => m.Files.Any(f =>
            MediaConstants.VideoExtensions.Any(ext => EF.Functions.Like(f.FileName, "%" + ext))));

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

        // Use the refactored filter logic
        var query = BuildFilteredQuery(db, queryParams);

        // Apply Sorting
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

        // Project and Paginate
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
        var query = BuildFilteredQuery(db, queryParams);

        return await query.CountAsync();
    }
    public async Task<List<string>> GetAllMovieIdsAsync()
    {
        using var db = _dbFactory.CreateDbContext();

        return await db.Movies
            .Select(m => m.Id)
            .ToListAsync();
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
                Cast = m.MovieActresses.Select(ma => new MovieActorDto
                {
                    Name = ma.Actress.Name,
                    ImagePath = ma.Actress.Images.OrderBy(i => i.Index).FirstOrDefault().Filepath
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
            movie.Files = movie.Files
                .Where(f => MediaConstants.VideoExtensions.Contains(Path.GetExtension(f.FileName)))
                .ToList();
        }

        return movie;
    }
}