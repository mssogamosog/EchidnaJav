using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Core.Infrastructure.Interfaces
{
    public interface IScrapeActressService
    {
        Task<ActressData> ScrapeActressAsync(ActressData actressData, LanguageType language);
    }
}
