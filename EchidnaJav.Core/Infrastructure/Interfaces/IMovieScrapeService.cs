using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Core.Infrastructure.Interfaces
{
    public interface IMovieScrapeService
    {
        Task<MovieMetadata> ScrapeMovieAsync(string movieID, string coverImagePath, LanguageType language);
        Task<MovieMetadata> ScrapeMovieFromMultipleUrlsAsync(string id, List<ManualUrlScrapeRequest> requests, string targetCoverPath, LanguageType english);
    }

}
