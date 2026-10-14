using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Workspace.Core;
using Workspace.Tools;

try
{
    var root = Configuration.FindRoot();
    var command = args.FirstOrDefault() ?? "validate";
    string? Option(string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    var profile = Option("--profile");
    if (command == "prepare")
    {
        var app = Json.Read<AppConfig>(Path.Combine(root, "config", "app.json"));
        var selected = profile ?? app.ActiveProfile;
        if (!Configuration.Id(selected)) throw new WorkspaceException("profile_invalid", "Use a lowercase profile identifier.");
        var directory = Path.Combine(root, "profiles", selected);
        var path = Path.Combine(directory, "sources.json");
        var sources = Json.Read<SourcesDefinition>(path);
        var updated = sources.Documents.Select(source =>
        {
            var file = Configuration.AssetPath(directory, source.File);
            return source with { Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) };
        }).ToArray();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(sources with { Documents = updated }, Json.Options) + "\n");
    }
    var config = new Configuration(root, profile);
    if (command == "check-approval")
    {
        if (!config.Approved) throw new WorkspaceException("approval_required", "Review and approve the exact active profile digest.");
        Console.WriteLine("The active profile has matching approval.");
        return;
    }
    if (command is "validate" or "prepare")
    {
        EvaluationChecks.Read(config);
        Console.WriteLine($"Profile: {config.Profile.Id}\nDigest: {config.Digest}\nApproval: {(config.Approved ? "approved" : "required")}");
        return;
    }
    if (command == "approve")
    {
        if (Option("--digest") != config.Digest) throw new WorkspaceException("approval_invalid", "Supply --digest with the exact reviewed digest.");
        await File.WriteAllTextAsync(Path.Combine(config.Directory, "approval.json"),
            JsonSerializer.Serialize(new Approval(config.Digest, "approved"), Json.Options) + "\n");
        Console.WriteLine("Recorded approval for the supplied digest.");
        return;
    }
    if (command == "select")
    {
        if (!config.Approved || Option("--digest") != config.Digest)
            throw new WorkspaceException("approval_required", "Select only a reviewed profile with its exact approved digest.");
        await File.WriteAllTextAsync(Path.Combine(root, "config", "app.json"),
            JsonSerializer.Serialize(config.App with { ActiveProfile = config.Profile.Id }, Json.Options) + "\n");
        Console.WriteLine("Selected the approved profile. Restart the application after reconciling its cloud state.");
        return;
    }
    if (command == "schemas")
    {
        var options = new JsonSerializerOptions(Json.Options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var directory = Path.Combine(root, "config", "schemas");
        Directory.CreateDirectory(directory);
        Type[] types = [typeof(AppConfig), typeof(ModelsConfig), typeof(CorePolicy), typeof(ProfileDefinition),
            typeof(ThemeDefinition), typeof(ProcessDefinition), typeof(PromptDefinition), typeof(ExamplesDefinition),
            typeof(SourcesDefinition), typeof(Approval), typeof(PolicyContent), typeof(EvaluationCase[])];
        foreach (var type in types)
            await File.WriteAllTextAsync(Path.Combine(directory, type.Name.Replace("[]", "Array", StringComparison.Ordinal) + ".schema.json"),
                options.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true })
                    .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "Answer.schema.json"), PromptComposer.AnswerSchema.GetRawText() + "\n");
        Console.WriteLine("Exported configuration and response schemas.");
        return;
    }
    if (command == "parameters")
    {
        var output = new
        {
            schema = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
            contentVersion = "1.0.0.0",
            parameters = new
            {
                models = new { value = config.Models.Profiles.Where(model => model.Enabled).ToArray() },
                knowledgeBase = new { value = config.Sources.KnowledgeBase },
                connectionName = new { value = config.Sources.Connection },
                searchApiVersion = new { value = config.Sources.SearchApiVersion }
            }
        };
        var node = JsonSerializer.SerializeToNode(output, Json.Options)!;
        node["$schema"] = node["schema"]!.DeepClone();
        node.AsObject().Remove("schema");
        var path = Path.Combine(root, "infra", "resolved.parameters.json");
        await File.WriteAllTextAsync(path, node.ToJsonString(Json.Options) + "\n");
        Console.WriteLine("Generated local deployment parameters.");
        return;
    }
    if (command == "manifests")
    {
        var directory = Path.Combine(root, ".local", "agents");
        Directory.CreateDirectory(directory);
        foreach (var model in config.Models.Answers.Where(model => model.Provider == "openai"))
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
                        server_url = "${SEARCH_ENDPOINT}/knowledgebases/" + config.Sources.KnowledgeBase + "/mcp?api-version=" + config.Sources.SearchApiVersion,
                        allowed_tools = new { tool_names = new[] { "knowledge_base_retrieve" } }, require_approval = "never",
                        project_connection_id = config.Sources.Connection
                    } }
                }
            };
            await File.WriteAllTextAsync(Path.Combine(directory, model.Key + ".agent.yaml"), JsonSerializer.Serialize(manifest, Json.Options) + "\n");
        }
        Console.WriteLine("Generated runtime manifests from the current profile.");
        return;
    }
    if (command is "ingest" or "deploy-agents" or "evaluate")
    {
        if (!config.Approved) throw new WorkspaceException("approval_required", "Review and approve the profile before cloud operations.");
        using var cloud = new CloudClient(config, CloudSettings.FromEnvironment());
        using var telemetry = Sdk.CreateTracerProviderBuilder().AddSource("ProcessWorkspace")
            .AddAzureMonitorTraceExporter(options =>
            {
                options.ConnectionString = cloud.Settings.Insights;
                options.Credential = cloud.Credential;
            }).Build();
        await cloud.VerifyIdentityAsync(CancellationToken.None);
        if (command == "ingest")
        {
            await Ingestion.RunAsync(config, cloud, CancellationToken.None);
            return;
        }
        if (command == "deploy-agents")
        {
            var versions = new Dictionary<string, string>();
            foreach (var model in config.Models.Answers.Where(model => model.Provider == "openai"))
            {
                var path = Path.Combine(root, ".local", "agents", model.Key + ".agent.yaml");
                var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
                if (manifest["metadata"]?["profile_digest"]?.GetValue<string>() != config.Digest)
                    throw new WorkspaceException("manifest_stale", "Regenerate the runtime manifests before deployment.");
                var tools = manifest["definition"]!["tools"]!.AsArray();
                foreach (var tool in tools) tool!["server_url"] = tool["server_url"]!.GetValue<string>()
                    .Replace("${SEARCH_ENDPOINT}", cloud.Settings.Search.ToString().TrimEnd('/'), StringComparison.Ordinal);
                var body = new { metadata = manifest["metadata"], definition = manifest["definition"] };
                var response = await cloud.ProjectAsync(HttpMethod.Post, "agents/" + GroundedAssistant.AgentName(config, model) + "/versions?api-version=v1", body, CancellationToken.None);
                var version = EvidenceParser.Get(response, "version");
                if (EvidenceParser.Get(response, "status") != "active" || !int.TryParse(version, out var number) || number < 1)
                    throw new WorkspaceException("agent_inactive", "The new agent version did not become active.");
                versions.Add(model.Key, version);
            }
            var directory = Path.Combine(root, ".local");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "deployment-state.json"),
                JsonSerializer.Serialize(new DeploymentState(config.Digest, versions), Json.Options) + "\n");
            Console.WriteLine($"Pinned {versions.Count} prompt-agent versions.");
            return;
        }
        var state = Json.Read<DeploymentState>(Path.Combine(root, ".local", "deployment-state.json"));
        var assistant = new GroundedAssistant(config, cloud, state);
        var casesPath = Path.Combine(root, "evals", config.Profile.Id, "cases.json");
        var cases = EvaluationChecks.Read(config);
        var results = new List<object>();
        var failures = 0;
        var resultDirectory = Path.Combine(root, "evals", "results");
        Directory.CreateDirectory(resultDirectory);
        var resultPath = Path.Combine(resultDirectory, config.Profile.Id + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + ".json");
        foreach (var model in config.Models.Answers)
        foreach (var item in cases)
        {
            var answer = await assistant.AskAsync(new(item.Question, [], item.Images.Select(config.ReadImage).ToArray(), model.Key), CancellationToken.None);
            var ids = answer.Sources.Select(source => source.DocumentId).ToHashSet();
            var recall = item.ExpectedSources.Length == 0 ? 1d : (double)item.ExpectedSources.Count(ids.Contains) / item.ExpectedSources.Length;
            var passed = answer.Outcome == item.ExpectedOutcome && recall >= item.MinimumRecall
                && answer.Issues.Length == 0 && (answer.Outcome != "answered" || answer.Verification == "passed" && answer.Answer is not null);
            if (!passed) failures++;
            results.Add(new { item.Id, model = model.Key, passed, recall, result = answer });
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
            {
                profileDigest = config.Digest, casesHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(casesPath))),
                failures, results
            }, Json.Options));
            Console.WriteLine($"{item.Id} {model.Key}: {(passed ? "PASS" : "FAIL")} {answer.Outcome}");
        }
        if (failures > 0) throw new WorkspaceException("evaluation_failed", $"{failures} evaluation cases failed. Retain the recorded results.");
        return;
    }
    throw new WorkspaceException("command_invalid", "Use validate, prepare, approve, select, schemas, parameters, manifests, ingest, deploy-agents, or evaluate.");
}
catch (WorkspaceException error)
{
    Console.Error.WriteLine($"{error.Code}: {error.Message}");
    Environment.ExitCode = 1;
}
catch (Azure.RequestFailedException error)
{
    Console.Error.WriteLine($"Azure data operation failed ({error.Status}). Check the selected environment and scoped permissions.");
    Environment.ExitCode = 1;
}
catch (CloudException error)
{
    Console.Error.WriteLine($"Azure service operation failed ({error.Status}). No successful result was recorded.");
    Environment.ExitCode = 1;
}
catch (JsonException)
{
    Console.Error.WriteLine("response_format_invalid: A configuration or service response has an invalid format.");
    Environment.ExitCode = 1;
}
