---
name: "Scraping Engine Development"
description: "Triggers when adding or modifying website scrapers, parsers, or data extraction logic"
---

# Scraping Engine Development Standards

## Purpose
Ensure new scrapers conform to the existing architecture, implement the correct interfaces, and register properly in Dependency Injection.

## When to use
*   Adding support for a new Jav/Movie metadata provider.
*   Modifying existing scrapers in `EchidnaJav.Scraper`.

## Relevant Folders
*   `EchidnaJav.Scraper/`
*   `EchidnaJav.Scraper/Interfaces/`

## Project-Specific Conventions
1.  **Interfaces:** New movie scrapers must implement `IMovieScraper` or inherit from `MovieScraperBase`. Actress scrapers implement `IActressScraper`.
2.  **DI Registration:** Every new scraper module must be explicitly registered as `Transient` or `Scoped` in `MauiProgram.cs` under the "Scraping Pipelines" region.
3.  **HttpClient:** Do not instantiate `HttpClient` manually. Rely on `IImageService` or injected `IHttpClientFactory`.
4.  **Sandbox Isolation:** HTML parsing logic should rely on `ISilentWebViewSandbox` if executing DOM/JS logic, or standard HTTP/Regex/HtmlAgilityPack otherwise.

## Common Pitfalls
*   **Missing DI Registration:** Creating a new scraper class but forgetting to add it to `MauiProgram.cs`.
*   **Blocking Calls:** Using `.Result` or `.Wait()` instead of `await` during HTTP calls.

## Validation Steps
*   Confirm the new scraper is registered in `MauiProgram.cs`.
*   Verify the scraper gracefully handles network timeouts or missing DOM elements without crashing the application.
