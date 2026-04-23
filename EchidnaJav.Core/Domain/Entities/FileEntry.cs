using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace EchidnaJav.Core.Domain.Entities
{
    public class FileEntry
    {
        [Key]
        public int Id { get; set; }
        public string MovieId { get; set; }
        public Movie Movie { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
        public string Hash { get; set; } // SHA256 or MD5
        public bool IsScanned { get; set; } // flag
    }
}
