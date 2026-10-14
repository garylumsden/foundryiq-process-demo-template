---
name: scenario-validation
description: Validate profile structure, source hashes, prompt composition, business process fields, branding, evidence gates, and permitted live checks. Use before profile approval or activation.
---

# Scenario validation

Run the deterministic repository commands from the repository root.
GitHub Copilot is not required to execute them.

```powershell
dotnet run --project tools\Workspace.Tools -- prepare --profile <profile-id>
dotnet run --project tools\Workspace.Tools -- validate --profile <profile-id>
dotnet test Workspace.sln --no-restore
```

Check source rights and status evidence separately.
Passing structure checks does not establish permission or subject-matter approval.
Check domain instructions against the fixed evidence rules.
Review sample questions without exposing evaluation expectations.
Check form examples, required fields, source references, and configured colors.
Check that `selection.count` matches the enabled answer profiles.
Check that explicit model keys match the selection exactly.
Require prompt-agent, MCP, and structured-output support for every selected answer model.
Check image support against the inputs and evaluation cases.
Do not treat a capability flag as proof of catalog availability or successful deployment.

Generate manifests without creating Azure resources:

```powershell
dotnet run --project tools\Workspace.Tools -- manifests
```

Require one distinct named manifest per selected answer model.
Exclude verification, extraction, and embedding dependencies from that agent count.
After an approved `azd up`, require a pinned active agent version for every selected answer profile.
Missing, invalid, extra, or stale version mappings must not pass readiness.
Do not permit provider-specific native-API fallback paths.

Report the exact digest and changes for user review.
After explicit approval, record that supplied digest:

```powershell
dotnet run --project tools\Workspace.Tools -- approve --profile <profile-id> --digest <reviewed-digest>
```

Select a profile only after separate activation approval:

```powershell
dotnet run --project tools\Workspace.Tools -- select --profile <profile-id> --digest <reviewed-digest>
```

Do not approve content on the user's behalf.
Do not infer activation approval from a request to edit a profile.

Run frontend unit and browser checks using the commands in the README.
Run `scripts\evaluate.ps1` only against the approved separate Azure environment.
Check every enabled answer model.
Retain failed cases and record profile, corpus, and prompt versions.
Require supported claims, resolved citations, correct abstention, and on-task behavior.
Never treat a missing model, unavailable verifier, skipped cloud check, or disconnected telemetry as success.
