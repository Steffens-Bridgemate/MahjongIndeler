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
        string? title,
        string organizerBaseUrl)
    {
        // Players who dropped out mid-event are skipped: the scoring app then sees an ordinary
        // 3-player table (Mr. X is derived organizer-side) and never learns about the absence.
        var names = table.ActivePlayerIds().Select(playerNameResolver).ToList();
        // PlayerIds intentionally not carried — the scoring app and the organizer both pair
        // players by position (index in `names` == index in table.ActivePlayerIds()).
        var invite = new ScoringInvite(
            contextId,
            table.TableNumber,
            names,
            startingPoints,
            new List<int>(uma),
            title,
            organizerBaseUrl.TrimEnd('/'),
            sessionNumber);

        var encoded = ScoringPayloadCodec.EncodeInvite(invite);
        // Carry the club's primary language as a fragment param so the scoring app shows the same
        // language as the organizer. Not part of the binary payload — just an `&l=` on the fragment.
        return $"{ScoringAppConfig.DeployedUrl.TrimEnd('/')}/score#p={encoded}&l={_lang.PrimaryLanguage}";
    }

    /// <summary>Formats the WhatsApp-friendly message that's copied to the clipboard:
    /// title, instruction, and the URL on three lines.</summary>
    public string BuildShareMessage(string title, string url)
        => $"📋 {title}\n{_lang.Get("ShareScoringLinkInstruction")}\n{url}";

    /// <summary>Invite URL for the table rendered as a (memoised) QR SVG — same URL and
    /// <c>QrCodeRenderer.ToSvg</c> cache the QR modal uses, so a QR already shown for this table is a
    /// cache hit here (and vice-versa). Used to stamp invite QRs onto printed scoresheets.</summary>
    public string BuildInviteQrSvg(
        Guid contextId,
        int sessionNumber,
        TableAssignment table,
        Func<Guid, string> playerNameResolver,
        int startingPoints,
        List<int> uma,
        string? title,
        string organizerBaseUrl,
        int pixelsPerModule = 4)
    {
        var url = BuildInviteUrl(contextId, sessionNumber, table, playerNameResolver,
            startingPoints, uma, title, organizerBaseUrl);
        return QrCodeRenderer.ToSvg(url, pixelsPerModule);
    }
}
