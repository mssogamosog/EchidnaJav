namespace EchidnaJav.Core.Domain.DTOs
{
    public enum ScraperSourceType
    {
        JavLibrary,
        JavDatabase,
        R18Dev,
        SupJav,
        MissAv,
        JavTiful
    }
    public class ManualUrlScrapeRequest
    {
        public ScraperSourceType Source { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}
