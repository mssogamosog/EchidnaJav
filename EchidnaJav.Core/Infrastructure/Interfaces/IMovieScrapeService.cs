using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Infrastructure.Interfaces
{
    public interface IMovieScrapeService
    {
        Task<MovieMetadata> ScrapeMovieAsync(string movieID, string coverImagePath, LanguageType language);
    }

}
