using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Domain.Entities
{
    public class MovieActress
    {
        public string MovieId { get; set; }
        public Movie Movie { get; set; }
        public int ActressId { get; set; }
        public Actress Actress { get; set; }
        public int? Order { get; set; } // from XML
    }
}
