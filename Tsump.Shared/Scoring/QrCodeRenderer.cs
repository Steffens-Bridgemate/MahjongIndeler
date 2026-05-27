using System.Text.RegularExpressions;
using QRCoder;

namespace Tsump.Scoring;

/// <summary>
/// Generates the SVG markup for a QR code with the root &lt;svg&gt; tag's hard-coded
/// width/height attributes replaced by a <c>viewBox</c> so CSS-sized inline rendering and
/// canvas rasterisation both work. The QR itself is pristine — the organizer/scoring
/// distinguishing badge lives in the modal header and the PNG's title band, not on the QR
/// (overlay attempts proved unreliable on hardware 2D scanners).
/// </summary>
public static class QrCodeRenderer
{
    /// <summary>Visual identity of which side of the round-trip a QR comes from. Used by
    /// <see cref="Tsump.Shared"/>'s QrCodeModal to render a small icon in the modal header
    /// and by the JS clipboard helper to colour the PNG's title band. No longer painted on
    /// the QR itself — hardware decoders couldn't tolerate it.</summary>
    public sealed record CenterOverlay(string GlyphSvg, string BackgroundColor);

    public static class Overlays
    {
        // Blue clipboard. "This QR opens the scoring page — fill the scores in."
        public static readonly CenterOverlay Organizer = new(
            GlyphSvg: "<path fill-rule=\"evenodd\" d=\"M10 1.5a.5.5 0 0 0-.5-.5h-3a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h3a.5.5 0 0 0 .5-.5v-1z\"/><path d=\"M4.085 1H3.5A1.5 1.5 0 0 0 2 2.5v12A1.5 1.5 0 0 0 3.5 16h9a1.5 1.5 0 0 0 1.5-1.5v-12A1.5 1.5 0 0 0 12.5 1h-.585c.055.156.085.325.085.5V2a1.5 1.5 0 0 1-1.5 1.5h-5A1.5 1.5 0 0 1 4 2v-.5c0-.175.03-.344.085-.5z\"/>",
            BackgroundColor: "#0d6efd");

        // Green check. "This QR carries completed scores back to the organizer."
        public static readonly CenterOverlay ScoringResult = new(
            GlyphSvg: "<path d=\"M12.736 3.97a.733.733 0 0 1 1.047 0c.286.289.29.756.01 1.05L7.88 12.01a.733.733 0 0 1-1.066.02L3.217 8.384a.757.757 0 1 1 1.06-1.06l3.052 3.093 5.4-6.425z\"/>",
            BackgroundColor: "#198754");
    }

    public static string ToSvg(string url, int pixelsPerModule = 4)
    {
        using var gen = new QRCodeGenerator();
        // Level H kept (carried over from the centre-overlay era). Larger QR, but each
        // module is bigger relative to the badge — friendlier for hardware decoders.
        var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        var renderer = new SvgQRCode(data);
        var rawSvg = renderer.GetGraphic(pixelsPerModule);

        var openTag = Regex.Match(rawSvg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
        if (!openTag.Success) return rawSvg;

        // Read the QR's pixel width before stripping it — needed for the viewBox.
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
        return rawSvg.Substring(0, openTag.Index)
               + cleaned
               + rawSvg.Substring(openTag.Index + openTag.Length);
    }
}
