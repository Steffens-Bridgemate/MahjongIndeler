using System.Text;
using System.Text.Json;

namespace Tsump.Scoring;

/// <summary>
/// Outbound payload: organizer → scoring app. Encoded into the URL fragment of a /score link.
/// ContextId identifies the container the result belongs to: a Hanchan.Id for weekly sessions
/// or a TournamentSession.Id for tournaments. Resolvers on the organizer side disambiguate.
/// </summary>
public record ScoringInvite(
    Guid ContextId,
    int TableNumber,
    List<string> PlayerNames,
    List<Guid> PlayerIds,
    int StartingPoints,
    List<int> Uma,
    string? Title,
    string? OrganizerUrl,
    int SessionNumber = 1);

/// <summary>
/// Inbound payload: scoring app → organizer. Encoded into the URL fragment of an /import-score link.
/// Uma is intentionally omitted — the organizer recomputes it from settings.
/// </summary>
public record ScoringResult(
    Guid ContextId,
    int TableNumber,
    List<PlayerResultEntry> Scores);

public record PlayerResultEntry(Guid PlayerId, int EndPoints, int Loan, int Penalty);

public static class ScoringPayloadCodec
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string EncodeInvite(ScoringInvite invite)
        => Base64UrlEncode(JsonSerializer.Serialize(invite, JsonOpts));

    public static ScoringInvite? DecodeInvite(string payload)
    {
        try { return JsonSerializer.Deserialize<ScoringInvite>(Base64UrlDecode(payload), JsonOpts); }
        catch { return null; }
    }

    public static string EncodeResult(ScoringResult result)
        => Base64UrlEncode(JsonSerializer.Serialize(result, JsonOpts));

    public static ScoringResult? DecodeResult(string payload)
    {
        try { return JsonSerializer.Deserialize<ScoringResult>(Base64UrlDecode(payload), JsonOpts); }
        catch { return null; }
    }

    private static string Base64UrlEncode(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
