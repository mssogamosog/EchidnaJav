using System.ComponentModel.DataAnnotations;

namespace EchidnaJav.Core.Domain.Entities
{
    public class Genre
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
