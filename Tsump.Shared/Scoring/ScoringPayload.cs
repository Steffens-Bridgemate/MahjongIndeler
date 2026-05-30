using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;

namespace Tsump.Scoring;

/// <summary>
/// Outbound payload: organizer → scoring app. Encoded into the URL fragment of a /score link.
/// ContextId identifies the container the result belongs to: a Hanchan.Id for weekly sessions
/// or a TournamentSession.Id for tournaments. Resolvers on the organizer side disambiguate.
/// PlayerIds intentionally absent — the scoring app shows players in PlayerNames order and
/// the result comes back in that same order (see <see cref="ScoringResult.Scores"/>).
/// Encoded to a compact binary form (see <see cref="ScoringPayloadCodec"/>) to keep the QR small
/// for hardware scanners; the short JsonPropertyName names are retained only as field documentation.
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

    /// <summary>Opening marker wrapped around the URL encoded into the result QR. Lets
    /// the organizer's scan-input handler detect a complete framed scan without relying
    /// on scanner-specific terminator keystrokes, and recognise front-truncation when
    /// the closing marker arrives but the opening one is missing. Neither `[` nor `]`
    /// appears in our URLs (base64url uses [A-Za-z0-9_-]; URL schemes use :/.#=).</summary>
    public const string ScanFrameStart = "[[";

    /// <summary>Closing marker — paired with <see cref="ScanFrameStart"/>.</summary>
    public const string ScanFrameEnd = "]]";

    // ── Binary wire format ──────────────────────────────────────────────────────────────────
    // Payload = Base64Url( formatByte ++ body ), where body is a hand-packed binary buffer
    // (16-byte Guid, zig-zag varints, length-prefixed UTF-8) that is optionally Deflate-
    // compressed. The format byte's low nibble is the payload type; bit 0x10 marks a compressed
    // body. Binary packing — not the old minified JSON — is what keeps the QR small: the Guid
    // drops from a 36-char string to 16 bytes and all JSON syntax/field-names disappear.
    // Compression is then applied only when it actually shrinks the body (it helps the text-heavy
    // invite; the dense result is left raw), and the format byte records which was used.
    private const int TypeInvite = 1;
    private const int TypeResult = 2;
    private const int CompressedFlag = 0x10;

    public static string EncodeInvite(ScoringInvite invite) => Encode(TypeInvite, PackInvite(invite));
    public static string EncodeResult(ScoringResult result) => Encode(TypeResult, PackResult(result));

    public static ScoringInvite? DecodeInvite(string payload)
    {
        var body = TryDecodeBody(payload, TypeInvite);
        if (body == null) return null;
        try { return UnpackInvite(body); } catch { return null; }
    }

    public static ScoringResult? DecodeResult(string payload)
    {
        var body = TryDecodeBody(payload, TypeResult);
        if (body == null) return null;
        try { return UnpackResult(body); } catch { return null; }
    }

    private static string Encode(int type, byte[] packed)
    {
        var deflated = Deflate(packed);
        var compressed = deflated.Length < packed.Length;
        var body = compressed ? deflated : packed;
        var buf = new byte[body.Length + 1];
        buf[0] = (byte)(type | (compressed ? CompressedFlag : 0));
        Buffer.BlockCopy(body, 0, buf, 1, body.Length);
        return Base64UrlEncode(buf);
    }

    private static byte[]? TryDecodeBody(string payload, int expectedType)
    {
        try
        {
            var buf = Base64UrlDecode(payload);
            if (buf.Length < 1 || (buf[0] & 0x0F) != expectedType) return null;
            var body = new byte[buf.Length - 1];
            Buffer.BlockCopy(buf, 1, body, 0, body.Length);
            return (buf[0] & CompressedFlag) != 0 ? Inflate(body) : body;
        }
        catch { return null; }
    }

    // ── Packing ─────────────────────────────────────────────────────────────────────────────

    private static byte[] PackInvite(ScoringInvite i)
    {
        var buf = new List<byte>(64);
        WriteGuid(buf, i.ContextId);
        WriteInt(buf, i.TableNumber);
        WriteInt(buf, i.StartingPoints);
        WriteInt(buf, i.SessionNumber);
        WriteVarint(buf, (ulong)(i.Uma?.Count ?? 0));
        foreach (var u in i.Uma ?? new List<int>()) WriteInt(buf, u);
        WriteVarint(buf, (ulong)(i.PlayerNames?.Count ?? 0));
        foreach (var n in i.PlayerNames ?? new List<string>()) WriteString(buf, n ?? "");
        WriteString(buf, i.Title);
        WriteString(buf, i.OrganizerUrl);
        return buf.ToArray();
    }

    private static ScoringInvite UnpackInvite(byte[] buf)
    {
        var pos = 0;
        var contextId = ReadGuid(buf, ref pos);
        var table = (int)ReadInt(buf, ref pos);
        var startingPoints = (int)ReadInt(buf, ref pos);
        var sessionNumber = (int)ReadInt(buf, ref pos);
        var umaCount = (int)ReadVarint(buf, ref pos);
        var uma = new List<int>(umaCount);
        for (var k = 0; k < umaCount; k++) uma.Add((int)ReadInt(buf, ref pos));
        var nameCount = (int)ReadVarint(buf, ref pos);
        var names = new List<string>(nameCount);
        for (var k = 0; k < nameCount; k++) names.Add(ReadString(buf, ref pos) ?? "");
        var title = ReadString(buf, ref pos);
        var organizerUrl = ReadString(buf, ref pos);
        return new ScoringInvite(contextId, table, names, startingPoints, uma, title, organizerUrl, sessionNumber);
    }

    private static byte[] PackResult(ScoringResult r)
    {
        var buf = new List<byte>(48);
        WriteGuid(buf, r.ContextId);
        WriteInt(buf, r.TableNumber);
        WriteVarint(buf, (ulong)(r.Scores?.Count ?? 0));
        foreach (var s in r.Scores ?? new List<int[]>())
        {
            WriteInt(buf, s.Length > ScoreEndPoints ? s[ScoreEndPoints] : 0);
            WriteInt(buf, s.Length > ScoreLoan ? s[ScoreLoan] : 0);
            WriteInt(buf, s.Length > ScorePenalty ? s[ScorePenalty] : 0);
        }
        return buf.ToArray();
    }

    private static ScoringResult UnpackResult(byte[] buf)
    {
        var pos = 0;
        var contextId = ReadGuid(buf, ref pos);
        var table = (int)ReadInt(buf, ref pos);
        var count = (int)ReadVarint(buf, ref pos);
        var scores = new List<int[]>(count);
        for (var k = 0; k < count; k++)
            scores.Add(new[] { (int)ReadInt(buf, ref pos), (int)ReadInt(buf, ref pos), (int)ReadInt(buf, ref pos) });
        return new ScoringResult(contextId, table, scores);
    }

    // ── Primitive writers/readers ───────────────────────────────────────────────────────────

    private static void WriteGuid(List<byte> buf, Guid g) => buf.AddRange(g.ToByteArray());

    private static Guid ReadGuid(byte[] buf, ref int pos)
    {
        var g = new Guid(buf.AsSpan(pos, 16));
        pos += 16;
        return g;
    }

    private static void WriteString(List<byte> buf, string? s)
    {
        if (s == null) { WriteVarint(buf, 0); return; } // 0 = null; otherwise length n is stored as n+1
        var bytes = Encoding.UTF8.GetBytes(s);
        WriteVarint(buf, (ulong)bytes.Length + 1);
        buf.AddRange(bytes);
    }

    private static string? ReadString(byte[] buf, ref int pos)
    {
        var lenPlusOne = ReadVarint(buf, ref pos);
        if (lenPlusOne == 0) return null;
        var len = (int)(lenPlusOne - 1);
        var s = Encoding.UTF8.GetString(buf, pos, len);
        pos += len;
        return s;
    }

    // Zig-zag varint: maps signed → unsigned so small-magnitude negatives still fit in one byte.
    private static void WriteInt(List<byte> buf, long v) => WriteVarint(buf, ZigZag(v));
    private static long ReadInt(byte[] buf, ref int pos) => UnZigZag(ReadVarint(buf, ref pos));
    private static ulong ZigZag(long v) => (ulong)((v << 1) ^ (v >> 63));
    private static long UnZigZag(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);

    private static void WriteVarint(List<byte> buf, ulong v)
    {
        while (v >= 0x80) { buf.Add((byte)(v | 0x80)); v >>= 7; }
        buf.Add((byte)v);
    }

    private static ulong ReadVarint(byte[] buf, ref int pos)
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = buf[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
    }

    // ── Deflate (raw, no zlib/gzip header) + Base64Url over bytes ────────────────────────────

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            ds.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var ds = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        ds.CopyTo(output);
        return output.ToArray();
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
