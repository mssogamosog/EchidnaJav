using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Scraper.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Scraper
{
    abstract public class ActressScraperBase : ScraperBase, IActressScraper
    {
        protected ActressScraperBase(ILogger<ActressScraperBase> logger) : base(logger)
        {
        }

        public ActressData Actress { get; protected set; } = new ActressData();
    }
}
