---
name: scenario-branding
description: Research and configure customer or industry presentation, supported theme values, accessible layouts, and an optional supplied logo for all workspace experiences.
---

# Scenario branding

Produce a consistent, readable theme for chat, process forms, records, and source displays.

Read public brand guidance when the user permits research for a named customer.
Otherwise, propose a fitting industry style and identify it as an interpretation.
Record design sources in `.local/setup`.
Do not claim invented styling is official customer branding.

Configure colors, font choice, text size, and layout in `theme.json`.
Use only the supported values in the schema.
Keep every text, hover, focus, disabled, error, and evidence state readable.
Meet text contrast of at least 4.5:1.
Preserve keyboard access, visible focus, reduced motion, labels, and narrow-screen behavior.

Accept an optional user-supplied JPEG or PNG logo.
Check its usage rights, file type, dimensions, size, and alternative text.
Convert or sanitize a supplied vector before considering it for use.
Do not download or publish customer assets without authorization.
Do not use remote fonts, tracking assets, executable markup, or generated unrestricted CSS.
Use text branding when no logo is supplied.

Validate the configured contrast and inspect the app at 390 px and 1440 px.
Inspect both logo and no-logo layouts when a logo is introduced.
Do not replace required evidence displays or add repeated presentation notices.
