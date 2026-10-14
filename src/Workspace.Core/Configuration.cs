using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workspace.Core;

public sealed class Configuration
{
    public string Root { get; }
    public string Directory { get; }
    public AppConfig App { get; }
    public ModelsConfig Models { get; }
    public CorePolicy Policy { get; }
    public ProfileDefinition Profile { get; }
    public Dictionary<string, string> Terminology { get; }
    public Dictionary<string, string> Ui { get; }
    public ThemeDefinition Theme { get; }
    public ProcessDefinition Process { get; }
    public PromptDefinition Prompts { get; }
    public ExamplesDefinition Examples { get; }
    public SourcesDefinition Sources { get; }
    public string Digest { get; }
    public bool Approved { get; }
    public string Text(string key) => Ui.TryGetValue(key, out var value) ? value
        : throw new WorkspaceException("configuration_invalid", $"ui.json: missing '{key}'.");

    public Configuration(string root, string? profile = null)
    {
        Root = Path.GetFullPath(root);
        App = Json.Read<AppConfig>(Path.Combine(Root, "config", "app.json"));
        Models = Json.Read<ModelsConfig>(Path.Combine(Root, "config", "models.json"));
        Policy = Json.Read<CorePolicy>(Path.Combine(Root, "config", "policy.core.json"));
        var id = profile ?? App.ActiveProfile;
        Require(Id(id), "app.json activeProfile", "Use a lowercase identifier.");
        Directory = Path.Combine(Root, "profiles", id);
        Require(!File.GetAttributes(Directory).HasFlag(FileAttributes.ReparsePoint), "profile.json", "Linked profile directories are not supported.");
        Profile = Read<ProfileDefinition>("profile.json");
        Terminology = Read<Dictionary<string, string>>("terminology.json");
        Ui = Read<Dictionary<string, string>>("ui.json");
        Theme = Read<ThemeDefinition>("theme.json");
        Process = Read<ProcessDefinition>("process.json");
        Prompts = Read<PromptDefinition>("prompts.json");
        Examples = Read<ExamplesDefinition>("examples.json");
        Sources = Read<SourcesDefinition>("sources.json");
        Validate(id);
        Digest = ComputeDigest();
        var approvalPath = Path.Combine(Directory, "approval.json");
        Approved = File.Exists(approvalPath) && Json.Read<Approval>(approvalPath) is { Decision: "approved" } approval
            && approval.Digest == Digest;
    }

    private T Read<T>(string name) => Json.Read<T>(Path.Combine(Directory, name));
    public static bool Id(string value) => Regex.IsMatch(value, "^[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant);
    public static void Require(bool condition, string location, string message)
    {
        if (!condition) throw new WorkspaceException("configuration_invalid", $"{location}: {message}");
    }

    public string Asset(string relative) => AssetPath(Directory, relative);

    public static string AssetPath(string directory, string relative)
    {
        var normalized = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(directory, normalized));
        Require(!File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)
            && path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint), relative, "Use an existing local profile asset.");
        for (var parent = Path.GetDirectoryName(path); parent is not null && parent.Length > directory.Length; parent = Path.GetDirectoryName(parent))
            Require(!File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint), relative, "Linked asset directories are not supported.");
        return path;
    }

    public string Expand(string text) => Regex.Replace(text, @"\{\{([a-zA-Z][a-zA-Z0-9]*)\}\}", match =>
        Terminology.TryGetValue(match.Groups[1].Value, out var value) ? value
        : throw new WorkspaceException("configuration_invalid", $"prompts.json: unknown placeholder {match.Value}."));

    private void Validate(string selected)
    {
        Require(App.SchemaVersion == 1 && Profile.SchemaVersion == 1 && Profile.Id == selected, "profile.json", "Unsupported schema or mismatched profile ID.");
        Require(App.Port is >= 1024 and <= 65535 && App.StoragePath == ".local/records", "app.json", "Use a supported port and the local record directory.");
        Require(Profile.AllowedTopics.Length > 0 && Profile.AllowedTopics.All(topic => !string.IsNullOrWhiteSpace(topic))
            && !string.IsNullOrWhiteSpace(Profile.Purpose) && !string.IsNullOrWhiteSpace(Profile.Customer)
            && !string.IsNullOrWhiteSpace(Profile.UseCase) && !string.IsNullOrWhiteSpace(Profile.Audience),
            "profile.json", "State the customer context, use case, audience, purpose, and allowed topics.");
        Require(Policy.Id == "evidence-v1" && Policy.Rules.Length >= 6, "policy.core.json", "The shared evidence policy is required.");
        Require(Models.Profiles.Select(model => model.Key).Distinct().Count() == Models.Profiles.Length, "models.json profiles", "Model keys must be unique.");
        foreach (var model in Models.Profiles)
            Require(Id(model.Key) && Id(model.Deployment) && Id(model.Provider)
                && model.Role is "answer" or "verification" or "embedding" or "extraction"
                && model.Capacity > 0 && model.MaxOutputTokens is > 0 and <= 16000
                && !string.IsNullOrWhiteSpace(model.Model) && !string.IsNullOrWhiteSpace(model.Version)
                && !string.IsNullOrWhiteSpace(model.Sku) && !string.IsNullOrWhiteSpace(model.Format),
                $"models.json {model.Key}", "Invalid deployment, provider format, or limits.");
        Require(Models.Selection.Count > 0 && Models.Selection.Count == Models.Answers.Length
            && !string.IsNullOrWhiteSpace(Models.Selection.CapabilityMix),
            "models.json selection", "The requested model count must match enabled answer profiles and include a capability mix.");
        Require(Models.Selection.ExplicitModelKeys.Length == 0
            || Models.Selection.ExplicitModelKeys.Length == Models.Selection.Count
                && Models.Selection.ExplicitModelKeys.Distinct().Count() == Models.Selection.Count
                && Models.Selection.ExplicitModelKeys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(Models.Answers.Select(model => model.Key)),
            "models.json selection.explicitModelKeys", "Explicit model choices must match the enabled answer profiles exactly.");
        foreach (var model in Models.Answers)
            Require(model.Capabilities is { PromptAgent: true, Mcp: true, StructuredOutputs: true },
                $"models.json {model.Key}.capabilities",
                "Selected answer models must support Foundry prompt agents, MCP retrieval, and structured outputs. No native-API fallback is available.");
        Require(Models.Get(Models.DefaultProfile).Role == "answer", "models.json defaultProfile", "Select an answer model.");
        Require(Models.Get(Models.VerificationProfile) is { Role: "verification", Provider: "openai" }, "models.json verificationProfile", "Select an enabled OpenAI verifier.");
        Require(Models.Get(Models.EmbeddingProfile) is { Role: "embedding", Provider: "openai" }
            && Models.Get(Models.ExtractionProfile) is { Role: "extraction", Provider: "openai" },
            "models.json", "Select enabled embedding and extraction models.");
        Require(Models.Profiles.Where(model => model.Enabled).Select(model => model.Deployment).Distinct().Count()
            == Models.Profiles.Count(model => model.Enabled), "models.json", "Enabled deployment names must be unique.");
        Require(Models.Answers.All(model => GroundedAssistant.AgentName(this, model).Length <= 63),
            "models.json", "The combined profile and answer-model identifier must fit the agent name limit.");
        Require(Id(Process.Id) && Process.Stages.Length > 0 && Process.Fields.Length > 0
            && Process.Fields.Select(field => field.Id).Distinct().Count() == Process.Fields.Length
            && Process.Stages.Select(stage => stage.Id).Distinct().Count() == Process.Stages.Length, "process.json", "Use unique field and stage identifiers.");
        foreach (var field in Process.Fields)
        {
            Require(Id(field.Id) && Process.Stages.Any(stage => stage.Id == field.Stage)
                && field.Type is "text" or "multiline" or "select" or "multiselect" or "date" or "number" or "boolean"
                && !string.IsNullOrWhiteSpace(field.Label) && field.MaxLength is null or > 0 and <= 4000
                && (field.Min is null || field.Max is null || field.Min <= field.Max), $"process.json fields.{field.Id}", "Invalid type, stage, label, or bounds.");
            Require(field.Type is not ("select" or "multiselect") || field.Options.Length > 0
                && field.Options.Distinct().Count() == field.Options.Length, $"process.json fields.{field.Id}.options", "Provide unique options.");
        }
        foreach (var field in Process.RegisterColumns.Concat(Process.SummaryFields).Concat(Process.AssessmentFields).Append(Process.ReferenceField))
            Require(Process.Fields.Any(item => item.Id == field), $"process.json {field}", "Unknown field reference.");
        Require(Process.AssessmentFields.Length > 0, "process.json assessmentFields", "Select assessment inputs.");
        Require(Examples.Prompts.Select(item => item.Id).Distinct().Count() == Examples.Prompts.Length,
            "examples.json", "Prompt identifiers must be unique.");
        foreach (var example in Examples.Prompts)
        {
            Require(Id(example.Id) && example.Question.Length is > 0 and <= 4000 && example.Images.Length <= 5,
                $"examples.json {example.Id}", "Invalid question or image count.");
            foreach (var image in example.Images) _ = ImageValidation.Normalize([ReadImage(image)]);
        }
        ProcessValidation.Validate(Process, Examples.FormValues);
        Require(Sources.Documents.Length > 0 && Sources.Documents.Select(source => source.Id).Distinct().Count() == Sources.Documents.Length
            && Sources.MaxPassages is > 0 and <= 16, "sources.json", "Provide unique documents and a bounded retrieval limit.");
        foreach (var name in new[] { Sources.Collection, Sources.Container, Sources.AssetContainer, Sources.KnowledgeSource,
            Sources.KnowledgeBase, Sources.Index, Sources.Connection })
            Require(Id(name), "sources.json", "Use lowercase resource identifiers.");
        Require(Sources.SearchApiVersion == "2026-08-01-preview", "sources.json searchApiVersion", "Use the supported retrieval API contract.");
        foreach (var source in Sources.Documents)
        {
            Require(Regex.IsMatch(source.Id, "^[A-Z0-9][A-Z0-9-]{0,63}$") && source.Kind is "authored" or "supplied"
                && source.Status is "current" or "withdrawn" or "unknown"
                && !string.IsNullOrWhiteSpace(source.StatusEvidence) && !string.IsNullOrWhiteSpace(source.Rights)
                && !string.IsNullOrWhiteSpace(source.Publisher) && !string.IsNullOrWhiteSpace(source.Origin)
                && !string.IsNullOrWhiteSpace(source.Title) && !string.IsNullOrWhiteSpace(source.Revision),
                $"sources.json {source.Id}", "Source provenance, rights, lifecycle evidence, and identity are required.");
            Require(Path.GetExtension(source.File) is ".json" or ".pdf", $"sources.json {source.Id}.file", "Use a policy JSON document or PDF.");
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Asset(source.File))));
            Require(source.Sha256 == actual, $"sources.json {source.Id}.sha256", "Content changed. Prepare and review the updated profile.");
            if (Path.GetExtension(source.File) == ".json")
            {
                var content = Json.Read<PolicyContent>(Asset(source.File));
                Require(content.Title == source.Title && content.Sections.Length > 0
                    && content.Sections.All(section => !string.IsNullOrWhiteSpace(section.Heading) && !string.IsNullOrWhiteSpace(section.Text)),
                    $"sources.json {source.Id}", "The policy needs a matching title and complete sections.");
            }
            else Require(File.ReadAllBytes(Asset(source.File)).AsSpan().StartsWith("%PDF-"u8),
                $"sources.json {source.Id}", "The supplied original is not a PDF.");
        }
        Require(Sources.Index == Sources.Collection + "-documents-index", "sources.json index", "Use the native ingestion index for this collection.");
        Require(Sources.Documents.Select(source => source.Kind).Distinct().Count() == 1, "sources.json", "Keep different source authority in separate collections.");
        Require(Theme.Layout is "comfortable" or "compact", "theme.json layout", "Select a supported layout.");
        Require(Theme.Font is "system" or "humanist" or "serif" && Theme.TextSize is >= 14 and <= 18,
            "theme.json", "Select a supported font and a text size from 14 to 18.");
        string[] colors = ["ink", "muted", "primary", "onPrimary", "canvas", "surface", "border", "focus", "danger", "banner"];
        Require(Theme.Tokens.Count == colors.Length && colors.All(Theme.Tokens.ContainsKey), "theme.json tokens", "Provide the supported color tokens.");
        foreach (var color in Theme.Tokens)
            Require(Regex.IsMatch(color.Value, "^#[0-9a-fA-F]{6}$"), $"theme.json {color.Key}", "Use a six-digit hex color.");
        Require(Contrast(Theme.Tokens["ink"], Theme.Tokens["surface"]) >= 4.5
            && Contrast(Theme.Tokens["muted"], Theme.Tokens["surface"]) >= 4.5
            && Contrast(Theme.Tokens["primary"], Theme.Tokens["onPrimary"]) >= 4.5
            && Contrast("#ffffff", Theme.Tokens["banner"]) >= 4.5, "theme.json", "Text contrast must be at least 4.5:1.");
        if (Theme.Logo is not null)
        {
            Require(!string.IsNullOrWhiteSpace(Theme.LogoAlt), "theme.json logoAlt", "Describe the logo.");
            _ = ImageValidation.Normalize([ReadImage(Theme.Logo)]);
        }
        foreach (var key in UiKeys) _ = Text(key);
        foreach (var text in new[] { Prompts.TaskInstructions, Prompts.ProcessInstructions, Prompts.VerificationInstructions })
        {
            var expanded = Expand(text);
            Require(!expanded.Contains("{{", StringComparison.Ordinal) && !expanded.Contains("}}", StringComparison.Ordinal),
                "prompts.json", "Resolve every placeholder.");
        }
    }

    public UploadImage ReadImage(string path) => new(Path.GetFileName(path),
        Path.GetExtension(path) == ".png" ? "image/png" : "image/jpeg", File.ReadAllBytes(Asset(path)));

    private string ComputeDigest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string[] names = ["profile.json", "terminology.json", "ui.json", "theme.json", "process.json", "prompts.json", "examples.json", "sources.json"];
        var files = names.Select(name => Path.Combine(Directory, name))
            .Concat(new[] { Path.Combine(Root, "config", "models.json"), Path.Combine(Root, "config", "policy.core.json") })
            .Concat(Sources.Documents.Select(source => Asset(source.File)))
            .Concat(Examples.Prompts.SelectMany(example => example.Images).Select(Asset))
            .Concat(Theme.Logo is null ? [] : new[] { Asset(Theme.Logo) }).Distinct().Order(StringComparer.Ordinal).ToArray();
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(Root, file).Replace('\\', '/')));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData([0]);
        }
        foreach (var contract in new[] { PromptComposer.Instructions(this), PromptComposer.VerifierInstructions(this),
            PromptComposer.AnswerSchema.GetRawText(), PromptComposer.VerificationSchema.GetRawText() })
        {
            hash.AppendData(Encoding.UTF8.GetBytes(contract));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static double Contrast(string first, string second)
    {
        static double Luminance(string color)
        {
            double Channel(int offset)
            {
                var value = int.Parse(color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(1) + .7152 * Channel(3) + .0722 * Channel(5);
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    public static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Workspace.sln"))) return current.FullName;
        throw new WorkspaceException("configuration_missing", "Run the application from its repository.");
    }

    public static readonly string[] UiKeys = [
        "title", "chatNav", "processNav", "chatHeading", "chatHelp", "questionLabel", "questionPlaceholder",
        "ask", "newConversation", "modelLabel", "imagesLabel", "imagesHelp", "removeImage",
        "promptHeading", "promptHelp", "searchLabel", "searchPlaceholder", "search", "answerHeading",
        "sourcesHeading", "limitsHeading", "imagesHeading", "rawEvidenceHelp", "openDocument", "noAnswer",
        "busy", "ready", "preview", "unavailable", "insufficientEvidence", "clarificationRequired", "outOfScope",
        "verificationFailed", "processHeading", "processHelp", "createRecord", "registerHeading", "emptyRegister",
        "referenceColumn", "outcomeColumn", "recordedColumn", "submitProcess", "fillExample", "back",
        "assessmentHeading", "detailsHeading", "evidenceAtTime", "feedbackUseful", "feedbackIncorrect",
        "feedbackUnsupported", "footer", "imageUncertainty", "validationError", "requestFailed", "loading",
        "answered", "saved", "skipLink", "close", "usageLabel", "tokenLabel", "sourceCount", "questionCount",
        "previousPage", "nextPage", "pageLabel"
    ];
}
