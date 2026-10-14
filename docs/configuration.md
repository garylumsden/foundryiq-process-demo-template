# Configuration reference

Run all commands from the repository root.
The manual README procedure and the setup agent use the same contract.

## Files and ownership

| File | Purpose |
|---|---|
| `config\app.json` | Active profile, local port, and local record path |
| `config\models.json` | Enabled model profiles, provider paths, deployment names, versions, and capacities |
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
Regenerate manifests and deploy pinned agents after prompt changes.
Reingest after source or profile changes.
Do not edit generated manifests or deployment state to bypass readiness checks.

## Supported presentation

Use six-digit hex colors for the defined theme tokens.
Use a configured local font choice, not a remote font URL.
Use supported layout variants and a text size from 14 px to 18 px.
The server supplies a validated external theme stylesheet.
No unrestricted HTML, CSS, scripts, shell commands, or provider adapters can enter through profile configuration.
