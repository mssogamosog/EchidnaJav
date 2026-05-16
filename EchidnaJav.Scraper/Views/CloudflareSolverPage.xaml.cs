using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace EchidnaJav.Scraper.Views;

public partial class CloudflareSolverPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _tcs = new();
    private readonly string _targetUrl;
    private bool _isResolved = false;

    public Task<bool> SolutionTask => _tcs.Task;

    public CloudflareSolverPage(string targetUrl)
    {
        InitializeComponent();
        _targetUrl = targetUrl;
        ChallengeWebView.Source = new UrlWebViewSource { Url = targetUrl };
    }

    private async void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
    {
        // Prevent duplicate execution evaluations if a solution is already captured
        if (_isResolved) return;

        string? html = await ChallengeWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML;");
        if (string.IsNullOrWhiteSpace(html)) return;

        // 🔥 1. ABSOLUTE CLOUDFLARE BLOCK CHECK
        // If any of Cloudflare's core tracking scripts or wrapper titles exist, we are 100% still blocked.
        bool isCloudflareActive = html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase) ||
                                  html.Contains("_cf_chl_opt", StringComparison.OrdinalIgnoreCase) ||
                                  html.Contains("cf-turnstile", StringComparison.OrdinalIgnoreCase) ||
                                  html.Contains("<title>Just a moment", StringComparison.OrdinalIgnoreCase);

        if (isCloudflareActive)
        {
            // Force the layout to stay mounted so you can manually click the Turnstile widget
            return;
        }

        // 🔥 2. POSITIVE SUCCESS CONFIRMATION
        // To prevent false positives on intermediate blank loading screens, demand proof of payload delivery.
        bool isSafeLength = html.Length > 2000; // Real target pages and JSON payloads are heavily populated

        // Standard HTML Web responses will always contain a legitimate customized title
        bool hasRealTitle = html.Contains("<title>", StringComparison.OrdinalIgnoreCase) &&
                            !html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);

        // Pure API JSON responses (like R18.dev payloads) lack HTML <title> nodes but contain distinct properties
        bool isJsonResponse = html.Contains("runtimeminutes", StringComparison.OrdinalIgnoreCase) ||
                              html.Contains("jacketimage", StringComparison.OrdinalIgnoreCase);

        // If Cloudflare is gone AND we have substantial, authentic markup/JSON, the session is officially unblocked
        if (!isCloudflareActive && isSafeLength && (hasRealTitle || isJsonResponse))
        {
            _isResolved = true;
            _tcs.TrySetResult(true);
        }
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        if (!_isResolved)
        {
            _isResolved = true;
            _tcs.TrySetResult(false);
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
        if (!_isResolved)
        {
            _isResolved = true;
            _tcs.TrySetResult(false);
        }
        return base.OnBackButtonPressed();
    }
}