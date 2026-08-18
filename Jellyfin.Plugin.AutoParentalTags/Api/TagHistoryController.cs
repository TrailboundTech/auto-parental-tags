using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoParentalTags.Models;
using Jellyfin.Plugin.AutoParentalTags.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AutoParentalTags.Api;

/// <summary>Exposes tag history and administrator overrides.</summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AutoParentalTags/History")]
public sealed class TagHistoryController : ControllerBase
{
    private static readonly HashSet<string> AudienceTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "kids", "teens", "adults"
    };

    private readonly ILibraryManager _libraryManager;
    private readonly TagHistoryService _historyService;

    /// <summary>Initializes a new instance of the <see cref="TagHistoryController"/> class.</summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="historyService">Tag-history persistence service.</param>
    public TagHistoryController(ILibraryManager libraryManager, TagHistoryService historyService)
    {
        _libraryManager = libraryManager;
        _historyService = historyService;
    }

    /// <summary>Gets all recorded tag changes, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recorded tag changes.</returns>
    [HttpGet]
    public Task<IReadOnlyList<TagHistoryEntry>> GetHistory(CancellationToken cancellationToken)
        => _historyService.GetEntriesAsync(cancellationToken);

    /// <summary>Applies an administrator-selected audience tag.</summary>
    /// <param name="request">Requested item and audience tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An HTTP result describing the operation.</returns>
    [HttpPost("Override")]
    public async Task<IActionResult> OverrideTag([FromBody] TagOverrideRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.ItemId, out var itemId))
        {
            return BadRequest("A valid Jellyfin item ID is required.");
        }

        var newTag = request.Tag?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(newTag) || !AudienceTags.Contains(newTag))
        {
            return BadRequest("Tag must be kids, teens, or adults.");
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var tags = item.Tags?.ToList() ?? new List<string>();
        var previousTag = tags.FirstOrDefault(tag => AudienceTags.Contains(tag));
        tags.RemoveAll(tag => AudienceTags.Contains(tag));
        tags.Add(newTag);
        item.Tags = tags.ToArray();

        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        await _historyService.RecordAsync(
            new TagHistoryEntry
            {
                ItemId = item.Id,
                Title = item.Name,
                MediaType = item.GetType().Name,
                ProductionYear = item.ProductionYear,
                PreviousTag = previousTag,
                NewTag = newTag,
                IsManualOverride = true,
                Source = "Manual override",
                TimestampUtc = DateTimeOffset.UtcNow
            },
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
