using System.Text.Json;
using System.Text.Json.Nodes;
using Workspace.Core;

namespace Workspace.Tools;

public static class AgentManifests
{
    public static IReadOnlyDictionary<string, JsonObject> Create(Configuration config, string searchEndpoint = "${SEARCH_ENDPOINT}")
    {
        var manifests = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var model in config.Models.Answers)
        {
            var manifest = new
            {
                name = GroundedAssistant.AgentName(config, model),
                metadata = new { profile_digest = config.Digest, model_profile = model.Key },
                definition = new
                {
                    kind = "prompt", model = model.Deployment, instructions = PromptComposer.Instructions(config),
                    text = new { format = new { type = "json_schema", name = "grounded_answer", strict = true, schema = PromptComposer.AnswerSchema } },
                    tools = new[] { new
                    {
                        type = "mcp", server_label = "workspace-grounding",
                        server_url = searchEndpoint.TrimEnd('/') + "/knowledgebases/" + config.Sources.KnowledgeBase + "/mcp?api-version=" + config.Sources.SearchApiVersion,
                        allowed_tools = new { tool_names = new[] { "knowledge_base_retrieve" } }, require_approval = "never",
                        project_connection_id = config.Sources.Connection
                    } }
                }
            };
            manifests.Add(model.Key, JsonSerializer.SerializeToNode(manifest, Json.Options)!.AsObject());
        }
        return manifests;
    }

    public static void RequireCurrent(Configuration config, string key, JsonObject actual)
    {
        if (!Create(config).TryGetValue(key, out var expected) || !JsonNode.DeepEquals(actual, expected))
            throw new WorkspaceException("manifest_stale", "Regenerate runtime manifests from the current selection before deployment.");
    }
}
