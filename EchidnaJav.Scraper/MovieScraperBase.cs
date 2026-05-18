using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Scraper.Interfaces;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Scraper
{
    public abstract class MovieScraperBase : ScraperBase, IMovieScraper
    {
        public MovieMetadata Metadata { get; protected set; }

        public MovieScraperBase(ILogger<ScraperBase> logger, ISilentWebViewSandbox sandbox) : base(logger, sandbox)
        {
        }
        public abstract Task ScrapeAsync(string movieID, LanguageType language);
        public virtual Task ScrapeFromUrlAsync(string url, LanguageType language)
        {
            m_parsingSuccessful = false;
            SearchNotFound = false;
            ImageSource = string.Empty;
            Metadata = new MovieMetadata("");
            m_language = language;
            if (!IsLanguageSupported())
            {
                return Task.CompletedTask;
            }
            return ScrapeWebsiteAsync(url);
        }
    }
}
