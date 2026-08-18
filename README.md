# Auto Parental Tags

[![CI](https://github.com/TrailboundTech/auto-parental-tags/actions/workflows/build-test-coverage.yaml/badge.svg)](https://github.com/TrailboundTech/auto-parental-tags/actions/workflows/build-test-coverage.yaml)

A Jellyfin plugin that uses AI to analyze movie and TV-series metadata and automatically apply audience-target tags:

- `kids`
- `teens`
- `adults`

The plugin classifies content by its intended audience rather than relying only on its official content rating.

## Features

- Supports movies, TV series, or both
- Classifies TV shows once at the series level
- Does not process seasons or episodes individually
- Supports Google Gemini, OpenAI, LocalAI, and compatible OpenAI APIs
- Retrieves available models directly from the selected AI provider
- Applies one audience tag: `kids`, `teens`, or `adults`
- Can automatically run after Jellyfin library scans
- Includes an on-demand Jellyfin scheduled task
- Can skip content that has already been classified
- Can replace existing audience tags when reprocessing content
- Preserves unrelated Jellyfin tags
- Considers intended demographic, themes, historical context, and storytelling complexity
- Supports authenticated and unauthenticated LocalAI installations
- Supports reasoning models that expose OpenAI-compatible chat-completion APIs
- Provides a fully self-hosted, privacy-focused option through LocalAI

## Supported Content

The plugin can be configured to scan:

- **Movies only**
- **TV series only**
- **Movies and TV series**

TV shows are classified using the metadata attached to the Jellyfin series. Seasons and episodes are not sent to the AI provider or tagged individually.

## Audience Categories

| Tag | Intended audience |
|---|---|
| `kids` | Primarily targeted at children, generally ages 2–11 |
| `teens` | Primarily targeted at teenagers, generally ages 12–17 |
| `adults` | Primarily targeted at adults, generally ages 18 and older |

These categories describe the content’s primary intended audience, not whether every member of that audience should be allowed to view it.

For example, a family-safe movie may still be primarily written for adults, while an unrated animated special may clearly be intended for children.

## Supported AI Providers

### Google Gemini

- Google-hosted generative AI
- Available free and paid usage tiers
- Dynamically retrieves models that support `generateContent`
- Requires a Gemini API key

Create an API key through [Google AI Studio](https://aistudio.google.com/app/apikey).

### OpenAI

- Supports compatible OpenAI chat-completion models
- Uses OpenAI’s hosted API
- Requires an OpenAI API key
- Usage is billed according to the selected model and account plan

Create an API key through the [OpenAI Platform](https://platform.openai.com/api-keys).

### LocalAI

- Self-hosted and privacy-focused
- Provides an OpenAI-compatible API
- Runs on your own CPU or GPU hardware
- Supports installations with or without API authentication
- Avoids sending media metadata to an external provider
- Can discover models exposed by the LocalAI models endpoint

Learn more at [localai.io](https://localai.io/).

Other OpenAI-compatible APIs may also work when they expose compatible `/v1/models` and `/v1/chat/completions` endpoints.

## Requirements

- **Jellyfin Server:** 10.11.11
- **.NET target framework:** .NET 9
- One supported AI provider:
  - Google Gemini API key
  - OpenAI API key
  - LocalAI or another compatible self-hosted API

The current plugin build is compiled and tested against Jellyfin 10.11.11. Binary compatibility with older Jellyfin releases is not guaranteed.

## Installation

### From the Jellyfin Plugin Catalog

1. In Jellyfin, open **Dashboard → Plugins → Repositories**.
2. Add a repository named **Auto Parental Tags** with this URL:

   ```text
   https://raw.githubusercontent.com/TrailboundTech/auto-parental-tags/master/repository.json
   ```

3. Open **Catalog**, select **Auto Parental Tags**, and install it.
4. Restart Jellyfin, then configure the plugin under **Dashboard → Plugins**.

### From a Release

1. Download the latest package from the [Releases page](https://github.com/TrailboundTech/auto-parental-tags/releases).
2. Extract the release archive.
3. Create a directory for the plugin in Jellyfin’s plugin data directory.
4. Copy the plugin DLL and dependency manifest into that directory.
5. Restart Jellyfin.
6. Open **Dashboard → Plugins → Auto Parental Tags** and configure the plugin.

Common plugin locations include:

| Installation | Plugin directory |
|---|---|
| Linux package | `/var/lib/jellyfin/plugins/` |
| Windows | `C:\ProgramData\Jellyfin\Server\plugins\` |
| Docker | `/config/plugins/` |

A typical Docker installation might use:

```text
/config/plugins/Auto Parental Tags_1.0.2.0/
├── Jellyfin.Plugin.AutoParentalTags.dll
└── Jellyfin.Plugin.AutoParentalTags.deps.json
```

Restart Jellyfin after installing or replacing plugin files.

### Building from Source

Clone the repository:

```bash
git clone https://github.com/TrailboundTech/auto-parental-tags.git
cd auto-parental-tags
```

Restore dependencies:

```bash
dotnet restore Jellyfin.Plugin.AutoParentalTags.sln
```

Build the solution:

```bash
dotnet build \
  Jellyfin.Plugin.AutoParentalTags.sln \
  --configuration Release
```

Run the tests:

```bash
dotnet test \
  Jellyfin.Plugin.AutoParentalTags.sln \
  --configuration Release \
  --no-build
```

Publish the plugin:

```bash
dotnet publish \
  Jellyfin.Plugin.AutoParentalTags/Jellyfin.Plugin.AutoParentalTags.csproj \
  --configuration Release \
  --output publish
```

The deployable files will be placed in:

```text
publish/
```

At minimum, copy these files to the Jellyfin plugin directory:

```text
Jellyfin.Plugin.AutoParentalTags.dll
Jellyfin.Plugin.AutoParentalTags.deps.json
```

## Configuration

Open:

**Dashboard → Plugins → Auto Parental Tags**

### AI Provider

Select one of the available providers:

- **Google Gemini**
- **OpenAI**
- **LocalAI / Custom Endpoint**

### API Key

Enter the API key required by the selected provider.

For LocalAI:

- Enter an API key when LocalAI authentication is enabled.
- Leave the field empty when the LocalAI server allows unauthenticated API access.

### API Endpoint

The API endpoint is used for LocalAI and compatible custom providers.

The plugin accepts any of these forms:

```text
http://localhost:8080
http://localhost:8080/v1
http://localhost:8080/v1/chat/completions
```

The plugin normalizes the URL to the correct chat-completions endpoint.

When Jellyfin and LocalAI run in different containers or hosts, do not use `localhost` unless both services share the same network namespace. Use an address reachable from the Jellyfin container, such as:

```text
http://192.168.1.26:8080
```

### Model

Use **Refresh Models** to retrieve available models from the configured provider.

For LocalAI, the model must already be installed and exposed by its models endpoint.

### Content to Scan

Choose one:

- **Movies only**
- **TV series only**
- **Movies and TV series**

When TV series are selected, each series is processed once. Its seasons and episodes are not processed separately.

### Enable Automatic Tagging

Globally enables or disables audience classification.

### Process on Library Scan

Runs the plugin after Jellyfin completes a library scan.

The plugin also provides an on-demand entry under:

**Dashboard → Scheduled Tasks → Auto Parental Tags**

### Skip Previously Classified Content

When enabled, the plugin skips items that already have one or more of these tags:

```text
kids
teens
adults
```

Unrelated tags do not cause an item to be skipped.

This setting is enabled by default and helps avoid:

- repeated AI requests
- unnecessary API usage
- duplicate classification work
- reprocessing an entire library during each scan

### Overwrite Existing Audience Tags

When enabled, the plugin removes existing `kids`, `teens`, and `adults` tags before applying the new result.

Other Jellyfin tags are preserved.

To intentionally reclassify previously tagged content:

1. Disable **Skip Previously Classified Content**.
2. Enable **Overwrite Existing Audience Tags**.
3. Run the scheduled task.

## LocalAI Setup Example

Assume LocalAI is available at:

```text
http://192.168.1.26:8080
```

Configure the plugin as follows:

1. Set **AI Provider** to **LocalAI / Custom Endpoint**.
2. Set **API Endpoint** to:

   ```text
   http://192.168.1.26:8080
   ```

3. Enter the LocalAI API key if authentication is enabled.
4. Click **Refresh Models**.
5. Select the desired model.
6. Select the content type to scan.
7. Enable **Skip Previously Classified Content** for normal incremental processing.
8. Click **Save**.
9. Run **Auto Parental Tags** from Jellyfin’s Scheduled Tasks page.

The plugin uses an OpenAI-compatible chat-completion request and asks the model to return exactly one supported audience category.

## How It Works

For each selected movie or TV series, the plugin reads:

- title
- release or premiere year
- overview or synopsis
- official content rating, when available
- genres
- whether the item is a movie or TV series

The plugin asks the AI provider to determine the item’s primary target audience.

The prompt instructs the model to consider:

- marketing and intended demographic
- themes and subject-matter complexity
- historical and cultural context
- franchise or brand audience
- storytelling sophistication
- the overall intended audience of a TV series
- the difference between content appropriateness and target audience

The AI must return one of:

```text
kids
teens
adults
```

Responses that cannot be parsed into a supported category are rejected rather than automatically defaulting to `adults`.

## Example Classifications

These examples illustrate the intended classification approach. Actual results may vary by provider, model, and available metadata.

| Title | Type | Rating | Possible tag | Rationale |
|---|---|---:|---|---|
| A Charlie Brown Christmas | Movie/special | NR | `kids` | Primarily intended for children despite being unrated |
| Toy Story | Movie | G | `kids` | Animated family movie primarily marketed to children |
| The Empire Strikes Back | Movie | PG | `teens` | Darker themes and more complex conflict |
| Early James Bond films | Movie | PG | `adults` | Primarily marketed to adults despite historical PG ratings |
| The Godfather | Movie | R | `adults` | Adult-oriented themes, violence, and storytelling |
| A preschool animated series | TV series | TV-Y | `kids` | Series premise and presentation target young children |

## Privacy and Data Handling

### Data Sent to AI Providers

The plugin may send the following metadata:

- movie or TV-series title
- release or premiere year
- overview or synopsis
- official content rating
- genres
- media type

The plugin does not intentionally send:

- viewing history
- usernames
- passwords
- personal profile information
- file paths
- media files
- video or audio content

### Provider Privacy

When using Gemini or OpenAI, metadata is sent to the selected external provider and is subject to that provider’s policies.

When using LocalAI, classification can remain entirely within your own network and hardware.

For the highest level of privacy, use a locally hosted model and restrict LocalAI access to trusted systems.

## Logging

Useful Jellyfin log entries include:

```text
Manual Auto Parental Tags task started
Found 123 items to process using scan mode Both
Skipping previously classified movie 'Example Movie' with tag(s): adults
Classified TV series 'Example Series' (2024) as 'teens'
Added 'teens' tag to TV series 'Example Series' (2024)
Completed audience classification. Examined: 123, tagged: 40, skipped: 82, failed: 1
```

For Docker installations, logs can be followed with:

```bash
docker logs -f jellyfin
```

A filtered view can be produced with:

```bash
docker logs -f --since 30s jellyfin 2>&1 | \
grep --line-buffered -Ei \
'AutoParentalTags|Auto Parental Tags|Classified|Added|Skipping|Completed audience|error|exception|failed'
```

## Troubleshooting

### Plugin Does Not Appear in the Dashboard

- Verify the DLL is in Jellyfin’s active plugin data directory.
- Confirm that Jellyfin can read the plugin files.
- Restart Jellyfin completely.
- Check Jellyfin logs for plugin-loading errors.
- Confirm the plugin was built against a compatible Jellyfin version.

A successful load resembles:

```text
Loaded assembly Jellyfin.Plugin.AutoParentalTags
Auto Parental Tags plugin initialized
```

### `Method not found` During Task Execution

This usually indicates that the plugin was compiled against a different Jellyfin API version than the running server.

Confirm the server version:

```bash
docker exec jellyfin jellyfin --version
```

Confirm the project’s `Jellyfin.Controller` package version matches the running server, then clean, restore, rebuild, and redeploy the plugin.

### Content Is Not Being Tagged

- Confirm **Enable Automatic Tagging** is enabled.
- Confirm the desired **Content to Scan** option is selected.
- Check whether **Skip Previously Classified Content** is skipping the item.
- Verify that the item is a movie or TV series.
- Confirm the selected model is available.
- Confirm the API key is valid.
- Confirm the provider endpoint is reachable from Jellyfin.
- Check logs for API errors, unsupported responses, or rate limiting.

### Previously Tagged Content Is Being Skipped

This is expected when **Skip Previously Classified Content** is enabled.

To reprocess it:

1. Disable **Skip Previously Classified Content**.
2. Enable **Overwrite Existing Audience Tags** if the old classification should be replaced.
3. Run the scheduled task again.

### LocalAI Models Do Not Appear

- Confirm LocalAI is running.
- Confirm the endpoint is reachable from Jellyfin.
- Confirm the API key is correct when authentication is enabled.
- Verify that LocalAI’s `/v1/models` endpoint returns installed models.
- Check LocalAI and Jellyfin logs for authorization or connection errors.

Example test:

```bash
curl -H "Authorization: Bearer YOUR_API_KEY" \
  http://192.168.1.26:8080/v1/models
```

Omit the authorization header when LocalAI authentication is disabled.

### LocalAI Returns an Empty Classification

Some reasoning models may consume their entire response budget internally without returning visible content.

The plugin sends settings intended to reduce or disable extended reasoning for compatible models and allows enough output tokens for a visible result. Model behavior still varies, so a smaller instruction-following model may produce more reliable single-word classifications.

### Tags Appear Incorrect

- Classification quality depends on the model and available metadata.
- Ensure the item has a useful overview, rating, and genre data.
- Try another model or provider.
- Disable skipping and enable overwrite to rerun classification.
- Review whether the model is identifying target audience rather than only content suitability.

## Development

### Project Structure

```text
auto-parental-tags/
├── Jellyfin.Plugin.AutoParentalTags/
│   ├── Api/
│   ├── Configuration/
│   │   ├── PluginConfiguration.cs
│   │   └── configPage.html
│   ├── Services/
│   │   ├── IAiService.cs
│   │   ├── AiServiceFactory.cs
│   │   ├── GeminiService.cs
│   │   └── OpenAiService.cs
│   ├── AutoParentalTagsScheduledTask.cs
│   ├── LibraryMonitor.cs
│   ├── Plugin.cs
│   └── ServiceRegistrator.cs
├── Jellyfin.Plugin.AutoParentalTags.Tests/
├── .github/workflows/
├── Directory.Build.props
└── README.md
```

### Running Tests

Build the complete solution:

```bash
dotnet build \
  Jellyfin.Plugin.AutoParentalTags.sln \
  --configuration Release
```

Run the tests:

```bash
dotnet test \
  Jellyfin.Plugin.AutoParentalTags.sln \
  --configuration Release \
  --no-build
```

Run tests with Coverlet coverage collection:

```bash
dotnet test \
  Jellyfin.Plugin.AutoParentalTags.sln \
  --configuration Release \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=lcov
```

### Code Quality

The project uses:

- .NET analyzers
- StyleCop analyzers
- xUnit
- Moq
- Coverlet
- GitHub Actions
- `dotnet format`

Before opening a pull request:

```bash
dotnet format
dotnet build --configuration Release
dotnet test --configuration Release
```

## Contributing

1. Fork the repository.
2. Create a feature branch:

   ```bash
   git switch -c feature/example
   ```

3. Make and test your changes.
4. Ensure formatting and analyzers pass.
5. Commit the changes:

   ```bash
   git commit -m "Describe the change"
   ```

6. Push the branch:

   ```bash
   git push origin feature/example
   ```

7. Open a pull request.

Bug reports and pull requests should include:

- Jellyfin version
- plugin version or commit
- selected AI provider
- relevant configuration, with secrets removed
- relevant Jellyfin log output
- steps required to reproduce the issue

## Roadmap

- [x] Support movies
- [x] Support TV series
- [x] Select movies, TV series, or both
- [x] Classify TV shows at the series level
- [x] Skip previously classified content
- [x] Discover available provider models
- [x] Support authenticated LocalAI instances
- [ ] Add dedicated tests for every scan-mode branch
- [ ] Restore and increase coverage above the CI threshold
- [ ] Add library and collection filters
- [ ] Add manual classification controls to item pages
- [ ] Add configurable audience categories and tag names
- [ ] Add batch-size and rate-limit controls
- [ ] Add optional classification caching
- [ ] Add multi-language prompts
- [ ] Add provider-specific advanced settings
- [ ] Integrate with Jellyfin smart filters

## License

This project is licensed under the terms in [LICENSE](LICENSE).

## Acknowledgments

- [Jellyfin](https://jellyfin.org/) — free and open-source media server
- [LocalAI](https://localai.io/) — privacy-focused local AI inference
- [jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template) — plugin project and build structure
- The original Auto Parental Tags project and its contributors

## Support

- [GitHub Issues](https://github.com/TrailboundTech/auto-parental-tags/issues)
- [GitHub Discussions](https://github.com/TrailboundTech/auto-parental-tags/discussions)
- [Jellyfin Community Forum](https://forum.jellyfin.org/)
