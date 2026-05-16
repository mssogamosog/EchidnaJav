using AngleSharp.Browser;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Scraper.Interfaces;
using EchidnaJav.Scraper.Services;
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
    public abstract class ScraperBase : IScraper
    {
        private readonly ILogger<ScraperBase> _logger;
        protected static readonly CookieContainer _sharedCookies = new CookieContainer();
        protected static HttpClient? _httpClient;
        protected static string _webViewUserAgent = string.Empty;
        private readonly ISilentWebViewSandbox _sandbox;
        private static readonly SemaphoreSlim _cloudflareLock = new(1, 1);
        protected LanguageType m_language;
        protected bool m_parsingSuccessful = false;
        protected virtual bool EnableCloudflareHandling => true;

        public string ImageSource { get; protected set; } = string.Empty;
        public bool SearchNotFound { get; protected set; }

        public ScraperBase(ILogger<ScraperBase> logger , ISilentWebViewSandbox sandbox)
        {
            _logger = logger ;
            _sandbox = sandbox;
            if (_httpClient == null)
            {
                var handler = new HttpClientHandler
                {
                    CookieContainer = _sharedCookies,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
                _httpClient = new HttpClient(handler);
            }
        }

        protected abstract bool IsLanguageSupported();
        protected abstract bool IsValidDataParsed();
        protected abstract void ParseDocument(IHtmlDocument document);

        protected async Task ScrapeWebsiteAsync(string siteURL)
        {
            _logger.LogInformation($"Scraping website for data: {siteURL}");

            bool parseError = false;
            int loadCounter = 0;
            const int browserRetries = 3;

            do
            {
                string html = string.Empty;
                bool requiresNativeSocketBypass = false;

                try
                {
                    // 1. THE FAST PATH: Try pure HttpClient first
                    html = await FastHttpScrapeAsync(siteURL);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden || ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    _logger.LogWarning($"HTTP 403/503 blocked. Pivoting to background native socket bypass for {siteURL}");
                    // Flag that we need to fetch via the OS engine, but DO NOT push a UI page yet.
                    requiresNativeSocketBypass = true;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogInformation($"Page not found (404) at {siteURL}.");
                    SearchNotFound = true;
                    break;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, $"Network error while scraping {siteURL}");
                    parseError = true;
                    break;
                }

                // 2. SILENT BACKGROUND BYPASS: Executed if HTTP was blocked by advanced TLS/Referer checks
                if (requiresNativeSocketBypass)
                {
                    try
                    {
                        // Execute traversal using an off-screen native engine without touching the visual navigation stack
                        html = await _sandbox.ExecuteSilentExtractionAsync(siteURL);
                        
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to execute silent background WebView extraction.");
                        parseError = true;
                        break;
                    }
                }

                // Parse the initial payload returned by either method
                var parser = new HtmlParser();
                var document = await parser.ParseDocumentAsync(html ?? string.Empty);

                // 3. EXPLICIT CAPTCHA INTERCEPT: Pushes visual UI ONLY if a definitive Captcha is detected in the DOM
                if (EnableCloudflareHandling && IsCloudflarePage(document))
                {
                    _logger.LogWarning("Definitive Cloudflare Captcha detected in HTML! Pushing UI Solver modally...");

                    // This is the ONLY place where a UI page is pushed to the screen
                    await ResolveCloudflareViaUIAsync(siteURL);

                    loadCounter++;
                    continue; // Loop restarts, utilizing verified clearance cookies internally
                }

                // 4. Standard Parsing Execution
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

            if (!string.IsNullOrEmpty(_webViewUserAgent))
                request.Headers.Add("User-Agent", _webViewUserAgent);
            else
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.5");

            CustomizeHttpRequest(request, url);

            var response = await _httpClient!.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        protected virtual void CustomizeHttpRequest(HttpRequestMessage request, string targetUrl) { }

        /// <summary>
        /// Pushes the UI view onto the main screen explicitly to allow manual Captcha interaction.
        /// </summary>
        private async Task ResolveCloudflareViaUIAsync(string url)
        {
            _logger.LogInformation($"Awaiting global UI clearance lock for target: {url}");

            // 1. Serialize access globally so multiple concurrent scrapers never collide on the UI stack
            await _cloudflareLock.WaitAsync();
            try
            {
                // 2. SILENT GRACE PERIOD: Allow background network adapters to process standard Turnstile passes
                _logger.LogInformation($"Acquired lock. Evaluating Cloudflare state silently for: {url}");
                await Task.Delay(1500);

                // Quick evaluation check: If a previous task just solved the challenge while we were waiting inline,
                // our shared HttpClients/Cookies may already be cleared. Short-circuit immediately if unblocked.
                if (m_parsingSuccessful || SearchNotFound)
                {
                    _logger.LogInformation("Session natively unblocked by concurrent session traversal. Bypassing visual mount.");
                    return;
                }

                CloudflareSolverPage? solverPage = null;

                // 3. MOUNT MODAL: Exclusively present the visual challenge
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    solverPage = new CloudflareSolverPage(url);
                    var rootNav = Application.Current?.MainPage?.Navigation;

                    if (rootNav != null)
                    {
                        _logger.LogInformation("Mounting Cloudflare solver overlay cleanly to the active root stack.");
                        await rootNav.PushModalAsync(solverPage);
                    }
                });

                if (solverPage == null) return;

                // 4. AWAIT RESOLUTION: Suspend current pipeline execution safely off the UI thread
                bool wasSolved = await solverPage.SolutionTask;

                if (wasSolved)
                {
                    // Allow local Webview scripts to settle native storage flushes
                    await Task.Delay(300);

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var data = await solverPage.ExtractBrowserDataAsync();

                        if (!string.IsNullOrEmpty(data.UserAgent))
                            _webViewUserAgent = data.UserAgent;

                        if (!string.IsNullOrEmpty(data.Cookies))
                        {
                            var baseUri = new Uri(url);
                            string rootDomain = baseUri.Host;

                            var cookiePairs = data.Cookies.Split(';');
                            foreach (var pair in cookiePairs)
                            {
                                var split = pair.Split(new[] { '=' }, 2);
                                if (split.Length == 2)
                                {
                                    try
                                    {
                                        string cookieName = split[0].Trim();
                                        string cookieVal = split[1].Trim();

                                        if (!string.IsNullOrEmpty(cookieName))
                                        {
                                            _sharedCookies.Add(new Cookie(cookieName, cookieVal)
                                            {
                                                Domain = rootDomain,
                                                Path = "/"
                                            });
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    });

                    _logger.LogInformation("Cloudflare clearance parameters successfully synchronized downstream.");
                }
                else
                {
                    _logger.LogWarning("Cloudflare Captcha resolution explicitly canceled or dismissed by user.");
                }

                // 5. SAFE UNMOUNTING: Flush modal cleanly off the display hierarchy
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var rootNav = Application.Current?.MainPage?.Navigation;

                    if (rootNav != null && rootNav.ModalStack.Contains(solverPage))
                    {
                        await rootNav.PopModalAsync();
                    }
                });
            }
            finally
            {
                // CRITICAL: Guarantee the lock releases even if view controllers throw unhandled exceptions
                _cloudflareLock.Release();
            }
        }

        protected bool IsCloudflarePage(IHtmlDocument? document)
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