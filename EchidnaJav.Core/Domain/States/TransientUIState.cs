namespace EchidnaJav.Core.Domain.States
{
    public class TransientUIState
    {
        public int SelectedMoviesCount { get; private set; }
        public string? FooterText { get; private set; }

        public event Action? OnChange;

        public void UpdateSelectedCount(int count)
        {
            SelectedMoviesCount = count;
            Notify();
        }

        public void SetFooterText(string? title)
        {
            if (FooterText != title)
            {
                FooterText = title;
                Notify();
            }
        }

        private void Notify()
        {
            OnChange?.Invoke();
        }
    }
}