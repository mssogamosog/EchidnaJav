using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Scraper.Interfaces;
using EchidnaJav.Scraper.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Scraper
{
    abstract public class ActressScraperBase : ScraperBase, IActressScraper
    {
        protected ActressScraperBase(ILogger<ActressScraperBase> logger, ISilentWebViewSandbox sandbox) : base(logger, sandbox)
        {
        }
        public abstract Task ScrapeAsync(string actressName, LanguageType language);
        public ActressData Actress { get; protected set; } = new ActressData();
    }
}
