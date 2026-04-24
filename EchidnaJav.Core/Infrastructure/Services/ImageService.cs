using EchidnaJav.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;
using System.Text;
using Image = SixLabors.ImageSharp.Image;
using ResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using Size = SixLabors.ImageSharp.Size;

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
    }

    public class ImageService : IImageService
    {
        private readonly ILogger<ImageService> _logger;
        private readonly IAppPaths _appPaths;
        public event Action<string>? OnImageGenerated;
        private const int ThumbnailHeight = 420;
        private const int CoverWidth = 300;
        private const int CoverHeight = 420;

        private readonly string _cacheFolder;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

        private static readonly SemaphoreSlim _semaphore = new(2);

        public ImageService(ILogger<ImageService> logger, IAppPaths appPaths)
        {
            _logger = logger;
            _appPaths = appPaths;

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
    }
}
