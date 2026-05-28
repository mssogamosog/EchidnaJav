namespace EchidnaJav.Core.Domain.DTOs
{
    public class ImportProgress
    {
        public int Processed { get; set; }
        public int Total { get; set; }
        public string? CurrentMovieId { get; set; }
        public string? Status { get; set; } // "Imported", "Skipped", "Failed"
    }
}
