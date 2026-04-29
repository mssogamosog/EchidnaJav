using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface IFileUtilityService
    {
        List<string> DeleteDuplicateFiles(string folder, List<string> fileNames);
    }

    public class FileUtilityService : IFileUtilityService
    {
        private readonly ILogger<FileUtilityService> _logger;

        // 1. Inject the modern logger
        public FileUtilityService(ILogger<FileUtilityService> logger)
        {
            _logger = logger;
        }

        // 2. The Public entry point (The one your ScrapeService actually calls)
        public List<string> DeleteDuplicateFiles(string folder, List<string> fileNames)
        {
            var fullPathFilenames = new List<string>();

            foreach (var fileName in fileNames)
            {
                // Path.Combine is smart enough to just return the fileName if it is ALREADY a full path,
                // so this safely handles both old and new data!
                fullPathFilenames.Add(Path.Combine(folder, fileName));
            }

            // Call your private implementation to do the hashing and deleting
            var deduplicatedPaths = DeleteDuplicateFiles(fullPathFilenames);

            // 🔥 FIX: Return the full absolute paths directly! No more Path.GetFileName()
            return deduplicatedPaths;
        }

        // 3. Your core logic, updated for DI
        private List<string> DeleteDuplicateFiles(List<string> fileNames)
        {
            if (fileNames.Count < 2)
                return fileNames;

            List<string> result = new List<string>();
            Dictionary<string, string> hashFilenamePairs = new Dictionary<string, string>();

            foreach (string fileName in fileNames)
            {
                if (File.Exists(fileName) == false)
                    continue;

                string hash = GetSHA1Checksum(fileName);

                if (hash != string.Empty && hashFilenamePairs.ContainsKey(hash))
                {
                    try
                    {
                        // 🔥 Updated to use injected _logger
                        _logger.LogInformation("Deleting duplicate file " + fileName);
                        File.Delete(fileName);
                        continue; // Skip adding to the result list since it was deleted
                    }
                    catch (Exception ex)
                    {
                        // 🔥 Updated to use injected _logger
                        _logger.LogError(ex, "Unable to delete duplicate file " + fileName);
                    }
                }

                // Only add to the dictionary if we successfully generated a hash
                if (hash != string.Empty && !hashFilenamePairs.ContainsKey(hash))
                {
                    hashFilenamePairs.Add(hash, fileName);
                }

                result.Add(fileName);
            }

            return result;
        }

        // 4. Local helper to generate the SHA1 hash
        private string GetSHA1Checksum(string filename)
        {
            if (!File.Exists(filename)) return string.Empty;

            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
            using var sha1 = SHA1.Create();

            byte[] hashBytes = sha1.ComputeHash(fs);
            return BitConverter.ToString(hashBytes);
        }
    }
}