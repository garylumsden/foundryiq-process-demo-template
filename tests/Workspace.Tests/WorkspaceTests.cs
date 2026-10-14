using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using Workspace.Core;
using Workspace.Tools;

namespace Workspace.Tests;

public sealed class WorkspaceTests
{
    private static readonly string Root = Configuration.FindRoot();

    [Theory]
    [InlineData("operational-request-review")]
    [InlineData("retail-supplier-onboarding")]
    [InlineData("document-change-review")]
    public void CompleteProfilesValidateWithoutChangingSourceCode(string profile)
    {
        var config = new Configuration(Root, profile);
        Assert.Equal(profile, config.Profile.Id);
        Assert.Matches("^[A-F0-9]{64}$", config.Digest);
        Assert.NotEmpty(config.Sources.Documents);
        Assert.NotEmpty(config.Examples.Prompts);
        Assert.Equal(config.Sources.Collection + "-documents-index", config.Sources.Index);
        var question = ProcessValidation.Question(config, config.Examples.FormValues);
        Assert.InRange(question.Length, 1, 4000);
        Assert.DoesNotContain("{{", PromptComposer.Instructions(config));
        var cases = Json.Read<EvaluationCase[]>(Path.Combine(Root, "evals", profile, "cases.json"));
        Assert.Contains(cases, item => item.ExpectedOutcome == "out_of_scope");
        Assert.Contains(cases, item => item.ExpectedOutcome == "insufficient_evidence");
        Assert.All(cases, item => Assert.All(item.ExpectedSources, source => Assert.Contains(config.Sources.Documents, document => document.Id == source)));
    }

    [Fact]
    public void ProfilesUseDifferentFieldsPromptsAndThemes()
    {
        var first = new Configuration(Root, "operational-request-review");
        var second = new Configuration(Root, "retail-supplier-onboarding");
        var third = new Configuration(Root, "document-change-review");
        Assert.NotEqual(first.Process.Fields.Select(field => field.Id), second.Process.Fields.Select(field => field.Id));
        Assert.NotEqual(second.Process.Fields.Select(field => field.Id), third.Process.Fields.Select(field => field.Id));
        Assert.NotEqual(first.Prompts.TaskInstructions, second.Prompts.TaskInstructions);
        Assert.NotEqual(second.Theme.Tokens["primary"], third.Theme.Tokens["primary"]);
    }

    [Fact]
    public void BannerContrastUsesTheConfiguredForeground()
    {
        using var copy = new RepositoryCopy();
        copy.Change("profiles/operational-request-review/theme.json", node =>
        {
            foreach (var token in new[] { "ink", "muted", "primary", "focus", "danger" })
                node["tokens"]![token] = "#FFFFFF";
            foreach (var token in new[] { "surface", "canvas", "onPrimary", "banner" })
                node["tokens"]![token] = "#000000";
        });
        var error = Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
        Assert.Contains("onPrimary/banner", error.Message);
        copy.Change("profiles/operational-request-review/theme.json", node => node["tokens"]!["banner"] = "#FFFFFF");
        _ = new Configuration(copy.Root);
    }

    [Theory]
    [InlineData("primary", "surface")]
    [InlineData("danger", "surface")]
    [InlineData("focus", "surface")]
    [InlineData("ink", "canvas")]
    [InlineData("muted", "canvas")]
    public void ConfiguredTextAndFocusMustContrastWithTheirBackgrounds(string foreground, string background)
    {
        using var copy = new RepositoryCopy();
        copy.Change("profiles/operational-request-review/theme.json", node =>
            node["tokens"]![foreground] = node["tokens"]![background]!.GetValue<string>());
        Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
    }

    [Fact]
    public void TintedStatesCannotReduceTextBelowTheContrastThreshold()
    {
        using var copy = new RepositoryCopy();
        Assert.True(Configuration.Contrast("#767676", "#FFFFFF") >= 4.5);
        copy.Change("profiles/operational-request-review/theme.json", node =>
        {
            node["tokens"]!["primary"] = "#767676";
            node["tokens"]!["canvas"] = "#FFFFFF";
        });
        var error = Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
        Assert.Contains("primary/surface", error.Message);
    }

    [Fact]
    public void ContrastCalculationPreservesRequiredThresholds()
    {
        Assert.Equal(21, Configuration.Contrast("#000000", "#FFFFFF"), 6);
        Assert.Equal(1, Configuration.Contrast("#000000", "#000000"), 6);
        Assert.True(Configuration.Contrast("#777777", "#FFFFFF") < 4.5);
        Assert.True(Configuration.Contrast("#767676", "#FFFFFF") >= 4.5);
        Assert.True(Configuration.Contrast("#959595", "#FFFFFF") < 3);
        Assert.True(Configuration.Contrast("#949494", "#FFFFFF") >= 3);
    }

    [Fact]
    public void OneManifestIsCreatedForEverySelectedAnswerModel()
    {
        using var copy = TwoModelCopy();
        var config = new Configuration(copy.Root);
        var manifests = AgentManifests.Create(config);
        Assert.Equal(2, config.Models.Selection.Count);
        Assert.Equal(2, manifests.Count);
        Assert.Equal(2, manifests.Values.Select(manifest => manifest["name"]!.GetValue<string>()).Distinct().Count());
        foreach (var model in config.Models.Answers)
        {
            var manifest = manifests[model.Key];
            Assert.Equal(GroundedAssistant.AgentName(config, model), manifest["name"]!.GetValue<string>());
            Assert.Equal(model.Deployment, manifest["definition"]!["model"]!.GetValue<string>());
            Assert.Equal(config.Digest, manifest["metadata"]!["profile_digest"]!.GetValue<string>());
            Assert.Equal("workspace-grounding", manifest["definition"]!["tools"]![0]!["server_label"]!.GetValue<string>());
            Assert.Equal("json_schema", manifest["definition"]!["text"]!["format"]!["type"]!.GetValue<string>());
            AgentManifests.RequireCurrent(config, model.Key, manifest);
        }
        Assert.DoesNotContain(config.Models.VerificationProfile, manifests.Keys);
        Assert.DoesNotContain(config.Models.EmbeddingProfile, manifests.Keys);
        Assert.DoesNotContain(config.Models.ExtractionProfile, manifests.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RequestedModelCountMustMatchEnabledAnswers(int count)
    {
        using var copy = TwoModelCopy();
        copy.Change("config/models.json", node => node["selection"]!["count"] = count);
        Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
    }

    [Fact]
    public void ExplicitModelChoicesCannotBeSilentlySubstituted()
    {
        using var copy = TwoModelCopy();
        copy.Change("config/models.json", node => node["selection"]!["explicitModelKeys"] = new JsonArray("gpt", "terra"));
        _ = new Configuration(copy.Root);
        copy.Change("config/models.json", node => node["selection"]!["explicitModelKeys"] = new JsonArray("gpt", "missing"));
        Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
    }

    [Theory]
    [InlineData("promptAgent")]
    [InlineData("mcp")]
    [InlineData("structuredOutputs")]
    public void SelectedModelsMustSupportTheManagedAgentContract(string capability)
    {
        using var copy = TwoModelCopy();
        copy.Change("config/models.json", node => node["profiles"]![1]!["capabilities"]![capability] = false);
        Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
    }

    [Fact]
    public void ManifestGenerationDoesNotFilterByProviderName()
    {
        using var copy = TwoModelCopy();
        copy.Change("config/models.json", node =>
        {
            node["profiles"]![1]!["provider"] = "partner";
            node["profiles"]![1]!["format"] = "CatalogFormat";
        });
        var config = new Configuration(copy.Root);
        Assert.Equal(2, AgentManifests.Create(config).Count);
    }

    [Fact]
    public void AgentCompletionRequiresEverySelectedVersionAndRejectsExtras()
    {
        using var copy = TwoModelCopy();
        var config = new Configuration(copy.Root);
        var settings = Settings();
        var complete = new DeploymentState(config.Digest, DeploymentTarget.FromSettings(settings), new() { ["gpt"] = "1", ["terra"] = "2" });
        complete.RequireComplete(config, settings);
        Assert.Throws<WorkspaceException>(() => (complete with { ProfileDigest = "stale" }).RequireComplete(config, settings));
        Assert.Throws<WorkspaceException>(() => (complete with { AgentVersions = new() { ["gpt"] = "1" } }).RequireComplete(config, settings));
        Assert.Throws<WorkspaceException>(() => (complete with { AgentVersions = new() { ["gpt"] = "1", ["terra"] = "0" } }).RequireComplete(config, settings));
        Assert.Throws<WorkspaceException>(() => (complete with { AgentVersions = new() { ["gpt"] = "1", ["terra"] = "2", ["extra"] = "3" } }).RequireComplete(config, settings));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("foundry")]
    [InlineData("project")]
    [InlineData("search")]
    [InlineData("storage")]
    public void PinnedAgentsCannotBeUsedAgainstAnotherCloudTarget(string changed)
    {
        var config = new Configuration(Root);
        var settings = Settings();
        var state = new DeploymentState(config.Digest, DeploymentTarget.FromSettings(settings), new() { ["gpt"] = "1" });
        var other = changed switch
        {
            "tenant" => settings with { TenantId = Guid.NewGuid().ToString() },
            "foundry" => settings with { Foundry = new("https://other.openai.azure.com") },
            "project" => settings with { Project = new("https://workspace.services.ai.azure.com/api/projects/other") },
            "search" => settings with { Search = new("https://other.search.windows.net") },
            "storage" => settings with { Storage = new("https://other.blob.core.windows.net") },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        var error = Assert.Throws<WorkspaceException>(() => state.RequireComplete(config, other));
        Assert.Equal("deployment_target_mismatch", error.Code);
        using var cloud = new CloudClient(config, other);
        Assert.Throws<WorkspaceException>(() => new GroundedAssistant(config, cloud, state));
    }

    [Fact]
    public void DeploymentTargetNormalizesEndpointFormatting()
    {
        var settings = Settings();
        Assert.Equal(DeploymentTarget.FromSettings(settings), DeploymentTarget.FromSettings(settings with
        {
            Project = new("https://WORKSPACE.services.ai.azure.com/api/projects/process/"),
            Search = new("https://workspace.search.windows.net/")
        }));
    }

    [Fact]
    public void LegacyDeploymentStateWithoutTargetIdentityIsRejected()
    {
        using var copy = new RepositoryCopy();
        var path = Path.Combine(copy.Root, "deployment-state.json");
        File.WriteAllText(path, """{"profileDigest":"previous","agentVersions":{"gpt":"1"}}""");
        var error = Assert.Throws<WorkspaceException>(() => Json.Read<DeploymentState>(path));
        Assert.Equal("configuration_invalid", error.Code);
    }

    [Fact]
    public void AManifestCannotKeepTheDigestWhileChangingItsModel()
    {
        var config = new Configuration(Root);
        var manifest = AgentManifests.Create(config)[config.Models.DefaultProfile];
        manifest["definition"]!["model"] = "unselected-deployment";
        Assert.Throws<WorkspaceException>(() => AgentManifests.RequireCurrent(config, config.Models.DefaultProfile, manifest));
    }

    [Fact]
    public void ChangingModelSelectionInvalidatesApproval()
    {
        using var copy = new RepositoryCopy();
        var original = new Configuration(copy.Root);
        File.WriteAllText(Path.Combine(original.Directory, "approval.json"),
            JsonSerializer.Serialize(new Approval(original.Digest, "approved"), Json.Options));
        copy.Change("config/models.json", node => node["selection"]!["capabilityMix"] = "Fast responses with image input");
        Assert.False(new Configuration(copy.Root).Approved);
    }

    [Fact]
    public async Task ImageInputIsRejectedBeforeCallingAnIncompatibleModel()
    {
        using var copy = new RepositoryCopy();
        copy.Change("config/models.json", node => node["profiles"]![0]!["capabilities"]!["images"] = false);
        var config = new Configuration(copy.Root);
        using var cloud = new CloudClient(config, Settings());
        var assistant = new GroundedAssistant(config, cloud, new(config.Digest, DeploymentTarget.FromSettings(cloud.Settings), new() { ["gpt"] = "1" }));
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => assistant.AskAsync(
            new("Review this input.", [], [new("input.png", "image/png", [1])], "gpt"), CancellationToken.None));
        Assert.Equal("images_not_supported", error.Code);
    }

    [Fact]
    public void ImageEvaluationsRequireCompatibleSelections()
    {
        using var copy = new RepositoryCopy();
        copy.Change("config/models.json", node => node["profiles"]![0]!["capabilities"]!["images"] = false);
        var config = new Configuration(copy.Root);
        var item = new EvaluationCase("image-case", "Review the visible context.", [config.Sources.Documents[0].Id], "answered", ["input.png"], .9);
        var error = Assert.Throws<WorkspaceException>(() => EvaluationChecks.Validate(config, [item]));
        Assert.Equal("cases_incompatible", error.Code);
    }

    private static RepositoryCopy TwoModelCopy()
    {
        var copy = new RepositoryCopy();
        copy.Change("config/models.json", node =>
        {
            node["selection"]!["count"] = 2;
            node["selection"]!["capabilityMix"] = "Balanced and deeper reasoning";
            node["profiles"]![1]!["enabled"] = true;
        });
        return copy;
    }

    [Fact]
    public void UnknownConfigurationPropertiesFailWithAPropertyPath()
    {
        using var copy = new RepositoryCopy();
        copy.Change("profiles/operational-request-review/profile.json", node => node["allowUnverifiedAnswers"] = true);
        var error = Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
        Assert.Contains("profile.json", error.Message);
        Assert.Contains("allowUnverifiedAnswers", error.Message);
    }

    [Fact]
    public void NumericConfigurationDoesNotAcceptStringValues()
    {
        using var copy = new RepositoryCopy();
        copy.Change("config/app.json", node => node["port"] = "5187");
        Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root));
    }

    [Fact]
    public void SourceChangesInvalidateAdmission()
    {
        using var copy = new RepositoryCopy();
        var original = new Configuration(copy.Root);
        File.AppendAllText(original.Asset(original.Sources.Documents[0].File), "\n ");
        Assert.Contains("sha256", Assert.Throws<WorkspaceException>(() => new Configuration(copy.Root)).Message);
    }

    [Fact]
    public void ApprovalBindsToExactProfileContent()
    {
        using var copy = new RepositoryCopy();
        var original = new Configuration(copy.Root);
        File.WriteAllText(Path.Combine(original.Directory, "approval.json"),
            JsonSerializer.Serialize(new Approval(original.Digest, "approved"), Json.Options));
        Assert.True(new Configuration(copy.Root).Approved);
        copy.Change("profiles/operational-request-review/ui.json", node => node["chatHeading"] = "Policy questions");
        Assert.False(new Configuration(copy.Root).Approved);
    }

    [Fact]
    public void UnknownPlaceholdersAndOutsideAssetsFail()
    {
        var config = new Configuration(Root);
        Assert.Throws<WorkspaceException>(() => config.Expand("{{missingTerm}}"));
        Assert.Throws<WorkspaceException>(() => config.Asset("../../config/app.json"));
    }

    [Fact]
    public void ConfiguredFieldValidationRejectsUnknownFieldsAndInvalidOptions()
    {
        var config = new Configuration(Root);
        var values = new Dictionary<string, JsonElement>(config.Examples.FormValues) { ["unexpected"] = JsonSerializer.SerializeToElement("value") };
        Assert.Throws<WorkspaceException>(() => ProcessValidation.Validate(config.Process, values));
        values.Remove("unexpected");
        var field = config.Process.Fields.First(item => item.Type == "select");
        values[field.Id] = JsonSerializer.SerializeToElement("not-an-option");
        Assert.Throws<WorkspaceException>(() => ProcessValidation.Validate(config.Process, values));
    }

    [Theory]
    [InlineData("number", "3", true)]
    [InlineData("number", "11", false)]
    [InlineData("number", "\"3\"", false)]
    [InlineData("boolean", "false", true)]
    [InlineData("boolean", "\"false\"", false)]
    [InlineData("date", "\"2026-11-04\"", true)]
    [InlineData("date", "\"2026-02-30\"", false)]
    [InlineData("multiselect", "[\"A\",\"B\"]", true)]
    [InlineData("multiselect", "[\"A\",\"A\"]", false)]
    [InlineData("multiselect", "[]", false)]
    public void EveryControlUsesItsActualTypedContract(string type, string json, bool valid)
    {
        var field = new FieldDefinition("field", "Field", type, true, "stage", "", "", ["A", "B"], 8, 0, 10);
        var process = new ProcessDefinition("process", "1", "Process", [new("stage", "Stage")], [field], "field", ["field"], ["field"], ["field"]);
        var values = new Dictionary<string, JsonElement> { ["field"] = JsonDocument.Parse(json).RootElement.Clone() };
        if (valid) ProcessValidation.Validate(process, values);
        else Assert.Throws<WorkspaceException>(() => ProcessValidation.Validate(process, values));
    }

    [Fact]
    public void CombinedProcessInputIsRejectedRatherThanTruncated()
    {
        var config = new Configuration(Root);
        var values = new Dictionary<string, JsonElement>(config.Examples.FormValues);
        var field = config.Process.Fields.First(item => item.Type == "multiline");
        values[field.Id] = JsonSerializer.SerializeToElement(new string('a', 4001));
        Assert.Throws<WorkspaceException>(() => ProcessValidation.Question(config, values));
    }

    [Fact]
    public void ExactPassagesAndReferenceIdentityAreRequired()
    {
        var config = new Configuration(Root);
        var registry = Registry(config);
        var document = registry.Documents[0];
        var data = new { uid = "chunk-1", blob_url = document.BlobUrl, snippet = document.Passages[0].Text, image_path = "" };
        var response = JsonSerializer.SerializeToElement(new { references = new[] { new { id = "0", sourceData = data } } });
        var evidence = Assert.Single(EvidenceParser.FromRetrieve(response, registry, config));
        Assert.Equal(document.Id, evidence.DocumentId);
        var modified = JsonSerializer.SerializeToElement(new { references = new[] { new { id = "0", sourceData = new
            { uid = "chunk-1", blob_url = document.BlobUrl, snippet = "Different content", image_path = "" } } } });
        Assert.Throws<WorkspaceException>(() => EvidenceParser.FromRetrieve(modified, registry, config));
    }

    [Fact]
    public void RegistryRequiresEverySourceAndTheApprovedDigest()
    {
        var config = new Configuration(Root);
        var registry = Registry(config);
        CloudClient.ValidateRegistry(config, Settings(), registry);
        Assert.Throws<WorkspaceException>(() => CloudClient.ValidateRegistry(config, Settings(), registry with { ProfileDigest = "changed" }));
        Assert.Throws<WorkspaceException>(() => CloudClient.ValidateRegistry(config, Settings(), registry with { Documents = registry.Documents.Skip(1).ToArray() }));
    }

    [Fact]
    public void NativeToolResponseRequiresTheSingleConfiguredRetrieval()
    {
        var config = new Configuration(Root);
        Assert.Throws<WorkspaceException>(() => EvidenceParser.FromAgent(JsonSerializer.SerializeToElement(new { output = Array.Empty<object>() }), Registry(config), config));
    }

    [Fact]
    public void UnknownAndFutureSourceStatusCannotBecomeEvidence()
    {
        var source = new Configuration(Root).Sources.Documents[0];
        Assert.False((source with { Status = "unknown" }).Eligible(DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.False((source with { EffectiveFrom = DateOnly.MaxValue }).Eligible(DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.False((source with { Published = DateOnly.MaxValue }).Eligible(DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.False((source with { Status = "withdrawn" }).Eligible(DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [Fact]
    public void ValidReferencesDoNotBypassClaimAndScopeVerification()
    {
        var answer = Answer();
        var claims = VerificationGate.Claims(answer).Select(claim => new ClaimDecision(claim.Id, "supported")).ToArray();
        Assert.True(VerificationGate.Accept(answer, new("in_scope", claims)));
        Assert.False(VerificationGate.Accept(answer, new("out_of_scope", claims)));
        Assert.False(VerificationGate.Accept(answer, new("uncertain", claims)));
        Assert.False(VerificationGate.Accept(answer, new("in_scope", claims.Skip(1).ToArray())));
        Assert.False(VerificationGate.Accept(answer, new("in_scope", [.. claims, claims[0]])));
        Assert.False(VerificationGate.Accept(answer, new("in_scope", claims.Select(claim => claim with { Decision = "uncertain" }).ToArray())));
    }

    [Fact]
    public void MissingOrIncompleteVerificationIsAnExplicitFailure()
    {
        Assert.Throws<WorkspaceException>(() => GroundedAssistant.ReadChatText(JsonSerializer.SerializeToElement(new { })));
        Assert.Throws<WorkspaceException>(() => GroundedAssistant.ReadChatText(JsonSerializer.SerializeToElement(new
        {
            choices = new[] { new { finish_reason = "length", message = new { content = "{}" } } }
        })));
        Assert.Throws<WorkspaceException>(() => GroundedAssistant.ReadUsage(JsonSerializer.SerializeToElement(new { usage = new { } })));
    }

    [Fact]
    public void StructuralFailuresRemainBlockingInputs()
    {
        var answer = Answer();
        var source = new Evidence("0", "chunk", "POL-001", "Policy", "1", "current", "Policy section",
            "Record the business objective.", new string('A', 64), "", "/documents/key");
        Assert.Empty(AnswerValidation.Inspect(answer, [source], 0));
        Assert.NotEmpty(AnswerValidation.Inspect(answer with { Overview = [new("Unsupported text.", ["unknown"])] }, [source], 0));
        Assert.NotEmpty(AnswerValidation.Inspect(answer with { Outcome = "insufficient_evidence" }, [source], 0));
    }

    [Fact]
    public async Task RealLocalStorePersistsConcurrentRecordsAndHistoricalSchema()
    {
        using var copy = new RepositoryCopy();
        var config = new Configuration(copy.Root);
        var store = new ProcessStore(config);
        var result = new AnswerResult("answered", Answer(), [], [], [], new(1, 1), Guid.NewGuid().ToString("N"), config.Digest, "gpt", "passed");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.SaveAsync(config.Examples.FormValues, result, CancellationToken.None)));
        var reopened = new ProcessStore(config);
        var records = await reopened.ListAsync(CancellationToken.None);
        Assert.Equal(8, records.Length);
        Assert.Equal(8, records.Select(record => record.Id).Distinct().Count());
        Assert.All(records, record => Assert.Equal(config.Process.Fields.Select(field => field.Label), record.Process.Fields.Select(field => field.Label)));
        await Assert.ThrowsAsync<WorkspaceException>(() => store.SaveAsync(config.Examples.FormValues, result with { Verification = "failed" }, CancellationToken.None));
        await Assert.ThrowsAsync<WorkspaceException>(() => store.SaveAsync(config.Examples.FormValues, result with { Answer = null }, CancellationToken.None));
        await Assert.ThrowsAsync<WorkspaceException>(() => store.SaveAsync(config.Examples.FormValues, result with { Outcome = "out_of_scope" }, CancellationToken.None));
    }

    [Fact]
    public async Task AnInterruptedTemporaryWriteDoesNotReplaceExistingRecords()
    {
        using var copy = new RepositoryCopy();
        var config = new Configuration(copy.Root);
        var store = new ProcessStore(config);
        var result = new AnswerResult("insufficient_evidence", null, [], ["More evidence is required."], [], new(0, 0),
            Guid.NewGuid().ToString("N"), config.Digest, "gpt", "not_run");
        await store.SaveAsync(config.Examples.FormValues, result, CancellationToken.None);
        var path = Path.Combine(copy.Root, ".local", "records", config.Profile.Id, config.Process.Id + ".json.tmp");
        File.WriteAllText(path, "{incomplete");
        Assert.Single(await new ProcessStore(config).ListAsync(CancellationToken.None));
    }

    [Fact]
    public void RenderedPolicyIsAReadableCompletePdf()
    {
        var content = new PolicyContent("Operational review policy", [new("Record requirements",
            "Every request must state its business objective and responsible business area.")]);
        var bytes = PolicyPdf.Render(content);
        Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(PolicyPdf.Render(content)));
        Assert.True(bytes.AsSpan().StartsWith("%PDF-"u8));
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.Contains("Operational review policy", pdf.GetPage(1).Text);
        Assert.Contains("responsible business area", pdf.GetPage(1).Text);
    }

    [Fact]
    public void ImagesAreDecodedAndMetadataFree()
    {
        using var bitmap = new SKBitmap(16, 16);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var upload = new UploadImage("input.png", "image/png", data.ToArray());
        Assert.Single(ImageValidation.Normalize([upload]));
        Assert.Throws<WorkspaceException>(() => ImageValidation.Normalize(Enumerable.Repeat(upload, 6).ToArray()));
        Assert.Throws<WorkspaceException>(() => ImageValidation.Normalize([upload with { MimeType = "image/jpeg" }]));
    }

    [Fact]
    public void EmptyScheduledRunsCannotClearFailedNativeBatches()
    {
        var failed = new { status = "success", itemsProcessed = 3, itemsFailed = 1, startTime = "2026-10-14T08:00:00Z" };
        var empty = new { status = "success", itemsProcessed = 0, itemsFailed = 0, startTime = "2026-10-14T09:00:00Z" };
        var status = JsonSerializer.SerializeToElement(new { lastResult = empty, executionHistory = new[] { failed, empty } });
        Assert.Throws<WorkspaceException>(() => Ingestion.RequirePreviousSuccess(status));
        var recovered = new { status = "success", itemsProcessed = 4, itemsFailed = 0, startTime = "2026-10-14T10:00:00Z" };
        Ingestion.RequirePreviousSuccess(JsonSerializer.SerializeToElement(new { lastResult = recovered, executionHistory = new[] { failed, empty, recovered } }));
    }

    [Fact]
    public void AcceptanceCannotUseUnknownSourcesOrSuccessShapedServiceFailures()
    {
        var config = new Configuration(Root, "operational-request-review");
        var item = new EvaluationCase("case-one", "What request context is required?", [config.Sources.Documents[0].Id], "answered", [], .9);
        EvaluationChecks.Validate(config, [item]);
        Assert.Throws<WorkspaceException>(() => EvaluationChecks.Validate(config, [item with { ExpectedSources = ["UNKNOWN"] }]));
        Assert.Throws<WorkspaceException>(() => EvaluationChecks.Validate(config, [item with { ExpectedOutcome = "generation_unavailable" }]));
        Assert.Throws<WorkspaceException>(() => EvaluationChecks.Validate(config, [item with { MinimumRecall = .5 }]));
        Assert.Throws<WorkspaceException>(() => EvaluationChecks.Validate(config, [item, item]));
    }

    private static AgentAnswer Answer() => new("answered", [new("Record the business objective.", ["0"])], [], [], [], ["The source does not cover every business decision."]);
    private static CloudSettings Settings() => new(Guid.Empty.ToString(), Guid.Empty.ToString(),
        new("https://workspace.openai.azure.com"), new("https://workspace.services.ai.azure.com/api/projects/process"),
        new("https://workspace.search.windows.net"), new("https://workspace.blob.core.windows.net/"), "");
    private static SourceRegistry Registry(Configuration config) => new(config.Profile.Id, config.Digest,
        config.Sources.Collection, config.Sources.Version, config.Sources.Documents.Select(source =>
        new RegistryDocument(source.Id, source.Title, source.Revision, source.Status, source.EffectiveFrom, source.WithdrawnAt,
            $"https://workspace.blob.core.windows.net/{config.Sources.Container}/{source.Id.ToLowerInvariant()}.pdf",
            source.Id.ToLowerInvariant() + ".pdf", new string('A', 64), source.Sha256, [new("chunk-1", "Record the business objective.", "")])).ToArray());
}

internal sealed class RepositoryCopy : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "workspace-test-" + Guid.NewGuid().ToString("N"));
    public RepositoryCopy()
    {
        var source = Configuration.FindRoot();
        foreach (var directory in new[] { "config", Path.Combine("profiles", "operational-request-review") })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, directory), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var appPath = Path.Combine(Root, "config", "app.json");
        var app = Json.Read<AppConfig>(appPath);
        File.WriteAllText(appPath, JsonSerializer.Serialize(app with { ActiveProfile = "operational-request-review" }, Json.Options));
    }
    public void Change(string relative, Action<JsonNode> mutate)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        mutate(node);
        File.WriteAllText(path, node.ToJsonString(Json.Options));
    }
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
