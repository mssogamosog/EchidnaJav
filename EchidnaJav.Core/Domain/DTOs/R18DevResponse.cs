namespace EchidnaJav.Core.Domain.DTOs
{
    public class R18DevResponse
    {
        public string? Title { get; set; }
        public string? ReleaseDate { get; set; }
        public int RuntimeMinutes { get; set; }
        public string? Director { get; set; }
        public R18EntityDescriptor? Maker { get; set; }
        public R18EntityDescriptor? Label { get; set; }
        public List<R18EntityDescriptor>? Categories { get; set; }
        public List<R18EntityDescriptor>? Actresses { get; set; }
        public R18ImageContainer? Images { get; set; }
    }

    public class R18EntityDescriptor
    {
        public string? Name { get; set; }
    }

    public class R18ImageContainer
    {
        public R18JacketImages? JacketImage { get; set; }
    }

    public class R18JacketImages
    {
        public string? Large { get; set; }
        public string? Large2 { get; set; }
    }
}
