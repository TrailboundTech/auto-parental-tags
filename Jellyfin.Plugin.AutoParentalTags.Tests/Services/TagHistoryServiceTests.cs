using System;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoParentalTags.Models;
using Jellyfin.Plugin.AutoParentalTags.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AutoParentalTags.Tests.Services;

/// <summary>Tests durable audience-tag history.</summary>
public sealed class TagHistoryServiceTests : IDisposable
{
    private readonly string _historyPath = Path.Combine(
        Path.GetTempPath(),
        "auto-parental-tags-tests",
        Guid.NewGuid().ToString("N"),
        "tag-history.json");

    /// <summary>Verifies that recorded entries persist and are returned newest first.</summary>
    [Fact]
    public async Task RecordAsync_ShouldPersistEntriesNewestFirst()
    {
        using var service = CreateService();
        var itemId = Guid.NewGuid();

        await service.RecordAsync(CreateEntry(itemId, "kids", false, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await service.RecordAsync(CreateEntry(itemId, "teens", true, DateTimeOffset.UtcNow));

        var entries = await service.GetEntriesAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal("teens", entries[0].NewTag);
        Assert.True(entries[0].IsManualOverride);
        Assert.Contains("\n", await File.ReadAllTextAsync(_historyPath));
    }

    /// <summary>Verifies that the latest manual change protects an item.</summary>
    [Fact]
    public async Task HasManualOverrideAsync_WhenLatestEntryIsManual_ShouldReturnTrue()
    {
        using var service = CreateService();
        var itemId = Guid.NewGuid();
        await service.RecordAsync(CreateEntry(itemId, "adults", true, DateTimeOffset.UtcNow));

        Assert.True(await service.HasManualOverrideAsync(itemId));
        Assert.False(await service.HasManualOverrideAsync(Guid.NewGuid()));
    }

    /// <summary>Verifies that a later AI entry clears manual-override protection.</summary>
    [Fact]
    public async Task HasManualOverrideAsync_WhenLatestEntryIsAi_ShouldReturnFalse()
    {
        using var service = CreateService();
        var itemId = Guid.NewGuid();
        await service.RecordAsync(CreateEntry(itemId, "kids", true, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await service.RecordAsync(CreateEntry(itemId, "teens", false, DateTimeOffset.UtcNow));

        Assert.False(await service.HasManualOverrideAsync(itemId));
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

    private TagHistoryService CreateService()
        => new(NullLogger<TagHistoryService>.Instance, _historyPath);

    private static TagHistoryEntry CreateEntry(Guid itemId, string tag, bool manual, DateTimeOffset timestamp)
        => new()
        {
            ItemId = itemId,
            Title = "Test Movie",
            MediaType = "movie",
            NewTag = tag,
            IsManualOverride = manual,
            Source = manual ? "Manual override" : "LocalAI",
            TimestampUtc = timestamp
        };
}
