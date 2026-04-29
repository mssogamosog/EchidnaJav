using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Scraper.Interfaces
{
    public interface IActressScraper
    {
        ActressData Actress { get; }
        string ImageSource { get; }
        Task ScrapeAsync(string name, LanguageType language);
    }
}
