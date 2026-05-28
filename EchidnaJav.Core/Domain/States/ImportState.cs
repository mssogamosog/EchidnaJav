using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Core.Domain.States
{
    public class ImportState
    {
        public bool IsRunning { get; set; }
        public ImportProgress Progress { get; set; } = new();
        public CancellationTokenSource? Cts { get; set; }

        public event Action? OnChange;

        public void Notify() => OnChange?.Invoke();
    }
}
