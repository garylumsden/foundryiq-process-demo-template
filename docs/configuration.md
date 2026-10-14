# Configuration reference

Run all commands from the repository root.
The manual README procedure and the setup agent use the same contract.

## Files and ownership

| File | Purpose |
|---|---|
| `config\app.json` | Active profile, local port, and local record path |
| `config\models.json` | Requested count and capability mix, explicit choices, enabled profiles, provider formats, capabilities, and deployments |
| `config\policy.core.json` | Shared non-overridable evidence and task rules |
| `profile.json` | Identity, version, customer context, industry, use case, audience, purpose, and task scope |
| `terminology.json` | Simple placeholder names and their values |
| `ui.json` | Visible labels, help, states, actions, and footer |
| `theme.json` | Supported colors, font, text size, layout, and optional local logo |
| `process.json` | Stages, typed fields, register columns, and assessment mappings |
| `prompts.json` | Domain-specific task, process, retrieval, and verification guidance |
| `examples.json` | Prompt library, featured questions, image references, and form example values |
| `sources.json` | Source identity, rights, lifecycle evidence, file hashes, and native retrieval names |
| `approval.json` | Approval for one exact content digest |
| `evals\<profile-id>\cases.json` | Private-to-runtime expectations and source recall thresholds |

Profile files belong under `profiles\<profile-id>`.
For an ignored local profile, use `profiles\private` with `id` set to `private`.
Alternatively, exclude a specific local profile directory through `.git\info\exclude` before adding confidential files.
Do not commit private customer material merely to activate a profile.
Keep the complete customer repository private when its contents require that boundary.
Do not commit a private active-profile selection to a public repository.

The JSON schemas under `config\schemas` describe file shapes.
The validator also checks cross-file references, asset paths, source hashes, contrast, options, and supported limits.
Unknown properties fail instead of being ignored.

## Model selection

The setup agent asks for the number of answer models and the desired capability mix.
It offers explicit model names as an alternative to capability-led recommendations.
It confirms the resolved IDs, versions, deployments, and default profile before activation.
It must not treat the starter configuration as your decision.

`config\models.json` stores that selection:

```json
{
  "selection": {
    "count": 2,
    "capabilityMix": "Balanced responses and deeper reasoning",
    "explicitModelKeys": ["gpt", "terra"]
  }
}
```

Enable the corresponding answer profiles when using that example.
The requested count must equal the number of enabled profiles whose role is `answer`.
An empty `explicitModelKeys` array means capability-led selection.
A nonempty array must match the enabled answer-profile keys exactly, without duplicates.
`defaultProfile` must select an enabled answer profile.
Verification, extraction, and embedding dependencies do not count as answer choices.

Each profile declares its model ID, version, provider, Azure deployment format, SKU, capacity, and token limit.
Use the exact format returned by the model catalog; do not infer it from an API name.
Its `capabilities` object records `promptAgent`, `mcp`, `structuredOutputs`, and `images`.
Every enabled answer model must support the first three.
Image input support is required when submitting images or running image cases.

Verify those capabilities, location availability, quota, and any provider terms against current evidence.
Declared flags are not proof that a model is deployable.
An incompatible explicit choice requires an alternative, not silent substitution or a different runtime.
The template supports managed prompt agents only.
It does not deploy native-API or hosted model adapters.
This boundary does not exclude Claude by provider name.
The [model compatibility findings](model-compatibility.md) distinguish historical workarounds from verified platform restrictions.

`azd up` provisions the enabled model deployments and creates one prompt agent per answer profile.
Agent names are `<profile-id>-<model-key>`.
Supporting model dependencies do not receive conversation agents.
Every selected agent must become active before the tooling publishes its pinned version map.
Runtime readiness requires the exact selected key set, valid pinned versions, and a matching content digest.
The pinned state must also match the current cloud target.
Changing the count or mix invalidates previous approval.

## Source documents

Use a unique source ID and an accurate title, revision, publisher, and origin.
Use `authored` or `supplied` consistently within a collection.
Use `current`, `withdrawn`, or `unknown` for status.
Record why the status is known.
Keep unknown dates as `null`.
Future-published, future-effective, and withdrawn sources cannot support answers.
Use the optional `published` date when it is known.

File paths must remain inside the profile.
Linked files and linked asset directories are not supported.
Use PDF for supplied originals and structured policy JSON for authored content.
After editing documents, run `prepare`, review the changed hashes, and obtain fresh approval.

The native ingestion source is `<collection>-documents`.
Its generated index must be `<collection>-documents-index`.
The configured current-source wrapper and knowledge base use their configured names.
Registry publication requires every approved original and exact native passages.

## Process definition

Each stage has `id` and `label`.
Each field defines:

| Property | Meaning |
|---|---|
| `id` | Stable lowercase field key |
| `label` | Display label retained in saved schema snapshots |
| `type` | `text`, `multiline`, `select`, `multiselect`, `date`, `number`, or `boolean` |
| `required` | Whether a value must be supplied |
| `stage` | Existing ordered stage ID |
| `help`, `placeholder` | Configured form guidance |
| `options` | Unique options for select controls; otherwise an empty array |
| `maxLength` | Text limit or `null`; the application ceiling is 4,000 |
| `min`, `max` | Numeric bounds or `null` |

`referenceField`, `registerColumns`, `summaryFields`, and `assessmentFields` must reference defined fields.
The server rejects unknown fields and invalid typed values.
It does not truncate oversized combined assessment inputs.
Fields and stages define a guided form, not arbitrary code or an approval engine.

Changing a label does not change a historical record's saved schema.
Removing or repurposing a field requires review of existing records.
Use a new process ID for an incompatible process.

## Prompt composition

The application combines fixed evidence rules, provider protocol rules, approved scope, domain guidance, and terminology.
Only defined `{{termName}}` placeholders are supported.
There is no executable template language.
The application owns the answer schema and verification gate.
Scenario configuration cannot disable retrieval, source checks, citations, scope checks, or verification.

Keep samples as normal user questions.
Do not include desired document IDs, expected answers, or output-format instructions in them.
Store evaluation expectations separately.

## Approval and cloud state

The digest covers profile files, source content, example images, the optional logo, model configuration, and shared policy.
It also covers the resolved runtime instructions and response contracts.
Approval files are excluded from their own digest.
A relevant edit invalidates approval.
The active profile is selected at startup.
Changing the selected profile requires a restart.

The cloud registry and `.local\deployment-state.json` must match the digest.
Deployment state records the tenant and the Foundry account, project, Search, and Storage endpoints.
Startup, evaluation, and assistant construction reject a different target.
The tooling replaces the state atomically only after every selected agent becomes active.
Switching targets requires a matching agent deployment for the selected target.
Older state files without target identity are rejected.
Deploy the approved agents again to create the current state format.
Regenerate manifests and deploy pinned agents after prompt changes.
Reingest after source or profile changes.
Do not edit generated manifests or deployment state to bypass readiness checks.

## Supported presentation

Use six-digit hex colors for the defined theme tokens.
Use a configured local font choice, not a remote font URL.
Use supported layout variants and a text size from 14 px to 18 px.
Configured text requires at least 4.5:1 contrast on its actual backgrounds, including tinted and hover states.
The banner uses `onPrimary` against `banner`, not an assumed white foreground.
Focus indicators require at least 3:1 contrast against the surface and canvas.
The server supplies a validated external theme stylesheet.
No unrestricted HTML, CSS, scripts, shell commands, or provider adapters can enter through profile configuration.

## Deployment controls

The `up` workflow calls `azd provision`.
AZD runs the registered preparation and configuration hooks once.
Do not add manual hook calls around `provision`.
See the [AZD hook contract](https://learn.microsoft.com/azure/developer/azure-developer-cli/azd-extensibility).

Application Insights uses an ordinary deployment output for its routing connection string.
AZD cannot export secure ARM outputs to its environment.
The instrumentation key is an identifier, not an authentication token.
The resource disables local authentication and requires Microsoft Entra credentials for ingestion.
See [Application Insights connection strings](https://learn.microsoft.com/azure/azure-monitor/app/connection-strings).

Teardown requires process-only approval and verified resource-group identity and ownership tags.
The guard rejects missing selected-environment values instead of using inherited values.
It does not grant deletion approval or delete resources itself.
Package-source configuration remains outside the repository, including CI commands.
