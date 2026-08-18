using System;

namespace Jellyfin.Plugin.AutoParentalTags.Models;

/// <summary>
/// Describes one audience-tag change made by the plugin or an administrator.
/// </summary>
public sealed class TagHistoryEntry
{
    /// <summary>Gets or sets the Jellyfin item identifier.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the item title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the media type.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the production year.</summary>
    public int? ProductionYear { get; set; }

    /// <summary>Gets or sets the audience tag that was replaced.</summary>
    public string? PreviousTag { get; set; }

    /// <summary>Gets or sets the audience tag that was applied.</summary>
    public string NewTag { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this was an administrator override.</summary>
    public bool IsManualOverride { get; set; }

    /// <summary>Gets or sets the AI provider or manual source.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Gets or sets the model used for an AI classification.</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets when the change occurred in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; set; }
}
