namespace RevitAI.Models;

/// <summary>
/// Immutable settings for a single Claude API request.
/// Use with expressions to create modified copies for per-request overrides.
/// </summary>
/// <example>
/// var customSettings = ApiSettings.Default with { Temperature = 0.9 };
/// </example>
public sealed record ApiSettings
{
    /// <summary>
    /// The Claude model to use for the request.
    /// </summary>
    public string Model { get; init; } = "claude-sonnet-4-5-20250929";

    /// <summary>
    /// Temperature controls randomness. Lower values (0.0-0.3) are more deterministic,
    /// higher values (0.7-1.0) are more creative.
    /// </summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>
    /// Maximum number of tokens to generate in the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Gets the default API settings.
    /// </summary>
    public static ApiSettings Default => new();
}
