using Tsump.Models;

namespace Tsump.Services;

public class TournamentService
{
    private const string StorageKey = "tsump_tournaments";
    private readonly LocalStorageService _storage;

    public TournamentService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public async Task<List<Tournament>> GetAllAsync()
    {
        return await _storage.GetAsync<List<Tournament>>(StorageKey) ?? new List<Tournament>();
    }

    public async Task<Tournament?> GetByIdAsync(Guid id)
    {
        var tournaments = await GetAllAsync();
        return tournaments.FirstOrDefault(t => t.Id == id);
    }

    public async Task SaveAsync(Tournament tournament)
    {
        var tournaments = await GetAllAsync();
        var index = tournaments.FindIndex(t => t.Id == tournament.Id);
        if (index >= 0)
            tournaments[index] = tournament;
        else
            tournaments.Add(tournament);
        await _storage.SetAsync(StorageKey, tournaments);
    }

    public async Task DeleteAsync(Guid id)
    {
        var tournaments = await GetAllAsync();
        tournaments.RemoveAll(t => t.Id == id);
        await _storage.SetAsync(StorageKey, tournaments);
    }
}
