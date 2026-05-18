using Tsump.Models;
using Tsump.Scoring;

namespace Tsump.Services;

/// <summary>
/// Builds the share-to-scoring-app URL and clipboard-friendly message for a given table.
/// Context-agnostic: works for weekly hanchans and tournament sessions alike — the caller
/// passes whichever <c>ContextId</c> (Hanchan.Id or TournamentSession.Id) the result should
/// round-trip back to.
/// </summary>
public class ScoreInviteService
{
    private readonly LanguageService _lang;

    public ScoreInviteService(LanguageService lang)
    {
        _lang = lang;
    }

    /// <summary>Encodes a <see cref="ScoringInvite"/> into a /score URL on the deployed scoring app.</summary>
    public string BuildInviteUrl(
        Guid contextId,
        int sessionNumber,
        TableAssignment table,
        Func<Guid, string> playerNameResolver,
        int startingPoints,
        List<int> uma,
        string title,
        string organizerBaseUrl)
    {
        var names = table.PlayerIds.Select(playerNameResolver).ToList();
        var invite = new ScoringInvite(
            contextId,
            table.TableNumber,
            names,
            new List<Guid>(table.PlayerIds),
            startingPoints,
            new List<int>(uma),
            title,
            organizerBaseUrl.TrimEnd('/'),
            sessionNumber);

        var encoded = ScoringPayloadCodec.EncodeInvite(invite);
        return $"{ScoringAppConfig.DeployedUrl.TrimEnd('/')}/score#p={encoded}";
    }

    /// <summary>Formats the WhatsApp-friendly message that's copied to the clipboard:
    /// title, instruction, and the URL on three lines.</summary>
    public string BuildShareMessage(string title, string url)
        => $"📋 {title}\n{_lang.Get("ShareScoringLinkInstruction")}\n{url}";
}
