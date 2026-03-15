using Microsoft.JSInterop;

namespace Tsump.Services;

public enum AppMode
{
    Weekly,
    Tournament
}

public class AppModeService
{
    private readonly IJSRuntime _jsRuntime;
    private AppMode _currentMode = AppMode.Weekly;
    private bool _loaded;

    public event Action? OnModeChanged;

    public AppModeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public AppMode CurrentMode => _currentMode;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "tsump_app_mode");
        if (stored == "tournament")
            _currentMode = AppMode.Tournament;
        _loaded = true;
    }

    public async Task SetModeAsync(AppMode mode)
    {
        _currentMode = mode;
        var value = mode == AppMode.Tournament ? "tournament" : "weekly";
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "tsump_app_mode", value);
        OnModeChanged?.Invoke();
    }
}
