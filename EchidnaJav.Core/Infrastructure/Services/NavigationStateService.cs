using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace EchidnaJav.Core.Infrastructure.Services;

public interface INavigationStateService
{
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    event Action? StateChanged;
    event Action? OnReloadRequested;
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
    public event Action? OnReloadRequested;

    public NavigationStateService(NavigationManager navManager)
    {
        _navManager = navManager;
        _history.Add(_navManager.Uri);
        _currentIndex = 0;
        _navManager.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (_currentIndex >= 0 && _currentIndex < _history.Count && _history[_currentIndex] == e.Location)
        {
            return;
        }

        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
        }

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

    public void Reload() => OnReloadRequested?.Invoke();

    public void Dispose()
    {
        _navManager.LocationChanged -= OnLocationChanged;
    }
}