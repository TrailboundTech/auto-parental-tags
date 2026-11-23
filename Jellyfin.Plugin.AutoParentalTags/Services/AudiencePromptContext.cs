using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// Shared prompt fragments used by every AI provider.
/// </summary>
public static class AudiencePromptContext
{
    /// <summary>Maximum number of studios included in a prompt.</summary>
    public const int MaxStudios = 4;

    /// <summary>Maximum number of keywords included in a prompt.</summary>
    public const int MaxKeywords = 12;

    /// <summary>
    /// Guidance that balances the "family-safe titles may target adults" example.
    /// Without it, models over-assign "adults" to family films and sitcoms.
    /// </summary>
    public const string BalanceGuidance =
        "- A family film, sitcom, or cartoon made for parents to watch with their children targets kids or teens, not adults.\n"
        + "- Choose adults only when the title is made mainly for adult viewers, not merely because adults also enjoy it.";

    /// <summary>
    /// Formats the rating line value, preferring Jellyfin's rating and falling back to a looked-up one.
    /// </summary>
    /// <param name="request">The classification request.</param>
    /// <returns>The rating text.</returns>
    public static string FormatRating(AudienceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.OfficialRating))
        {
            return request.OfficialRating;
        }

        if (!string.IsNullOrWhiteSpace(request.LookedUpRating))
        {
            return string.IsNullOrWhiteSpace(request.LookedUpRatingSource)
                ? request.LookedUpRating
                : $"{request.LookedUpRating} ({request.LookedUpRatingSource})";
        }

        return "Not Rated";
    }

    /// <summary>
    /// Formats optional tagline, studio and keyword lines (each preceded by a newline).
    /// </summary>
    /// <param name="request">The classification request.</param>
    /// <returns>Additional metadata lines, or an empty string.</returns>
    public static string FormatAdditionalContext(AudienceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.Tagline))
        {
            builder.Append("\nTagline: ").Append(OneLine(request.Tagline));
        }

        var studios = Clean(request.Studios, MaxStudios);
        if (studios.Count > 0)
        {
            builder.Append("\nStudios/Networks: ").Append(string.Join(", ", studios));
        }

        var keywords = Clean(request.Keywords, MaxKeywords);
        if (keywords.Count > 0)
        {
            builder.Append("\nKeywords: ").Append(string.Join(", ", keywords));
        }

        return builder.ToString();
    }

    private static List<string> Clean(IReadOnlyList<string>? values, int max)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(OneLine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList()
            ?? new List<string>();
    }

    private static string OneLine(string value)
    {
        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
