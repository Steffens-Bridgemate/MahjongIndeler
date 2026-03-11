using Tsump.Models;

namespace Tsump.Services;

public class SessionService
{
    private const string StorageKey = "tsump_sessions";
    private readonly LocalStorageService _storage;

    public SessionService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public async Task<List<WeeklySession>> GetAllAsync()
    {
        return await _storage.GetAsync<List<WeeklySession>>(StorageKey) ?? new List<WeeklySession>();
    }

    public async Task<WeeklySession?> GetByIdAsync(Guid id)
    {
        var sessions = await GetAllAsync();
        return sessions.FirstOrDefault(s => s.Id == id);
    }

    public async Task SaveAsync(WeeklySession session)
    {
        var sessions = await GetAllAsync();
        var index = sessions.FindIndex(s => s.Id == session.Id);
        if (index >= 0)
            sessions[index] = session;
        else
            sessions.Add(session);
        await _storage.SetAsync(StorageKey, sessions);
    }

    public async Task DeleteAsync(Guid id)
    {
        var sessions = await GetAllAsync();
        sessions.RemoveAll(s => s.Id == id);
        await _storage.SetAsync(StorageKey, sessions);
    }
}
