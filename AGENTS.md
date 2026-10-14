# Repository guidance

Read `README.md` for setup, including the manual configuration path.
Use `.github/agents/scenario-setup.agent.md` for guided scenario changes.

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
