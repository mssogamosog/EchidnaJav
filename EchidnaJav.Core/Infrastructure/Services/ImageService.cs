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
        Task GenerateImagesAsync(string originalPath);
        string? GetBestImage(Movie movie);
        Task<string?> GetImageAsync(string originalPath, ImageType type);
        Task<bool> DownloadImageAsync( string destinationPath, string imageUrl);
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

        public async Task GenerateImagesAsync(string originalPath)
        {
            if (!File.Exists(originalPath))
                return;

            await _semaphore.WaitAsync();

            try
            {
                using var image = await Image.LoadAsync(originalPath);

                await GenerateVariant(image, originalPath, ImageType.Thumbnail);
                await GenerateVariant(image, originalPath, ImageType.Cover);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task GenerateVariant(Image original, string originalPath, ImageType type)
        {
            var cachePath = GetCachePath(originalPath, type);

            if (File.Exists(cachePath))
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
            var bytes = await File.ReadAllBytesAsync(path);
            var base64 = Convert.ToBase64String(bytes);
            return $"data:image/jpeg;base64,{base64}";
        }

        public string GetCachePath(string originalPath, ImageType type)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(originalPath + type));
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

        public async Task<bool> DownloadImageAsync(string targetFilePath, string sourceUrl)
        {
            if (string.IsNullOrEmpty(sourceUrl) || string.IsNullOrEmpty(targetFilePath))
                return false;

            // Ensure proper URL formatting
            if (!sourceUrl.StartsWith("http"))
                sourceUrl = "http:" + sourceUrl;

            // Ensure the target file has the correct extension based on the source URL
            string finalFilePath = Path.ChangeExtension(targetFilePath, Path.GetExtension(sourceUrl));
            string tempFileName = Path.GetTempFileName();

            try
            {
                _logger.LogInformation($"Downloading image from {sourceUrl}");

                // 1. Download the file to a temporary location using HttpClient
                using (var response = await _httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var fs = new FileStream(tempFileName, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                }

                // 2. Check if it's a banned "Unknown Actress" image
                if (IsBannedFile(tempFileName))
                {
                    _logger.LogWarning("Downloaded image matches a banned checksum. Discarding.");
                    File.Delete(tempFileName);
                    return false;
                }

                // 3. Load the new image with ImageSharp to inspect its quality
                var newImageInfo = await Image.IdentifyAsync(tempFileName);

                if (newImageInfo.Width < 150 || newImageInfo.Height < 220)
                {
                    _logger.LogInformation("Downloaded image is too small. Discarding.");
                    File.Delete(tempFileName);
                    return false;
                }
                string? destFolder = Path.GetDirectoryName(finalFilePath);
                if (!string.IsNullOrEmpty(destFolder) && !Directory.Exists(destFolder))
                {
                    Directory.CreateDirectory(destFolder);
                }
                // 4. If the file already exists, compare resolutions
                if (File.Exists(finalFilePath))
                {
                    var currentInfo = await Image.IdentifyAsync(finalFilePath);

                    double currentPixels = currentInfo.Width * currentInfo.Height;
                    double newPixels = newImageInfo.Width * newImageInfo.Height;

                    if (newPixels > currentPixels)
                    {
                        _logger.LogInformation($"New image ({newImageInfo.Width}x{newImageInfo.Height}) is larger. Replacing.");
                        File.Delete(finalFilePath);
                        File.Move(tempFileName, finalFilePath);
                        return true;
                    }
                    else
                    {
                        _logger.LogInformation("Existing image is larger or equal. Keeping existing.");
                        File.Delete(tempFileName);
                        return false;
                    }
                }

                // 5. If no existing file, just save the new one
                File.Move(tempFileName, finalFilePath);
                _logger.LogInformation($"Successfully saved new image to {finalFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading image from {sourceUrl}");
                if (File.Exists(tempFileName))
                    File.Delete(tempFileName);

                return false;
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
    }
}
