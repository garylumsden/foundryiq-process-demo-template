using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Workspace.Core;

var config = new Configuration(Workspace.Core.Configuration.FindRoot());
var origin = $"http://127.0.0.1:{config.App.Port}";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(origin);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ImageValidation.MaxTotalBytes + 128 * 1024);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ImageValidation.MaxTotalBytes + 128 * 1024;
    options.MemoryBufferThreshold = ImageValidation.MaxTotalBytes + 128 * 1024;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    options.SerializerOptions.RespectNullableAnnotations = true;
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<ProcessStore>();
builder.Services.AddSingleton<Metrics>();
CloudSettings? settings = null;
try { settings = CloudSettings.FromEnvironment(); }
catch (WorkspaceException error) when (error.Code == "configuration_missing") { }
if (settings is not null)
{
    builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource("ProcessWorkspace"))
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = settings.Insights;
            options.Credential = new AzureCliCredential(new AzureCliCredentialOptions { TenantId = settings.TenantId });
            options.EnableLiveMetrics = false;
        });
}
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var app = builder.Build();
var ready = false;
var readinessCode = settings is null ? "configuration_missing" : !config.Approved ? "approval_required" : "deployment_missing";
CloudClient? cloud = settings is null ? null : new CloudClient(config, settings);
GroundedAssistant? assistant = null;
var statePath = Path.Combine(config.Root, ".local", "deployment-state.json");
if (cloud is not null && config.Approved && File.Exists(statePath))
{
    try
    {
        var state = Json.Read<DeploymentState>(statePath);
        state.RequireComplete(config, cloud.Settings);
        await cloud.VerifyIdentityAsync(CancellationToken.None);
        await cloud.RegistryAsync(CancellationToken.None);
        assistant = new(config, cloud, state);
        ready = true;
        readinessCode = "ready";
    }
    catch (WorkspaceException error) { readinessCode = error.Code; }
    catch (RequestFailedException) { readinessCode = "registry_unavailable"; }
    catch (AuthenticationFailedException) { readinessCode = "authentication_unavailable"; }
    catch (HttpRequestException) { readinessCode = "service_unavailable"; }
    catch (CloudException) { readinessCode = "service_unavailable"; }
}
if (!ready) app.Logger.LogWarning("Inference is unavailable. Code={Code}", readinessCode);
app.Lifetime.ApplicationStopped.Register(() => cloud?.Dispose());

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError("Request failed. ErrorType={ErrorType}", error?.GetType().Name);
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new { error = config.Text("requestFailed"), code = "request_failed" });
}));
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)
        || context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != config.App.Port
        || context.Request.Headers.Origin is { Count: > 0 } requestOrigin && requestOrigin.ToString() != origin)
    {
        context.Response.StatusCode = 403;
        return;
    }
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' blob:; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        if (HttpMethods.IsPost(context.Request.Method))
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        if (!ready && (context.Request.Path == "/api/ask" || context.Request.Path == "/api/search"
            || context.Request.Path.StartsWithSegments("/documents") || HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/api/process-records"))
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { error = config.Text("preview"), code = readinessCode });
            return;
        }
        await next();
    }
    catch (AntiforgeryValidationException)
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { error = config.Text("requestFailed"), code = "verification_required" });
    }
    catch (WorkspaceException error)
    {
        app.Logger.LogWarning("Request rejected. Code={Code}", error.Code);
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = error.Message, code = error.Code });
    }
    catch (JsonException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = config.Text("validationError"), code = "request_invalid" });
    }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = config.Text("validationError"), code = "request_invalid" });
    }
    catch (CloudException error)
    {
        app.Logger.LogWarning("Cloud request failed. Code={Code}, status={Status}", error.Operation, error.Status);
        context.Response.StatusCode = 502;
        await context.Response.WriteAsJsonAsync(new { error = config.Text("unavailable"), code = "service_unavailable" });
    }
    catch (RequestFailedException error)
    {
        app.Logger.LogWarning("Source access failed. Status={Status}", error.Status);
        context.Response.StatusCode = 502;
        await context.Response.WriteAsJsonAsync(new { error = config.Text("unavailable"), code = "source_unavailable" });
    }
    catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
    {
        context.Response.StatusCode = 504;
        await context.Response.WriteAsJsonAsync(new { error = config.Text("unavailable"), code = "service_timeout" });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/api/config", () => Results.Json(new
{
    profile = new { config.Profile.Id, config.Profile.Version, config.Profile.Title, config.Profile.Purpose },
    config.Digest, ui = config.Ui, theme = config.Theme, process = config.Process,
    examples = new
    {
        prompts = config.Examples.Prompts.Select(example => new
        {
            example.Id, example.Group, example.Title, example.Question, example.Featured,
            images = example.Images.Select((path, index) => new { name = Path.GetFileName(path), url = $"/api/examples/{example.Id}/images/{index + 1}" })
        }),
        config.Examples.FormValues
    },
    models = config.Models.Answers.Select(model => new { model.Key, model.Label }),
    defaultModel = config.Models.DefaultProfile,
    limits = new { maxImages = 5, maxImageBytes = ImageValidation.MaxImageBytes, maxTotalBytes = ImageValidation.MaxTotalBytes, maxQuestionCharacters = 4000 },
    ready, readinessMessage = config.Text(ready ? "ready" : "preview")
}));
app.MapGet("/theme.css", () =>
{
    var colors = string.Join(";", config.Theme.Tokens.Select(token => $"--{token.Key}:{token.Value}"));
    var font = config.Theme.Font switch
    {
        "humanist" => "'Trebuchet MS',system-ui,sans-serif",
        "serif" => "Georgia,serif",
        _ => "'Segoe UI',system-ui,sans-serif"
    };
    return Results.Text($":root{{{colors};--font-family:{font};--text-size:{config.Theme.TextSize}px}}", "text/css");
});
app.MapGet("/api/token", (HttpContext context, IAntiforgery antiforgery) =>
    Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
app.MapGet("/api/status", (Metrics metrics) => Results.Json(new
{
    ready, readinessMessage = config.Text(ready ? "ready" : "preview"),
    metrics.Requests, metrics.Failures, metrics.InputTokens, metrics.OutputTokens
}));
app.MapGet("/api/logo", () =>
{
    if (config.Theme.Logo is null) return Results.NotFound();
    var image = ImageValidation.Normalize([config.ReadImage(config.Theme.Logo)])[0];
    return Results.Bytes(image.Bytes, image.MimeType);
});
app.MapGet("/api/examples/{id}/images/{number:int}", (string id, int number) =>
{
    var example = config.Examples.Prompts.SingleOrDefault(item => item.Id == id);
    if (example is null || number < 1 || number > example.Images.Length) return Results.NotFound();
    var image = ImageValidation.Normalize([config.ReadImage(example.Images[number - 1])])[0];
    return Results.Bytes(image.Bytes, image.MimeType);
});
app.MapPost("/api/ask", async (HttpRequest request, Metrics metrics, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var history = JsonSerializer.Deserialize<string[]>(form["previousQuestions"].ToString() is { Length: > 0 } text ? text : "[]", Json.Options)
        ?? throw new WorkspaceException("request_invalid", config.Text("validationError"));
    var result = await assistant!.AskAsync(new(form["question"].ToString(), history, await Images(form, ct), form["model"].ToString()), ct);
    metrics.Record(result);
    return Results.Json(result);
});
app.MapPost("/api/search", async (SearchRequest request, CancellationToken ct) =>
    Results.Json(await assistant!.SearchAsync(request.Question, request.Page, request.PageSize, ct)));
app.MapGet("/api/process-records", async (ProcessStore store, CancellationToken ct) =>
    Results.Json(new { records = await store.ListAsync(ct) }));
app.MapGet("/api/process-records/{id}", async (string id, ProcessStore store, CancellationToken ct) =>
    (await store.ListAsync(ct)).SingleOrDefault(record => record.Id == id) is { } found ? Results.Json(found) : Results.NotFound());
app.MapPost("/api/process-records", async (HttpRequest request, ProcessStore store, Metrics metrics, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    if (form.Keys.Any(key => key is not ("values" or "model"))) throw new WorkspaceException("request_invalid", config.Text("validationError"));
    var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(form["values"].ToString(), Json.Options)
        ?? throw new WorkspaceException("request_invalid", config.Text("validationError"));
    var question = ProcessValidation.Question(config, values);
    var result = await assistant!.AskAsync(new(question, [], await Images(form, ct), form["model"].ToString()), ct);
    metrics.Record(result);
    var saved = await store.SaveAsync(values, result, ct);
    return Results.Created("/api/process-records/" + saved.Id, saved);
});
app.MapPost("/api/feedback", (FeedbackRequest request) =>
{
    if (request.Category is not ("useful" or "incorrect" or "unsupported") || !Guid.TryParseExact(request.TraceId, "N", out _))
        throw new WorkspaceException("feedback_invalid", config.Text("validationError"));
    using var trace = GroundedAssistant.Traces.StartActivity("workspace.feedback");
    trace?.SetTag("feedback.category", request.Category);
    trace?.SetTag("feedback.trace", request.TraceId);
    return Results.Ok();
});
app.MapGet("/documents/{key}", async (string key, CancellationToken ct) =>
{
    var registry = await cloud!.RegistryAsync(ct);
    var document = registry.Documents.SingleOrDefault(item => EvidenceParser.Key(item.Id) == key
        && config.Sources.Documents.Single(source => source.Id == item.Id).Eligible(DateOnly.FromDateTime(DateTime.UtcNow)));
    if (document is null) return Results.NotFound();
    var downloaded = await cloud.Storage.GetBlobContainerClient(config.Sources.Container).GetBlobClient(document.BlobName).DownloadContentAsync(ct);
    var bytes = downloaded.Value.Content.ToArray();
    if (Convert.ToHexString(SHA256.HashData(bytes)) != document.ContentHash)
        throw new WorkspaceException("source_changed", config.Text("verificationFailed"));
    return Results.Bytes(bytes, "application/pdf");
});
await app.RunAsync();

static async Task<UploadImage[]> Images(IFormCollection form, CancellationToken ct)
{
    if (form.Files.Count > ImageValidation.MaxImages) throw new WorkspaceException("images_invalid", "Attach at most five images.");
    var images = new List<UploadImage>();
    long total = 0;
    foreach (var file in form.Files)
    {
        total += file.Length;
        if (file.Length > ImageValidation.MaxImageBytes || total > ImageValidation.MaxTotalBytes)
            throw new WorkspaceException("images_invalid", "The image upload exceeds its size limit.");
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        images.Add(new(file.FileName, file.ContentType, stream.ToArray()));
    }
    return images.ToArray();
}

public sealed record SearchRequest(string Question, int Page = 1, int PageSize = 8);
public sealed record FeedbackRequest(string TraceId, string Category);
public sealed class Metrics
{
    public int Requests;
    public int Failures;
    public int InputTokens;
    public int OutputTokens;
    public void Record(AnswerResult result)
    {
        Interlocked.Increment(ref Requests);
        if (result.Outcome == "generation_unavailable") Interlocked.Increment(ref Failures);
        Interlocked.Add(ref InputTokens, result.Usage.InputTokens);
        Interlocked.Add(ref OutputTokens, result.Usage.OutputTokens);
    }
}
