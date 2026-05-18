using EchidnaJav.Scraper.Services;
#if WINDOWS
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
#endif

namespace EchidnaJav
{
    public partial class MainPage : ContentPage
    {
        public MainPage(ISilentWebViewSandbox sandboxEngine)
        {
            InitializeComponent();
            sandboxEngine.AnchorToVisualTree(RootHostGrid);
#if WINDOWS
            // Wait for the Blazor WebView to load, then hijack its Windows handler
            blazorWebView.HandlerChanged += OnBlazorWebViewHandlerChanged;
#endif
        }

#if WINDOWS
        private void OnBlazorWebViewHandlerChanged(object? sender, EventArgs e)
        {
            if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView)
            {
                // Force the native window to accept file drops
                webView.AllowDrop = true;
                webView.DragOver += WebView_DragOver;
                webView.Drop += WebView_Drop;
            }
        }

        // 🔥 FIX: Fully qualified 'Microsoft.UI.Xaml.DragEventArgs'
        private void WebView_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            // 🔥 FIX: Fully qualified 'Windows.ApplicationModel.DataTransfer.DataPackageOperation'
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.Handled = true;
        }

        // 🔥 FIX: Fully qualified 'Microsoft.UI.Xaml.DragEventArgs'
        private async void WebView_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is StorageFile storageFile)
                {
                    // We successfully grabbed the physical path from Windows!
                    string filePath = storageFile.Path;

                    // 🔥 FIX: Fully qualified the Helper to ensure the compiler finds it
                    EchidnaJav.Core.Infrastructure.Services.NativeFileDragDropHelper.TriggerFileDrop(filePath);
                }
            }
        }
#endif
    }
}
