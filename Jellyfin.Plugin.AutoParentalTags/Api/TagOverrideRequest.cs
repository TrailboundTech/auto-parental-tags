namespace Jellyfin.Plugin.AutoParentalTags.Api;

/// <summary>Request to replace one item's audience tag.</summary>
public sealed class TagOverrideRequest
{
    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the selected audience tag.</summary>
    public string Tag { get; set; } = string.Empty;
}
