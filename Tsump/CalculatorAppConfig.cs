namespace Tsump;

/// <summary>
/// Build-time configuration for the standalone Riichi calculator app.
/// The organizer's nav "Riichi calculator" item launches this deployment in its own browser
/// window (a separate window the OS manages) rather than embedding a draggable in-app overlay.
/// Hard-coded for the same reason as <see cref="Tsump.Scoring.ScoringAppConfig"/>: PWA-installed
/// organizer instances hide the address bar, so a user-typed URL is impractical.
/// Edit the constant below and redeploy to point at a different calculator deployment.
/// </summary>
public static class CalculatorAppConfig
{
    /// <summary>Public URL of the deployed standalone calculator app (trailing slash so the PWA base resolves).</summary>
    public const string DeployedUrl = "https://steffens-bridgemate.github.io/MahjongRiichiCalc/";
}
