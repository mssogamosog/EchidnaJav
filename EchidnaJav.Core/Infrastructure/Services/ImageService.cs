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
    public interface IImageService
    {
        string? GetBestImage(Movie movie);
        Task<string?> GetImageAsync(string originalPath, ImageType type);
    }
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

    public class ImageService : IImageService
    {
        private static readonly SemaphoreSlim _semaphore = new(4);
        private readonly ILogger<ImageService> _logger;
        private readonly IAppPaths _appPaths;
        private const int ThumbnailHeight = 420;
        private const int CoverWidth = 300;
        private const int CoverHeight = 420;
        private const int FullMaxWidth = 800; // optional resize cap
        private readonly string _cacheFolder;
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };
        public ImageService(ILogger<ImageService> logger, IAppPaths appPaths)
        {
            _logger = logger;
            _appPaths = appPaths;
            _cacheFolder = Path.Combine(_appPaths.AppDataDirectory, "image-cache");

            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);
           
        }

        public async Task<string?> GetImageAsync(string originalPath, ImageType type)
        {
            if (!File.Exists(originalPath))
                return null;
            var cachePath = GetCachePath(originalPath, type);

            if (File.Exists(cachePath))
            {
                return await GetImageBytes(cachePath);
            }
            await _semaphore.WaitAsync();
            try
            {
                await Task.Run(async () =>
                {
                    using var image = await Image.LoadAsync(originalPath);

                    switch (type)
                    {
                        case ImageType.Thumbnail:
                            image.Mutate(x => x.Resize(0, ThumbnailHeight));
                            break;

                        case ImageType.Cover:
                            CropRightSide(image);
                            image.Mutate(x => x.Resize(CoverWidth, CoverHeight));
                            break;

                        case ImageType.Full:
                            image.Mutate(x => x.Resize(new ResizeOptions
                            {
                                Mode = ResizeMode.Max,
                                Size = new Size(FullMaxWidth, 0)
                            }));
                            break;
                    }

                    await image.SaveAsJpegAsync(cachePath);
                });
            }
            finally
            {
                _semaphore.Release();
            }
            

            return await GetImageBytes(cachePath);
        }

        private async Task<string?> GetImageBytes(string cachePath)
        {
            var bytes = await File.ReadAllBytesAsync(cachePath);
            var base64 = Convert.ToBase64String(bytes);
            var imgSource = $"data:image/jpeg;base64,{base64}";
            //_logger.LogInformation("Cached image: {Original}", imgSource);
            return imgSource;
        }

        private string GetCachePath(string originalPath, ImageType type)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(originalPath + type));
            var name = Convert.ToHexString(hash);

            return Path.Combine(_cacheFolder, $"{name}.jpg");
        }

        private void CropRightSide(Image image)
        {
            double targetRatio = (double)CoverWidth / CoverHeight; // 300 / 420 = 0.714

            int cropWidth = (int)(image.Height * targetRatio);

            // Prevent overflow
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
                .ThenBy(f => f.SizeBytes)
                .Select(f => f.FilePath)
                .FirstOrDefault();
        }
    }
}
