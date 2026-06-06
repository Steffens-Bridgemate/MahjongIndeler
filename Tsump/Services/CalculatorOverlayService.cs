namespace Tsump.Services;

// App-wide open/close state for the floating Riichi calculator overlay. The nav menu (every
// variant) opens it; the overlay lives in MainLayout so it floats above whatever page is showing.
public class CalculatorOverlayService
{
    public bool IsOpen { get; private set; }

    public event Action? OnChanged;

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        OnChanged?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        OnChanged?.Invoke();
    }
}
