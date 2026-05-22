using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Domain.DTOs
{
    public class ActressImageDto
    {
        public string Filepath { get; set; } = string.Empty;
        public int Index { get; set; }
    }
    public class ActressDetailsDto
    {
        public string Name { get; set; }
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
        public int MovieCount { get; set; }
        public List<ActressImageDto> Images { get; set; } = new();
        public List<MovieDto> Movies { get; set; } = new(); 
    }
}
