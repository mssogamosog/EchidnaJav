using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Domain.Constants
{
    public static class MediaConstants
    {
        public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".flv", ".m4v", ".ts", ".webm"
        };
    }
}
