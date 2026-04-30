using EchidnaJav.Core.Domain.Constants;
using EchidnaJav.Core.Infrastructure.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

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
            var allFiles = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories);
            var groups = new ConcurrentDictionary<string, ConcurrentBag<string>>();
            var dirIdCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await Parallel.ForEachAsync(allFiles, (file, _) =>
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

            return groups
                .Where(g => g.Value.Any(f => MediaConstants.VideoExtensions.Contains(Path.GetExtension(f))))
                .ToDictionary(k => k.Key, v => v.Value.ToList());
        }

        public string ComputeMetadataHash(string path)
        {
            var fi = new FileInfo(path);
            return $"{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
        }
    }
}
