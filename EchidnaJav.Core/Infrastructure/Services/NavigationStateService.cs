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
        // 1. Ignore if this was triggered by our own GoBack/GoForward methods
        if (_currentIndex >= 0 && _currentIndex < _history.Count && _history[_currentIndex] == e.Location)
        {
            return;
        }

        // 2. User clicked a real link: chop off any "forward" history to create a new branch
        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
        }

        // 🔥 3. THE MOVIE SLIDESHOW FIX
        bool isArrivingAtMovie = e.Location.Contains("/movie/", StringComparison.OrdinalIgnoreCase);
        bool isLeavingMovie = _history.Count > 0 && _history[_currentIndex].Contains("/movie/", StringComparison.OrdinalIgnoreCase);

        if (isArrivingAtMovie && isLeavingMovie)
        {
          
            _history[_currentIndex] = e.Location;
        }
        else if (_history.Count == 0 || _history.Last() != e.Location)
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
        _navManager.NavigateTo(_history[_currentIndex], replace: true);
        StateChanged?.Invoke();
    }

    public void GoForward()
    {
        if (!CanGoForward) return;

        _currentIndex++;
        _navManager.NavigateTo(_history[_currentIndex], replace: true);
        StateChanged?.Invoke();
    }

    public void Reload() => _navManager.Refresh(forceReload: true);

    public void Dispose()
    {
        _navManager.LocationChanged -= OnLocationChanged;
    }
}