using System.Collections.Generic;

namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// Metadata sent to an AI provider for a target-audience classification.
/// </summary>
/// <param name="MediaType">Movie or TV series label.</param>
/// <param name="Title">Item title.</param>
/// <param name="Year">Release or premiere year.</param>
/// <param name="Overview">Item synopsis.</param>
/// <param name="OfficialRating">Official content rating from Jellyfin metadata.</param>
/// <param name="Genres">Item genres.</param>
public sealed record AudienceRequest(
    string MediaType,
    string Title,
    int? Year,
    string? Overview,
    string? OfficialRating,
    IReadOnlyList<string>? Genres)
{
    /// <summary>
    /// Gets a content rating found by an external lookup when Jellyfin has none,
    /// for example "12A".
    /// </summary>
    public string? LookedUpRating { get; init; }

    /// <summary>
    /// Gets where <see cref="LookedUpRating"/> came from, for example "TMDb, GB".
    /// </summary>
    public string? LookedUpRatingSource { get; init; }

    /// <summary>Gets the item tagline.</summary>
    public string? Tagline { get; init; }

    /// <summary>Gets the production studios or networks.</summary>
    public IReadOnlyList<string>? Studios { get; init; }

    /// <summary>Gets descriptive keywords, excluding audience tags.</summary>
    public IReadOnlyList<string>? Keywords { get; init; }
}
