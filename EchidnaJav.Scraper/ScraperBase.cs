using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Scraper.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
namespace EchidnaJav.Scraper
{

    abstract public class ScraperBase
    {
        private readonly ILogger<ScraperBase> _logger;
        protected static CookieContainer _sharedCookies = new CookieContainer();
        protected static HttpClient _httpClient;
        protected static string _webViewUserAgent = string.Empty;     
        protected LanguageType m_language;
        protected bool m_parsingSuccessful = false;
        protected virtual bool EnableCloudflareHandling => true;
        public string ImageSource { get; protected set; }
        public bool SearchNotFound { get; protected set; }
        public ScraperBase(ILogger<ScraperBase> logger)
        {
            _logger = logger;
            ImageSource = string.Empty;

            if (_httpClient == null)
            {
                var handler = new HttpClientHandler { CookieContainer = _sharedCookies };
                _httpClient = new HttpClient(handler);
            }
        }      

        abstract public Task ScrapeAsync(string actressName, LanguageType language);
        abstract protected bool IsLanguageSupported();
        abstract protected bool IsValidDataParsed();
        abstract protected void ParseDocument(IHtmlDocument document);
        protected async Task ScrapeWebsiteAsync(string siteURL)
        {
            _logger.LogInformation("Scraping website for data: " + siteURL);

            bool parseError = false;
            int loadCounter = 0;
            const int browserRetries = 3;

            do
            {
                string html = string.Empty;

                try
                {
                    // 1. THE FAST PATH: Try HttpClient first
                    html = await FastHttpScrapeAsync(siteURL);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden || ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    // Cloudflare often returns 403 or 503 when challenging
                    _logger.LogWarning($"HTTP blocked (Possible Cloudflare). Starting WebView fallback for {siteURL}");
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // 🔥 NEW: Gracefully handle 404s!
                    _logger.LogInformation($"Actress page not found (404) at {siteURL}. She might be new or unlisted.");
                    SearchNotFound = true;
                    break; // Break completely out of the do-while loop!
                }
                catch (HttpRequestException ex)
                {
                    // Catch any other weird network errors (like timeouts) so they don't crash the import
                    _logger.LogError(ex, $"Network error while scraping {siteURL}");
                    parseError = true;
                    break;
                }

                var parser = new HtmlParser();
                var document = await parser.ParseDocumentAsync(html ?? string.Empty);

                // 2. THE SLOW PATH: Check if we are still trapped by Cloudflare
                if (EnableCloudflareHandling && IsCloudflarePage(document))
                {
                    _logger.LogWarning("Cloudflare challenge detected in HTML! Requesting User Intervention...");

                    // Pop the UI and solve
                    await ResolveCloudflareViaUIAsync(siteURL);

                    // Loop restarts, and FastHttpScrapeAsync will now use the new cookies!
                    continue;
                }

                // 3. Parse Data
                if (document != null && !string.IsNullOrEmpty(html))
                {
                    try
                    {
                        ParseDocument(document);
                    }
                    catch (Exception ex)
                    {   
                        _logger.LogError(ex, "HTML document parsing exception");
                        parseError = true;
                        break;
                    }
                }

                loadCounter++;
            }
            while (!parseError && !IsValidDataParsed() && loadCounter <= browserRetries);
        }
        private async Task<string> FastHttpScrapeAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Critical: Mask our HttpClient to look exactly like the WebView
            if (!string.IsNullOrEmpty(_webViewUserAgent))
            {
                request.Headers.Add("User-Agent", _webViewUserAgent);
            }
            else
            {
                // Sensible default until we grab the real one from the device
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            }

            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.5");

            var response = await _httpClient.SendAsync(request);

            // Will throw if 403/503 Cloudflare block
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        private async Task ResolveCloudflareViaUIAsync(string url)
        {
            CloudflareSolverPage solverPage = null;

            // 1. Push the Modal onto the screen (Must be on MainThread)
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                solverPage = new CloudflareSolverPage(url);

                // Find the current main page to push the modal
                var currentPage = Application.Current.MainPage;
                if (currentPage is NavigationPage navPage)
                    currentPage = navPage.CurrentPage;

                await currentPage.Navigation.PushModalAsync(solverPage);
            });

            // 2. Wait indefinitely until the user solves it (or cancels)
            bool wasSolved = await solverPage.SolutionTask;

            if (wasSolved)
            {
                // 3. THE HEIST: Steal the Cookies and User-Agent on the MainThread
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var data = await solverPage.ExtractBrowserDataAsync();

                    if (!string.IsNullOrEmpty(data.UserAgent))
                        _webViewUserAgent = data.UserAgent;

                    if (!string.IsNullOrEmpty(data.Cookies))
                    {
                        var baseUri = new Uri(url);
                        var cookiePairs = data.Cookies.Split(';');
                        foreach (var pair in cookiePairs)
                        {
                            var split = pair.Split(new[] { '=' }, 2);
                            if (split.Length == 2)
                            {
                                try { _sharedCookies.Add(baseUri, new Cookie(split[0].Trim(), split[1].Trim())); }
                                catch { /* Ignore malformed cookies */ }
                            }
                        }
                    }
                });
            }
            else
            {
                _logger.LogWarning("Cloudflare challenge was canceled by the user.");
            }

            // 4. Close the Modal and resume scraping
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var currentPage = Application.Current.MainPage;
                await currentPage.Navigation.PopModalAsync();
            });
        }
        protected bool IsCloudflarePage(IHtmlDocument document)
        {
            if (document?.DocumentElement == null) return true;

            var html = document.DocumentElement.OuterHtml;
            var title = document.Title ?? string.Empty;

            if (title.Contains("Just a moment...", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Attention Required!", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return html.IndexOf("cf-turnstile-wrapper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("cf-browser-verification", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("id=\"challenge-running\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

}
