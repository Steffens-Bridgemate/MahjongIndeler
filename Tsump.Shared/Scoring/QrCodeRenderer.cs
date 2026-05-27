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
    /// Caller supplies an SVG fragment for the glyph rendered against a 16x16 viewBox; the
    /// wrapping &lt;g&gt; sets fill to white. Kept small (~15% of QR diameter) so hardware
    /// 2D scanners — whose embedded decoders cope much worse with burst-error occlusion than
    /// phone-based decoders — still decode reliably. Larger overlays look better but failed
    /// on the user's USB HID scanner even though phones read them fine.</summary>
    public sealed record CenterOverlay(string GlyphSvg, string BackgroundColor, double RelativeSize = 0.15);

    public static class Overlays
    {
        // Blue circle + white clipboard. "This QR opens the scoring page — fill the scores in."
        // Glyph: Bootstrap Icons bi-clipboard-fill (MIT licence). Two filled paths, no stroke.
        public static readonly CenterOverlay Organizer = new(
            GlyphSvg: "<path fill-rule=\"evenodd\" d=\"M10 1.5a.5.5 0 0 0-.5-.5h-3a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h3a.5.5 0 0 0 .5-.5v-1z\"/><path d=\"M4.085 1H3.5A1.5 1.5 0 0 0 2 2.5v12A1.5 1.5 0 0 0 3.5 16h9a1.5 1.5 0 0 0 1.5-1.5v-12A1.5 1.5 0 0 0 12.5 1h-.585c.055.156.085.325.085.5V2a1.5 1.5 0 0 1-1.5 1.5h-5A1.5 1.5 0 0 1 4 2v-.5c0-.175.03-.344.085-.5z\"/>",
            BackgroundColor: "#0d6efd");

        // Green circle + white check. "This QR carries completed scores back to the organizer."
        // Glyph: Bootstrap Icons bi-check-lg (MIT licence). Filled-only, rounded arc joins —
        // no stroke at all, so no rasterisation surprises and no miter spikes that could
        // leak out of the badge and corrupt the surrounding QR modules.
        public static readonly CenterOverlay ScoringResult = new(
            GlyphSvg: "<path d=\"M12.736 3.97a.733.733 0 0 1 1.047 0c.286.289.29.756.01 1.05L7.88 12.01a.733.733 0 0 1-1.066.02L3.217 8.384a.757.757 0 1 1 1.06-1.06l3.052 3.093 5.4-6.425z\"/>",
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
        // which encode each QR module and need their dimensions intact). Add a viewBox in
        // their place so SVG consumers that need intrinsic dimensions (e.g. a canvas
        // rasteriser loading the SVG via an Image) can still tell the coordinate space —
        // without a viewBox the browser falls back to 300x150 and clips anything past that.
        var cleaned = Regex.Replace(openTag.Value, @"\s+(width|height)=""[^""]*""", "", RegexOptions.IgnoreCase);
        if (qrPixelSize > 0 && !cleaned.Contains("viewBox", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Insert(cleaned.Length - 1, $" viewBox=\"0 0 {qrPixelSize} {qrPixelSize}\"");
        }
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
        // Glyph fills 80% of the badge diameter — bumped from 70% to keep icons recognisable
        // now that the badge itself is small. Still leaves a thin coloured rim around the
        // glyph so the icon shape reads cleanly.
        var glyphBoxSize = overlaySize * 0.8;
        var glyphScale = glyphBoxSize / 16.0;
        var glyphOffset = center - glyphBoxSize / 2.0;

        // No white separator ring: every pixel we cover counts toward the QR's burst-error
        // budget, so we keep the occluded area as small as possible. The coloured circle
        // alone is contrast enough against the QR modules.
        var overlaySvg =
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
