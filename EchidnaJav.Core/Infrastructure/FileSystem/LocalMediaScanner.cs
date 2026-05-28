using EchidnaJav.Core.Domain.Constants;
using EchidnaJav.Core.Infrastructure.Services;
using System.Collections.Concurrent;

namespace EchidnaJav.Core.Infrastructure.FileSystem
{
    public interface ILocalMediaScanner
    {
        Task<Dictionary<string, List<string>>> GroupFilesByMovieAsync(string rootPath);
        string ComputeMetadataHash(string path);
    }

    public class LocalMediaScanner : ILocalMediaScanner
    {
        private readonly IMovieIdService _movieIdService;

        public LocalMediaScanner(IMovieIdService movieIdService)
        {
            _movieIdService = movieIdService;
        }

        public async Task<Dictionary<string, List<string>>> GroupFilesByMovieAsync(string rootPath)
        {

            return await Task.Run(async () =>
            {

                var allFiles = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories);

                var groups = new ConcurrentDictionary<string, ConcurrentBag<string>>();
                var dirIdCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // 🔥 3. CLAMP THE CPU
                // Limit regex string parsing to 3 concurrent threads. 
                // This leaves the rest of your CPU cores entirely free to render the Blazor UI smoothly.
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2)
                };

                await Parallel.ForEachAsync(allFiles, parallelOptions, (file, _) =>
                {
                    // 1. Check the FILE itself first (The most specific identifier)
                    string id = _movieIdService.ParseMovieID(file);

                    // 2. Fallback: If the file name is generic, check the parent directory
                    if (string.IsNullOrEmpty(id))
                    {
                        string directory = Path.GetDirectoryName(file);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            id = dirIdCache.GetOrAdd(directory, dir =>
                            {
                                // Check exactly ONE level up
                                var parsedId = _movieIdService.ParseMovieID(dir);
                                return parsedId ?? string.Empty;
                            });
                        }
                    }

                    // 3. Group the file if we found a valid ID
                    if (string.IsNullOrWhiteSpace(id))
                        return ValueTask.CompletedTask;

                    var bag = groups.GetOrAdd(id, _ => new ConcurrentBag<string>());
                    bag.Add(file);

                    return ValueTask.CompletedTask;
                });

                // 4. Final aggregation
                return groups
                    .Where(g => g.Value.Any(f => MediaConstants.VideoExtensions.Contains(Path.GetExtension(f))))
                    .ToDictionary(k => k.Key, v => v.Value.ToList());
            });
        }
        public string ComputeMetadataHash(string path)
        {
            var fi = new FileInfo(path);
            return $"{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
        }
    }
}
