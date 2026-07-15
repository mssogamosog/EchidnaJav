using System.ComponentModel.DataAnnotations;

namespace EchidnaJav.Core.Domain.Entities
{
    public class Actress
    {
        [Key]
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? JapaneseName { get; set; }
        public int? DobYear { get; set; }
        public int? DobMonth { get; set; }
        public int? DobDay { get; set; }

        public int? Height { get; set; }
        public string? Cup { get; set; }

        public int? Bust { get; set; }
        public int? Waist { get; set; }
        public int? Hips { get; set; }

        public string? BloodType { get; set; }

        public double? UserRating { get; set; }
        public string? Notes { get; set; }
        public bool? IsFavorite { get; set; } = false;
        
        public DateTime? LastScanDate { get; set; }
        public bool AutoImportEnabled { get; set; } = false;

        public List<ActressAltName> AltNames { get; set; } = new List<ActressAltName>();
        public List<ActressImage> Images { get; set; } = new List<ActressImage>();
        public List<MovieActress> MovieActresses { get; set; } = new List<MovieActress>();
    }
    public class ActressAltName
    {
        public int Id { get; set; }
        public int ActressId { get; set; }
        public string Name { get; set; }
    }
    public class ActressImage
    {
        public int Id { get; set; }
        public int ActressId { get; set; }
        public string Filepath { get; set; }
        public int Index { get; set; }
    }
}
