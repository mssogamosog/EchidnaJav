using CommunityToolkit.Maui.Storage;
using EchidnaJav.Core.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace EchidnaJav.Services
{
    public class MauiNativeDialogService : INativeDialogService
    {
        private readonly ILogger<MauiNativeDialogService> _logger;

        public MauiNativeDialogService(ILogger<MauiNativeDialogService> logger)
        {
            _logger = logger;
        }

        public async Task<string?> PickFolderAsync(string title = "Select Folder")
        {
            try
            {
                var result = await FolderPicker.Default.PickAsync(default);
                if (result.IsSuccessful)
                {
                    return result.Folder?.Path;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open MAUI folder picker.");
                return null;
            }
        }

        public async Task<string?> PickFileAsync(string title = "Select File", string filter = "Image Files|*.jpg;*.jpeg;*.png")
        {
            try
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.image" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png" } },
                    { DevicePlatform.macOS, new[] { "jpg", "jpeg", "png" } }
                });

                var options = new PickOptions
                {
                    PickerTitle = title,
                    FileTypes = customFileType
                };

                var result = await FilePicker.Default.PickAsync(options);
                return result?.FullPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open MAUI file picker.");
                return null;
            }
        }
    }
}
