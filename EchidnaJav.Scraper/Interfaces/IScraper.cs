using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Scraper.Interfaces
{
    public interface IScraper
    {
        string ImageSource { get; }
        bool SearchNotFound { get; }
    }
}
