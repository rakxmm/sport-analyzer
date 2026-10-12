using System.ComponentModel.DataAnnotations;

namespace FootballApp.Infrastructure.Storage;

/// <summary>
/// Cloudflare R2 connection settings (section "R2"). Keys go in user-secrets / env variables, never in
/// appsettings. Use the EU jurisdiction endpoint (https://&lt;account-id&gt;.eu.r2.cloudflarestorage.com) — NFR-1.
/// </summary>
public class R2Options
{
    public const string SectionName = "R2";

    [Required, Url]
    public string ServiceUrl { get; set; } = string.Empty;

    [Required]
    public string AccessKeyId { get; set; } = string.Empty;

    [Required]
    public string SecretAccessKey { get; set; } = string.Empty;

    [Required]
    public string BucketName { get; set; } = string.Empty;
}
