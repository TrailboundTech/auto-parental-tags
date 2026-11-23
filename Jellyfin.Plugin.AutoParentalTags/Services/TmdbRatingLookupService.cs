using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// Looks up a content rating on TMDb for items that have no official rating in Jellyfin.
/// </summary>
public class TmdbRatingLookupService : IDisposable
{
    /// <summary>Countries checked in order when choosing a certification.</summary>
    public static readonly string[] PreferredCountries = { "US", "GB", "CA", "AU", "IE", "NZ", "DE" };

    private const string ApiBase = "https://api.themoviedb.org/3";

    private readonly ILogger<TmdbRatingLookupService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, RatingLookupResult?> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbRatingLookupService"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{TmdbRatingLookupService}"/> interface.</param>
    public TmdbRatingLookupService(ILogger<TmdbRatingLookupService> logger)
        : this(logger, new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbRatingLookupService"/> class with a custom client.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">HTTP client used for TMDb requests.</param>
    public TmdbRatingLookupService(ILogger<TmdbRatingLookupService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Looks up a certification for a movie or series.
    /// </summary>
    /// <param name="isSeries">True for TV series, false for movies.</param>
    /// <param name="tmdbId">TMDb identifier, if known.</param>
    /// <param name="imdbId">IMDb identifier, used to find the TMDb identifier when needed.</param>
    /// <param name="configuredApiKey">Optional TMDb API key from plugin settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rating and its source, or null when none was found.</returns>
    public virtual async Task<RatingLookupResult?> LookupAsync(
        bool isSeries,
        string? tmdbId,
        string? imdbId,
        string? configuredApiKey,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey(configuredApiKey);
        if (string.IsNullOrWhiteSpace(apiKey)
            || (string.IsNullOrWhiteSpace(tmdbId) && string.IsNullOrWhiteSpace(imdbId)))
        {
            return null;
        }

        var cacheKey = $"{(isSeries ? "tv" : "movie")}:{tmdbId}:{imdbId}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        RatingLookupResult? result = null;
        try
        {
            var id = tmdbId;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = await FindTmdbIdAsync(isSeries, imdbId!, apiKey, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                result = isSeries
                    ? await LookupSeriesAsync(id, apiKey, cancellationToken).ConfigureAwait(false)
                    : await LookupMovieAsync(id, apiKey, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A failed lookup never blocks classification; the model still gets all other metadata.
            _logger.LogDebug(ex, "TMDb rating lookup failed for {Key}", cacheKey);
            return null;
        }

        _cache[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Picks the certification from the most preferred country that has one.
    /// </summary>
    /// <param name="ratings">Country code and certification pairs.</param>
    /// <returns>The chosen rating, or null.</returns>
    public static RatingLookupResult? PickPreferred(System.Collections.Generic.IEnumerable<(string Country, string Rating)> ratings)
    {
        var usable = ratings
            .Where(r => !string.IsNullOrWhiteSpace(r.Rating) && !string.IsNullOrWhiteSpace(r.Country))
            .ToList();

        foreach (var country in PreferredCountries)
        {
            var match = usable.FirstOrDefault(r => string.Equals(r.Country, country, StringComparison.OrdinalIgnoreCase));
            if (match.Rating is not null)
            {
                return new RatingLookupResult(match.Rating.Trim(), $"TMDb, {country}");
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the TMDb API key: plugin setting, then the Jellyfin TMDb plugin setting,
    /// then Jellyfin's built-in TMDb key.
    /// </summary>
    /// <param name="configuredApiKey">Key from this plugin's settings.</param>
    /// <returns>The API key, or null when none is available.</returns>
    protected virtual string? ResolveApiKey(string? configuredApiKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return configuredApiKey.Trim();
        }

        try
        {
            var pluginType = Type.GetType("MediaBrowser.Providers.Plugins.Tmdb.Plugin, MediaBrowser.Providers");
            var instance = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var configuration = instance?.GetType().GetProperty("Configuration")?.GetValue(instance);
            if (configuration?.GetType().GetProperty("TmdbApiKey")?.GetValue(configuration) is string userKey
                && !string.IsNullOrWhiteSpace(userKey))
            {
                return userKey.Trim();
            }

            var utilsType = Type.GetType("MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils, MediaBrowser.Providers");
            if (utilsType?.GetField("ApiKey", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is string builtInKey
                && !string.IsNullOrWhiteSpace(builtInKey))
            {
                return builtInKey;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or TypeLoadException)
        {
            _logger.LogDebug(ex, "Could not read Jellyfin's TMDb key");
        }

        return null;
    }

    private async Task<string?> FindTmdbIdAsync(bool isSeries, string imdbId, string apiKey, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(
            $"{ApiBase}/find/{Uri.EscapeDataString(imdbId)}?external_source=imdb_id&api_key={Uri.EscapeDataString(apiKey)}",
            cancellationToken).ConfigureAwait(false);
        if (doc is null
            || !doc.RootElement.TryGetProperty(isSeries ? "tv_results" : "movie_results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0
            || !results[0].TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.GetInt64().ToString(CultureInfo.InvariantCulture);
    }

    private async Task<RatingLookupResult?> LookupMovieAsync(string tmdbId, string apiKey, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(
            $"{ApiBase}/movie/{Uri.EscapeDataString(tmdbId)}/release_dates?api_key={Uri.EscapeDataString(apiKey)}",
            cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var pairs = results.EnumerateArray()
            .SelectMany(country =>
            {
                var code = country.TryGetProperty("iso_3166_1", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                return country.TryGetProperty("release_dates", out var dates) && dates.ValueKind == JsonValueKind.Array
                    ? dates.EnumerateArray()
                        .Select(d => (code, d.TryGetProperty("certification", out var cert) ? cert.GetString() ?? string.Empty : string.Empty))
                        .ToList()
                    : new System.Collections.Generic.List<(string, string)>();
            })
            .ToList();

        return PickPreferred(pairs);
    }

    private async Task<RatingLookupResult?> LookupSeriesAsync(string tmdbId, string apiKey, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(
            $"{ApiBase}/tv/{Uri.EscapeDataString(tmdbId)}/content_ratings?api_key={Uri.EscapeDataString(apiKey)}",
            cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var pairs = results.EnumerateArray()
            .Select(r => (
                r.TryGetProperty("iso_3166_1", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                r.TryGetProperty("rating", out var rating) ? rating.GetString() ?? string.Empty : string.Empty))
            .ToList();

        return PickPreferred(pairs);
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("TMDb returned {StatusCode}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient.Dispose();
        }
    }
}
