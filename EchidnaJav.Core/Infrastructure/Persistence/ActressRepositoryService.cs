using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
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
    }

    public class ActressRepositoryService : IActressRepositoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public ActressRepositoryService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
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
    }
}