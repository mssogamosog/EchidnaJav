using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace EchidnaJav.Scraper.Views;

public partial class CloudflareSolverPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _tcs = new();
    public Task<bool> SolutionTask => _tcs.Task;

    public CloudflareSolverPage(string targetUrl)
    {
        InitializeComponent();
        ChallengeWebView.Source = new UrlWebViewSource { Url = targetUrl };
    }

    private async void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
    {
        string? html = await ChallengeWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML;");
        if (string.IsNullOrEmpty(html)) return;

        // Evaluate if visible validation blocks have cleared
        bool isSolved = !html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) &&
                        !html.Contains("cf-turnstile", StringComparison.OrdinalIgnoreCase) &&
                        !html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);

        if (isSolved)
        {
            _tcs.TrySetResult(true);
        }
    }

    public async Task<(string UserAgent, string Cookies)> ExtractBrowserDataAsync()
    {
        var rawUserAgent = await ChallengeWebView.EvaluateJavaScriptAsync("navigator.userAgent;");
        var rawCookies = await ChallengeWebView.EvaluateJavaScriptAsync("document.cookie;");

        return (
            rawUserAgent?.Trim('"') ?? string.Empty,
            rawCookies?.Trim('"') ?? string.Empty
        );
    }

    protected override bool OnBackButtonPressed()
    {
        _tcs.TrySetResult(false);
        return base.OnBackButtonPressed();
    }
}