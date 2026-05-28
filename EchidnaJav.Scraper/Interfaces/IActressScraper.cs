using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Scraper.Interfaces
{
    public interface IActressScraper : IScraper
    {
        ActressData Actress { get; }
        Task ScrapeAsync(string actressName, LanguageType language);
    }
}
