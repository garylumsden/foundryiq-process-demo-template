using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace Workspace.Core;

public sealed record CloudSettings(string TenantId, string PrincipalId, Uri Foundry, Uri Project,
    Uri Search, Uri Storage, string Insights)
{
    public static CloudSettings FromEnvironment()
    {
        string Get(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
            : throw new WorkspaceException("configuration_missing", $"Missing {name}. Configure the Azure environment before enabling inference.");
        Uri Endpoint(string name, string suffix)
        {
            if (!Uri.TryCreate(Get(name), UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new WorkspaceException("configuration_invalid", $"Invalid Azure endpoint: {name}.");
            return uri;
        }
        var tenant = Get("AZURE_TENANT_ID");
        var principal = Get("AZURE_PRINCIPAL_ID");
        if (!Guid.TryParse(tenant, out _) || !Guid.TryParse(principal, out _))
            throw new WorkspaceException("configuration_invalid", "Azure identity settings must be GUIDs.");
        return new(tenant, principal, Endpoint("FOUNDRY_ENDPOINT", ".openai.azure.com"),
            Endpoint("FOUNDRY_PROJECT_ENDPOINT", ".services.ai.azure.com"), Endpoint("SEARCH_ENDPOINT", ".search.windows.net"),
            Endpoint("STORAGE_ENDPOINT", ".blob.core.windows.net"), Get("APPLICATIONINSIGHTS_CONNECTION_STRING"));
    }
}

public sealed class CloudClient : IDisposable
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromMinutes(3) };
    public CloudSettings Settings { get; }
    public AzureCliCredential Credential { get; }
    public Configuration Config { get; }
    public BlobServiceClient Storage { get; }

    public CloudClient(Configuration config, CloudSettings settings)
    {
        Config = config;
        Settings = settings;
        Credential = new(new AzureCliCredentialOptions { TenantId = settings.TenantId });
        Storage = new(settings.Storage, Credential);
    }

    public async Task VerifyIdentityAsync(CancellationToken ct)
    {
        var token = await Credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), ct);
        var segment = token.Token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight((segment.Length + 3) / 4 * 4, '=');
        using var payload = JsonDocument.Parse(Convert.FromBase64String(segment));
        if (payload.RootElement.GetProperty("tid").GetString() != Settings.TenantId
            || payload.RootElement.GetProperty("oid").GetString() != Settings.PrincipalId)
            throw new WorkspaceException("identity_mismatch", "The signed-in identity does not match the configured resource identity.");
    }

    public async Task<JsonElement> SendAsync(HttpMethod method, Uri uri, string audience, object? body, CancellationToken ct)
    {
        var serialized = body is null ? null : JsonSerializer.Serialize(body, Json.Options);
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            var token = await Credential.GetTokenAsync(new TokenRequestContext([audience + "/.default"]), ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            if (serialized is not null) request.Content = new StringContent(serialized, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length == 0) return JsonSerializer.SerializeToElement(new { });
                using var parsed = JsonDocument.Parse(bytes);
                return parsed.RootElement.Clone();
            }
            var status = (int)response.StatusCode;
            if (attempt >= 2 || status is not (408 or 429 or 500 or 502 or 503 or 504))
                throw new CloudException("service_request", status);
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt + 1);
            await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, ct);
        }
    }

    public Task<JsonElement> SearchAsync(HttpMethod method, string path, object? body, CancellationToken ct) =>
        SendAsync(method, new Uri(Settings.Search, path + (path.Contains('?') ? "&" : "?") + "api-version=" + Config.Sources.SearchApiVersion),
            "https://search.azure.com", body, ct);
    public Task<JsonElement> ProjectAsync(HttpMethod method, string path, object? body, CancellationToken ct) =>
        SendAsync(method, new Uri(Settings.Project.ToString().TrimEnd('/') + "/" + path.TrimStart('/')), "https://ai.azure.com", body, ct);
    public Task<JsonElement> OpenAiAsync(object body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, new Uri(Settings.Foundry, "/openai/v1/chat/completions"), "https://cognitiveservices.azure.com", body, ct);

    public async Task<SourceRegistry> RegistryAsync(CancellationToken ct)
    {
        var downloaded = await Storage.GetBlobContainerClient("registries").GetBlobClient(Config.Sources.Collection + ".json").DownloadContentAsync(ct);
        var registry = downloaded.Value.Content.ToObjectFromJson<SourceRegistry>(Json.Options)
            ?? throw new WorkspaceException("registry_invalid", "The source registry is invalid.");
        ValidateRegistry(Config, Settings, registry);
        return registry;
    }

    public static void ValidateRegistry(Configuration config, CloudSettings settings, SourceRegistry registry)
    {
        if (registry.ProfileId != config.Profile.Id || registry.ProfileDigest != config.Digest
            || registry.Collection != config.Sources.Collection || registry.CorpusVersion != config.Sources.Version
            || registry.Documents.Length != config.Sources.Documents.Length
            || registry.Documents.Select(document => document.Id).Distinct().Count() != registry.Documents.Length
            || registry.Documents.Select(document => document.BlobUrl).Distinct().Count() != registry.Documents.Length)
            throw new WorkspaceException("registry_invalid", "The complete registry must match the approved active profile.");
        foreach (var source in config.Sources.Documents)
        {
            var document = registry.Documents.SingleOrDefault(item => item.Id == source.Id);
            if (document is null || document.DefinitionHash != source.Sha256 || document.Title != source.Title
                || document.Revision != source.Revision || document.Status != source.Status
                || document.EffectiveFrom != source.EffectiveFrom || document.WithdrawnAt != source.WithdrawnAt || document.Published != source.Published
                || document.Passages.Length == 0 || document.Passages.Any(passage => string.IsNullOrWhiteSpace(passage.Id) || string.IsNullOrWhiteSpace(passage.Text))
                || document.Passages.Select(passage => passage.Id).Distinct().Count() != document.Passages.Length
                || Path.GetFileName(document.BlobName) != document.BlobName || !document.BlobName.EndsWith(".pdf", StringComparison.Ordinal)
                || document.BlobUrl != new Uri(settings.Storage, config.Sources.Container + "/" + document.BlobName).ToString()
                || !System.Text.RegularExpressions.Regex.IsMatch(document.ContentHash, "^[A-F0-9]{64}$"))
                throw new WorkspaceException("registry_invalid", "The registry does not bind every approved source to its original file and exact passages.");
        }
    }

    public void Dispose() => client.Dispose();
}
