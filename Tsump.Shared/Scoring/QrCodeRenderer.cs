using System.Globalization;
using System.Text.RegularExpressions;
using QRCoder;

namespace Tsump.Scoring;

/// <summary>
/// Generates the SVG markup for a QR code, with the root &lt;svg&gt; tag's hard-coded
/// width/height attributes stripped so the consumer (CSS or canvas) can size it. Supports
/// an optional centre badge so QRs from different sides of the round-trip (organizer vs
/// scoring app) can be told apart at a glance.
/// </summary>
public static class QrCodeRenderer
{
    /// <summary>A circular coloured badge plus a white glyph, drawn over the centre of the QR.
    /// Caller supplies an SVG fragment for the glyph rendered against a 16x16 viewBox using
    /// <c>fill="currentColor"</c> (or no fill — the wrapping &lt;g&gt; sets fill to white).</summary>
    public sealed record CenterOverlay(string GlyphSvg, string BackgroundColor, double RelativeSize = 0.22);

    public static class Overlays
    {
        // Blue circle + white pencil. "This QR opens the scoring page so the player can enter scores."
        // Glyph: Bootstrap Icons bi-pencil-fill (MIT licence).
        public static readonly CenterOverlay Organizer = new(
            GlyphSvg: "<path d=\"M12.854.146a.5.5 0 0 0-.707 0L10.5 1.793 14.207 5.5l1.647-1.646a.5.5 0 0 0 0-.708zM11.207 2.5 13.5 4.793 14.793 3.5 12.5 1.207zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.293zm-9.761 5.175-.106.106-1.528 3.821 3.821-1.528.106-.106A.5.5 0 0 1 6 12.5V12h-.5a.5.5 0 0 1-.5-.5V11h-.5a.5.5 0 0 1-.5-.5V10h-.5a.5.5 0 0 1-.5-.5z\"/>",
            BackgroundColor: "#0d6efd");

        // Green circle + white check. "This QR carries completed scores back to the organizer."
        // Glyph: Bootstrap Icons bi-check (MIT licence), scaled up so the tick fills the badge.
        public static readonly CenterOverlay ScoringResult = new(
            GlyphSvg: "<path d=\"M13.854 3.646a.5.5 0 0 1 0 .708l-7 7a.5.5 0 0 1-.708 0l-3.5-3.5a.5.5 0 1 1 .708-.708L6.5 10.293l6.646-6.647a.5.5 0 0 1 .708 0z\" stroke=\"white\" stroke-width=\"1.5\"/>",
            BackgroundColor: "#198754");
    }

    public static string ToSvg(string url, int pixelsPerModule = 4, CenterOverlay? overlay = null)
    {
        using var gen = new QRCodeGenerator();
        // Level H (~30% recovery) — needed so the centre overlay covering ~22% of the QR
        // still leaves enough redundant data for reliable decode.
        var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        var renderer = new SvgQRCode(data);
        var rawSvg = renderer.GetGraphic(pixelsPerModule);

        var openTag = Regex.Match(rawSvg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
        if (!openTag.Success) return rawSvg;

        // Read the QR's pixel width from the original width="…" attribute before we strip
        // it — needed to position and size the centre overlay.
        int qrPixelSize = 0;
        var widthMatch = Regex.Match(openTag.Value, @"width=""(\d+)""");
        if (widthMatch.Success) int.TryParse(widthMatch.Groups[1].Value, out qrPixelSize);

        // Strip width/height from the opening <svg> tag only (not from inner <rect> elements,
        // which encode each QR module and need their dimensions intact).
        var cleaned = Regex.Replace(openTag.Value, @"\s+(width|height)=""[^""]*""", "", RegexOptions.IgnoreCase);
        var svg = rawSvg.Substring(0, openTag.Index)
                  + cleaned
                  + rawSvg.Substring(openTag.Index + openTag.Length);

        if (overlay != null && qrPixelSize > 0)
        {
            svg = InjectOverlay(svg, qrPixelSize, overlay);
        }
        return svg;
    }

    private static string InjectOverlay(string svg, int qrPixelSize, CenterOverlay overlay)
    {
        var inv = CultureInfo.InvariantCulture;
        var center = qrPixelSize / 2.0;
        var overlaySize = qrPixelSize * overlay.RelativeSize;
        var bgRadius = overlaySize / 2.0;
        // A thin white ring around the coloured circle so the badge separates visually from
        // the dense QR modules behind it.
        var ringRadius = bgRadius + qrPixelSize * 0.012;
        // Glyph occupies 70% of the badge diameter so it has breathing room inside the circle.
        var glyphBoxSize = overlaySize * 0.7;
        var glyphScale = glyphBoxSize / 16.0;
        var glyphOffset = center - glyphBoxSize / 2.0;

        var overlaySvg =
            $"<circle cx=\"{center.ToString(inv)}\" cy=\"{center.ToString(inv)}\" " +
            $"r=\"{ringRadius.ToString(inv)}\" fill=\"white\" />" +
            $"<circle cx=\"{center.ToString(inv)}\" cy=\"{center.ToString(inv)}\" " +
            $"r=\"{bgRadius.ToString(inv)}\" fill=\"{overlay.BackgroundColor}\" />" +
            $"<g transform=\"translate({glyphOffset.ToString(inv)},{glyphOffset.ToString(inv)}) " +
            $"scale({glyphScale.ToString(inv)})\" fill=\"white\" stroke=\"white\">{overlay.GlyphSvg}</g>";

        var closeIdx = svg.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        return closeIdx > 0
            ? svg.Substring(0, closeIdx) + overlaySvg + svg.Substring(closeIdx)
            : svg + overlaySvg;
    }
}
