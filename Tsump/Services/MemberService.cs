using Tsump.Models;

namespace Tsump.Services;

public class MemberService
{
    private const string StorageKey = "tsump_members";
    private readonly LocalStorageService _storage;

    public MemberService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public async Task<List<Member>> GetAllAsync()
    {
        return await _storage.GetAsync<List<Member>>(StorageKey) ?? new List<Member>();
    }

    public async Task<Member?> GetByIdAsync(Guid id)
    {
        var members = await GetAllAsync();
        return members.FirstOrDefault(m => m.Id == id);
    }

    public async Task AddAsync(Member member)
    {
        var members = await GetAllAsync();
        members.Add(member);
        await _storage.SetAsync(StorageKey, members);
    }

    public async Task UpdateAsync(Member member)
    {
        var members = await GetAllAsync();
        var index = members.FindIndex(m => m.Id == member.Id);
        if (index >= 0)
        {
            members[index] = member;
            await _storage.SetAsync(StorageKey, members);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var members = await GetAllAsync();
        members.RemoveAll(m => m.Id == id);
        await _storage.SetAsync(StorageKey, members);
    }
}
