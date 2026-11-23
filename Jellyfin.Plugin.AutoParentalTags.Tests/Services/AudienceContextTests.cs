using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoParentalTags.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AutoParentalTags.Tests.Services;

/// <summary>
/// Tests for prompt context, TMDb rating lookup and request building.
/// </summary>
public class AudienceContextTests
{
    private static AudienceRequest Request(string? rating = null) =>
        new("movie", "Test", 2020, "Overview", rating, new[] { "Comedy" });

    /// <summary>Official rating wins over a looked-up rating.</summary>
    [Fact]
    public void FormatRating_PrefersOfficialRating()
    {
        var request = Request("PG") with { LookedUpRating = "12A", LookedUpRatingSource = "TMDb, GB" };
        Assert.Equal("PG", AudiencePromptContext.FormatRating(request));
    }

    /// <summary>Looked-up rating is labelled with its source.</summary>
    [Fact]
    public void FormatRating_UsesLookedUpRatingWithSource()
    {
        var request = Request() with { LookedUpRating = "12A", LookedUpRatingSource = "TMDb, GB" };
        Assert.Equal("12A (TMDb, GB)", AudiencePromptContext.FormatRating(request));
    }

    /// <summary>No rating at all reads as Not Rated.</summary>
    [Fact]
    public void FormatRating_FallsBackToNotRated()
    {
        Assert.Equal("Not Rated", AudiencePromptContext.FormatRating(Request()));
    }

    /// <summary>Extra context includes tagline, studios and keywords, capped and on single lines.</summary>
    [Fact]
    public void FormatAdditionalContext_IncludesCappedSingleLineFields()
    {
        var request = Request() with
        {
            Tagline = "Line one\nline two",
            Studios = new[] { "Disney", "Pixar", "disney", "A", "B", "C" },
            Keywords = Enumerable.Range(1, 20).Select(i => $"k{i}").ToArray()
        };

        var text = AudiencePromptContext.FormatAdditionalContext(request);

        Assert.Contains("\nTagline: Line one line two", text, StringComparison.Ordinal);
        Assert.Contains("\nStudios/Networks: Disney, Pixar, A, B", text, StringComparison.Ordinal);
        Assert.DoesNotContain("C", text.Split("Studios/Networks: ")[1].Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("k12", text, StringComparison.Ordinal);
        Assert.DoesNotContain("k13", text, StringComparison.Ordinal);
    }

    /// <summary>Empty metadata adds nothing to the prompt.</summary>
    [Fact]
    public void FormatAdditionalContext_EmptyWhenNoMetadata()
    {
        Assert.Equal(string.Empty, AudiencePromptContext.FormatAdditionalContext(Request()));
    }

    /// <summary>US certification is preferred over other countries.</summary>
    [Fact]
    public void PickPreferred_PrefersUnitedStates()
    {
        var result = TmdbRatingLookupService.PickPreferred(new[] { ("GB", "12A"), ("US", "PG-13"), ("DE", "12") });
        Assert.Equal(new RatingLookupResult("PG-13", "TMDb, US"), result);
    }

    /// <summary>Falls back through the preferred country list and ignores blanks.</summary>
    [Fact]
    public void PickPreferred_FallsBackAndIgnoresBlank()
    {
        var result = TmdbRatingLookupService.PickPreferred(new[] { ("US", string.Empty), ("FR", "U"), ("GB", "PG") });
        Assert.Equal(new RatingLookupResult("PG", "TMDb, GB"), result);
        Assert.Null(TmdbRatingLookupService.PickPreferred(new[] { ("FR", "U") }));
    }

    /// <summary>Movie lookup by IMDb id resolves the TMDb id, then reads release certifications.</summary>
    [Fact]
    public async Task LookupAsync_MovieByImdbId_ReturnsCertification()
    {
        var handler = new StubHandler(url =>
            url.Contains("/find/tt123", StringComparison.Ordinal)
                ? "{\"movie_results\":[{\"id\":42}]}"
                : url.Contains("/movie/42/release_dates", StringComparison.Ordinal)
                    ? "{\"results\":[{\"iso_3166_1\":\"GB\",\"release_dates\":[{\"certification\":\"U\"}]}]}"
                    : null);
        using var service = new TmdbRatingLookupService(NullLogger<TmdbRatingLookupService>.Instance, new HttpClient(handler));

        var result = await service.LookupAsync(false, null, "tt123", "key");

        Assert.Equal(new RatingLookupResult("U", "TMDb, GB"), result);
        Assert.All(handler.Urls, u => Assert.Contains("api_key=key", u, StringComparison.Ordinal));
    }

    /// <summary>Series lookup reads content ratings, and results are cached.</summary>
    [Fact]
    public async Task LookupAsync_Series_ReadsContentRatingsAndCaches()
    {
        var handler = new StubHandler(url => url.Contains("/tv/7/content_ratings", StringComparison.Ordinal)
            ? "{\"results\":[{\"iso_3166_1\":\"US\",\"rating\":\"TV-Y7\"}]}"
            : null);
        using var service = new TmdbRatingLookupService(NullLogger<TmdbRatingLookupService>.Instance, new HttpClient(handler));

        var first = await service.LookupAsync(true, "7", null, "key");
        var second = await service.LookupAsync(true, "7", null, "key");

        Assert.Equal(new RatingLookupResult("TV-Y7", "TMDb, US"), first);
        Assert.Equal(first, second);
        Assert.Single(handler.Urls);
    }

    /// <summary>HTTP failures return null instead of throwing.</summary>
    [Fact]
    public async Task LookupAsync_HttpFailure_ReturnsNull()
    {
        var handler = new StubHandler(_ => null);
        using var service = new TmdbRatingLookupService(NullLogger<TmdbRatingLookupService>.Instance, new HttpClient(handler));

        Assert.Null(await service.LookupAsync(false, "1", null, "key"));
    }

    /// <summary>NR is treated as missing and replaced by a looked-up rating; audience tags are not sent as keywords.</summary>
    [Fact]
    public async Task BuildAudienceRequest_UsesMetadataAndLookup()
    {
        var lookup = new Mock<TmdbRatingLookupService>(NullLogger<TmdbRatingLookupService>.Instance);
        lookup.Setup(x => x.LookupAsync(false, "99", null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RatingLookupResult("PG", "TMDb, US"));

        var monitor = new LibraryMonitor(
            Mock.Of<ILibraryManager>(),
            NullLogger<LibraryMonitor>.Instance,
            new AiServiceFactory(NullLoggerFactory.Instance),
            TimeSpan.Zero,
            null,
            lookup.Object);

        var movie = new Movie
        {
            Name = "Classic",
            OfficialRating = "NR",
            Tagline = "A tagline",
            Studios = new[] { "Studio A" },
            Tags = new[] { "kids", "christmas", "Teens" }
        };
        movie.SetProviderId(MetadataProvider.Tmdb, "99");

        var request = await monitor.BuildAudienceRequestAsync(movie, "movie", CancellationToken.None);

        Assert.Null(request.OfficialRating);
        Assert.Equal("PG", request.LookedUpRating);
        Assert.Equal("A tagline", request.Tagline);
        Assert.Equal(new[] { "Studio A" }, request.Studios);
        Assert.Equal(new[] { "christmas" }, request.Keywords);
    }

    /// <summary>An official rating skips the lookup.</summary>
    [Fact]
    public async Task BuildAudienceRequest_OfficialRating_SkipsLookup()
    {
        var lookup = new Mock<TmdbRatingLookupService>(NullLogger<TmdbRatingLookupService>.Instance);
        var monitor = new LibraryMonitor(
            Mock.Of<ILibraryManager>(),
            NullLogger<LibraryMonitor>.Instance,
            new AiServiceFactory(NullLoggerFactory.Instance),
            TimeSpan.Zero,
            null,
            lookup.Object);

        var request = await monitor.BuildAudienceRequestAsync(new Movie { Name = "Rated", OfficialRating = "PG-13" }, "movie", CancellationToken.None);

        Assert.Equal("PG-13", request.OfficialRating);
        lookup.Verify(
            x => x.LookupAsync(It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<string, string?> _respond;

        public StubHandler(Func<string, string?> respond) => _respond = respond;

        public System.Collections.Generic.List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            var body = _respond(url);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
