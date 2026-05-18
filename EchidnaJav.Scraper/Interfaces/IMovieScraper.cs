using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Scraper.Interfaces
{
    public interface IMovieScraper : IScraper
    {
        MovieMetadata Metadata { get; }
        Task ScrapeAsync(string movieID, LanguageType language);
        Task ScrapeFromUrlAsync(string url, LanguageType language);
    }
}
