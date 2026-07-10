namespace EchidnaJav.Core.Domain.DTOs
{
    public enum SortMoviesBy
    {
        Title,
        ID,
        DateNewest,
        DateOldest,
        Random,
        RecentlyAdded,
        ActressName
    }
    public class MovieQueryParameters
    {
        public string? SearchText { get; set; }
        public string? SearchActress { get; set; }
        public SortMoviesBy SortBy { get; set; } = SortMoviesBy.Title;
        public bool MissingImageOnly { get; set; }
        public bool FavoritesOnly { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; } = 50;
    }
}
