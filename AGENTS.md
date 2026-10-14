# Repository guidance

Read `README.md` for the recommended Copilot-guided setup and manual fallback.
Use `.github/agents/scenario-setup.agent.md` as the primary route for scenario configuration.
Retain the manual configuration path for users without Copilot.
Ask for answer-model count and capability mix, with an option to name exact models.
Use only compatible Foundry prompt-agent models.
Keep one deployed agent per selected answer model; do not add native-API fallbacks.

Validate profiles before changing runtime behavior:

```powershell
dotnet run --project tools\Workspace.Tools -- validate
dotnet test Workspace.sln --no-restore
```

Keep scenario text, fields, prompts, source metadata, and themes in configuration.
Preserve exact-passage validation, source admission, task scope, citation checks, and the evidence-only verifier.
Never return failed factual answers or accept client-supplied assessment results.
Use realistic application language and the existing top banner.
Keep accurate source provenance in metadata.

Keep private files, environment values, uploaded images, research notes, and evaluation results out of Git.
Do not log user inputs, source text, images, or credentials.
Use the machine's configured package sources and pinned dependencies.
Do not add repository-level feed configuration or command-line feed overrides.
Do not initiate interactive authentication from deployment scripts.
Cloud changes require approval for the exact environment.
