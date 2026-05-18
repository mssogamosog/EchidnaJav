using EchidnaJav.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;
using System.Text;
using Image = SixLabors.ImageSharp.Image;

namespace EchidnaJav.Core.Infrastructure.Services
{
   
    public interface IAppPaths
    {
        string AppDataDirectory { get; }
    }
    public enum ImageType
    {
        Full,           // original (or near-original)
        Thumbnail,      // small resized
        Cover           // cropped (DVD-style right side)
    }

    public interface IImageService
    {
        event Action<string>? OnImageGenerated;
        string GetCachePath(string originalPath, ImageType type);
        Task GenerateImagesAsync(string originalPath, bool forceOverwrite = false);
        string? GetBestImage(Movie movie);
        Task<string?> GetImageAsync(string originalPath, ImageType type);
        Task<string?> DownloadImageAsync( string destinationPath, string imageUrl);
        Task ImportCoverImageAsync(string movieId, string sourceFilePath);
    }

    public class ImageService : IImageService
    {
        private readonly ILogger<ImageService> _logger;
        private readonly IAppPaths _appPaths;
        public event Action<string>? OnImageGenerated;
        private const int ThumbnailHeight = 420;
        private const int CoverWidth = 300;
        private const int CoverHeight = 420;
        private HttpClient _httpClient;

        private readonly string _cacheFolder;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        private static readonly SemaphoreSlim _semaphore = new(2);

        public ImageService(ILogger<ImageService> logger, IAppPaths appPaths, HttpClient httpClient)
        {
            _logger = logger;
            _appPaths = appPaths;
            _httpClient = httpClient;

            _cacheFolder = Path.Combine(_appPaths.AppDataDirectory, "image-cache");

            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);
        }

        // ==============================
        // 🔥 IMPORT PHASE
        // ==============================

        public async Task GenerateImagesAsync(string originalPath, bool forceOverwrite = false)
        {
            if (!File.Exists(originalPath))
                return;

            await _semaphore.WaitAsync();

            try
            {
                using var image = await Image.LoadAsync(originalPath);

                await GenerateVariant(image, originalPath, ImageType.Thumbnail, forceOverwrite);
                await GenerateVariant(image, originalPath, ImageType.Cover, forceOverwrite);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task GenerateVariant(Image original, string originalPath, ImageType type, bool forceOverwrite = false)
        {
            var cachePath = GetCachePath(originalPath, type);

            if (!forceOverwrite && File.Exists(cachePath))
                return;

            using var image = original.Clone(ctx => { });

            switch (type)
            {
                case ImageType.Thumbnail:
                    image.Mutate(x => x.Resize(0, ThumbnailHeight));
                    break;

                case ImageType.Cover:
                    CropRightSide(image);
                    image.Mutate(x => x.Resize(CoverWidth, CoverHeight));
                    break;
            }

            await image.SaveAsJpegAsync(cachePath);

            OnImageGenerated?.Invoke(cachePath);
        }

        // ==============================
        // 🎯 UI PHASE
        // ==============================

        public async Task<string?> GetImageAsync(string originalPath, ImageType type)
        {
            if (originalPath == null) return null;
            if (type == ImageType.Full)
            {
                // 🔥 JUST LOAD ORIGINAL (no processing)
                return await GetImageBytes(originalPath);
            }

            var cachePath = GetCachePath(originalPath, type);

            if (!File.Exists(cachePath))
            {
                // optional fallback (should rarely happen)
                _ = GenerateImagesAsync(originalPath);
                return null;
            }

            return await GetImageBytes(cachePath);
        }

        // ==============================
        // 🧩 HELPERS
        // ==============================

        private async Task<string?> GetImageBytes(string path)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var base64 = Convert.ToBase64String(bytes);
                return $"data:image/jpeg;base64,{base64}";
            }
            catch (Exception)
            {

                return $"data:image/jpeg;base64,";
            }
            
        }

        public string GetCachePath(string originalPath, ImageType type)
        {
            if (string.IsNullOrWhiteSpace(originalPath)) return string.Empty;

            string fileName = Path.GetFileName(originalPath);
            string uniqueKey = fileName;

            if (File.Exists(originalPath))
            {
                try
                {
                    long fileSize = new FileInfo(originalPath).Length;
                    uniqueKey = $"{fileName}_{fileSize}";
                }
                catch
                {
                }
            }

            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(uniqueKey + type));
            var name = Convert.ToHexString(hash);

            return Path.Combine(_cacheFolder, $"{name}.jpg");
        }

        private void CropRightSide(Image image)
        {
            double targetRatio = (double)CoverWidth / CoverHeight;

            int cropWidth = (int)(image.Height * targetRatio);

            if (cropWidth > image.Width)
                cropWidth = image.Width;

            int x = image.Width - cropWidth;

            image.Mutate(ctx => ctx.Crop(new Rectangle(
                x,
                0,
                cropWidth,
                image.Height
            )));
        }

        public string? GetBestImage(Movie movie)
        {
            return movie.Files
                .Where(f =>
                    ImageExtensions.Contains(Path.GetExtension(f.FileName)) &&
                    !f.FileName.Contains("thumb", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.FileName.Contains("cover", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.FileName.Contains("poster", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.SizeBytes)
                .Select(f => f.FilePath)
                .FirstOrDefault();
        }

        public async Task<string?> DownloadImageAsync(string destFolder, string sourceUrl)
        {
            if (string.IsNullOrEmpty(sourceUrl) || string.IsNullOrEmpty(destFolder))
                return null;

            if (!sourceUrl.StartsWith("http"))
                sourceUrl = "http:" + sourceUrl;

            // ==========================================
            // 🔥 1. HASH THE URL FOR THE FILENAME
            // ==========================================
            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(sourceUrl));
            var hashHex = Convert.ToHexString(hashBytes);

            string extension = Path.GetExtension(sourceUrl);
            if (string.IsNullOrEmpty(extension)) extension = ".jpg"; // fallback

            string finalFilePath = Path.Combine(destFolder, $"{hashHex}{extension}");

            // ==========================================
            // 🔥 2. CHECK BEFORE DOWNLOADING
            // ==========================================
            if (File.Exists(finalFilePath))
            {
                _logger.LogInformation($"URL already downloaded. Skipping network request: {sourceUrl}");
                return finalFilePath; // Return the path so the caller can save it to the DB!
            }

            // ==========================================
            // 3. PROCEED WITH DOWNLOAD
            // ==========================================
            string tempFileName = Path.GetTempFileName();

            try
            {
                _logger.LogInformation($"Downloading image from {sourceUrl}");

                using (var response = await _httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var fs = new FileStream(tempFileName, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                }

                // Check if it's a banned "Unknown Actress" image
                if (IsBannedFile(tempFileName))
                {
                    _logger.LogWarning("Downloaded image matches a banned checksum. Discarding.");
                    File.Delete(tempFileName);
                    return null;
                }

                // Load the new image with ImageSharp to inspect its quality
                var newImageInfo = await Image.IdentifyAsync(tempFileName);
                if (newImageInfo.Width < 150 || newImageInfo.Height < 220)
                {
                    _logger.LogInformation("Downloaded image is too small. Discarding.");
                    File.Delete(tempFileName);
                    return null;
                }

                if (!Directory.Exists(destFolder))
                {
                    Directory.CreateDirectory(destFolder);
                }

                // Move temp file to final hashed path
                File.Move(tempFileName, finalFilePath);
                _logger.LogInformation($"Successfully saved new image to {finalFilePath}");

                return finalFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading image from {sourceUrl}");
                if (File.Exists(tempFileName))
                    File.Delete(tempFileName);

                return null;
            }
        }
        private bool IsBannedFile(string filename)
        {
            string checksum = GetSHA1Checksum(filename);

            // "Unknown actress" image from JavDatabase
            if (checksum == "69-BB-2B-57-50-7E-18-0F-91-DB-2A-03-06-79-39-AA-75-EB-05-F3")
                return true;

            // "Unknown actress" image from JavRave.club
            if (checksum == "EA-C4-BE-81-0E-EB-0A-56-C4-91-AF-BA-3E-41-FA-F6-06-64-F6-F2")
                return true;

            return false;
        }

        public string GetSHA1Checksum(string filename)
        {
            if (!File.Exists(filename)) return string.Empty;

            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
            using var sha1 = SHA1.Create();

            byte[] hashBytes = sha1.ComputeHash(fs);

            // Format to match your legacy checksum format (AA-BB-CC...)
            return BitConverter.ToString(hashBytes);
        }

        public Task ImportCoverImageAsync(string movieId, string sourceFilePath)
        {
            throw new NotImplementedException();
        }
    }
}
