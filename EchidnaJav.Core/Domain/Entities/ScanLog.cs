using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EchidnaJav.Core.Domain.Entities
{
    public class ScanLog
    {
        [Key]
        public int Id { get; set; }
        
        public DateTime Date { get; set; } = DateTime.UtcNow;
        
        public int ActressId { get; set; }
        [ForeignKey("ActressId")]
        public virtual Actress Actress { get; set; }
        
        public int FoundCount { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsSuccess { get; set; }
    }
}
