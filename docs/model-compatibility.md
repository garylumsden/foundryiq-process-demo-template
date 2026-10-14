# Model compatibility and Claude

## Selection rule

Select models by verified managed-agent capabilities, not by provider name.
The managed-only deployment boundary is not a Claude exclusion.
Each selected model must satisfy the same prompt-agent, MCP retrieval, structured-output, and input requirements.

Direct model inference and the Foundry managed agent runtime are different interfaces.
Claude's documented native Messages API does not establish whether a managed prompt agent can use a particular Claude deployment.
Do not infer managed-agent incompatibility from a native API list.

## Historical findings

The earlier application used managed prompt agents for Claude before changing to a native Messages workflow.
The recorded explanation for that change was an OpenAI Responses API compatibility limitation.
The retained evidence does not support turning that explanation into a universal Claude restriction.

| Date | Retained evidence | Interpretation |
|---|---|---|
| 2026-10-05 | Opus and Sonnet responses record `status: completed`, named `agent_reference` values, and completed MCP retrieval calls. | The archived artifacts record successful managed-agent execution. |
| 2026-10-05 | Both responses record `text.format.type: json_schema` and `strict: true`. | The successful artifacts also record strict structured output. |
| 2026-10-07 | Separate diagnostics report HTTP 403 during MCP tool discovery or retrieval. | These are MCP access failures, not explicit model-support rejections. |
| 2026-10-08 | The implementation changed to an application-managed native Messages path. | This was a compatibility workaround, not proof of a provider-wide restriction. |

The historical deployment definitions identify the intended underlying models as Azure-hosted Claude Opus and Sonnet.
The diagnostic files remain outside this template and are not distributed as fixtures.
They are historical observations, not current live service verification.
The exact later failing request or feature remains unconfirmed.
A generic `invalid_request_error` does not identify an unsupported model or capability by itself.

## Template behavior

This template deploys one named Foundry prompt agent per selected answer model.
All selected models use the same managed-agent inference path and evidence controls.
There is no native-API or hosted adapter fallback.

A Claude profile is eligible only after verifying its exact model/version and the required managed-agent capabilities.
The same rule applies to every provider.
Do not enable a model merely because its native inference API works.
Do not exclude a model merely because its provider also has a different native API.
Configured capability flags record a reviewed choice; they do not prove service availability.

## Verification before making a support claim

Verify the current agent-supported catalog, actual model identity, version, deployment format, target location, access, quota, and provider terms.
Do not rely only on a deployment name, agent name, or historical response.

After approval for a diagnostic invocation:

1. Create or select the intended managed prompt agent and record its pinned version.
2. Invoke that agent through the Foundry project interface.
3. Require successful MCP retrieval against the intended authorized knowledge source.
4. Check the returned evidence and the required structured answer contract.
5. Exercise image input if the use case needs it.
6. Retain any failure with its model, version, API, tool, and connection context.

An active agent alone does not establish successful inference or tool use.
A failed request to another API does not establish a provider-wide limitation.
If current verification is unavailable, report the gap instead of claiming support or incompatibility.

## References

- [Microsoft Foundry Agent Service](https://learn.microsoft.com/azure/foundry/agents/overview)
- [Agent-supported model catalog](https://ai.azure.com/catalog/models?capabilities=agentsv2)
- [Claude model APIs and capabilities](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/claude-models)
- [Deploy and use Claude models](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/use-foundry-models-claude)
