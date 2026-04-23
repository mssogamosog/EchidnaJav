using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Domain.States
{
    public class UIState
    {
        public bool UseWideView { get; private set; } = false;

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
    }
}
