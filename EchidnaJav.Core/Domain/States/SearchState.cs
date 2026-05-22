using System;
using System.Collections.Generic;
using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Core.Domain.States
{
    public class SearchState
    {
        public string SearchText { get; private set; } = string.Empty;
        public SortMoviesBy CurrentSort { get; private set; } = SortMoviesBy.RecentlyAdded;
        public int DisplayedMoviesCount => CachedMovies.Count;
        public double ActressesScrollPosition { get; set; } = 0;
        public List<MovieDto> HomeBackupMovies { get; set; } = new();
        public int HomeBackupTotal { get; set; }
        public bool HomeBackupHasMore { get; set; }
        public double HomeBackupScroll { get; set; }
        public string? ActiveCacheActress { get; set; }
        public int TotalDatabaseCount { get; private set; }
        public event Action? OnSearchChanged;
        public event Action? OnMetadataChanged;
        public List<MovieDto> CachedMovies { get; set; } = new();
        public bool HasMoreCached { get; set; } = true;
        public double ScrollPosition { get; set; } = 0;
        public void SetTotalCount(int count)
        {
            if (TotalDatabaseCount != count)
            {
                TotalDatabaseCount = count;
                OnMetadataChanged?.Invoke();
            }
        }
        public void SetSearchText(string text)
        {
            if (SearchText != text)
            {
                SearchText = text;
                ResetNavigation();
                OnSearchChanged?.Invoke();
            }
        }

        public void SetSort(SortMoviesBy sort)
        {
            if (CurrentSort != sort)
            {
                CurrentSort = sort;
                ResetNavigation();
                OnSearchChanged?.Invoke();
            }
        }

        private void ResetNavigation()
        {
            CachedMovies.Clear();
            HasMoreCached = true;
            ScrollPosition = 0;
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