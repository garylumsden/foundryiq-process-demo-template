---
name: process-profile-authoring
description: Create or update configuration profiles, process fields, terminology, domain prompt sections, policies, sample questions, and evaluation drafts without changing workspace code.
---

# Process profile authoring

Produce a complete profile using `docs/configuration.md` and `config/schemas`.

1. Confirm the proposed business process and field set.
2. Confirm the requested answer-model count, capability mix, and any explicit model choices.
3. Resolve compatible models and present their exact IDs, versions, deployment names, and default choice.
4. Create a lowercase profile ID and a versioned process definition.
5. Define ordered stages and supported typed fields.
6. Explain each field's purpose and assessment mapping.
7. Define register columns, summary fields, and the reference field.
8. Write terminology and user-interface text for the approved context.
9. Generate task-specific prompt sections without altering the shared evidence policy.
10. Add supplied sources or reviewable authored policies with accurate source metadata.
11. Create sample questions and valid example form values.
12. Draft separate evaluation cases with expected evidence and safe outcomes.

Write model choices to `config/models.json`.
Set `selection.count` to the number of enabled answer profiles.
Describe the requested capabilities in `selection.capabilityMix`.
Use `selection.explicitModelKeys` for explicit choices, or an empty array for a capability-led recommendation.
Resolve any user-supplied model names to the exact confirmed profile keys.
Keep the supporting verification, embedding, and extraction models outside the answer-model count.

Check current catalog evidence for the model version, provider format, managed prompt agents, MCP, structured outputs, and any required image support.
Capability flags record the reviewed selection; they do not prove deployment success.
Do not guess availability or quota.
Reject incompatible choices without changing their names or using another runtime.
Every selected answer model must use a distinct deployed Foundry prompt agent.

Keep all required files from a complete starter profile.
Read the schemas instead of inventing property names.
Use `{{termName}}` only for a defined terminology key.
Use substantive domain instructions when a task changes; word substitution alone is insufficient.
Keep the response schema, provider protocols, and evidence enforcement outside scenario control.

Use realistic policy titles, identifiers, and process language.
Keep provenance in metadata without repeated labels in normal content.
Never present authored material as a real publisher's or customer's policy.
Preserve source rights, status evidence, and unknown dates accurately.
Do not mix external authority and authored requirements in one collection.

Keep expected sources and expected answers out of user-facing sample questions.
Include discovery, clarification, insufficient evidence, conflicts, injection attempts, and out-of-scope cases.
Mark expectations for user review before live acceptance.
Do not adjust expected results merely to obtain passing evaluations.

For updates, preserve user edits and explain breaking field changes.
Run `prepare` and `validate`.
Do not run `approve`, `select`, or cloud commands without the relevant user approval.
