using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tsump.Scoring;

/// <summary>
/// Outbound payload: organizer → scoring app. Encoded into the URL fragment of a /score link.
/// ContextId identifies the container the result belongs to: a Hanchan.Id for weekly sessions
/// or a TournamentSession.Id for tournaments. Resolvers on the organizer side disambiguate.
/// PlayerIds intentionally absent — the scoring app shows players in PlayerNames order and
/// the result comes back in that same order (see <see cref="ScoringResult.Scores"/>).
/// Field names minified (c/t/n/p/u/o/sn) to keep the encoded QR small for hardware scanners.
/// </summary>
public record ScoringInvite(
    [property: JsonPropertyName("c")]     Guid ContextId,
    [property: JsonPropertyName("t")]     int TableNumber,
    [property: JsonPropertyName("n")]     List<string> PlayerNames,
    [property: JsonPropertyName("p")]     int StartingPoints,
    [property: JsonPropertyName("u")]     List<int> Uma,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("o")]     string? OrganizerUrl,
    [property: JsonPropertyName("sn")]    int SessionNumber = 1);

/// <summary>
/// Inbound payload: scoring app → organizer. Encoded into the URL fragment of an /import-score link.
/// Each entry in <see cref="Scores"/> is a 3-int array <c>[endPoints, loan, penalty]</c>, indexed
/// by table-player position — the organizer's <c>ScoreImportService.ApplyAsync</c> pairs each
/// entry with the i-th non-virtual <c>PlayerScore</c> on the looked-up table. No PlayerIds in
/// the wire format; no per-player starting points (same for all four); no Uma (recomputed).
/// Use the <c>Score*</c> index constants below to access the fields readably.
/// </summary>
public record ScoringResult(
    [property: JsonPropertyName("c")] Guid ContextId,
    [property: JsonPropertyName("t")] int TableNumber,
    [property: JsonPropertyName("s")] List<int[]> Scores);

public static class ScoringPayloadCodec
{
    /// <summary>Index of the EndPoints value inside each <see cref="ScoringResult.Scores"/> entry.</summary>
    public const int ScoreEndPoints = 0;
    /// <summary>Index of the Loan value inside each <see cref="ScoringResult.Scores"/> entry.</summary>
    public const int ScoreLoan = 1;
    /// <summary>Index of the Penalty value inside each <see cref="ScoringResult.Scores"/> entry.</summary>
    public const int ScorePenalty = 2;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
