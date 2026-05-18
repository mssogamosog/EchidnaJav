using EchidnaJav.Core.Domain.Constants;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Interfaces;
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
        Task<bool> MoveMovieToFolderAsync(string movieId, string destinationFolder);
        Task UpdateMoviePathsAsync(string movieId, List<string> newFilePaths);
        Task RescanMovieDirectoryAsync(string movieId);
        Task<bool> RegenerateMetadataAsync(string movieId);
        Task<bool> RegenerateMetadataFromUrlsAsync(string movieId, List<ManualUrlScrapeRequest> requests);
        Task<bool> DeleteMovieAsync(string movieId);
    }

    public class MovieRepositoryService : IMovieRepositoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IImageService _imageService;
        private readonly IMovieScrapeService _scrapeService; 
        private readonly INfoGeneratorService _nfoGenerator;
        private readonly IActressScrapeQueue _actressQueue;
        public MovieRepositoryService(IDbContextFactory<AppDbContext> dbFactory, IImageService imageService, IMovieScrapeService scrapeService, INfoGeneratorService nfoGenerator, IActressScrapeQueue actressQueue)
        {
            _dbFactory = dbFactory;
            _imageService = imageService;
            _scrapeService = scrapeService;
            _nfoGenerator = nfoGenerator;
            _actressQueue = actressQueue;
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

                // --- STEP A: Fetch or Create the Actress Entity ---
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

                    await _actressQueue.QueueActressAsync(cleanName);
                }
                else
                {
                    if (db.Entry(actress).State != EntityState.Added)
                    {
                        bool hasImages = await db.Entry(actress)
                                                 .Collection(a => a.Images)
                                                 .Query()
                                                 .AnyAsync();

                        if (!hasImages)
                        {
                            await _actressQueue.QueueActressAsync(cleanName);
                        }
                    }
                }

                var existingJoin = movie.MovieActresses.FirstOrDefault(ma => ma.Actress != null && ma.Actress.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

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
        #region File & Metadata Operations
        public async Task RescanMovieDirectoryAsync(string movieId)
        {
            using var db = _dbFactory.CreateDbContext();

            var movie = await db.Movies
                .Include(m => m.Files)
                .FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null)
            {
                return;
            }

            string? directoryPath = null;

            var firstValidFile = movie.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.FilePath) && File.Exists(f.FilePath));
            if (firstValidFile != null)
            {
                directoryPath = Path.GetDirectoryName(firstValidFile.FilePath);
            }
            else if (!string.IsNullOrWhiteSpace(movie.PrimaryImagePath) && File.Exists(movie.PrimaryImagePath))
            {
                directoryPath = Path.GetDirectoryName(movie.PrimaryImagePath);
            }

            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            var allowedExtensions = new HashSet<string>(MediaConstants.VideoExtensions, StringComparer.OrdinalIgnoreCase)
            {
                ".nfo", ".jpg", ".jpeg", ".png", ".webp"
            };

            var enumOptions = new EnumerationOptions { IgnoreInaccessible = true };
            var allPhysicalFiles = Directory.EnumerateFiles(directoryPath, "*.*", enumOptions)
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            string folderName = new DirectoryInfo(directoryPath).Name;
            string normalizedId = movie.NormalizedId ?? movieId.Replace("-", "");

            bool isDedicatedFolder = folderName.Contains(movieId, StringComparison.OrdinalIgnoreCase) ||
                                     folderName.Contains(normalizedId, StringComparison.OrdinalIgnoreCase);

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
                    if (fileName.Contains(movieId, StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains(normalizedId, StringComparison.OrdinalIgnoreCase))
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

            if (hasChanges)
            {
                await db.SaveChangesAsync();
            }
        }
        public async Task<bool> MoveMovieToFolderAsync(string movieId, string destinationFolder)
        {
            if (string.IsNullOrWhiteSpace(movieId) || string.IsNullOrWhiteSpace(destinationFolder))
                return false;

            using var db = _dbFactory.CreateDbContext();

            var movie = await db.Movies
                .Include(m => m.Files)
                .FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null || !movie.Files.Any())
            {
                return false;
            }

            if (!Directory.Exists(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            bool hasErrors = false;
            if (!string.IsNullOrWhiteSpace(movie.PrimaryImagePath))
            {
                var sourceImagePath = movie.PrimaryImagePath;

                if (File.Exists(sourceImagePath))
                {
                    var imageFileName = Path.GetFileName(sourceImagePath);
                    var destImagePath = Path.Combine(destinationFolder, imageFileName);

                    if (!File.Exists(destImagePath))
                    {
                        try
                        {
                            await Task.Run(() => File.Move(sourceImagePath, destImagePath));

                            movie.PrimaryImagePath = destImagePath;
                        }
                        catch (Exception ex)
                        {
                            hasErrors = true;
                        }
                    }
                    else
                    {
                        movie.PrimaryImagePath = destImagePath;
                    }
                }
                else
                {
                    // Optional: you could set movie.PrimaryImagePath = null here if you want to clear dead links.
                }
            }
            foreach (var fileEntry in movie.Files)
            {
                var sourcePath = fileEntry.FilePath;

                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    continue;
                }

                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(destinationFolder, fileName);

                if (File.Exists(destPath))
                {
                    fileEntry.FilePath = destPath; 
                    continue;
                }

                try
                {
                    await Task.Run(() => File.Move(sourcePath, destPath));
                    fileEntry.FilePath = destPath;
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                }
            }

            await db.SaveChangesAsync();

            return !hasErrors;
        }

        public async Task UpdateMoviePathsAsync(string movieId, List<string> newFilePaths)
        {
            // This is useful if an external process moved the files and you just need to update the DB
            using var db = _dbFactory.CreateDbContext();
            var movie = await db.Movies.Include(m => m.Files).FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null) return;

            // Simple wipe-and-replace strategy for file paths
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

        public async Task<bool> RegenerateMetadataAsync(string movieId)
        {
            try
            {
                using var db = _dbFactory.CreateDbContext();

                // 1. Fetch the movie to find out where it lives on the hard drive
                var movie = await db.Movies
                    .Include(m => m.Files)
                    .FirstOrDefaultAsync(m => m.Id == movieId);

                if (movie == null) return false;

                // 2. Determine the physical target directory
                var firstValidFile = movie.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.FilePath) && File.Exists(f.FilePath));
                string? targetDirectory = firstValidFile != null ? Path.GetDirectoryName(firstValidFile.FilePath) : null;

                // Fallback to cover image path if video files are missing
                if (string.IsNullOrWhiteSpace(targetDirectory) && !string.IsNullOrWhiteSpace(movie.PrimaryImagePath) && File.Exists(movie.PrimaryImagePath))
                {
                    targetDirectory = Path.GetDirectoryName(movie.PrimaryImagePath);
                }

                if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
                {
                    // We can't regenerate metadata if we don't know where to save the cover/.nfo!
                    return false;
                }

                string targetCoverPath = Path.Combine(targetDirectory, $"{movie.Id}-cover.jpg");

                var scrapedDto = await _scrapeService.ScrapeMovieAsync(movie.Id, targetCoverPath, LanguageType.English);

                if (scrapedDto == null) return false;

                var existingFiles = movie.Files
                    .Where(f => !string.IsNullOrWhiteSpace(f.FilePath))
                    .Select(f => f.FilePath!)
                    .ToList();

                await UpsertScrapedMovieAsync(scrapedDto, targetCoverPath, existingFiles);

                if (File.Exists(targetCoverPath))
                {
                    await _imageService.GenerateImagesAsync(targetCoverPath, forceOverwrite: true);
                }

                await _nfoGenerator.GenerateNfoAsync(movie.Id, targetDirectory);
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

            // Determine target directory
            var firstValidFile = movie.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.FilePath) && File.Exists(f.FilePath));
            string? targetDirectory = firstValidFile != null ? Path.GetDirectoryName(firstValidFile.FilePath) : null;
            if (string.IsNullOrWhiteSpace(targetDirectory)) targetDirectory = Path.GetDirectoryName(movie.PrimaryImagePath);
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory)) return false;

            string targetCoverPath = Path.Combine(targetDirectory, $"{movie.Id}-cover.jpg");

            // 🔥 Call the new Direct URL Scrape method
            var scrapedDto = await _scrapeService.ScrapeMovieFromMultipleUrlsAsync(movie.Id, requests, targetCoverPath, LanguageType.English);

            if (scrapedDto == null) return false;

            var existingFiles = movie.Files.Where(f => !string.IsNullOrWhiteSpace(f.FilePath)).Select(f => f.FilePath!).ToList();

            // Save to DB
            await UpsertScrapedMovieAsync(scrapedDto, targetCoverPath, existingFiles);

            // Refresh thumbnail cache
            if (File.Exists(targetCoverPath))
            {
                await _imageService.GenerateImagesAsync(targetCoverPath, forceOverwrite: true);
            }

            // Regenerate .nfo
            await _nfoGenerator.GenerateNfoAsync(movie.Id, targetDirectory);

            return true;
        }

        public async Task<bool> DeleteMovieAsync(string movieId)
        {
            using var db = _dbFactory.CreateDbContext();

            // Fetch the movie
            var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == movieId);

            if (movie == null) return false;
            db.Movies.Remove(movie);
            await db.SaveChangesAsync();

            return true;
        }
        #endregion

        #endregion
    }
}