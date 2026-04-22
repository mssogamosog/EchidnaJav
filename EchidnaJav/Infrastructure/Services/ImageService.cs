using EchidnaJav.Domain.Entities;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;
using System.Text;
using Image = SixLabors.ImageSharp.Image;
using ResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using Size = SixLabors.ImageSharp.Size;

namespace EchidnaJav.Infrastructure.Services
{
    public interface IImageService
    {
        string? GetBestImage(Movie movie);
        Task<string?> GetImageAsync(string originalPath, ImageType type);
    }
    public enum ImageType
    {
        Full,           // original (or near-original)
        Thumbnail,      // small resized
        Cover           // cropped (DVD-style right side)
    }

    public class ImageService : IImageService
    {

        private readonly ILogger<ImportService> _logger;
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
        public ImageService(ILogger<ImportService> logger)
        {
            _logger = logger;
            _cacheFolder = Path.Combine(FileSystem.AppDataDirectory, "image-cache");

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
            const double coverRatio = 0.45;

            int cropWidth = (int)(image.Width * coverRatio);

            image.Mutate(x => x.Crop(new Rectangle(
                image.Width - cropWidth, // right side
                0,
                cropWidth,
                image.Height
            )));
        }
        public string? GetBestImage(Movie movie)
        {
            return movie.Files
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f.FileName)))
                .OrderByDescending(f => f.FileName.Contains("cover", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.FileName.Contains("poster", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.FileName.Contains("thumb", StringComparison.OrdinalIgnoreCase))
                .ThenBy(f => f.SizeBytes)
                .Select(f => f.FilePath)
                .FirstOrDefault();
        }
    }
}
