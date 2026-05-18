using EchidnaJav.Core.Domain.Constants;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface IFileUtilityService
    {
        List<string> DeleteDuplicateFiles(string folder, List<string> fileNames);
        void OpenDirectory(string? folderPath);
        Task<bool> MoveFilesToFolderAsync(string movieId, string destinationFolder);

        // NEW METHODS
        Task OpenDirectoryAndSelectVideoAsync(string movieId);
        void ShowInExplorer(string filePath);
    }

    public class FileUtilityService : IFileUtilityService
    {
        private readonly ILogger<FileUtilityService> _logger;
        private readonly IMovieRepositoryService _movieRepository; 

        public FileUtilityService(ILogger<FileUtilityService> logger, IMovieRepositoryService movieRepository)
        {
            _logger = logger;
            _movieRepository = movieRepository;
        }

        // --- NEW: Finds the video file and opens it ---
        public async Task OpenDirectoryAndSelectVideoAsync(string movieId)
        {
            var fullDetails = await _movieRepository.GetMovieDetailsAsync(movieId);
            var filePaths = fullDetails?.Files.Select(f => f.FilePath).ToList();
            if (filePaths == null || !filePaths.Any()) return;

            // 1. Try to find the first file that matches your video extensions
            var targetFile = filePaths.FirstOrDefault(f =>
                !string.IsNullOrWhiteSpace(f) &&
                MediaConstants.VideoExtensions.Contains(Path.GetExtension(f)));

            // 2. If no video file was found, fallback to the very first file in the list
            if (targetFile == null)
            {
                targetFile = filePaths.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));
            }

            // 3. Reveal it in the OS
            if (targetFile != null)
            {
                ShowInExplorer(targetFile);
            }
        }

        // --- NEW: Native OS logic to highlight a specific file ---
        public void ShowInExplorer(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) OpenDirectory(dir);
                return;
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", $"-R \"{filePath}\"");
                }
                else
                {
                    var dir = Path.GetDirectoryName(filePath);
                    Process.Start("xdg-open", $"\"{dir}\"");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reveal file in OS explorer: {FilePath}", filePath);
            }
        }


        public List<string> DeleteDuplicateFiles(string folder, List<string> fileNames)
        {
            var fullPathFilenames = new List<string>();

            foreach (var fileName in fileNames)
            {
                fullPathFilenames.Add(Path.Combine(folder, fileName));
            }

            var deduplicatedPaths = DeleteDuplicateFiles(fullPathFilenames);

            return deduplicatedPaths;
        }

        public void OpenDirectory(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                _logger.LogWarning("Cannot open directory. Path does not exist: {FolderPath}", folderPath);
                return;
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("explorer.exe", $"\"{folderPath}\"");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", $"\"{folderPath}\"");
                }
                else
                {
                    Process.Start("xdg-open", $"\"{folderPath}\"");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open folder explorer at: {FolderPath}", folderPath);
            }
        }

        public async Task<bool> MoveFilesToFolderAsync(string movieId, string destinationFolder)
        {
            var fullDetails = await _movieRepository.GetMovieDetailsAsync(movieId);
            var filePaths = fullDetails?.Files.Select(f => f.FilePath).ToList();
            if (filePaths == null || string.IsNullOrWhiteSpace(destinationFolder)) return false;

            try
            {
                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                foreach (var sourcePath in filePaths)
                {
                    if (!File.Exists(sourcePath)) continue;

                    var fileName = Path.GetFileName(sourcePath);
                    var destPath = Path.Combine(destinationFolder, fileName);

                    if (File.Exists(destPath))
                    {
                        _logger.LogWarning("File already exists at destination, skipping to prevent overwrite: {DestPath}", destPath);
                        continue;
                    }

                    // Offload file I/O to a background thread
                    await Task.Run(() => File.Move(sourcePath, destPath));
                    _logger.LogInformation("Moved file from {Source} to {Destination}", sourcePath, destPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while moving files to: {Destination}", destinationFolder);
                return false;
            }
        }

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
                        _logger.LogInformation("Deleting duplicate file " + fileName);
                        File.Delete(fileName);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unable to delete duplicate file " + fileName);
                    }
                }

                if (hash != string.Empty && !hashFilenamePairs.ContainsKey(hash))
                {
                    hashFilenamePairs.Add(hash, fileName);
                }

                result.Add(fileName);
            }

            return result;
        }

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