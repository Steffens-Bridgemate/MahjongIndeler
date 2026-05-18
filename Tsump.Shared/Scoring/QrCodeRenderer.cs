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
        // Blue circle + white clipboard. "This QR opens the scoring page — fill the scores in."
        // Glyph: Bootstrap Icons bi-clipboard-fill (MIT licence). Two filled paths, no stroke.
        public static readonly CenterOverlay Organizer = new(
            GlyphSvg: "<path fill-rule=\"evenodd\" d=\"M10 1.5a.5.5 0 0 0-.5-.5h-3a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h3a.5.5 0 0 0 .5-.5v-1z\"/><path d=\"M4.085 1H3.5A1.5 1.5 0 0 0 2 2.5v12A1.5 1.5 0 0 0 3.5 16h9a1.5 1.5 0 0 0 1.5-1.5v-12A1.5 1.5 0 0 0 12.5 1h-.585c.055.156.085.325.085.5V2a1.5 1.5 0 0 1-1.5 1.5h-5A1.5 1.5 0 0 1 4 2v-.5c0-.175.03-.344.085-.5z\"/>",
            BackgroundColor: "#0d6efd");

        // Green circle + white check. "This QR carries completed scores back to the organizer."
        // Drawn as a 3-point polyline with rounded joins/caps so the stroke can't miter-spike
        // out of the badge — the earlier filled bi-check glyph's stroked sharp corners broke
        // QR decode by extending into the surrounding modules.
        public static readonly CenterOverlay ScoringResult = new(
            GlyphSvg: "<path d=\"M4 8.5 L7 11.5 L12.5 5\" fill=\"none\" stroke=\"white\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
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
            $"scale({glyphScale.ToString(inv)})\" fill=\"white\">{overlay.GlyphSvg}</g>";

        var closeIdx = svg.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        return closeIdx > 0
            ? svg.Substring(0, closeIdx) + overlaySvg + svg.Substring(closeIdx)
            : svg + overlaySvg;
    }
}
