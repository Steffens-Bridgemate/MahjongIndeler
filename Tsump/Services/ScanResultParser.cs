using System.Text.RegularExpressions;
using Tsump.Scoring;

namespace Tsump.Services;

/// <summary>
/// Turns a raw captured string (HID scan, paste, clipboard, camera/file decode) into a
/// <see cref="ScoringResult"/>. Shared by the import panel and the scan-log page so both
/// interpret captures identically. Mirrors the unwrap/extract/decode steps:
///   1. strip the optional `[[ … ]]` scan-frame markers
///   2. URL-unescape (handles %23 etc.)
///   3. flag scoring-invite URLs (`#p=` / `?p=`) — those are not results
///   4. pull the base64url payload from `r=<payload>`, then a garbled-prefix fallback
///      (`=<long-payload>`), then fall back to the whole string
///   5. decode and sanity-check the score-row count (3 or 4)
/// </summary>
public static class ScanResultParser
{
    private static readonly Regex ResultPayloadRegex =
        new(@"r=([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

    // Fallback for HID-scanned URLs where keyboard-layout differences garble the `#r=` prefix
    // while leaving the payload intact. Any `=<base64url>` run long enough to rule out short
    // query values (ScoringResult payloads are well above 20 chars).
    private static readonly Regex FallbackPayloadRegex =
        new(@"=([A-Za-z0-9_\-]{20,})", RegexOptions.Compiled);

    /// <summary>
    /// Parses <paramref name="text"/> into a result. Returns null when it isn't a usable result.
    /// <paramref name="looksLikeInvite"/> is set when the text is a scoring-invite link (so the
    /// caller can show "this is an invite, not a result" rather than a generic failure).
    /// </summary>
    public static ScoringResult? TryParse(string? text, out bool looksLikeInvite)
    {
        looksLikeInvite = false;

        var raw = (text ?? "").Trim();
        if (raw.StartsWith(ScoringPayloadCodec.ScanFrameStart))
            raw = raw.Substring(ScoringPayloadCodec.ScanFrameStart.Length);
        if (raw.EndsWith(ScoringPayloadCodec.ScanFrameEnd))
            raw = raw.Substring(0, raw.Length - ScoringPayloadCodec.ScanFrameEnd.Length);
        raw = raw.Trim();
        if (raw.Length == 0) return null;

        var decoded = Uri.UnescapeDataString(raw);

        if (decoded.Contains("#p=", StringComparison.Ordinal) || decoded.Contains("?p=", StringComparison.Ordinal))
        {
            looksLikeInvite = true;
            return null;
        }

        var match = ResultPayloadRegex.Match(decoded);
        string payload;
        if (match.Success)
        {
            payload = match.Groups[1].Value;
        }
        else
        {
            var fb = FallbackPayloadRegex.Match(decoded);
            payload = fb.Success ? fb.Groups[1].Value : decoded;
        }

        var parsed = ScoringPayloadCodec.DecodeResult(payload);
        if (parsed?.Scores == null || parsed.Scores.Count < 3 || parsed.Scores.Count > 4)
            return null;

        return parsed;
    }
}
