using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Helpers;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace EchidnaJav.Core.Infrastructure.Persistence
{
    public interface IActressRepositoryService
    {
        Task SaveScrapedActressAsync(ActressData scrapedData);
        Task<ActressDetailsDto?> GetActressDetailsAsync(string name);
        Task SetDefaultActressImageAsync(string actressName, string filePath);
        Task<ActressData?> GetActressDataForScraperAsync(string name);
        Task<List<string>> SearchActressNamesAsync(string query);
        Task<List<ActressDetailsDto>> GetAllActressesAsync(string? searchText, SortActressesBy sortBy);
        Task MergeActressesAsync(string targetName, List<string> sourceNames);
    }

    public class ActressRepositoryService : IActressRepositoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly INfoGeneratorService _nfoGenerator;

        public ActressRepositoryService(
            IDbContextFactory<AppDbContext> dbFactory,
            INfoGeneratorService nfoGenerator)
        {
            _dbFactory = dbFactory;
            _nfoGenerator = nfoGenerator;
        }

        public async Task SaveScrapedActressAsync(ActressData scrapedData)
        {
            if (string.IsNullOrWhiteSpace(scrapedData.Name)) return;

            using var db = await _dbFactory.CreateDbContextAsync();

            // 1. Check if the actress already exists (Include collections so we can merge them)
            var actress = await db.Actresses
                .Include(a => a.AltNames)
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a => a.Name == scrapedData.Name);

            bool isNew = false;

            if (actress == null)
            {
                actress = new Actress
                {
                    Name = scrapedData.Name,
                    AltNames = new List<ActressAltName>(),
                    Images = new List<ActressImage>()
                };
                isNew = true;
            }

            // 2. Map Scalar Properties (Only overwrite if the scraper actually found something)
            if (!string.IsNullOrEmpty(scrapedData.JapaneseName)) actress.JapaneseName = scrapedData.JapaneseName;
            if (scrapedData.DobYear > 0) actress.DobYear = scrapedData.DobYear;
            if (scrapedData.DobMonth > 0) actress.DobMonth = scrapedData.DobMonth;
            if (scrapedData.DobDay > 0) actress.DobDay = scrapedData.DobDay;
            if (scrapedData.Height > 0) actress.Height = scrapedData.Height;
            if (scrapedData.Bust > 0) actress.Bust = scrapedData.Bust;
            if (scrapedData.Waist > 0) actress.Waist = scrapedData.Waist;
            if (scrapedData.Hips > 0) actress.Hips = scrapedData.Hips;
            if (!string.IsNullOrEmpty(scrapedData.Cup)) actress.Cup = scrapedData.Cup;
            if (!string.IsNullOrEmpty(scrapedData.BloodType)) actress.BloodType = scrapedData.BloodType;

            // 3. Merge Alternate Names (Avoid duplicates)
            if (scrapedData.AltNames != null)
            {
                actress.AltNames ??= new List<ActressAltName>(); // Safety check

                foreach (var altName in scrapedData.AltNames)
                {
                    if (!string.IsNullOrWhiteSpace(altName) &&
                        !actress.AltNames.Any(a => a.Name.Equals(altName, StringComparison.OrdinalIgnoreCase)))
                    {
                        actress.AltNames.Add(new ActressAltName { Name = altName });
                    }
                }
            }

            // 4. Merge Images (Avoid duplicates, maintain index)
            if (scrapedData.ImageFileNames != null)
            {
                actress.Images ??= new List<ActressImage>(); // Safety check

                // Get the highest index currently in the DB so new images append to the end
                int nextIndex = actress.Images.Any() ? actress.Images.Max(i => i.Index) + 1 : 0;

                foreach (var fileName in scrapedData.ImageFileNames)
                {
                    if (!string.IsNullOrWhiteSpace(fileName) &&
                        !actress.Images.Any(i => i.Filepath.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        actress.Images.Add(new ActressImage
                        {
                            Filepath = fileName,
                            Index = nextIndex
                        });
                        nextIndex++;
                    }
                }
            }

            // 5. Save to SQLite
            if (isNew)
            {
                db.Actresses.Add(actress);
            }
            // If it's not new, EF Core's Change Tracker automatically knows what fields to UPDATE

            await db.SaveChangesAsync();
        }
        public async Task<ActressDetailsDto?> GetActressDetailsAsync(string name)
        {
            // Safety check
            if (string.IsNullOrWhiteSpace(name)) return null;

            using var db = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch the data with all relationships included
            var entity = await db.Actresses
                .AsNoTracking() // 🔥 Crucial for UI reads: Makes the query significantly faster!
                .Include(a => a.Images)
                .Include(a => a.MovieActresses)
                    .ThenInclude(ma => ma.Movie) // Traverses the join table to get the actual Movies
                .FirstOrDefaultAsync(a => a.Name != null && a.Name.ToLower() == name.ToLower());

            // 2. If she isn't in the database, return null so the UI can show a loading/not found state
            if (entity == null) return null;

            // 3. Map the Entity to your DTO
            var dto = new ActressDetailsDto
            {
                Name = entity.Name ?? "Unknown",
                JapaneseName = entity.JapaneseName,
                DobYear = entity.DobYear,
                DobMonth = entity.DobMonth,
                DobDay = entity.DobDay,
                Height = entity.Height,
                Cup = entity.Cup,
                Bust = entity.Bust,
                Waist = entity.Waist,
                Hips = entity.Hips,
                BloodType = entity.BloodType,

                // Map the images and sort them by their Index
                Images = entity.Images != null
                    ? entity.Images
                        .OrderBy(i => i.Index)
                        .Select(i => new ActressImageDto
                        {
                            Filepath = i.Filepath,
                            Index = i.Index
                        }).ToList()
                    : new List<ActressImageDto>(),

                // Map the movies she appears in
                Movies = entity.MovieActresses != null
                    ? entity.MovieActresses
                        .Where(ma => ma.Movie != null) // Safety check for bad data
                        .Select(ma => new MovieDto
                        {
                            Id = ma.Movie.Id,
                            Title = ma.Movie.Title,
                            ImagePath = ma.Movie.PrimaryImagePath
                            // Map any other properties your MovieDto needs here!
                        }).ToList()
                    : new List<MovieDto>()
            };

            return dto;
        }
        public async Task<ActressData?> GetActressDataForScraperAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            using var db = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch the actress with Images and AltNames (but NOT movies!)
            var entity = await db.Actresses
                .AsNoTracking()
                .Include(a => a.Images)
                .Include(a => a.AltNames) // <-- Added this!
                .FirstOrDefaultAsync(a => a.Name != null && a.Name.ToLower() == name.ToLower());

            if (entity == null) return null;

            // 2. Map directly to the Scraper Data object
            var scraperData = new ActressData
            {
                Name = entity.Name ?? string.Empty,
                JapaneseName = entity.JapaneseName ?? string.Empty,
                DobYear = entity.DobYear ?? 0,
                DobMonth = entity.DobMonth ?? 0,
                DobDay = entity.DobDay ?? 0,
                Height = entity.Height ?? 0,
                Cup = entity.Cup ?? string.Empty,
                Bust = entity.Bust ?? 0,
                Waist = entity.Waist ?? 0,
                Hips = entity.Hips ?? 0,
                BloodType = entity.BloodType ?? string.Empty,

                // Flatten the AltNames into a simple list of strings
                AltNames = entity.AltNames != null
                    ? entity.AltNames.Select(an => an.Name).ToList()
                    : new List<string>(),

                // Flatten the images into the string list the scraper uses
                ImageFileNames = entity.Images != null
                    ? entity.Images.OrderBy(i => i.Index).Select(i => i.Filepath).ToList()
                    : new List<string>()
            };

            return scraperData;
        }
        public async Task SetDefaultActressImageAsync(string actressName, string filePath)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var actress = await db.Actresses
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a => a.Name != null && a.Name.ToLower() == actressName.ToLower());

            
            if (actress == null || actress.Images == null || !actress.Images.Any()) return;

         
            var selected = actress.Images.FirstOrDefault(i => i.Filepath == filePath);


            if (selected == null || selected.Index == 0) return;

 
            var sortedImages = actress.Images.OrderBy(i => i.Index).ToList();

            sortedImages.Remove(selected);
            sortedImages.Insert(0, selected);

            for (int i = 0; i < sortedImages.Count; i++)
            {
                sortedImages[i].Index = i;
            }

            await db.SaveChangesAsync();
        }
        public async Task<List<string>> SearchActressNamesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<string>();
            }

            using var db = await _dbFactory.CreateDbContextAsync();

            string lowerQuery = query.ToLower();

            var suggestions = await db.Actresses
                .AsNoTracking() 
                .Where(a => a.Name != null && a.Name.ToLower().Contains(lowerQuery))
                .OrderBy(a => a.Name)
                .Select(a => a.Name!)
                .Take(10) 
                .ToListAsync();

            return suggestions;
        }
        public async Task<List<ActressDetailsDto>> GetAllActressesAsync(string? searchText, SortActressesBy sortBy)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var query = db.Actresses
                .AsNoTracking()
                .Where(a => a.Name != null)
                .AsQueryable();
            // 1. Apply Multi-Token Search
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var tokens = searchText
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => $"%{t.Trim()}%")
                    .ToArray();

                query = query.Where(a =>
                    tokens.Any(pattern =>
                        EF.Functions.Like(a.Name!, pattern) ||
                        (a.JapaneseName != null &&
                         EF.Functions.Like(a.JapaneseName, pattern))
                    ));
            }
            
            int currentMonth = DateTime.Today.Month;
            int currentDay = DateTime.Today.Day;
            // 2. Apply Sorting (pushing empty/zero values to the bottom)
            query = sortBy switch
            {
                SortActressesBy.MovieCount => query.OrderByDescending(a => a.MovieActresses!.Count),

                // Age Youngest: Valid years get '1' (sorted to top), nulls/0s get '0' (sorted to bottom)
                SortActressesBy.AgeYoungest => query
                    .OrderByDescending(a => a.DobYear != null && a.DobYear > 0 ? 1 : 0)
                    .ThenByDescending(a => a.DobYear)
                    .ThenByDescending(a => a.DobMonth)
                    .ThenByDescending(a => a.DobDay),

                // Age Oldest: Nulls/0s get '1' (sorted to bottom), valid years get '0' (sorted to top)
                SortActressesBy.AgeOldest => query
                    .OrderBy(a => a.DobYear == null || a.DobYear == 0 ? 1 : 0)
                    .ThenBy(a => a.DobYear)
                    .ThenBy(a => a.DobMonth)
                    .ThenBy(a => a.DobDay),

                // Same logic applied to Height
                SortActressesBy.HeightTallest => query
                    .OrderByDescending(a => a.Height != null && a.Height > 0 ? 1 : 0)
                    .ThenByDescending(a => a.Height),

                SortActressesBy.HeightShortest => query
                    .OrderBy(a => a.Height == null || a.Height == 0 ? 1 : 0)
                    .ThenBy(a => a.Height),

                // Same logic applied to Cup Size
                SortActressesBy.CupSmallest => query
                    .OrderBy(a => a.Cup == null || a.Cup == "" ? 1 : 0)
                    .ThenBy(a => a.Cup),

                SortActressesBy.CupBiggest => query
                    .OrderByDescending(a => a.Cup != null && a.Cup != "" ? 1 : 0)
                    .ThenByDescending(a => a.Cup),

                // Same logic applied to Birthdays
                SortActressesBy.Birthday => query
                    .OrderBy(a =>
                        // Weight 2: Unknown birthdays pushed to the absolute bottom
                        a.DobMonth == null || a.DobMonth == 0 ? 2 :

                        // Weight 0: Upcoming birthdays (Month is greater, OR same month but day is >= today)
                        (a.DobMonth > currentMonth || (a.DobMonth == currentMonth && a.DobDay >= currentDay)) ? 0 :

                        // Weight 1: Birthdays that already passed this year (wrapped around to next year)
                        1
                    )
                    .ThenBy(a => a.DobMonth) // Sort normally within their assigned weight group
                    .ThenBy(a => a.DobDay),

                _ => query.OrderBy(a => a.Name)
            };

            // 3. Project directly into DTO for fast performance
            var projectedQuery = query.Select(a => new ActressDetailsDto
            {
                Name = a.Name ?? "Unknown",
                JapaneseName = a.JapaneseName,
                DobYear = a.DobYear,
                DobMonth = a.DobMonth,
                DobDay = a.DobDay,
                Height = a.Height,
                Cup = a.Cup,
                MovieCount = a.MovieActresses != null ? a.MovieActresses.Count : 0,
                Images = a.Images != null
                    ? a.Images.OrderBy(i => i.Index)
                              .Select(i => new ActressImageDto { Filepath = i.Filepath, Index = i.Index })
                              .Take(1).ToList()
                    : new List<ActressImageDto>()
            });

            return await projectedQuery.ToListAsync();
        }
        public async Task MergeActressesAsync(string targetName, List<string> sourceNames)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch the target with all relationships
            var target = await db.Actresses
                .Include(a => a.Images)
                .Include(a => a.AltNames)
                .Include(a => a.MovieActresses)
                .FirstOrDefaultAsync(a => a.Name == targetName);

            if (target == null) return;

            // 2. Fetch all sources
            var sources = await db.Actresses
                .Include(a => a.Images)
                .Include(a => a.AltNames)
                .Include(a => a.MovieActresses)
                .Where(a => sourceNames.Contains(a.Name))
                .ToListAsync();

            target.AltNames ??= new List<ActressAltName>();
            target.Images ??= new List<ActressImage>();
            target.MovieActresses ??= new List<MovieActress>();

            int nextImageIndex = target.Images.Any() ? target.Images.Max(i => i.Index) + 1 : 0;

            var affectedMovieIds = new HashSet<string>();

            foreach (var source in sources)
            {
                // 3. Keep the old name as an AltName
                if (!target.AltNames.Any(an => an.Name == source.Name) && source.Name != target.Name)
                    target.AltNames.Add(new ActressAltName { Name = source.Name });

                if (source.AltNames != null)
                {
                    foreach (var alt in source.AltNames)
                    {
                        if (!target.AltNames.Any(an => an.Name == alt.Name) && alt.Name != target.Name)
                            target.AltNames.Add(new ActressAltName { Name = alt.Name });
                    }
                }

                // 4. Move Images over
                if (source.Images != null)
                {
                    foreach (var img in source.Images)
                    {
                        if (!target.Images.Any(ti => ti.Filepath == img.Filepath))
                            target.Images.Add(new ActressImage { Filepath = img.Filepath, Index = nextImageIndex++ });
                    }
                }

                // 5. Reassign Movies
                if (source.MovieActresses != null)
                {
                    foreach (var ma in source.MovieActresses.ToList())
                    {
                        affectedMovieIds.Add(ma.MovieId);
                        bool existsInTarget = target.MovieActresses.Any(tma => tma.MovieId == ma.MovieId);

                        if (!existsInTarget)
                        {
                            var newJoin = new MovieActress
                            {
                                MovieId = ma.MovieId,
                                Actress = target,
                                Order = ma.Order
                            };
                            target.MovieActresses.Add(newJoin);
                        }

                        db.MovieActresses.Remove(ma);
                    }
                }

                // 6. Fill in missing metadata gracefully
                target.JapaneseName = string.IsNullOrWhiteSpace(target.JapaneseName) ? source.JapaneseName : target.JapaneseName;
                target.DobYear = (target.DobYear == null || target.DobYear == 0) ? source.DobYear : target.DobYear;
                target.DobMonth = (target.DobMonth == null || target.DobMonth == 0) ? source.DobMonth : target.DobMonth;
                target.DobDay = (target.DobDay == null || target.DobDay == 0) ? source.DobDay : target.DobDay;
                target.Height = (target.Height == null || target.Height == 0) ? source.Height : target.Height;
                target.Cup = string.IsNullOrWhiteSpace(target.Cup) ? source.Cup : target.Cup;
                target.Bust = (target.Bust == null || target.Bust == 0) ? source.Bust : target.Bust;
                target.Waist = (target.Waist == null || target.Waist == 0) ? source.Waist : target.Waist;
                target.Hips = (target.Hips == null || target.Hips == 0) ? source.Hips : target.Hips;

                // 7. Remove the merged entity
                db.Actresses.Remove(source);
            }

            await db.SaveChangesAsync();

            if (affectedMovieIds.Any())
            {
                // Need to grab the files to figure out where the NFO goes
                var affectedMovies = await db.Movies
                    .Include(m => m.Files)
                    .Where(m => affectedMovieIds.Contains(m.Id))
                    .ToListAsync();

                foreach (var movie in affectedMovies)
                {
                    var targetDir = GetTargetDirectory(movie);
                    if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
                    {
                        await _nfoGenerator.GenerateNfoAsync(movie.Id, targetDir);
                    }
                }
            }
        }
        private string? GetTargetDirectory(Movie movie)
        {
            var firstValidFile = movie.Files?.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.FilePath) && File.Exists(f.FilePath));
            if (firstValidFile != null) return Path.GetDirectoryName(firstValidFile.FilePath);

            if (!string.IsNullOrWhiteSpace(movie.PrimaryImagePath) && File.Exists(movie.PrimaryImagePath))
                return Path.GetDirectoryName(movie.PrimaryImagePath);

            return null;
        }
    }
}