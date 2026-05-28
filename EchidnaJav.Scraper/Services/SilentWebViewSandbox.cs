using System.Text.RegularExpressions;

namespace EchidnaJav.Scraper.Services
{
    public interface ISilentWebViewSandbox
    {
        void AnchorToVisualTree(Layout parentContainer);
        Task<string> ExecuteSilentExtractionAsync(string targetUrl);
    }

    public class SilentWebViewSandbox : ISilentWebViewSandbox
    {
        private WebView? _sandboxEngine;
        private readonly SemaphoreSlim _concurrencyLock = new(1, 1);

        public SilentWebViewSandbox()
        {
            // Initialize platform web engine components safely on the MainThread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _sandboxEngine = new WebView
                {
                    // Crucial: Give it nominal dimensions so layout engines evaluate scripts natively
                    WidthRequest = 100,
                    HeightRequest = 100
                };
            });
        }

        /// <summary>
        /// Must be called once during app initialization to anchor the engine into the live display tree.
        /// </summary>
        public void AnchorToVisualTree(Layout parentContainer)
        {
            if (_sandboxEngine != null && !parentContainer.Children.Contains(_sandboxEngine))
            {
                // Mount to the layout but entirely mask its visual rendering footprint
                _sandboxEngine.IsVisible = false;
                parentContainer.Children.Add(_sandboxEngine);
            }
        }

        public async Task<string> ExecuteSilentExtractionAsync(string targetUrl)
        {
            // Guarantee isolated single-file execution through the shared browser engine
            await _concurrencyLock.WaitAsync();

            try
            {
                var tcs = new TaskCompletionSource<string>();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_sandboxEngine == null)
                    {
                        tcs.TrySetException(new InvalidOperationException("Sandbox engine uninitialized."));
                        return;
                    }

                    async void OnNavigated(object? sender, WebNavigatedEventArgs e)
                    {
                        try
                        {
                            // Detach immediately to prevent multiple execution callbacks
                            _sandboxEngine.Navigated -= OnNavigated;

                            // Extract the fully evaluated DOM structure
                            string? rawHtml = await _sandboxEngine.EvaluateJavaScriptAsync("document.documentElement.outerHTML;");
                            string cleanHtml = Regex.Unescape(rawHtml ?? string.Empty).Trim('"');

                            tcs.TrySetResult(cleanHtml);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }

                    _sandboxEngine.Navigated += OnNavigated;
                    _sandboxEngine.Source = new UrlWebViewSource { Url = targetUrl };
                });

                // Implement structural execution boundaries (Safety cutoff)
                var extractionTask = tcs.Task;
                if (await Task.WhenAny(extractionTask, Task.Delay(TimeSpan.FromSeconds(20))) == extractionTask)
                {
                    return await extractionTask;
                }

                // If timeout occurs, force cancellation and reset view state on the MainThread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_sandboxEngine != null) _sandboxEngine.Source = new HtmlWebViewSource { Html = "" };
                });

                throw new TimeoutException($"Silent Sandbox traversal timed out for {targetUrl}");
            }
            finally
            {
                _concurrencyLock.Release();
            }
        }
    }
}
