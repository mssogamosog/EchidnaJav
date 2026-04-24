using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace EchidnaJav.Core.Infrastructure.Services;

public interface INavigationStateService
{
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    event Action? StateChanged;
    void Dispose();
    void GoBack();
    void GoForward();
    void Reload();
}

public class NavigationStateService : IDisposable, INavigationStateService
{
    private readonly NavigationManager _navManager;
    private readonly List<string> _history = new();
    private int _currentIndex = -1;

    public bool CanGoBack => _currentIndex > 0;
    public bool CanGoForward => _currentIndex < _history.Count - 1;

    public event Action? StateChanged;

    public NavigationStateService(NavigationManager navManager)
    {
        _navManager = navManager;
        _history.Add(_navManager.Uri);
        _currentIndex = 0;
        _navManager.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // FIX 1: URL Matching instead of Booleans.
        // If the URL we arrived at matches exactly where our index is pointing, 
        // it means this event was triggered by our own GoBack/GoForward methods. Ignore it!
        if (_currentIndex >= 0 && _currentIndex < _history.Count && _history[_currentIndex] == e.Location)
        {
            return;
        }

        // User clicked a real link: chop off any "forward" history to create a new branch.
        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
        }

        // Add the new location and advance the index
        if (_history.Count == 0 || _history.Last() != e.Location)
        {
            _history.Add(e.Location);
            _currentIndex++;
        }

        StateChanged?.Invoke();
    }

    public void GoBack()
    {
        if (!CanGoBack) return;

        _currentIndex--;

        // FIX 2: replace: true 
        // This tells the native MAUI WebView to replace the current page in its memory 
        // rather than stacking it infinitely, eliminating the lag.
        _navManager.NavigateTo(_history[_currentIndex], replace: true);

        StateChanged?.Invoke();
    }

    public void GoForward()
    {
        if (!CanGoForward) return;

        _currentIndex++;

        // FIX 2: replace: true
        _navManager.NavigateTo(_history[_currentIndex], replace: true);

        StateChanged?.Invoke();
    }

    public void Reload() => _navManager.Refresh(forceReload: true);

    public void Dispose()
    {
        _navManager.LocationChanged -= OnLocationChanged;
    }
}