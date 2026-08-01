using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AutoParentalTags.Configuration;
using Jellyfin.Plugin.AutoParentalTags.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoParentalTags;

/// <summary>
/// Monitors library changes and processes movies and TV series.
/// </summary>
public class LibraryMonitor : ILibraryPostScanTask
{
    private static readonly HashSet<string> AudienceTags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "kids",
            "teens",
            "adults"
        };

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryMonitor> _logger;
    private readonly AiServiceFactory _aiServiceFactory;
    private readonly TimeSpan _processingDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMonitor"/> class.
    /// </summary>
    /// <param name="libraryManager">
    /// Instance of the <see cref="ILibraryManager"/> interface.
    /// </param>
    /// <param name="logger">
    /// Instance of the <see cref="ILogger{LibraryMonitor}"/> interface.
    /// </param>
    /// <param name="aiServiceFactory">
    /// Instance of the <see cref="AiServiceFactory"/> class.
    /// </param>
    /// <param name="processingDelay">
    /// Optional delay between processing items.
    /// </param>
    public LibraryMonitor(
        ILibraryManager libraryManager,
        ILogger<LibraryMonitor> logger,
        AiServiceFactory aiServiceFactory,
        TimeSpan? processingDelay = null)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _aiServiceFactory = aiServiceFactory;
        _processingDelay = processingDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Sanitizes a string for logging to prevent log forging attacks.
    /// </summary>
    /// <param name="value">The value to sanitize.</param>
    /// <returns>A sanitized string safe for logging.</returns>
    private static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the audience classification tags already assigned to an item.
    /// </summary>
    /// <param name="item">The Jellyfin library item.</param>
    /// <returns>A list of existing audience tags.</returns>
    private static List<string> GetExistingAudienceTags(BaseItem item)
    {
        return item.Tags?
            .Where(tag => AudienceTags.Contains(tag))
            .ToList()
            ?? new List<string>();
    }

    /// <summary>
    /// Gets a human-readable media-type label.
    /// </summary>
    /// <param name="item">The Jellyfin library item.</param>
    /// <returns>The media-type label.</returns>
    private static string GetMediaTypeLabel(BaseItem item)
    {
        return item is Series ? "TV series" : "movie";
    }

    /// <summary>
    /// Gets the item types to include for the configured scan mode.
    /// </summary>
    /// <param name="scanMode">The configured media scan mode.</param>
    /// <returns>An array of Jellyfin item types.</returns>
    private static BaseItemKind[] GetIncludedItemTypes(MediaScanMode scanMode)
    {
        return scanMode switch
        {
            MediaScanMode.TvSeries =>
                new[]
                {
                    BaseItemKind.Series
                },

            MediaScanMode.Both =>
                new[]
                {
                    BaseItemKind.Movie,
                    BaseItemKind.Series
                },

            _ =>
                new[]
                {
                    BaseItemKind.Movie
                }
        };
    }

    /// <inheritdoc />
    public async Task Run(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        PluginConfiguration? config;

        try
        {
            config = Plugin.Instance?.Configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to load plugin configuration");

            progress?.Report(100);
            return;
        }

        if (config == null
            || !config.EnableAutoTagging
            || !config.ProcessOnLibraryScan)
        {
            _logger.LogDebug(
                "Auto-tagging is disabled or not configured to run on library scan");

            progress?.Report(100);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning(
                "AI API key is not configured");

            progress?.Report(100);
            return;
        }

        using var aiService =
            _aiServiceFactory.CreateService(config);

        var includedItemTypes =
            GetIncludedItemTypes(config.ScanMode);

        var items = _libraryManager
            .GetItemList(
                new InternalItemsQuery
                {
                    IncludeItemTypes = includedItemTypes,
                    IsVirtualItem = false,
                    Recursive = true
                })
            .Where(item => item is Movie || item is Series)
            .ToList();

        _logger.LogInformation(
            "Found {Count} items to process using scan mode {ScanMode}",
            items.Count,
            config.ScanMode);

        if (items.Count == 0)
        {
            progress?.Report(100);

            _logger.LogInformation(
                "No matching movies or TV series were found");

            return;
        }

        var completedCount = 0;
        var taggedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var totalCount = items.Count;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mediaType = GetMediaTypeLabel(item);
            var existingAudienceTags =
                GetExistingAudienceTags(item);

            try
            {
                if (config.SkipPreviouslyTagged
                    && existingAudienceTags.Count > 0)
                {
                    skippedCount++;

                    _logger.LogInformation(
                        "Skipping previously classified {MediaType} '{Title}' with tag(s): {Tags}",
                        mediaType,
                        SanitizeForLog(item.Name),
                        string.Join(", ", existingAudienceTags));
                }
                else
                {
                    var wasTagged = await ProcessItemAsync(
                            item,
                            aiService,
                            config.OverwriteExistingTags,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (wasTagged)
                    {
                        taggedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                failedCount++;

                _logger.LogError(
                    ex,
                    "Error processing {MediaType} '{Title}': {Message}",
                    mediaType,
                    SanitizeForLog(item.Name),
                    ex.Message);
            }

            completedCount++;

            progress?.Report(
                (double)completedCount / totalCount * 100);

            if (completedCount < totalCount)
            {
                await Task.Delay(
                        _processingDelay,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "Completed audience classification. Examined: {Examined}, tagged: {Tagged}, skipped: {Skipped}, failed: {Failed}",
            completedCount,
            taggedCount,
            skippedCount,
            failedCount);

        progress?.Report(100);
    }

    /// <summary>
    /// Processes a movie or TV series and applies an audience tag.
    /// </summary>
    /// <param name="item">The movie or TV series to process.</param>
    /// <param name="aiService">The AI service to use.</param>
    /// <param name="overwriteExisting">
    /// Whether existing audience tags should be replaced.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token.
    /// </param>
    /// <returns>
    /// True when a new classification was applied; otherwise false.
    /// </returns>
    public async Task<bool> ProcessItemAsync(
        BaseItem item,
        IAiService aiService,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        if (item is not Movie && item is not Series)
        {
            _logger.LogDebug(
                "Skipping unsupported item type {ItemType} for '{Title}'",
                item.GetType().Name,
                SanitizeForLog(item.Name));

            return false;
        }

        var mediaType = GetMediaTypeLabel(item);
        var existingAudienceTags =
            GetExistingAudienceTags(item);

        if (existingAudienceTags.Count > 0
            && !overwriteExisting)
        {
            _logger.LogInformation(
                "{MediaType} '{Title}' already has audience tag(s): {Tags}",
                mediaType,
                SanitizeForLog(item.Name),
                string.Join(", ", existingAudienceTags));

            return false;
        }

        var title = item.Name;
        var year = item.ProductionYear;
        var overview = item.Overview;
        var rating = item.OfficialRating;
        var genres = item.Genres?.ToArray();

        var audienceTag =
            await aiService.DetermineTargetAudienceAsync(
                    title,
                    year,
                    overview,
                    rating,
                    genres)
                .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(audienceTag))
        {
            _logger.LogWarning(
                "Could not determine audience for {MediaType} '{Title}'",
                mediaType,
                SanitizeForLog(title));

            return false;
        }

        audienceTag = audienceTag.Trim().ToLowerInvariant();

        if (!AudienceTags.Contains(audienceTag))
        {
            _logger.LogWarning(
                "AI returned unsupported audience tag '{Tag}' for {MediaType} '{Title}'",
                SanitizeForLog(audienceTag),
                mediaType,
                SanitizeForLog(title));

            return false;
        }

        var currentTags =
            item.Tags?.ToList()
            ?? new List<string>();

        if (overwriteExisting
            && existingAudienceTags.Count > 0)
        {
            currentTags.RemoveAll(
                tag => AudienceTags.Contains(tag));
        }

        if (!currentTags.Contains(
                audienceTag,
                StringComparer.OrdinalIgnoreCase))
        {
            currentTags.Add(audienceTag);
        }

        item.Tags = currentTags.ToArray();

        await item.UpdateToRepositoryAsync(
                ItemUpdateType.MetadataEdit,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Added '{Tag}' tag to {MediaType} '{Title}' ({Year})",
            audienceTag,
            mediaType,
            SanitizeForLog(title),
            year);

        return true;
    }

    /// <summary>
    /// Processes a single movie to add audience tags.
    /// Retained for compatibility with existing callers and tests.
    /// </summary>
    /// <param name="movie">The movie to process.</param>
    /// <param name="aiService">The AI service to use.</param>
    /// <param name="overwriteExisting">
    /// Whether existing audience tags should be replaced.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ProcessMovieAsync(
        Movie movie,
        IAiService aiService,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        await ProcessItemAsync(
                movie,
                aiService,
                overwriteExisting,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a TV series once at the series level.
    /// Seasons and episodes are not processed individually.
    /// </summary>
    /// <param name="series">The TV series to process.</param>
    /// <param name="aiService">The AI service to use.</param>
    /// <param name="overwriteExisting">
    /// Whether existing audience tags should be replaced.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ProcessSeriesAsync(
        Series series,
        IAiService aiService,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        await ProcessItemAsync(
                series,
                aiService,
                overwriteExisting,
                cancellationToken)
            .ConfigureAwait(false);
    }
}