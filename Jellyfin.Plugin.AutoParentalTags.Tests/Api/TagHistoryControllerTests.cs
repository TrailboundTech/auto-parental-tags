using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoParentalTags.Api;
using Jellyfin.Plugin.AutoParentalTags.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AutoParentalTags.Tests.Api;

/// <summary>Tests administrator audience-tag overrides.</summary>
public sealed class TagHistoryControllerTests : IDisposable
{
    private readonly string _historyPath = Path.Combine(
        Path.GetTempPath(),
        "auto-parental-tags-tests",
        Guid.NewGuid().ToString("N"),
        "tag-history.json");

    /// <summary>Rejects malformed item identifiers.</summary>
    [Fact]
    public async Task OverrideTag_WithInvalidItemId_ShouldReturnBadRequest()
    {
        using var history = CreateHistory();
        var controller = CreateController(new Mock<ILibraryManager>(), history);

        var result = await controller.OverrideTag(
            new TagOverrideRequest { ItemId = "invalid", Tag = "kids" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>Rejects unsupported audience tags.</summary>
    [Fact]
    public async Task OverrideTag_WithInvalidTag_ShouldReturnBadRequest()
    {
        using var history = CreateHistory();
        var controller = CreateController(new Mock<ILibraryManager>(), history);

        var result = await controller.OverrideTag(
            new TagOverrideRequest { ItemId = Guid.NewGuid().ToString(), Tag = "everyone" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>Returns not found when the Jellyfin item no longer exists.</summary>
    [Fact]
    public async Task OverrideTag_WithMissingItem_ShouldReturnNotFound()
    {
        using var history = CreateHistory();
        var library = new Mock<ILibraryManager>();
        var itemId = Guid.NewGuid();
        library.Setup(manager => manager.GetItemById(itemId)).Returns((MediaBrowser.Controller.Entities.BaseItem?)null);
        var controller = CreateController(library, history);

        var result = await controller.OverrideTag(
            new TagOverrideRequest { ItemId = itemId.ToString(), Tag = "kids" },
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    /// <summary>Replaces the tag and records a protected manual override.</summary>
    [Fact]
    public async Task OverrideTag_WithValidRequest_ShouldUpdateItemAndHistory()
    {
        using var history = CreateHistory();
        var item = new TestMovie
        {
            Id = Guid.NewGuid(),
            Name = "Example",
            ProductionYear = 2025,
            Tags = new[] { "favorite", "adults" }
        };
        var library = new Mock<ILibraryManager>();
        library.Setup(manager => manager.GetItemById(item.Id)).Returns(item);
        var controller = CreateController(library, history);

        var result = await controller.OverrideTag(
            new TagOverrideRequest { ItemId = item.Id.ToString(), Tag = " KIDS " },
            CancellationToken.None);
        var entries = await controller.GetHistory(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Contains("favorite", item.Tags);
        Assert.Contains("kids", item.Tags);
        Assert.DoesNotContain("adults", item.Tags);
        Assert.Single(entries);
        Assert.Equal("adults", entries[0].PreviousTag);
        Assert.True(entries[0].IsManualOverride);
        Assert.True(await history.HasManualOverrideAsync(item.Id));
    }

    /// <summary>Removes temporary test data.</summary>
    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_historyPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private TagHistoryService CreateHistory()
        => new(NullLogger<TagHistoryService>.Instance, _historyPath);

    private static TagHistoryController CreateController(Mock<ILibraryManager> library, TagHistoryService history)
        => new(library.Object, history);
}
