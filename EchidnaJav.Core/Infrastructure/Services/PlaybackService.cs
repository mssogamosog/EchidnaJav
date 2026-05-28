using EchidnaJav.Core.Domain.DTOs;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface IPlaybackService
    {
        void PlayFile(string? filePath);
        Task PlayAllAsync(string movieId, IEnumerable<FileDto>? files);
    }

    public class PlaybackService : IPlaybackService
    {
        private readonly ILogger<PlaybackService> _logger;

        public PlaybackService(ILogger<PlaybackService> logger)
        {
            _logger = logger;
        }

        public void PlayFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open media file: {FilePath}", filePath);
            }
        }

        public async Task PlayAllAsync(string movieId, IEnumerable<FileDto>? files)
        {
            if (files == null || !files.Any()) return;

            var fileList = files.ToList();

            // 1. Single file fallback (bypasses playlist completely)
            if (fileList.Count == 1)
            {
                PlayFile(fileList[0].FilePath);
                return;
            }

            try
            {
                var safeId = string.Join("_", movieId.Split(Path.GetInvalidFileNameChars()));
                var tempFolder = Path.GetTempPath();
                var playlistPath = Path.Combine(tempFolder, $"echidna_playlist_{safeId}.m3u");

                var lines = new List<string> { "#EXTM3U" };

                foreach (var file in fileList)
                {
                    if (File.Exists(file.FilePath))
                    {
                        lines.Add($"#EXTINF:-1,{file.FileName}");
                        lines.Add(file.FilePath);
                    }
                }

                await File.WriteAllLinesAsync(playlistPath, lines, System.Text.Encoding.UTF8);

                Process.Start(new ProcessStartInfo
                {
                    FileName = playlistPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create or open playlist for movie: {MovieId}", movieId);
                PlayFile(fileList.FirstOrDefault()?.FilePath);
            }
        }
    }
}