using System;
using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Core.Domain.States
{
    public class SearchFilterState
    {
        public string SearchText { get; private set; } = string.Empty;
        public SortMoviesBy CurrentSort { get; private set; } = SortMoviesBy.RecentlyAdded;
        public int TotalDatabaseCount { get; private set; }
        public SortActressesBy CurrentActressSort { get; private set; } = SortActressesBy.MovieCount;
        public event Action? OnSearchChanged;
        public event Action? OnMetadataChanged;

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
                OnSearchChanged?.Invoke();
            }
        }
        public void SetActressSort(SortActressesBy sort)
        {
            if (CurrentActressSort != sort)
            {
                CurrentActressSort = sort;
                OnSearchChanged?.Invoke();
            }
        }
        public void SetSort(SortMoviesBy sort)
        {
            if (CurrentSort != sort)
            {
                CurrentSort = sort;
                OnSearchChanged?.Invoke();
            }
        }
    }
}