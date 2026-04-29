using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Infrastructure.Interfaces
{
    public interface IScrapeService
    {
        Task<ActressData> ScrapeActressAsync(ActressData actressData, LanguageType language);
    }
}
