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
        public string? GetPreviousMovieId(string currentMovieId)
        {
            var index = CachedMovies.FindIndex(m => m.Id == currentMovieId);
            if (index > 0)
                return CachedMovies[index - 1].Id;

            return null;
        }

        public string? GetNextMovieId(string currentMovieId)
        {
            var index = CachedMovies.FindIndex(m => m.Id == currentMovieId);
            if (index >= 0 && index < CachedMovies.Count - 1)
                return CachedMovies[index + 1].Id;

            return null;
        }
    }
}