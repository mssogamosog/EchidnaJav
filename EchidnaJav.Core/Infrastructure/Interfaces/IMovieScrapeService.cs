using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Infrastructure.Interfaces
{
    public interface IMovieScrapeService
    {
        Task<MovieMetadata> ScrapeMovieAsync(string movieID, string coverImagePath, LanguageType language);
        Task<MovieMetadata> ScrapeMovieFromMultipleUrlsAsync(string id, List<ManualUrlScrapeRequest> requests, string targetCoverPath, LanguageType english);
    }

}
