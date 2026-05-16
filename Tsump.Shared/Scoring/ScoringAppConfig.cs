namespace Tsump.Scoring;

/// <summary>
/// Build-time configuration for the scoring app integration.
/// Hard-coded rather than user-configurable because PWA-installed organizer instances
/// hide the address bar, so requiring users to type a URL is impractical.
/// Edit the constant below and redeploy to point at a different scoring app deployment.
/// </summary>
public static class ScoringAppConfig
{
    /// <summary>
    /// Public URL of the deployed Tsump.Scoring app (no trailing slash).
    /// Replace this with the production URL before publishing the organizer app.
    /// </summary>
    public const string DeployedUrl = "https://steffens-bridgemate.github.io/MahjongIndeler/scoring";
}
