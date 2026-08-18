using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoParentalTags.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// Persists a human-readable audit history of audience-tag changes.
/// </summary>
public sealed class TagHistoryService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<TagHistoryService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string? _historyPathOverride;

    /// <summary>Initializes a new instance of the <see cref="TagHistoryService"/> class.</summary>
    /// <param name="logger">Logger used for persistence errors.</param>
    public TagHistoryService(ILogger<TagHistoryService> logger)
        : this(logger, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TagHistoryService"/> class with an explicit path.</summary>
    /// <param name="logger">Logger used for persistence errors.</param>
    /// <param name="historyPath">Optional explicit history file path.</param>
    public TagHistoryService(ILogger<TagHistoryService> logger, string? historyPath)
    {
        _logger = logger;
        _historyPathOverride = historyPath;
    }

    /// <summary>Gets tag changes, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recorded tag changes.</returns>
    public async Task<IReadOnlyList<TagHistoryEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadEntriesUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .OrderByDescending(entry => entry.TimestampUtc)
                .ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>Records a tag change.</summary>
    /// <param name="entry">The tag change to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the write operation.</returns>
    public async Task RecordAsync(TagHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadEntriesUnsafeAsync(cancellationToken).ConfigureAwait(false);
            entries.Add(entry);
            await WriteEntriesUnsafeAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Returns true when the most recent entry for an item is a manual override.
    /// </summary>
    /// <param name="itemId">Jellyfin item identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the item is protected by a manual override.</returns>
    public async Task<bool> HasManualOverrideAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var entries = await GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault(entry => entry.ItemId == itemId)?.IsManualOverride == true;
    }

    private string GetHistoryPath()
    {
        if (!string.IsNullOrWhiteSpace(_historyPathOverride))
        {
            return _historyPathOverride;
        }

        var dataFolder = Plugin.Instance?.DataFolderPath
            ?? throw new InvalidOperationException("Plugin data folder is not available.");

        return Path.Combine(dataFolder, "tag-history.json");
    }

    private async Task<List<TagHistoryEntry>> ReadEntriesUnsafeAsync(CancellationToken cancellationToken)
    {
        var path = GetHistoryPath();
        if (!File.Exists(path))
        {
            return new List<TagHistoryEntry>();
        }

        try
        {
            using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<TagHistoryEntry>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new List<TagHistoryEntry>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Unable to read Auto Parental Tags history from {Path}", path);
            return new List<TagHistoryEntry>();
        }
    }

    private async Task WriteEntriesUnsafeAsync(List<TagHistoryEntry> entries, CancellationToken cancellationToken)
    {
        var path = GetHistoryPath();
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Tag history path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _fileLock.Dispose();
    }
}
