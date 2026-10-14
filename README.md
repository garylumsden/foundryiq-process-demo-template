# Foundry IQ Process Workspace

A configurable application for evidence-grounded chat and business process forms.
Use the template to adapt the workflow, policies, prompts, examples, and presentation to a customer or industry.

The application provides:

- Chat and source search over an approved document collection.
- A configurable business process form, register, and saved assessments.
- Verified source passages, document links, citations, and optional image inputs.
- A separate evidence-only check for claim support and task scope.
- Copilot-guided configuration with a complete manual fallback.

**Use [Copilot-guided configuration](#recommended-copilot-guided-configuration) as the recommended setup experience.**
The agent turns your scenario into a reviewable profile instead of requiring you to edit each configuration file.
Use [manual configuration](#manual-configuration-without-github-copilot) when Copilot is unavailable.

## Recommended: Copilot-guided configuration

Create your own repository with **Use this template**.
Open its root folder in VS Code or GitHub Copilot CLI.
The repository includes the **scenario-setup** agent and four supporting skills.
No personal skill installation is required.

### Start the setup agent

**In VS Code:** Open Chat in the repository workspace.
Select **scenario-setup** from the **Agent** dropdown.

**In GitHub Copilot CLI:** Start Copilot in the repository root.
Select **scenario-setup** with `/agent`.

Describe the customer, industry, use case, intended users, and available source documents.
For example:

```text
Configure this workspace for a retail purchasing team's supplier onboarding process.
Suggest the process, stages, and fields before writing the final profile.
Research the topic deeply, then propose policies, sample questions, and a suitable visual style.
I will supply the logo.
Ask how many answer models I want and which capability mix I need.
Let me name exact models if I have a preference.
Keep responses within the approved scope and support factual document claims with verified evidence.
```

### Choose the model count and capability mix

The setup agent asks how many answer models you want and which capabilities matter.
You do not need to know product names.
Typical choices are one balanced model, two models for fast responses and stronger reasoning, or a custom mix.
You can instead specify exact models.
Image input is checked when the scenario needs it.

The agent recommends compatible models and asks you to confirm their exact IDs, versions, deployment names, and default choice.
It checks Foundry prompt-agent support, MCP tools, structured outputs, target availability, and quota.
It must report unresolved availability rather than guess or silently replace an explicit choice.
Raw model inference support alone is not enough.
Native-API fallbacks and hosted model adapters are outside this template's deployment path.
This is not a Claude provider ban.
Read the [model compatibility findings](docs/model-compatibility.md) for the historical evidence and the required verification.

The confirmed choice is stored in `config\models.json`.
`selection.count` must match the enabled answer profiles.
`selection.capabilityMix` records your intent.
`selection.explicitModelKeys` records explicit profile choices, or remains empty for capability-led recommendations.
The count excludes this Copilot session's model and the verification, extraction, and embedding dependencies.
The agent presents those supporting dependencies separately.

### What the agent configures

The agent proposes a business process and explains which fields the process needs.
It prepares terminology, prompts, policies, sample questions, form examples, evaluation cases, and presentation settings.
It can research the topic deeply, use approved supplied documents, or author policies for your review.
It adapts the visual style and can use a permitted logo that you supply.
It preserves the shared grounding, citation, task-scope, and verification rules.

| Skill | Purpose |
|---|---|
| `scenario-research` | Research sources, status, rights, contradictions, and gaps |
| `process-profile-authoring` | Create fields, terminology, prompts, content, examples, and evaluation drafts |
| `scenario-branding` | Configure the visual style and optional logo |
| `scenario-validation` | Run checks and report approval and readiness |

Deep research is optional and depends on the tools available in your Copilot session.
The agent must report unavailable tools or evidence gaps rather than invent research.
It can also update an existing profile while preserving your edits.

### Review, approve, and activate

Review the proposed process, fields, source rights, content, prompts, and [evaluation expectations](#live-evaluations).
The agent runs preparation and validation, then presents the exact content digest for approval.
The digest is a SHA-256 fingerprint of the configuration, content, and runtime contracts.
Approve that digest only after reviewing the content.
Activation requires a separate decision.
Relevant changes invalidate earlier approval.

The agent uses the same files and commands as the manual route.
It does not provision Azure resources or publish customer content implicitly.
Continue with [Azure configuration](#configure-azure-services) after approving the profile.
Use [Start locally](#start-locally) for dependencies and a preview without live inference.

## Architecture

`Browser -> local ASP.NET Core app -> Foundry model -> Foundry IQ -> Azure AI Search`

Private Blob Storage holds admitted originals and the source registry.
Native ingestion extracts PDF content and figure descriptions.
Application Insights receives safe operational traces.
The local app uses your existing Azure CLI identity.
It does not deploy a public web application.

Each selected answer model uses its own named, version-pinned Foundry prompt agent.
All answer models use the same evidence and verification controls.
Only compatible enabled model profiles and their supporting deployments are provisioned.

## Start locally

Prerequisites:

- .NET 10 SDK compatible with `global.json`.
- Node.js 24 or later.
- PowerShell 7 and Git.
- Azure CLI and Azure Developer CLI only when using Azure services.
- An installed Edge browser for the browser checks.

Create a repository with **Use this template**, or clone your independent copy.
Run these commands from its root:

```powershell
dotnet restore Workspace.sln
Push-Location src\Workspace.Web\frontend
npm ci
npm run build
Pop-Location
dotnet run --project tools\Workspace.Tools -- validate
.\scripts\start.ps1
```

Open `http://127.0.0.1:5187`.
Package commands use the machine's existing source configuration.
The repository does not configure package feeds.
Without an approved profile and Azure configuration, the app starts in preview.
You can inspect the questions, process fields, examples, and presentation.
Inference remains disabled; the app does not substitute local or invented answers.

The starter process reviews operational requests against the bundled policies.
Two additional profiles cover supplier onboarding and document change review.
Policy source metadata records their repository authorship and rights.
Review the content before activating it or replacing it with your own approved material.

## Manual configuration without GitHub Copilot

Use this fallback when Copilot is unavailable or when you prefer to edit the JSON files directly.
This procedure does not require Copilot, a Copilot subscription, or the `gh` command.
Replace values inside angle brackets before running a command.

### 1. Create or choose a profile

Start from one complete profile:

```powershell
Copy-Item profiles\operational-request-review profiles\my-process -Recurse
```

Use a lowercase profile directory name.
Set `profile.json` `id` to the same name.
Update its version, title, industry, customer context, use case, audience, purpose, allowed topics, and excluded topics.

Alternatively, configure one of the bundled profiles directly.
Do not change `config\app.json` until the new profile has passed review.

### 2. Define the process and fields

Edit `process.json`.
Set the process name, version, ordered stages, and field definitions.
Supported controls are text, multiline text, select, multiselect, date, number, and boolean.
Optional image attachments are available alongside the form.

For each field, set its identifier, label, type, stage, required state, and applicable limits.
Use `assessmentFields` to select the values sent for assessment.
Use `registerColumns` and `summaryFields` to define the record displays.
Set `referenceField` to the primary record reference.

Update `examples.json` `formValues` with valid values for every required field.
The server validates the same definition that the browser renders.

### 3. Configure language, prompts, and presentation

Edit `terminology.json` and `ui.json`.
Keep all required UI keys.
These files control headings, navigation, labels, help, states, and action text.

Edit `prompts.json`.
Use terminology placeholders such as `{{documentPlural}}` only when that key exists in `terminology.json`.
Define domain-specific task and process instructions.
Keep the shared policy in `config\policy.core.json` unchanged during scenario setup.
The application enforces evidence and scope checks independently of scenario instructions.

Edit `theme.json` for colors, font choice, text size, and layout.
Supported fonts are `system`, `humanist`, and `serif`.
Text sizes range from 14 px to 18 px.
Layouts are `comfortable` and `compact`.
The validator checks configured text contrast.

For a logo, add a permitted JPEG or PNG inside the profile directory.
Set its relative path and alternative text in `theme.json`.
Use `null` for `logo` to retain text branding.
Do not commit customer assets without publication permission.

### 4. Supply the policies or standards

Edit `sources.json`.
Each document needs an ID, title, revision, source kind, lifecycle status, status evidence, rights, publisher, origin, and file path.
Preserve unknown dates as `null`.
Only eligible current documents can support an answer.

Use `kind: "supplied"` for approved source PDFs.
Use `kind: "authored"` for repository-authored policy content.
Keep different source authority in separate profiles.
Do not attribute authored content to a real customer or publisher.

An authored document uses this structure:

```json
{
  "title": "Operational request review",
  "sections": [
    {
      "heading": "Required context",
      "text": "Record the objective, business area, priority, and target date before review."
    }
  ]
}
```

Store it inside the profile and reference its relative path in `sources.json`.
The tooling renders approved structured content to PDFs for native ingestion.
It preserves supplied PDFs unchanged.
Neither file access nor a valid hash establishes usage rights.

### 5. Define questions and expected behavior

Edit `examples.json` to configure questions, groups, featured questions, and optional image references.
Selecting a question must not submit it automatically.

Create `evals\<profile-id>\cases.json`.
Keep expected source IDs and expected outcomes there, not inside user-facing questions.
Include supported questions, missing information, evidence gaps, injection attempts, and off-task requests.
Review the expectations before running live acceptance.

Also review `config\models.json` when configuring manually.
Set the requested count and capability mix, then enable exactly that many answer profiles.
Set `defaultProfile` to one of those profiles.
For explicit choices, list their profile keys in `selection.explicitModelKeys`.
Populate provider format and capability fields from current catalog evidence.
See [Model selection](docs/configuration.md#model-selection) for the full contract.

### 6. Prepare, validate, and review

```powershell
dotnet run --project tools\Workspace.Tools -- prepare --profile <profile-id>
dotnet run --project tools\Workspace.Tools -- validate --profile <profile-id>
dotnet test Workspace.sln --no-restore
```

`prepare` updates source hashes.
`validate` checks the complete profile and prints its content digest.
A digest is a SHA-256 fingerprint of the profile and its referenced content.

Read the policies, process fields, prompts, source metadata, examples, and theme.
Resolve rights, status, and content questions before approval.
Structural validation does not replace that review.

### 7. Approve and select the exact content

Copy the reviewed digest from the validation output:

```powershell
dotnet run --project tools\Workspace.Tools -- approve --profile <profile-id> --digest <reviewed-digest>
dotnet run --project tools\Workspace.Tools -- select --profile <profile-id> --digest <reviewed-digest>
```

Approval is an explicit operator action.
The agent and preparation commands do not approve content automatically.
Relevant content changes invalidate the recorded approval.
Restart the app after selecting a profile.
Reconcile its corpus and deployed prompt versions before enabling inference.

See [Configuration reference](docs/configuration.md) for the full contract.

## Configure Azure services

Use a separate environment for this repository.
Confirm your subscription, customer-selected location, available model versions, quotas, and permitted role changes.
Authenticate the Azure CLI and AZD separately before provisioning.
The deployment scripts only check existing authentication.

Edit `config\models.json` to select enabled answer models and supporting deployments.
Do not enable a model that is unavailable in the selected subscription and location.
The default enables one GPT answer profile plus verification, extraction, and embedding models.
The setup agent must confirm your preferred count and mix rather than assume that starter choice.
Each answer model must support managed prompt agents, MCP retrieval, and structured outputs.
The configured capability flags record your selection; they do not prove service availability.
Review any required provider attestations and terms before deployment.

After approving the profile:

```powershell
azd env new <environment>
azd env set AZURE_SUBSCRIPTION_ID <subscription-id>
azd env set AZURE_LOCATION <customer-selected-location>
```

After approving resource creation and role assignments for that exact target:

```powershell
$env:WORKSPACE_DEPLOYMENT_APPROVAL = "create:<environment>:<subscription-id>:<customer-selected-location>"
azd up --no-prompt
.\scripts\start.ps1 -Cloud
```

The lifecycle validates the profile, resolves Bicep parameters, provisions selected dependencies, and ingests documents.
`azd provision` runs the registered preparation and configuration hooks once.
It generates and deploys a separate prompt agent for every selected answer model.
Agent names use `<profile-id>-<model-key>`.
Each agent uses its selected model deployment and the approved grounding instructions.
The lifecycle records pinned versions only after every selected agent becomes active.
The app rejects missing, invalid, extra, stale, or cross-target agent-version mappings.
It creates no public web host.
It does not configure a spending limit.
Review the resulting Azure charges.

Cloud settings remain in ignored AZD files.
AZD exports the Application Insights routing connection string as a normal output.
The string identifies the telemetry destination; it does not authorize ingestion.
Microsoft Entra authentication remains mandatory, and local authentication remains disabled.
The application requires matching profile, registry, and deployment digests.
Pinned agents also require a matching tenant and Foundry account, project, Search, and Storage endpoints.
Older deployment-state files without target identity require agent deployment again.
Ingestion publishes no partial registry.
No answer appears if required verification fails.
Disabling a model changes the active selection but does not automatically delete earlier Azure deployments or agent versions.
Cleanup requires a separate approved action.

## Checks

### Local checks

Stop any app instance using the configured port before running the script.
Run the combined local checks:

```powershell
.\scripts\check.ps1 -Browser
```

These checks cover profile validation, deterministic evidence rules, typed process fields, local persistence, and browser rendering.
The script starts and stops its local check server.
Browser cases use controlled API replies, even when they load assets from the actual app.
They do not call live models or establish answer quality.

### Live evaluations

A live evaluation sends reviewed questions through the same assessment pipeline used by chat and business process forms.
It calls the configured models and Foundry IQ rather than using browser fixtures.
It does not replace the HTTP, form, persistence, or browser checks above.
Live runs consume Azure model and search resources.

Before running it, approve the active profile and complete its Azure setup.
Its source registry and pinned runtime agents must match the active content digest.
Select the correct AZD environment and use an existing authenticated Azure CLI session.

```powershell
dotnet run --project tools\Workspace.Tools -- validate
.\scripts\evaluate.ps1
```

The evaluator reads `evals\<active-profile-id>\cases.json`.
It runs each case once for every enabled answer model in `config\models.json`.
It evaluates only the active profile, not every profile directory.
The Copilot setup agent can draft these cases, but you must review their expectations against the admitted sources.
Image cases require image input support in every selected answer model.

Each case defines:

| Field | Meaning |
|---|---|
| `id` | Unique case identifier |
| `question` | Input sent to the app; keep expected answers and expected source IDs separate |
| `expectedSources` | Document IDs that retrieval should return |
| `expectedOutcome` | `answered`, `insufficient_evidence`, `clarification_required`, or `out_of_scope` |
| `images` | Optional image paths relative to the profile |
| `minimumRecall` | Required source recall, from `0.9` to `1.0` |

For examples, read the [operational request cases](evals/operational-request-review/cases.json).
Keep expectations separate from the public prompt library.
Include supported questions, missing context, evidence gaps, conflicts, instruction-injection attempts, and off-task requests.

### How a live case passes

The assessment pipeline retrieves admitted evidence, validates exact passages and citations, and checks the generated response.
For an `answered` response, the runtime's evidence-only verifier checks claim support and task scope.
This is the runtime verification call, not a separate evaluation service.
A failed or uncertain verification decision blocks factual output.

The evaluator then requires all of these conditions:

1. The actual outcome exactly matches `expectedOutcome`.
2. Source recall meets the case's `minimumRecall`.
3. The result contains no response-validation issues.
4. An `answered` result contains an answer and has verification state `passed`.

Source recall is the number of expected document IDs returned, divided by the number of expected document IDs.
Multiple passages from one document do not increase that count.
When `expectedSources` is empty, the evaluator reports recall as `1`; treat that value as not applicable.
Recall does not measure factual correctness or penalize additional retrieved documents.
Service failures are not acceptable abstention outcomes.

### Results and reruns

The evaluator prints `PASS` or `FAIL` for each case and model.
After each completed case, it writes a timestamped JSON report under ignored `evals\results`.
The report contains the profile digest, case-file hash, failure count, per-case outcome, recall, returned answer, sources, verification state, and token usage.
Configuration or service errors can stop execution before a case result is recorded.
Any failed case makes the command exit with a nonzero status.

Inspect the retained sources and result before changing a failed case.
Correct an expectation only when source review establishes that it was wrong.
Do not lower thresholds or alter expected outcomes merely to obtain a pass.
Reports can contain retrieved source content; keep them out of public commits.

Rerun live evaluations after changing sources, scope, prompts, models, or retrieval settings.
Reconcile approval and cloud artifacts with the updated digest first.
Repeat a run when you need to examine variation; the evaluator does not repeat cases automatically.
Model-based verification can make mistakes, so subject-matter review remains necessary.

### Configuration schemas

```powershell
dotnet run --project tools\Workspace.Tools -- schemas
```

This command exports the configuration and response schemas for editor support.

## Evidence, data, and deployment limits

The top banner identifies the presentation context.
Ordinary process and policy content uses realistic terminology.

Only admitted current source passages can establish document requirements.
Images can support visible observations and retrieval context, not policy authority.
The model-based verifier can make mistakes.
Supported citations do not establish exhaustive coverage or approval of a business decision.
Read the original documents and apply the appropriate human review.

The app accepts up to five JPEG or PNG images.
It strips image metadata and excludes uploads from the source index.
Uploads are not retained with saved records.
Saved records contain form values, the checked assessment, and source evidence at assessment time.
They remain in ignored local JSON files.
Do not place sensitive real-world case data in a public repository.

The local host uses loopback HTTP, request-origin checks, antiforgery protection, and safe source formatting.
Production hosting needs HTTPS, user authorization, a suitable data service, and an approved security design.
MISE requirements are not assumed.
See [Architecture and controls](docs/architecture.md).

For teardown, first confirm the exact environment and subscription:

```powershell
$env:WORKSPACE_DELETE_APPROVAL = "delete:<environment>:<subscription-id>"
azd down --force --purge --no-prompt
```

This deletes the selected environment and can purge supported soft-deleted resources.
The guard verifies the selected subscription, `rg-<environment>` group, and ownership tags before permitting deletion.
Missing or mismatched ownership stops the command.
Never use it against an unrelated environment.

## License

Code and bundled authored policies use the [MIT license](LICENSE).
Supplied customer or publisher documents retain their own rights.
See [Attribution](docs/attribution.md).
