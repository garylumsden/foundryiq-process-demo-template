using System.Security.Cryptography;
using System.Text.Json;
using Azure.Storage.Blobs.Models;
using Workspace.Core;

namespace Workspace.Tools;

public static class Ingestion
{
    public static async Task RunAsync(Configuration config, CloudClient api, CancellationToken ct)
    {
        if (!config.Approved) throw new WorkspaceException("approval_required", "Approve the exact profile digest before ingestion.");
        await api.VerifyIdentityAsync(ct);
        var blobs = api.Storage.GetBlobContainerClient(config.Sources.Container);
        var assets = api.Storage.GetBlobContainerClient(config.Sources.AssetContainer);
        await blobs.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await assets.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var originals = new Dictionary<string, (string Name, string Url, string Hash)>();
        foreach (var source in config.Sources.Documents)
        {
            var bytes = Path.GetExtension(source.File) == ".pdf" ? File.ReadAllBytes(config.Asset(source.File))
                : PolicyPdf.Render(Json.Read<PolicyContent>(config.Asset(source.File)));
            if (!bytes.AsSpan().StartsWith("%PDF-"u8)) throw new WorkspaceException("source_invalid", "The original is not a PDF.");
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var name = source.Id.ToLowerInvariant() + "-" + hash[..12].ToLowerInvariant() + ".pdf";
            var blob = blobs.GetBlobClient(name);
            await blob.UploadAsync(BinaryData.FromBytes(bytes), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" },
                Metadata = new Dictionary<string, string> { ["sha256"] = hash, ["profileDigest"] = config.Digest }
            }, ct);
            originals.Add(source.Id, (name, blob.Uri.ToString(), hash));
        }
        string Required(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value
            : throw new WorkspaceException("configuration_missing", $"Missing {key}.");
        var storageId = Required("AZURE_STORAGE_RESOURCE_ID");
        var aiUri = "https://" + api.Settings.Project.Host;
        var ingestionName = config.Sources.Collection + "-documents";
        var embedding = config.Models.Get(config.Models.EmbeddingProfile);
        var extraction = config.Models.Get(config.Models.ExtractionProfile);
        await api.SearchAsync(HttpMethod.Put, "/knowledgesources/" + ingestionName, new
        {
            name = ingestionName, kind = "azureBlob", azureBlobParameters = new
            {
                connectionString = "ResourceId=" + storageId, containerName = config.Sources.Container,
                ingestionParameters = new
                {
                    contentExtractionMode = "standard", disableImageVerbalization = false,
                    aiServices = new { uri = aiUri },
                    embeddingModel = new { kind = "azureOpenAI", azureOpenAIParameters = new
                        { resourceUri = api.Settings.Foundry.ToString(), deploymentId = embedding.Deployment, modelName = embedding.Model } },
                    chatCompletionModel = new { kind = "azureOpenAI", azureOpenAIParameters = new
                        { resourceUri = api.Settings.Foundry.ToString(), deploymentId = extraction.Deployment, modelName = extraction.Model } },
                    assetStore = new { connectionString = "ResourceId=" + storageId, containerName = config.Sources.AssetContainer }
                }
            }
        }, ct);
        var sourceResult = await api.SearchAsync(HttpMethod.Get, "/knowledgesources/" + ingestionName, null, ct);
        var generated = sourceResult.GetProperty("azureBlobParameters").GetProperty("createdResources");
        var index = generated.GetProperty("index").GetString()!;
        var indexer = generated.GetProperty("indexer").GetString()!;
        if (index != config.Sources.Index) throw new WorkspaceException("index_mismatch", "The generated native index differs from the configured index.");
        var current = await api.SearchAsync(HttpMethod.Get, "/indexers/" + indexer + "/status", null, ct);
        if (!current.TryGetProperty("lastResult", out var previous) || previous.ValueKind != JsonValueKind.Object
            || EvidenceParser.Get(previous, "status") != "inProgress")
        {
            RequirePreviousSuccess(current);
            try { await api.SearchAsync(HttpMethod.Post, "/indexers/" + indexer + "/run", null, ct); }
            catch (CloudException error) when (error.Status == 409)
            {
                var running = await api.SearchAsync(HttpMethod.Get, "/indexers/" + indexer + "/status", null, ct);
                if (!running.TryGetProperty("lastResult", out var last) || last.ValueKind != JsonValueKind.Object
                    || EvidenceParser.Get(last, "status") != "inProgress") throw;
                Console.WriteLine("Joined the active native ingestion run.");
            }
        }
        var diagnostics = Path.Combine(config.Root, ".local", "ingestion", config.Profile.Id);
        Directory.CreateDirectory(diagnostics);
        var completed = false;
        for (var attempt = 0; attempt < 180; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            current = await api.SearchAsync(HttpMethod.Get, "/indexers/" + indexer + "/status", null, ct);
            await File.WriteAllTextAsync(Path.Combine(diagnostics, "status.json"), current.GetRawText(), ct);
            if (!current.TryGetProperty("lastResult", out var result) || result.ValueKind != JsonValueKind.Object) continue;
            var status = EvidenceParser.Get(result, "status");
            if (status is "inProgress" or "reset") continue;
            if (status != "success" || result.GetProperty("itemsFailed").GetInt32() != 0)
                throw new WorkspaceException("ingestion_failed", "Native ingestion reported failed items. Inspect the retained local diagnostics.");
            RequirePreviousSuccess(current);
            completed = true;
            break;
        }
        if (!completed) throw new WorkspaceException("ingestion_timeout", "Native ingestion did not complete. No registry was published.");
        var passages = new Dictionary<string, List<NativePassage>>(StringComparer.Ordinal);
        for (var skip = 0; ; skip += 1000)
        {
            var rows = await api.SearchAsync(HttpMethod.Post, "/indexes/" + index + "/docs/search", new
                { search = "*", top = 1000, skip, select = "uid,blob_url,snippet,image_path" }, ct);
            var items = rows.GetProperty("value").EnumerateArray().ToArray();
            foreach (var item in items)
            {
                var url = EvidenceParser.Get(item, "blob_url");
                if (!passages.TryGetValue(url, out var list)) passages[url] = list = [];
                list.Add(new(EvidenceParser.Get(item, "uid"), EvidenceParser.Get(item, "snippet"), EvidenceParser.Get(item, "image_path")));
            }
            if (items.Length < 1000) break;
        }
        var documents = config.Sources.Documents.Select(source =>
        {
            var original = originals[source.Id];
            if (!passages.TryGetValue(original.Url, out var recovered) || recovered.Count == 0)
                throw new WorkspaceException("ingestion_incomplete", "An approved original has no recovered passages. No registry was published.");
            return new RegistryDocument(source.Id, source.Title, source.Revision, source.Status, source.EffectiveFrom,
                source.WithdrawnAt, original.Url, original.Name, original.Hash, source.Sha256, recovered.ToArray()) { Published = source.Published };
        }).ToArray();
        var registry = new SourceRegistry(config.Profile.Id, config.Digest, config.Sources.Collection, config.Sources.Version, documents);
        CloudClient.ValidateRegistry(config, api.Settings, registry);
        var indexResult = await api.SearchAsync(HttpMethod.Get, "/indexes/" + index, null, ct);
        var semantic = indexResult.GetProperty("semantic").GetProperty("configurations")[0].GetProperty("name").GetString()!;
        await api.SearchAsync(HttpMethod.Put, "/knowledgesources/" + config.Sources.KnowledgeSource, new
        {
            name = config.Sources.KnowledgeSource, kind = "searchIndex", searchIndexParameters = new
            {
                searchIndexName = index, semanticConfigurationName = semantic,
                sourceDataFields = new[] { "uid", "blob_url", "snippet", "image_path" }.Select(name => new { name }),
                searchFields = new[] { new { name = "snippet" } }, baseFilter = EvidenceParser.EligibleFilter(config, registry)
            }
        }, ct);
        await api.SearchAsync(HttpMethod.Put, "/knowledgebases/" + config.Sources.KnowledgeBase, new
        {
            name = config.Sources.KnowledgeBase, knowledgeSources = new[] { new { name = config.Sources.KnowledgeSource } },
            retrievalReasoningEffort = new { kind = "minimal" }, outputMode = "extractiveData",
            retrieveDefaults = new { maxRuntimeInSeconds = 60, maxOutputDocuments = config.Sources.MaxPassages, maxOutputSizeInTokens = 12000 },
            models = new[] { new { kind = "azureOpenAI", azureOpenAIParameters = new
                { resourceUri = api.Settings.Foundry.ToString(), deploymentId = extraction.Deployment, modelName = extraction.Model } } }
        }, ct);
        var registryContainer = api.Storage.GetBlobContainerClient("registries");
        await registryContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await registryContainer.GetBlobClient(config.Sources.Collection + ".json").UploadAsync(
            BinaryData.FromObjectAsJson(registry, Json.Options), overwrite: true, cancellationToken: ct);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "registry.json"), JsonSerializer.Serialize(registry, Json.Options), ct);
        Console.WriteLine($"Published {documents.Length} admitted documents and {documents.Sum(document => document.Passages.Length)} passages.");
    }

    public static void RequirePreviousSuccess(JsonElement status)
    {
        var runs = new List<JsonElement>();
        if (status.TryGetProperty("lastResult", out var last) && last.ValueKind == JsonValueKind.Object) runs.Add(last);
        if (status.TryGetProperty("executionHistory", out var history) && history.ValueKind == JsonValueKind.Array)
            runs.AddRange(history.EnumerateArray());
        static bool Unmatched(JsonElement run) => run.TryGetProperty("warnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array && warnings.EnumerateArray().Any(warning =>
                EvidenceParser.Get(warning, "name") == "ResetDocs"
                && EvidenceParser.Get(warning, "message").Contains("not yet been found", StringComparison.Ordinal));
        var latest = runs.Where(run => run.GetProperty("itemsProcessed").GetInt32() > 0
            || run.GetProperty("itemsFailed").GetInt32() > 0 || Unmatched(run)
            || EvidenceParser.Get(run, "status") is "persistentFailure" or "transientFailure")
            .OrderByDescending(run => run.GetProperty("startTime").GetDateTimeOffset()).FirstOrDefault();
        if (latest.ValueKind == JsonValueKind.Object && (Unmatched(latest)
            || latest.GetProperty("itemsFailed").GetInt32() > 0
            || EvidenceParser.Get(latest, "status") is "persistentFailure" or "transientFailure"))
            throw new WorkspaceException("ingestion_unresolved", "The previous native batch has unresolved failures. Preserve diagnostics and resolve the failed items before publication.");
    }
}
