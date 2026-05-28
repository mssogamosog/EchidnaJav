namespace EchidnaJav.Scraper.Interfaces
{
    public interface IScraper
    {
        string ImageSource { get; }
        bool SearchNotFound { get; }
    }
}
