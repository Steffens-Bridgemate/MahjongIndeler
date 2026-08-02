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
        var tournaments = await _storage.GetAsync<List<Tournament>>(StorageKey) ?? new List<Tournament>();

        // Backfill TournamentSession.Id for data persisted before the field existed.
        // One-shot migration: when any session has Guid.Empty, assign new Guids and re-save.
        bool dirty = false;
        foreach (var t in tournaments)
        {
            foreach (var s in t.Sessions)
            {
                if (s.Id == Guid.Empty)
                {
                    s.Id = Guid.NewGuid();
                    dirty = true;
                }
            }
        }
        if (dirty)
            await _storage.SetAsync(StorageKey, tournaments);

        return tournaments;
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

    /// <summary>Wipes every stored tournament. Used when a new competition round starts
    /// (see <see cref="ClubDataService"/>).</summary>
    public async Task DeleteAllAsync()
    {
        await _storage.RemoveAsync(StorageKey);
    }
}
