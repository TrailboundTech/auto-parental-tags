namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// A content rating found by an external lookup.
/// </summary>
/// <param name="Rating">The certification, for example "PG" or "12A".</param>
/// <param name="Source">Where it came from, for example "TMDb, GB".</param>
public sealed record RatingLookupResult(string Rating, string Source);
