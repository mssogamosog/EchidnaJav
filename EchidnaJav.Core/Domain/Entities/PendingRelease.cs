using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EchidnaJav.Core.Domain.Entities
{
    public class PendingRelease
    {
        [Key]
        public int Id { get; set; }
        
        public int ActressId { get; set; }
        [ForeignKey("ActressId")]
        public virtual Actress Actress { get; set; }
        
        public string Url { get; set; }
        public string Title { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string? ThumbnailUrl { get; set; }
        public PendingReleaseStatus Status { get; set; } = PendingReleaseStatus.Pending;
    }

    public enum PendingReleaseStatus
    {
        Pending,
        Ignored,
        SentToJD
    }
}
