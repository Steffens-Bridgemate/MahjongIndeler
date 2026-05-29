using Tsump.Models;

namespace Tsump.Services;

/// <summary>
/// Records import captures to localStorage when <see cref="ClubSettings.EnableScanLogging"/> is
/// on. Newest-first, capped so the log can't grow unbounded. A diagnostic aid for HID-scanner
/// round-trip issues — see the Log page (/scan-log).
/// </summary>
public class ScanLogService
{
    private const string StorageKey = "tsump_scan_log";
    private const int MaxEntries = 200;

    private readonly LocalStorageService _storage;
    private readonly SettingsService _settings;

    public ScanLogService(LocalStorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;
    }

    /// <summary>Appends a capture. No-op (and no storage write) when logging is disabled or the
    /// text is blank, so callers can fire this unconditionally from the import funnel.</summary>
    public async Task AddAsync(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var settings = await _settings.GetAsync();
        if (!settings.EnableScanLogging) return;

        var entries = await GetAllAsync();
        entries.Insert(0, new ScanLogEntry { Raw = raw.Trim() });
        if (entries.Count > MaxEntries)
            entries = entries.Take(MaxEntries).ToList();
        await _storage.SetAsync(StorageKey, entries);
    }

    /// <summary>All entries, newest first.</summary>
    public async Task<List<ScanLogEntry>> GetAllAsync()
        => await _storage.GetAsync<List<ScanLogEntry>>(StorageKey) ?? new List<ScanLogEntry>();

    public async Task ClearAsync() => await _storage.RemoveAsync(StorageKey);
}
