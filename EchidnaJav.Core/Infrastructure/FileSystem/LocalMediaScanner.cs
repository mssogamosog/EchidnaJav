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

            await Parallel.ForEachAsync(allFiles, (file, _) =>
            {
                var id = _movieIdService.ParseMovieID(file);

                if (string.IsNullOrEmpty(id))
                    return ValueTask.CompletedTask;

                var bag = groups.GetOrAdd(id, _ => new ConcurrentBag<string>());
                bag.Add(file);

                return ValueTask.CompletedTask;
            });

            return groups.ToDictionary(k => k.Key, v => v.Value.ToList());
        }

        public string ComputeMetadataHash(string path)
        {
            var fi = new FileInfo(path);
            return $"{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
        }
    }
}
