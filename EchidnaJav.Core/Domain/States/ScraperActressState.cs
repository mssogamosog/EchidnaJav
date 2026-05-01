using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Domain.States
{
    public class ScraperActressState
    {
        private int _totalQueued = 0;
        private int _processedCount = 0;
        public event Action? OnChange;
        public int TotalQueued => _totalQueued;
        public int ProcessedCount => _processedCount;
        public int RemainingCount => _totalQueued - _processedCount;
        public bool IsProcessing => RemainingCount > 0;

        public void AddToQueue()
        {
            Interlocked.Increment(ref _totalQueued);
            NotifyStateChanged();
        }

        public void MarkAsProcessed()
        {
            Interlocked.Increment(ref _processedCount);
            NotifyStateChanged();
        }
        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}