using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Workspace.Core;

namespace Workspace.Tests;

public sealed class DeploymentTests
{
    private static readonly string Root = Configuration.FindRoot();

    [Fact]
    public void ProvisionRunsRegisteredHooksWithoutManualDuplicates()
    {
        var yaml = File.ReadAllText(Path.Combine(Root, "azure.yaml"));
        Assert.Contains("- azd: provision", yaml);
        Assert.DoesNotContain("- azd: hooks run", yaml);
        Assert.Contains("run: scripts/preprovision.ps1", yaml);
        Assert.Contains("run: scripts/configure.ps1", yaml);
        Assert.Contains("run: scripts/check-down.ps1", yaml);
    }

    [Theory]
    [InlineData("main.bicep", "APPLICATIONINSIGHTS_CONNECTION_STRING")]
    [InlineData("resources.bicep", "insightsConnectionString")]
    public void RequiredTelemetryOutputIsReadableByAzd(string file, string output)
    {
        var text = File.ReadAllText(Path.Combine(Root, "infra", file));
        Assert.Contains("output " + output + " string =", text);
        Assert.DoesNotMatch(@"@secure\(\)\s*(?:@\w+\([^\r\n]*\)\s*)*output " + Regex.Escape(output), text);
        Assert.Contains("DisableLocalAuth: true", File.ReadAllText(Path.Combine(Root, "infra", "resources.bicep")));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing-approval", false)]
    [InlineData("persisted-approval", false)]
    [InlineData("missing-environment", false)]
    [InlineData("missing-subscription", false)]
    [InlineData("missing-group", false)]
    [InlineData("different-group", false)]
    [InlineData("different-subscription", false)]
    [InlineData("different-id", false)]
    [InlineData("different-name", false)]
    [InlineData("different-environment-tag", false)]
    [InlineData("different-purpose-tag", false)]
    [InlineData("missing-tags", false)]
    [InlineData("unavailable-group", false)]
    public async Task TeardownGuardRequiresApprovalAndVerifiedOwnership(string scenario, bool expected)
    {
        var subscription = Guid.Empty.ToString();
        var environment = new Dictionary<string, string>
        {
            ["AZURE_ENV_NAME"] = "process-checks",
            ["AZURE_SUBSCRIPTION_ID"] = subscription,
            ["AZURE_RESOURCE_GROUP"] = "rg-process-checks"
        };
        var tags = new Dictionary<string, string>
        {
            ["azd-env-name"] = "process-checks", ["purpose"] = "evidence-grounded-process"
        };
        var group = new Dictionary<string, object>
        {
            ["id"] = $"/subscriptions/{subscription}/resourceGroups/rg-process-checks",
            ["name"] = "rg-process-checks", ["tags"] = tags
        };
        switch (scenario)
        {
            case "persisted-approval": environment["WORKSPACE_DELETE_APPROVAL"] = "persisted"; break;
            case "missing-environment": environment.Remove("AZURE_ENV_NAME"); break;
            case "missing-subscription": environment.Remove("AZURE_SUBSCRIPTION_ID"); break;
            case "missing-group": environment.Remove("AZURE_RESOURCE_GROUP"); break;
            case "different-group": environment["AZURE_RESOURCE_GROUP"] = "rg-unrelated"; break;
            case "different-id": group["id"] = $"/subscriptions/{subscription}/resourceGroups/rg-unrelated"; break;
            case "different-name": group["name"] = "rg-unrelated"; break;
            case "different-environment-tag": tags["azd-env-name"] = "other"; break;
            case "different-purpose-tag": tags["purpose"] = "other"; break;
            case "missing-tags": group.Remove("tags"); break;
        }
        var account = new { id = scenario == "different-subscription" ? Guid.NewGuid().ToString() : subscription };
        var approval = scenario == "missing-approval" ? "" : $"delete:process-checks:{subscription}";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            function global:azd {
                $global:LASTEXITCODE = 0
                '{{JsonSerializer.Serialize(environment)}}'
            }
            function global:az {
                if ($args[0] -eq 'account' -and $args[1] -eq 'show') {
                    $global:LASTEXITCODE = 0
                    '{{JsonSerializer.Serialize(account)}}'
                } elseif ($args[0] -eq 'group' -and $args[1] -eq 'show') {
                    $global:LASTEXITCODE = {{(scenario == "unavailable-group" ? 1 : 0)}}
                    '{{JsonSerializer.Serialize(group)}}'
                } else { throw 'An unexpected Azure command was requested.' }
            }
            $env:AZURE_ENV_NAME = 'inherited-environment'
            $env:AZURE_SUBSCRIPTION_ID = '{{subscription}}'
            $env:AZURE_RESOURCE_GROUP = 'rg-process-checks'
            $env:WORKSPACE_DELETE_APPROVAL = '{{approval}}'
            & '{{Path.Combine(Root, "scripts", "check-down.ps1").Replace("'", "''")}}'
            """;
        var result = await RunPowerShellAsync(script);
        Assert.True(expected == (result.Code == 0), $"{scenario}: {result.Output}");
    }

    private static async Task<(int Code, string Output)> RunPowerShellAsync(string script)
    {
        var info = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Root
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("PowerShell did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await output + await errors);
    }
}
