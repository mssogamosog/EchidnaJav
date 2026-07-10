using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Core.Domain.States
{
    public class PageCacheEntry
    {
        public List<MovieCardDto> Movies { get; set; } = new();
        public int TotalCount { get; set; }
        public bool HasMore { get; set; } = true;
        public double ScrollPosition { get; set; }
        public string? ContextKey { get; set; } // e.g., the specific Actress Name currently being viewed
        public int FilterVersion { get; set; }
    }

    public class NavigationCacheState
    {
        private readonly Dictionary<string, PageCacheEntry> _pageCaches = new();
        public string LastActiveListKey { get; set; } = "Home";

        public PageCacheEntry GetCache(string pageKey)
        {
            if (!_pageCaches.ContainsKey(pageKey))
            {
                _pageCaches[pageKey] = new PageCacheEntry();
            }
            return _pageCaches[pageKey];
        }

        public void ResetCache(string pageKey)
        {
            if (_pageCaches.ContainsKey(pageKey))
            {
                _pageCaches[pageKey] = new PageCacheEntry();
            }
        }

        public void ClearAll()
        {
            _pageCaches.Clear();
        }

        public string? GetPreviousMovieId(string pageKey, string currentMovieId)
        {
            if (!_pageCaches.TryGetValue(pageKey, out var cache)) return null;

            var index = cache.Movies.FindIndex(m => m.Id == currentMovieId);
            if (index > 0)
                return cache.Movies[index - 1].Id;

            return null;
        }

        public string? GetNextMovieId(string pageKey, string currentMovieId)
        {
            if (!_pageCaches.TryGetValue(pageKey, out var cache)) return null;

            var index = cache.Movies.FindIndex(m => m.Id == currentMovieId);
            if (index >= 0 && index < cache.Movies.Count - 1)
                return cache.Movies[index + 1].Id;

            return null;
        }
    }
}