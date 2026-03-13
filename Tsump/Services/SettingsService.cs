using Tsump.Models;

namespace Tsump.Services;

public class SettingsService
{
    private const string StorageKey = "tsump_settings";
    private readonly LocalStorageService _storage;

    public SettingsService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public async Task<ClubSettings> GetAsync()
    {
        return await _storage.GetAsync<ClubSettings>(StorageKey) ?? new ClubSettings();
    }

    public async Task SaveAsync(ClubSettings settings)
    {
        await _storage.SetAsync(StorageKey, settings);
    }

    /// <summary>
    /// Finds the nearest future date+time that matches the club schedule.
    /// Returns null if no schedule is configured.
    /// </summary>
    public async Task<(DateTime Date, TimeSpan Time)?> GetNextScheduledSlotAsync()
    {
        var settings = await GetAsync();
        if (settings.Schedule.Count == 0)
            return null;

        var now = DateTime.Now;

        // Look up to 8 days ahead to cover all days of the week plus today
        for (int dayOffset = 0; dayOffset < 8; dayOffset++)
        {
            var candidate = now.Date.AddDays(dayOffset);
            var entry = settings.Schedule.FirstOrDefault(e => e.Day == candidate.DayOfWeek);
            if (entry == null || entry.StartTimes.Count == 0)
                continue;

            foreach (var time in entry.StartTimes.OrderBy(t => t))
            {
                var candidateDateTime = candidate.Add(time);
                if (candidateDateTime > now)
                    return (candidate, time);
            }
        }

        // All scheduled times are in the past this week — wrap to first slot next week
        for (int dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            var candidate = now.Date.AddDays(7 + dayOffset);
            var entry = settings.Schedule.FirstOrDefault(e => e.Day == candidate.DayOfWeek);
            if (entry == null || entry.StartTimes.Count == 0)
                continue;

            var time = entry.StartTimes.OrderBy(t => t).First();
            return (candidate, time);
        }

        return null;
    }
}
