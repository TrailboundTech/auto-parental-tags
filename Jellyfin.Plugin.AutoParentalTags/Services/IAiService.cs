using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// Interface for AI services that determine target audience.
/// </summary>
public interface IAiService : IDisposable
{
    /// <summary>
    /// Sets the API key or credentials for the AI service.
    /// </summary>
    /// <param name="apiKey">The API key or credentials.</param>
    void SetApiKey(string apiKey);

    /// <summary>
    /// Sets the API endpoint URL for self-hosted services such as LocalAI.
    /// </summary>
    /// <param name="endpoint">The endpoint URL.</param>
    void SetEndpoint(string endpoint);

    /// <summary>
    /// Sets the model name to use for AI requests.
    /// </summary>
    /// <param name="modelName">The model name.</param>
    void SetModelName(string modelName);

    /// <summary>
    /// Determines the primary target audience for a movie or TV series.
    /// </summary>
    /// <param name="request">Metadata describing the item.</param>
    /// <returns>kids, teens, adults, or null when no classification was possible.</returns>
    Task<string?> DetermineTargetAudienceAsync(AudienceRequest request);

    /// <summary>
    /// Gets a list of available models from the AI service.
    /// </summary>
    /// <returns>
    /// A task containing the available model names.
    /// </returns>
    Task<string[]> GetAvailableModelsAsync();
}
