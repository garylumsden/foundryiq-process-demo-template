# Foundry IQ Process Workspace

A configurable application for evidence-grounded chat and business process forms.
Use the template to adapt the workflow, policies, prompts, examples, and presentation to a customer or industry.

The application provides:

- Chat and source search over an approved document collection.
- A configurable business process form, register, and saved assessments.
- Verified source passages, document links, citations, and optional image inputs.
- A separate evidence-only check for claim support and task scope.
- Optional Copilot-assisted setup and a complete manual configuration path.

**GitHub Copilot is not required.**
The setup agent uses the same configuration files and commands described below.

## Architecture

`Browser -> local ASP.NET Core app -> Foundry model -> Foundry IQ -> Azure AI Search`

Private Blob Storage holds admitted originals and the source registry.
Native ingestion extracts PDF content and figure descriptions.
Application Insights receives safe operational traces.
The local app uses your existing Azure CLI identity.
It does not deploy a public web application.

GPT profiles use pinned Foundry prompt agents.
Claude profiles use the native Messages API and application-managed Foundry IQ retrieval.
Only enabled model profiles and their supporting deployments are provisioned.

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

## Optional Copilot-assisted setup

Select the repository's **scenario-setup** agent in GitHub Copilot CLI.
For example:

```text
Configure a supplier onboarding workflow for a retail purchasing team.
Suggest the process and fields first.
Research the topic deeply, then propose policies, questions, and a suitable visual style.
I will supply the logo.
```

The agent uses four bundled skills:

| Skill | Purpose |
|---|---|
| `scenario-research` | Research sources, status, rights, contradictions, and gaps |
| `process-profile-authoring` | Create fields, terminology, prompts, content, examples, and evaluation drafts |
| `scenario-branding` | Configure the visual style and optional logo |
| `scenario-validation` | Run checks and report approval and readiness |

Deep research is optional.
The agent can instead use approved supplied documents or author policies for review.
It asks for the process, fields, exact content digest, and activation decisions.
It can update an existing profile without replacing user edits.
It does not provision Azure resources or publish customer content implicitly.

## Configure Azure services

Use a separate environment for this repository.
Confirm your subscription, customer-selected location, available model versions, quotas, and permitted role changes.
Authenticate the Azure CLI and AZD separately before provisioning.
The deployment scripts only check existing authentication.

Edit `config\models.json` to select enabled answer models and supporting deployments.
Do not enable a model that is unavailable in the selected subscription and location.
The default enables one GPT answer profile plus verification, extraction, and embedding models.
Claude answer profiles are available but disabled by default.
They require accurate provider attestations and explicit acceptance of the applicable terms.

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

The lifecycle validates the profile, resolves Bicep parameters, provisions selected dependencies, ingests documents, and pins prompt-agent versions.
It creates no public web host.
It does not configure a spending limit.
Review the resulting Azure charges.

Cloud settings remain in ignored AZD files.
The application requires matching profile, registry, and deployment digests.
Ingestion publishes no partial registry.
No answer appears if required verification fails.

## Checks

```powershell
dotnet test Workspace.sln --no-restore
Push-Location src\Workspace.Web\frontend
npm test
npm run test:browser
Pop-Location
```

Start the local app before browser checks.
They use the configured local address.
Frontend rendering fixtures do not establish model quality.

After a successful approved Azure setup:

```powershell
.\scripts\evaluate.ps1
```

Live acceptance checks every enabled answer profile.
Results retain the profile digest, case hash, outcomes, source recall, and verification state.
Failed results remain under ignored `evals\results`.
Do not change expected results merely to obtain passing checks.

Generate editor schemas with:

```powershell
dotnet run --project tools\Workspace.Tools -- schemas
```

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
Never use it against an unrelated environment.

## License

Code and bundled authored policies use the [MIT license](LICENSE).
Supplied customer or publisher documents retain their own rights.
See [Attribution](docs/attribution.md).
