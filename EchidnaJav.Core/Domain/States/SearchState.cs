using System;
using System.Collections.Generic;
using EchidnaJav.Core.Domain.DTOs; 

namespace EchidnaJav.Core.Domain.States
{
    public class SearchState
    {
        public string SearchText { get; private set; } = string.Empty;
        public event Action? OnSearchChanged;
        public List<MovieDto> CachedMovies { get; set; } = new();
        public bool HasMoreCached { get; set; } = true;
        public double ScrollPosition { get; set; } = 0;

        public void SetSearchText(string text)
        {
            if (SearchText != text)
            {
                SearchText = text;
                CachedMovies.Clear();
                HasMoreCached = true;
                ScrollPosition = 0;

                OnSearchChanged?.Invoke();
            }
        }
    }
}