using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workspace.Core;

public static class EvidenceParser
{
    public static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    public static string Get(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    public static Evidence[] FromRetrieve(JsonElement response, SourceRegistry registry, Configuration config)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("references", out var references) || references.ValueKind != JsonValueKind.Array)
            throw new WorkspaceException("grounding_invalid", "The retrieval response has no reference contract.");
        var output = new Dictionary<string, Evidence>();
        foreach (var reference in references.EnumerateArray())
        {
            if (!reference.TryGetProperty("sourceData", out var data))
                throw new WorkspaceException("grounding_invalid", "A reference has no source data.");
            Add(Get(reference, "id"), data, registry, config, output);
        }
        return Finish(output, config);
    }

    public static Evidence[] FromAgent(JsonElement response, SourceRegistry registry, Configuration config)
    {
        var output = new Dictionary<string, Evidence>();
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("output", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new WorkspaceException("grounding_missing", "The agent returned no retrieval output.");
        var calls = items.EnumerateArray()
            .Where(item => Get(item, "type") == "mcp_call" && Get(item, "name") == "knowledge_base_retrieve").ToArray();
        if (calls.Length != 1 || Get(calls[0], "server_label") != "workspace-grounding")
            throw new WorkspaceException("grounding_missing", "Exactly one call to the configured knowledge source is required.");
        var call = calls[0];
        if (call.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new WorkspaceException("grounding_failed", "The knowledge source reported an error.");
        if (!call.TryGetProperty("output", out var raw) || raw.ValueKind != JsonValueKind.String)
            throw new WorkspaceException("grounding_invalid", "The knowledge source returned no verifiable data.");
        using var document = JsonDocument.Parse(raw.GetString()!);
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("documents", out var documents))
        {
            var index = 0;
            foreach (var item in documents.EnumerateArray()) Add((index++).ToString(System.Globalization.CultureInfo.InvariantCulture), item, registry, config, output);
        }
        else Visit(document.RootElement, registry, config, output, 0);
        return Finish(output, config);
    }

    private static void Visit(JsonElement element, SourceRegistry registry, Configuration config, Dictionary<string, Evidence> output, int depth)
    {
        if (depth > 12) throw new WorkspaceException("grounding_invalid", "The source response is nested too deeply.");
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Visit(child, registry, config, output, depth + 1);
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("uid", out _))
            {
                var reference = Get(element, "id");
                if (reference.Length == 0) reference = Get(element, "ref_id");
                if (reference.Length == 0) reference = Get(element, "uid");
                Add(reference, element, registry, config, output);
            }
            else foreach (var child in element.EnumerateObject()) Visit(child.Value, registry, config, output, depth + 1);
        }
        else if (element.ValueKind == JsonValueKind.String && element.GetString() is { } text
            && (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[')))
        {
            using var parsed = JsonDocument.Parse(text);
            Visit(parsed.RootElement, registry, config, output, depth + 1);
        }
    }

    private static string Find(JsonElement element, string name, int depth = 0)
    {
        if (depth > 12) return "";
        if (element.ValueKind != JsonValueKind.Object) return "";
        if (element.TryGetProperty(name, out var direct) && direct.ValueKind == JsonValueKind.String) return direct.GetString()!;
        foreach (var child in element.EnumerateObject())
        {
            var found = Find(child.Value, name, depth + 1);
            if (found.Length > 0) return found;
        }
        return "";
    }

    private static void Add(string reference, JsonElement data, SourceRegistry registry, Configuration config, Dictionary<string, Evidence> output)
    {
        var document = registry.Documents.SingleOrDefault(item => item.BlobUrl == Find(data, "blob_url"));
        var passage = document?.Passages.SingleOrDefault(item => item.Id == Find(data, "uid"));
        var source = config.Sources.Documents.SingleOrDefault(item => item.Id == document?.Id);
        if (reference.Length == 0 || source is null || !source.Eligible(DateOnly.FromDateTime(DateTime.UtcNow))
            || document is null || passage is null || passage.Text != Find(data, "snippet")
            || passage.ImagePath != Find(data, "image_path"))
            throw new WorkspaceException("evidence_invalid", "The evidence failed source identity, lifecycle, or exact-passage checks.");
        var evidence = new Evidence(reference, passage.Id, document.Id, document.Title, document.Revision,
            document.Status, "Indexed passage", passage.Text, document.ContentHash, passage.ImagePath, "/documents/" + Key(document.Id));
        if (output.TryGetValue(reference, out var previous) && previous != evidence)
            throw new WorkspaceException("evidence_invalid", "A reference identifies more than one passage.");
        output[reference] = evidence;
    }

    private static Evidence[] Finish(Dictionary<string, Evidence> output, Configuration config)
    {
        if (output.Count > config.Sources.MaxPassages)
            throw new WorkspaceException("evidence_limit", "The source response exceeds its passage limit.");
        return output.Values.ToArray();
    }

    public static string EligibleFilter(Configuration config, SourceRegistry registry)
    {
        var eligible = registry.Documents.Where(document => config.Sources.Documents.Single(source => source.Id == document.Id)
            .Eligible(DateOnly.FromDateTime(DateTime.UtcNow))).Select(document => document.BlobUrl).ToArray();
        if (eligible.Length == 0 || eligible.Any(url => url.Contains('|') || url.Contains('\'')))
            throw new WorkspaceException("sources_unavailable", "No eligible source collection is available.");
        return "search.in(blob_url, '" + string.Join('|', eligible) + "', '|')";
    }
}
