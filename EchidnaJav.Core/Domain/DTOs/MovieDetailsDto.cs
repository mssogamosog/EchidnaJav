using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Domain.DTOs
{
    public class MovieDetailsDto
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string? ImagePath { get; set; } 
        public DateTime? Premiered { get; set; }
        public int? Runtime { get; set; }
        public string? Studio { get; set; }
        public string? Director { get; set; }
        public string? Plot { get; set; }
        public List<string> Genres { get; set; } = new();
        public List<FileDto> Files { get; set; } = new();
        public List<MovieActorDto> Cast { get; set; } = new();
        public byte[]? NewCoverImageBytes { get; set; }
        public string? NewCoverImageExtension { get; set; }
    }

    public class FileDto
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
    }
}
