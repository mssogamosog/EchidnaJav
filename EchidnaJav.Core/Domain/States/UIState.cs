using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Domain.States
{
    public class UIState
    {
        public bool UseWideView { get; private set; } = false;
        public int SelectedMoviesCount { get; private set; }
        public string? HoveredMovieTitle { get; private set; }
        public int CardWidth { get; private set; } = 180;

        public event Action? OnChange;
        public void SetPosterView()
        {
            UseWideView = false;
            Notify();
        }

        public void SetWideView()
        {
            UseWideView = true;
            Notify();
        }

        public void SetWidth(int width)
        {
            CardWidth = width;
            Notify();
        }

        private void Notify()
        {
            OnChange?.Invoke();
        }
        public void UpdateSelectedCount(int count)
        {
            SelectedMoviesCount = count;
            Notify();
        }

        public void SetHoveredMovie(string? title)
        {
            if (HoveredMovieTitle != title)
            {
                HoveredMovieTitle = title;
                Notify();
            }
        }
    }
}
