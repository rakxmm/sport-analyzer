namespace FootballApp.Application.Clips;

/// <summary>Configuration section "Clips". Read by the worker, which stamps the expiry when a clip is done.</summary>
public class ClipOptions
{
    public const string SectionName = "Clips";

    /// <summary>How long a share link works after its clip was rendered (FR-15).</summary>
    public TimeSpan ShareLinkLifetime { get; set; } = TimeSpan.FromDays(30);
}
