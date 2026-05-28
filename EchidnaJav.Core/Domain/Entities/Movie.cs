using System.ComponentModel.DataAnnotations;

namespace EchidnaJav.Core.Domain.Entities
{
    public class Movie
    {
        [Key]
        public string Id { get; set; } // START-296
        public string NormalizedId { get; set; }
        public string Title { get; set; }
        public string? OriginalTitle { get; set; }
        public DateTime? Premiered { get; set; }
        public int? Year { get; set; }
        public string? Director { get; set; }
        public string? Studio { get; set; }
        public string? Label { get; set; }
        public string? Series { get; set; }
        public double? Rating { get; set; }
        public double? UserRating { get; set; }
        public string? Plot { get; set; }
        public int? Runtime { get; set; }
        public DateTime? DateAdded { get; set; }
        public string? PrimaryImagePath { get; set; }
        public bool? IsFavorite { get; set; } = false;
        public bool? IsWatched { get; set; } = false;
        public List<MovieActress> MovieActresses { get; set; } = new List<MovieActress>();
        public List<MovieGenre> MovieGenres { get; set; } = new List<MovieGenre>();
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
    }
}
