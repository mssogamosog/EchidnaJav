namespace EchidnaJav.Core.Infrastructure.Interfaces
{
    public interface INativeDialogService
    {
        Task<string?> PickFolderAsync(string title = "Select Folder");
        Task<string?> PickFileAsync(string title = "Select File", string filter = "Image Files|*.jpg;*.jpeg;*.png");
    }
}
