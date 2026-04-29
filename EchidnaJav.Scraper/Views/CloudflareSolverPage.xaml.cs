using Microsoft.Maui.Controls;

namespace EchidnaJav.Scraper.Views;

public partial class CloudflareSolverPage : ContentPage
{
    private TaskCompletionSource<bool> _tcs = new();
    public Task<bool> SolutionTask => _tcs.Task;

    public CloudflareSolverPage(string targetUrl)
    {
        InitializeComponent();
        ChallengeWebView.Source = new UrlWebViewSource { Url = targetUrl };
    }

    private async void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
    {
        // Every time the page navigates, check if Cloudflare is gone
        string html = await ChallengeWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML;");

        if (string.IsNullOrEmpty(html)) return;

        bool isSolved = !html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) &&
                        !html.Contains("cf-turnstile", StringComparison.OrdinalIgnoreCase);

        if (isSolved)
        {
            _tcs.TrySetResult(true);
        }
    }

    // A helper method for the scraper to grab the goods before the page closes
    public async Task<(string UserAgent, string Cookies)> ExtractBrowserDataAsync()
    {
        var rawUserAgent = await ChallengeWebView.EvaluateJavaScriptAsync("navigator.userAgent;");
        var rawCookies = await ChallengeWebView.EvaluateJavaScriptAsync("document.cookie;");

        return (
            rawUserAgent?.Trim('"') ?? string.Empty,
            rawCookies?.Trim('"') ?? string.Empty
        );
    }

    // In case the user presses the physical back button to cancel
    protected override bool OnBackButtonPressed()
    {
        _tcs.TrySetResult(false); // Canceled
        return base.OnBackButtonPressed();
    }
}