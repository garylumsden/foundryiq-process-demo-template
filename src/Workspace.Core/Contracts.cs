using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workspace.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        WriteIndented = true,
        NewLine = "\n"
    };

    public static T Read<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new WorkspaceException("configuration_invalid", $"{Path.GetFileName(path)}: the document is empty.");
        }
        catch (JsonException error)
        {
            throw new WorkspaceException("configuration_invalid", $"{Path.GetFileName(path)} {error.Path}: invalid JSON or unsupported properties.");
        }
    }
}

public sealed class WorkspaceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class CloudException(string operation, int status) : Exception($"Cloud request failed: {operation} ({status}).")
{
    public string Operation { get; } = operation;
    public int Status { get; } = status;
}

public sealed record AppConfig(int SchemaVersion, string ActiveProfile, int Port, string StoragePath);
public sealed record ModelCapabilities(bool PromptAgent, bool Mcp, bool StructuredOutputs, bool Images);
public sealed record ModelSelection(int Count, string CapabilityMix, string[] ExplicitModelKeys);
public sealed record ModelProfile(string Key, string Label, string Role, string Provider, string Format, string Deployment,
    string Model, string Version, string Sku, int Capacity, int MaxOutputTokens, bool Enabled, ModelCapabilities Capabilities);
public sealed record ModelsConfig(ModelSelection Selection, string DefaultProfile, string VerificationProfile, string EmbeddingProfile,
    string ExtractionProfile, ModelProfile[] Profiles)
{
    public ModelProfile Get(string key) => Profiles.SingleOrDefault(profile => profile.Key == key && profile.Enabled)
        ?? throw new WorkspaceException("model_invalid", "Select an enabled model profile.");
    public ModelProfile[] Answers => Profiles.Where(profile => profile.Enabled && profile.Role == "answer").ToArray();
}
public sealed record CorePolicy(string Id, string[] Rules);
public sealed record ProfileDefinition(int SchemaVersion, string Id, string Version, string Title, string Industry,
    string Customer, string UseCase, string Audience,
    string Purpose, string[] AllowedTopics, string[] ExcludedTopics);
public sealed record ThemeDefinition(string Layout, string Font, int TextSize, Dictionary<string, string> Tokens, string? Logo, string LogoAlt);
public sealed record StageDefinition(string Id, string Label);
public sealed record FieldDefinition(string Id, string Label, string Type, bool Required, string Stage,
    string Help, string Placeholder, string[] Options, int? MaxLength, decimal? Min, decimal? Max);
public sealed record ProcessDefinition(string Id, string Version, string Name, StageDefinition[] Stages,
    FieldDefinition[] Fields, string ReferenceField, string[] RegisterColumns, string[] SummaryFields, string[] AssessmentFields);
public sealed record PromptDefinition(string TaskInstructions, string ProcessInstructions, string[] RetrievalTerms,
    string VerificationInstructions);
public sealed record PromptExample(string Id, string Group, string Title, string Question, bool Featured, string[] Images);
public sealed record ExamplesDefinition(PromptExample[] Prompts, Dictionary<string, JsonElement> FormValues);
public sealed record SourceDefinition(string Id, string Title, string Revision, string Kind, string Status,
    string StatusEvidence, DateOnly? EffectiveFrom, DateOnly? WithdrawnAt, string File, string Sha256,
    string Publisher, string Origin, string Rights)
{
    public DateOnly? Published { get; init; }
    public bool Eligible(DateOnly today) => Status == "current" && (EffectiveFrom is null || EffectiveFrom <= today)
        && (WithdrawnAt is null || WithdrawnAt > today) && (Published is null || Published <= today);
}
public sealed record SourcesDefinition(string Collection, string Version, string SearchApiVersion,
    string Container, string AssetContainer, string KnowledgeSource, string KnowledgeBase, string Index,
    string Connection, int MaxPassages, SourceDefinition[] Documents);
public sealed record Approval(string Digest, string Decision);
public sealed record PolicySection(string Heading, string Text);
public sealed record PolicyContent(string Title, PolicySection[] Sections);
public sealed record NativePassage(string Id, string Text, string ImagePath);
public sealed record RegistryDocument(string Id, string Title, string Revision, string Status, DateOnly? EffectiveFrom,
    DateOnly? WithdrawnAt, string BlobUrl, string BlobName, string ContentHash, string DefinitionHash, NativePassage[] Passages)
{
    public DateOnly? Published { get; init; }
}
public sealed record SourceRegistry(string ProfileId, string ProfileDigest, string Collection, string CorpusVersion,
    RegistryDocument[] Documents);
public sealed record Evidence(string RefId, string ChunkId, string DocumentId, string Title, string Revision,
    string Status, string Section, string Text, string SourceHash, string ImagePath, string DocumentUrl);
public sealed record Statement(string Text, string[] References);
public sealed record DocumentSummary(string DocumentId, string Text, string[] References);
public sealed record Quotation(string Text, string Reference);
public sealed record ImageObservation(int Image, string Description, string Uncertainty);
public sealed record AgentAnswer(string Outcome, Statement[] Overview, DocumentSummary[] Summaries,
    Quotation[] Quotations, ImageObservation[] Images, string[] Limitations);
public sealed record AnswerIssue(string Code, string Location, string Message);
public sealed record UploadImage(string Name, string MimeType, byte[] Bytes);
public sealed record QuestionInput(string Question, string[] PreviousQuestions, UploadImage[] Images, string Model);
public sealed record Usage(int InputTokens, int OutputTokens)
{
    public static Usage operator +(Usage left, Usage right) => new(left.InputTokens + right.InputTokens, left.OutputTokens + right.OutputTokens);
}
public sealed record ClaimDecision(string Id, string Decision);
public sealed record VerificationResult(string Scope, ClaimDecision[] Claims);
public sealed record AnswerResult(string Outcome, AgentAnswer? Answer, Evidence[] Sources, string[] Messages,
    AnswerIssue[] Issues, Usage Usage, string TraceId, string ProfileDigest, string Model, string Verification);
public sealed record SearchResult(Evidence[] Documents, int Page, int PageSize, int Total, bool HasNext);
public sealed record ProcessRecord(string Id, string ProfileId, string ProfileDigest, string ProfileVersion,
    ProcessDefinition Process, Dictionary<string, JsonElement> Values, AnswerResult Assessment, DateTimeOffset RecordedAt);
public sealed record EvaluationCase(string Id, string Question, string[] ExpectedSources, string ExpectedOutcome,
    string[] Images, double MinimumRecall);
public sealed record DeploymentTarget(Guid TenantId, Uri Foundry, Uri Project, Uri Search, Uri Storage)
{
    public static DeploymentTarget FromSettings(CloudSettings settings) => new(Guid.Parse(settings.TenantId),
        Normalize(settings.Foundry), Normalize(settings.Project), Normalize(settings.Search), Normalize(settings.Storage));

    private static Uri Normalize(Uri endpoint) => new(endpoint.AbsoluteUri.TrimEnd('/'));
}

public sealed record DeploymentState(string ProfileDigest, DeploymentTarget Target, Dictionary<string, string> AgentVersions)
{
    public void RequireComplete(Configuration config, CloudSettings settings)
    {
        if (ProfileDigest != config.Digest)
            throw new WorkspaceException("deployment_mismatch", "The deployed agents do not match the active profile.");
        if (Target != DeploymentTarget.FromSettings(settings))
            throw new WorkspaceException("deployment_target_mismatch", "The pinned agents belong to a different Azure target. Deploy the agents for the selected target.");
        var selected = config.Models.Answers.Select(model => model.Key).ToHashSet(StringComparer.Ordinal);
        if (AgentVersions.Count != selected.Count || AgentVersions.Keys.Any(key => !selected.Contains(key))
            || selected.Any(key => !AgentVersions.TryGetValue(key, out var version)
                || !int.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                || number < 1))
            throw new WorkspaceException("agent_versions_incomplete", "Every selected answer model needs its own pinned deployed agent version.");
    }
}
