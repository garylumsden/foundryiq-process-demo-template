# Architecture and controls

## Request flow

1. The browser reads sanitized configuration and an antiforgery token.
2. The server validates the question, model choice, history, form fields, and images.
3. The selected model path retrieves from the configured Foundry IQ knowledge base.
4. The server binds reference IDs to admitted native passages.
5. The server checks response structure and reference identities.
6. An evidence-only verification call checks every claim and the task scope.
7. The server rechecks source state.
8. Only a passing answer reaches the browser or an accepted saved assessment.

The verifier receives the approved task, current request, candidate claims, relevant input images, and verified evidence.
It cannot retrieve other material.
Missing, uncertain, failed, incomplete, and unsupported decisions block factual output.
Rejected prose is not returned as an accepted answer.

## Evidence boundary

Registry admission binds the profile digest, source identities, lifecycle metadata, original hashes, and exact native passages.
Source-only search uses the same eligible-source filter and passage checks.
Original PDF delivery checks the current registry and the downloaded bytes.
Citation normalization resolves only exact provider labels or unambiguous verified passage identities.
The application does not infer a citation from a similar title or chunk suffix.
An empty scheduled indexing run does not clear a failed native batch.
Resolve failed items before publishing the complete registry; the tooling does not reset an indexer automatically.

Native figure descriptions remain enrichment, not publisher clauses.
User images are untrusted observations and do not establish policy requirements.
Earlier generated responses are not evidence.
Runtime inference does not use unrestricted web research.

## Process records

The server builds the assessment request and performs the assessment itself.
The browser cannot supply a trusted assessment, evidence list, or verification outcome.
Records retain typed values, profile digest, process schema, assessment state, and evidence at assessment time.
Atomic file replacement preserves previously committed records if a temporary write fails.
Records remain isolated by scenario and process.

## Identity and telemetry

Cloud-to-cloud access uses managed identities.
Local requests use an existing Azure CLI identity in the configured tenant.
The application checks the configured principal before cloud readiness.
Cloud scripts do not initiate interactive authentication.

Application Insights records safe outcome, verification, model, version, token, and latency metadata.
Questions, form data, source passages, images, customer names, and credentials must not enter logs.
The browser receives no deployment endpoints or environment identity settings.

## Deliberate boundaries

The web host runs on loopback HTTP.
Records use local JSON storage.
These choices keep the workflow easy to inspect.
A production service needs authenticated HTTPS hosting, user authorization, durable managed storage, and a reviewed operating model.

Schema checks and model verification do not guarantee truth, complete source coverage, or a correct business decision.
Live evaluations and appropriate human review remain necessary.
