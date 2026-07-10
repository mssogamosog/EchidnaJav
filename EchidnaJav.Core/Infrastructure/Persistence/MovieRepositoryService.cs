using EchidnaJav.Core.Domain.Constants;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;

namespace EchidnaJav.Core.Infrastructure.Persistence
{
    public interface IMovieRepositoryService
    {
        Task<MovieDetailsDto?> GetMovieDetailsAsync(string id);
        Task<List<MovieCardDto>> GetMoviesAsync(MovieQueryParameters queryParams);
        Task<int> GetTotalMovieCountAsync(MovieQueryParameters queryParams);
        Task<List<string>> GetAllMovieIdsAsync();
        Task<Movie> UpsertScrapedMovieAsync(MovieMetadata scrapedDto, string targetCoverPath, IReadOnlyList<string> files);
        Task<bool> MoveMovieToFolderAsync(string movieId, string destinationFolder);
        Task UpdateMoviePathsAsync(string movieId, List<string> newFilePaths);
        Task RescanMovieDirectoryAsync(string movieId);
        Task<bool> RegenerateMetadataAsync(string movieId);
        Task<bool> RegenerateMetadataFromUrlsAsync(string movieId, List<ManualUrlScrapeRequest> requests);
        Task<bool> DeleteMovieAsync(string movieId);
        Task<List<string>> SearchGenreNamesAsync(string query);
        Task<bool> UpdateMovieDetailsAsync(MovieDetailsDto dto);
        Task<bool?> ToggleMovieFavoriteAsync(string movieId);
        Task<bool?> ToggleMovieWatchLaterAsync(string movieId);
    }

    public class MovieRepositoryService : IMovieRepositoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IImageService _imageService;
        private readonly IMovieScrapeService _scrapeService;
        private readonly INfoGeneratorService _nfoGenerator;
        private readonly IActressScrapeQueue _actressQueue;
        private readonly IMovieIdService _movieIdService;
        private readonly ILogger<MovieRepositoryService> _logger;

        public MovieRepositoryService(
            IDbContextFactory<AppDbContext> dbFactory,
            IImageService imageService,
            IMovieScrapeService scrapeService,
            INfoGeneratorService nfoGenerator,
            IActressScrapeQueue actressQueue,
            IMovieIdService movieIdService,
            ILogger<MovieRepositoryService> logger)
        {
            _dbFactory = dbFactory;
            _imageService = imageService;
            _scrapeService = scrapeService;
            _nfoGenerator = nfoGenerator;
            _actressQueue = actressQueue;
            _movieIdService = movieIdService;
            _logger = logger;
        }

        #region Read Layer (Queries & Pagination)

        private IQueryable<Movie> BuildFilteredQuery(AppDbContext db, MovieQueryParameters queryParams)
        {
            var query = db.Movies.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(queryParams.SearchActress))
            {
                query = query.Where(m => m.MovieActresses.Any(a => a.Actress.Name == queryParams.SearchActress));
            }

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

            //query = query.Where(m => m.Files.Any(f =>
            //    MediaConstants.VideoExtensions.Any(ext => f.FileName.EndsWith(ext))));

            if (queryParams.MissingImageOnly)
            {
                query = query.Where(m => string.IsNullOrEmpty(m.PrimaryImagePath));
            }

            if (queryParams.FavoritesOnly)
            {
                query = query.Where(m => m.IsFavorite == true);
            }

            if (queryParams.WatchLaterOnly)
            {
                query = query.Where(m => m.IsWatchLater == true);
            }

            return query;
        }

        public async Task<List<MovieCardDto>> GetMoviesAsync(MovieQueryParameters queryParams)
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
                .Select(m => new MovieCardDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    ImagePath = m.PrimaryImagePath,
                    IsFavorite = m.IsFavorite,
                    IsWatchLater = m.IsWatchLater
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
                    }).ToList(),
                    IsFavorite = m.IsFavorite,
                    IsWatched = m.IsWatched,
                    IsWatchLater = m.IsWatchLater
                })
                .FirstOrDefaultAsync();

            if (movie != null)
            {
                movie.Files = movie.Files
                    .Where(f => MediaConstants.VideoExtensions.Any(ext => f.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            return movie;
        }

        public async Task<List<string>> SearchGenreNamesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<string>();

            using var db = _dbFactory.CreateDbContext();
            string lowerQuery = query.ToLower();

            return await db.Genres
                .AsNoTracking()
                .Where(g => g.Name != null && g.Name.ToLower().Contains(lowerQuery))
                .OrderBy(g => g.Name)
                .Select(g => g.Name!)
                .Take(10)
                .ToListAsync();
        }

        #endregion

        #region Write Layer (Scraper Persistence)

        public async Task<Movie> UpsertScrapedMovieAsync(MovieMetadata scrapedData, string primaryImagePath, IReadOnlyList<string> mediaFiles)
        {
            using var db = _dbFactory.CreateDbContext();
            string movieId = scrapedData.UniqueID.Value.ToUpper();

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

            movie.NormalizedId = movieId.Replace("-", "").Replace(" ", "");
            movie.Title = scrapedData.Title;
            movie.OriginalTitle = scrapedData.OriginalTitle;
            movie.Director = scrapedData.Director;
            movie.Studio = scrapedData.Studio;
            movie.Label = scrapedData.Label;
            movie.Series = scrapedData.Series;
            movie.Plot = scrapedData.Plot;

            if (scrapedData.Runtime > 0) movie.Runtime = scrapedData.Runtime;

            movie.PrimaryImagePath = primaryImagePath;
            movie.DateAdded ??= DateTime.UtcNow;

            if (DateTime.TryParse(scrapedData.Premiered, out DateTime premieredDate))
            {
                movie.Premiered = premieredDate;
                movie.Year = premieredDate.Year;
            }

            var primaryRating = scrapedData.Ratings.FirstOrDefault();
            if (primaryRating != null) movie.Rating = primaryRating.Value;

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

            await SyncGenresAsync(db, movie, scrapedData.Genres);
            await SyncActressesAsync(db, movie, scrapedData.Actors);

            await db.SaveChangesAsync();
            return movie;
        }

        private async Task SyncGenresAsync(AppDbContext db, Movie movie, List<string> scrapedGenres)
        {
            var targetGenres = scrapedGenres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var orphansToRemove = movie.MovieGenres
                .Where(mg => mg.Genre != null && !targetGenres.Contains(mg.Genre.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var orphan in orphansToRemove)
            {
                movie.MovieGenres.Remove(orphan);
                db.Remove(orphan);
            }

            foreach (string genreName in targetGenres)
            {
                if (movie.MovieGenres.Any(mg => mg.Genre != null && mg.Genre.Name.Equals(genreName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var genre = db.Genres.Local.FirstOrDefault(g => g.Name.Equals(genreName, StringComparison.OrdinalIgnoreCase))
                         ?? await db.Genres.FirstOrDefaultAsync(g => g.Name.ToLower() == genreName.ToLower());

                if (genre == null)
                {
                    genre = new Genre { Name = genreName };
                    db.Genres.Add(genre);
                }

                movie.MovieGenres.Add(new MovieGenre { MovieId = movie.Id, Movie = movie, Genre = genre });
            }
        }

        private async Task SyncActressesAsync(AppDbContext db, Movie movie, List<ActorData> scrapedActors)
        {
            var targetActors = scrapedActors
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .DistinctBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var orphansToRemove = movie.MovieActresses
                .Where(ma => ma.Actress != null && !targetActors.Any(ta => ta.Name.Trim().Equals(ma.Actress.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var orphan in orphansToRemove)
            {
                movie.MovieActresses.Remove(orphan);
                db.Remove(orphan);
            }

            for (int i = 0; i < targetActors.Count; i++)
            {
                var actorDto = targetActors[i];
                string cleanName = actorDto.Name.Trim();

                var actress = db.Actresses.Local.FirstOrDefault(a => a.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                           ?? await db.Actresses.FirstOrDefaultAsync(a =>
                                  a.Name.ToLower() == cleanName.ToLower() ||
                                  a.AltNames.Any(alt => alt != null && alt.Name.ToLower() == cleanName.ToLower()));

                if (actress == null)
                {
                    actress = new Actress { Name = cleanName };
                    db.Actresses.Add(actress);
                    await _actressQueue.QueueActressAsync(cleanName);
                }
                else if (db.Entry(actress).State != EntityState.Added)
                {
                    bool hasImages = await db.Entry(actress).Collection(a => a.Images).Query().AnyAsync();
                    if (!hasImages) await _actressQueue.QueueActressAsync(cleanName);
                }

                var existingJoin = movie.MovieActresses.FirstOrDefault(ma => ma.Actress == actress || (actress.Id != 0 && ma.ActressId == actress.Id));

                if (existingJoin != null)
                {
                    existingJoin.Order = i;
                }
                else
                {
                    movie.MovieActresses.Add(new MovieActress
                    {
                        MovieId = movie.Id,
                        Movie = movie,
                        Actress = actress,
                        Order = i
                    });
                }
            }
        }

        #endregion

        #region File & Metadata Operations

        private string? GetTargetDirectory(Movie movie)
        {
            var firstValidFile = movie.Files?.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.FilePath) && File.Exists(f.FilePath));
            if (firstValidFile != null) return Path.GetDirectoryName(firstValidFile.FilePath);

            if (!string.IsNullOrWhiteSpace(movie.PrimaryImagePath) && File.Exists(movie.PrimaryImagePath))
                return Path.GetDirectoryName(movie.PrimaryImagePath);

            return null;
        }

        private async Task FinalizeMetadataUpdateAsync(Movie movie, MovieMetadata scrapedDto, string targetCoverPath, string targetDirectory)
        {
            var existingFiles = movie.Files.Where(f => !string.IsNullOrWhiteSpace(f.FilePath)).Select(f => f.FilePath!).ToList();

            await UpsertScrapedMovieAsync(scrapedDto, targetCoverPath, existingFiles);

            if (File.Exists(targetCoverPath))
            {
                await _imageService.GenerateImagesAsync(targetCoverPath, forceOverwrite: true);
            }

            await _nfoGenerator.GenerateNfoAsync(movie.Id, targetDirectory);
        }
        public async Task<bool> UpdateMovieDetailsAsync(MovieDetailsDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Id)) return false;

            using var db = _dbFactory.CreateDbContext();

            var movie = await db.Movies
                .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
                .Include(m => m.MovieActresses).ThenInclude(ma => ma.Actress)
                .Include(m => m.Files)
                .FirstOrDefaultAsync(m => m.Id == dto.Id);

            if (movie == null) return false;

            // 1. Map Scalar Properties
            movie.Title = dto.Title ?? string.Empty;
            movie.Runtime = dto.Runtime;
            movie.Studio = dto.Studio;
            movie.Director = dto.Director;
            movie.Plot = dto.Plot;
            movie.IsFavorite = dto.IsFavorite;
            movie.IsWatched = dto.IsWatched;
            movie.IsWatchLater = dto.IsWatchLater;

            if (dto.Premiered.HasValue)
            {
                movie.Premiered = dto.Premiered.Value;
                movie.Year = dto.Premiered.Value.Year;
            }
            else
            {
                movie.Premiered = null;
                movie.Year = 0;
            }

            // 2. Sync Relationships
            await SyncGenresAsync(db, movie, dto.Genres);

            var mappedActors = dto.Cast
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => new ActorData { Name = c.Name.Trim() })
                .ToList();

            await SyncActressesAsync(db, movie, mappedActors);

            // 3. Determine the target directory for files
            var targetDirectory = GetTargetDirectory(movie);

            // 🔥 NEW: 4. Handle Image Update atomically BEFORE SaveChanges
            if (dto.NewCoverImageBytes != null && !string.IsNullOrWhiteSpace(dto.NewCoverImageExtension) && !string.IsNullOrWhiteSpace(targetDirectory))
            {
                string targetCoverPath = Path.Combine(targetDirectory, $"{movie.Id}-cover{dto.NewCoverImageExtension}");

                // Write the physical file
                await File.WriteAllBytesAsync(targetCoverPath, dto.NewCoverImageBytes);

                // Tell EF Core to update the database path
                movie.PrimaryImagePath = targetCoverPath;

                // Generate cache thumbnails
                await _imageService.GenerateImagesAsync(targetCoverPath, forceOverwrite: true);
            }

            // 5. Atomic Save (Updates text fields, relationships, and image path all at once!)
            await db.SaveChangesAsync();

            // 6. Update local NFO file
            if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
            {
                await _nfoGenerator.GenerateNfoAsync(movie.Id, targetDirectory);
            }

            return true;
        }

        public async Task RescanMovieDirectoryAsync(string movieId)
        {
            using var db = _dbFactory.CreateDbContext();

            var movie = await db.Movies.Include(m => m.Files).FirstOrDefaultAsync(m => m.Id == movieId);
            if (movie == null) return;

            string? directoryPath = GetTargetDirectory(movie);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath)) return;

            var allowedExtensions = new HashSet<string>(MediaConstants.VideoExtensions, StringComparer.OrdinalIgnoreCase)
            {
                ".nfo", ".jpg", ".jpeg", ".png", ".webp"
            };

            var enumOptions = new EnumerationOptions { IgnoreInaccessible = true };
            var allPhysicalFiles = Directory.EnumerateFiles(directoryPath, "*.*", enumOptions)
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            string folderName = new DirectoryInfo(directoryPath).Name;

            bool isDedicatedFolder = _movieIdService.MovieIDEquals(movie.Id, folderName);

            var targetMovieFiles = new List<string>();
            foreach (var file in allPhysicalFiles)
            {
                if (isDedicatedFolder)
                {
                    targetMovieFiles.Add(file);
                }
                else
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (_movieIdService.MovieIDEquals(movie.Id, fileName))
                    {
                        targetMovieFiles.Add(file);
                    }
                }
            }

            var physicalFilePathsSet = new HashSet<string>(targetMovieFiles, StringComparer.OrdinalIgnoreCase);
            bool hasChanges = false;

            var filesToRemove = movie.Files.Where(f => !physicalFilePathsSet.Contains(f.FilePath)).ToList();
            foreach (var file in filesToRemove)
            {
                movie.Files.Remove(file);
                db.Files.Remove(file);
                hasChanges = true;
            }

            var existingDbFiles = movie.Files.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);

            foreach (var physicalPath in targetMovieFiles)
            {
                var fileInfo = new FileInfo(physicalPath);

                if (existingDbFiles.TryGetValue(physicalPath, out var existingFile))
                {
                    if (existingFile.SizeBytes != fileInfo.Length)
                    {
                        existingFile.SizeBytes = fileInfo.Length;
                        existingFile.LastModified = fileInfo.LastWriteTimeUtc;
                        hasChanges = true;
                    }
                }
                else
                {
                    movie.Files.Add(new FileEntry
                    {
                        MovieId = movie.Id,
                        FileName = fileInfo.Name,
                        FilePath = fileInfo.FullName,
                        SizeBytes = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        IsScanned = true,
                        Hash = string.Empty
                    });
                    hasChanges = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(movie.PrimaryImagePath) && !File.Exists(movie.PrimaryImagePath))
            {
                movie.PrimaryImagePath = null;
                hasChanges = true;
            }

            var bestImage = _imageService.GetBestImage(movie);
            if (movie.PrimaryImagePath != bestImage)
            {
                movie.PrimaryImagePath = bestImage;
                hasChanges = true;
            }

            if (hasChanges) await db.SaveChangesAsync();
        }

        public async Task<bool> RegenerateMetadataAsync(string movieId)
        {
            try
            {
                using var db = _dbFactory.CreateDbContext();
                var movie = await db.Movies.Include(m => m.Files).FirstOrDefaultAsync(m => m.Id == movieId);
                if (movie == null) return false;

                string? targetDirectory = GetTargetDirectory(movie);
                if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory)) return false;

                string targetCoverPath = Path.Combine(targetDirectory, $"{movie.Id}-cover.jpg");
                var scrapedDto = await _scrapeService.ScrapeMovieAsync(movie.Id, targetCoverPath, LanguageType.English);

                if (scrapedDto == null) return false;

                await FinalizeMetadataUpdateAsync(movie, scrapedDto, targetCoverPath, targetDirectory);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> RegenerateMetadataFromUrlsAsync(string movieId, List<ManualUrlScrapeRequest> requests)
        {
            using var db = _dbFactory.CreateDbContext();
            var movie = await db.Movies.Include(m => m.Files).FirstOrDefaultAsync(m => m.Id == movieId);
            if (movie == null) return false;

            string? targetDirectory = GetTargetDirectory(movie);
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory)) return false;

            string targetCoverPath = Path.Combine(targetDirectory, $"{movie.Id}-cover.jpg");
            var scrapedDto = await _scrapeService.ScrapeMovieFromMultipleUrlsAsync(movie.Id, requests, targetCoverPath, LanguageType.English);

            if (scrapedDto == null) return false;

            await FinalizeMetadataUpdateAsync(movie, scrapedDto, targetCoverPath, targetDirectory);
            return true;
        }

        public async Task<bool> MoveMovieToFolderAsync(string movieId, string destinationFolder)
        {
            if (string.IsNullOrWhiteSpace(movieId) || string.IsNullOrWhiteSpace(destinationFolder)) return false;

            using var db = _dbFactory.CreateDbContext();
            var movie = await db.Movies.Include(m => m.Files).FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null || !movie.Files.Any()) return false;
            if (!Directory.Exists(destinationFolder)) Directory.CreateDirectory(destinationFolder);

            bool hasErrors = false;

            if (!string.IsNullOrWhiteSpace(movie.PrimaryImagePath) && File.Exists(movie.PrimaryImagePath))
            {
                var imageFileName = Path.GetFileName(movie.PrimaryImagePath);
                var destImagePath = Path.Combine(destinationFolder, imageFileName);

                if (!File.Exists(destImagePath))
                {
                    try
                    {
                        await Task.Run(() => File.Move(movie.PrimaryImagePath, destImagePath));
                        movie.PrimaryImagePath = destImagePath;
                    }
                    catch { hasErrors = true; }
                }
                else
                {
                    movie.PrimaryImagePath = destImagePath;
                }
            }

            foreach (var fileEntry in movie.Files)
            {
                if (string.IsNullOrWhiteSpace(fileEntry.FilePath) || !File.Exists(fileEntry.FilePath)) continue;

                var fileName = Path.GetFileName(fileEntry.FilePath);
                var destPath = Path.Combine(destinationFolder, fileName);

                if (File.Exists(destPath))
                {
                    fileEntry.FilePath = destPath;
                    continue;
                }

                try
                {
                    await Task.Run(() => File.Move(fileEntry.FilePath, destPath));
                    fileEntry.FilePath = destPath;
                }
                catch { hasErrors = true; }
            }

            await db.SaveChangesAsync();
            return !hasErrors;
        }
        public async Task<bool?> ToggleMovieFavoriteAsync(string movieId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var movie = await db.Movies.FindAsync(movieId);
            if (movie != null)
            {
                movie.IsFavorite = !(movie.IsFavorite ?? false);

                await db.SaveChangesAsync();
                return movie.IsFavorite;
            }

            return false;
        }
        
        public async Task<bool?> ToggleMovieWatchLaterAsync(string movieId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var movie = await db.Movies.FindAsync(movieId);
            if (movie != null)
            {
                movie.IsWatchLater = !(movie.IsWatchLater ?? false);

                await db.SaveChangesAsync();
                return movie.IsWatchLater;
            }

            return false;
        }
        public async Task UpdateMoviePathsAsync(string movieId, List<string> newFilePaths)
        {
            using var db = _dbFactory.CreateDbContext();
            var movie = await db.Movies.Include(m => m.Files).FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null) return;

            db.Files.RemoveRange(movie.Files);

            foreach (var path in newFilePaths)
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists)
                {
                    movie.Files.Add(new FileEntry
                    {
                        MovieId = movie.Id,
                        FileName = fileInfo.Name,
                        FilePath = fileInfo.FullName,
                        SizeBytes = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        IsScanned = true,
                        Hash = string.Empty
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        public async Task<bool> DeleteMovieAsync(string movieId)
        {
            using var db = _dbFactory.CreateDbContext();
            var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null) return false;
            db.Movies.Remove(movie);
            await db.SaveChangesAsync();

            return true;
        }

        #endregion
    }
}