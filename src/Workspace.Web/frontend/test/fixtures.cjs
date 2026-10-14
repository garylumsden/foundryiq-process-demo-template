const fields = [
  { id: "reference", label: "Request reference", type: "text", required: true, stage: "request", help: "Use the request tracking reference.", placeholder: "REQ-1042", options: [], maxLength: 80, min: null, max: null },
  { id: "context", label: "Proposed activity", type: "multiline", required: true, stage: "request", help: "Describe the proposed activity and its effect.", placeholder: "Describe the activity…", options: [], maxLength: 2000, min: null, max: null },
  { id: "state", label: "Review stage", type: "select", required: true, stage: "review", help: "Select the current review stage.", placeholder: "Select a stage…", options: ["Received", "In review", "Approved"], maxLength: null, min: null, max: null },
  { id: "areas", label: "Affected areas", type: "multiselect", required: true, stage: "review", help: "Select the areas affected by this request.", placeholder: "", options: ["Operations", "Purchasing", "Service delivery"], maxLength: null, min: null, max: null },
  { id: "required-by", label: "Required by", type: "date", required: true, stage: "review", help: "Select the required completion date.", placeholder: "", options: [], maxLength: null, min: null, max: null },
  { id: "effort", label: "Estimated hours", type: "number", required: true, stage: "review", help: "Enter the estimated effort in hours.", placeholder: "8", options: [], maxLength: null, min: 0, max: 100 },
  { id: "approval", label: "Approval confirmed", type: "boolean", required: true, stage: "review", help: "Select this option when the owner confirms approval.", placeholder: "", options: [], maxLength: null, min: null, max: null }
];
const ui = {
  title: "Operations Workspace", chatNav: "Policy assistant", processNav: "Request register",
  chatHeading: "Review a process question", chatHelp: "Use current policy documents to review a question.",
  questionLabel: "Your question", questionPlaceholder: "What evidence supports this request…",
  ask: "Ask question", newConversation: "New conversation", modelLabel: "Answer model",
  imagesLabel: "Attached images", imagesHelp: "Attach PNG or JPEG images. The server checks each file.",
  removeImage: "Remove image", promptHeading: "Example questions", promptHelp: "Select a question, review it, then submit it.",
  searchLabel: "Search source passages", searchPlaceholder: "Find a policy requirement…", search: "Search sources",
  answerHeading: "Policy answer", sourcesHeading: "Source evidence", limitsHeading: "Limits",
  imagesHeading: "Image observations", rawEvidenceHelp: "Check the extracted text against the original document.",
  openDocument: "Open original document", noAnswer: "No verified answer is available.",
  busy: "Processing…", ready: "Ready", preview: "Configuration preview", unavailable: "Service unavailable",
  insufficientEvidence: "Insufficient evidence", clarificationRequired: "Clarification required",
  outOfScope: "Outside the supported scope", verificationFailed: "Verification failed",
  processHeading: "Request register", processHelp: "Create a request and assess its proposed activity against the current policies.",
  createRecord: "Create request", registerHeading: "Recorded requests", emptyRegister: "No requests have been recorded.",
  referenceColumn: "Reference", outcomeColumn: "Assessment", recordedColumn: "Recorded",
  submitProcess: "Assess and record", fillExample: "Fill example values", back: "Back to register",
  assessmentHeading: "Request assessment", detailsHeading: "Request details",
  evidenceAtTime: "This evidence was captured when the request was recorded.",
  feedbackUseful: "Useful", feedbackIncorrect: "Incorrect", feedbackUnsupported: "Unsupported",
  footer: "Check each source before you act.", imageUncertainty: "Uncertainty",
  validationError: "Check the entered values.", requestFailed: "The request failed.",
  loading: "Loading…", answered: "Verified answer", saved: "Saved", skipLink: "Skip to main content",
  close: "Close", usageLabel: "Session usage", tokenLabel: "Tokens", sourceCount: "Sources",
  questionCount: "Questions", previousPage: "Previous page", nextPage: "Next page", pageLabel: "Page"
};
const formValues = {
  reference: "REQ-1042", context: "Review an update to the operating procedure.",
  state: "In review", areas: ["Operations", "Service delivery"], "required-by": "2027-01-15", effort: 8.5, approval: false
};
function configuration(options = {}) {
  return {
    profile: { id: "operations", version: "1.0", title: "Operations Workspace", purpose: "Review requests against approved policies." },
    digest: "A1B2C3", ui: { ...ui }, theme: {
      layout: "comfortable", font: "system", textSize: 16,
      tokens: { ink: "#172b3f", muted: "#4b5e70", primary: "#155c8a", onPrimary: "#ffffff", canvas: "#f3f6f8", surface: "#ffffff", border: "#c7d3de", focus: "#155c8a", danger: "#a32332", banner: "#213b50" },
      logo: null, logoAlt: "Workspace logo"
    },
    process: { id: "request-review", version: "1.0", name: "Request review",
      stages: [{ id: "request", label: "Request information" }, { id: "review", label: "Review information" }],
      fields: structuredClone(fields), referenceField: "reference", registerColumns: ["reference", "state", "effort"],
      summaryFields: ["reference", "context", "state"], assessmentFields: ["context", "areas", "approval"]
    },
    examples: { prompts: [
      { id: "approval-check", group: "Request review", title: "Check approval requirements", question: "Which approval evidence is required before work starts?", featured: true, images: [] },
      { id: "check-image", group: "Supporting information", title: "Review an attached checklist", question: "Which review steps appear on this checklist?", featured: false, images: [{ url: "/api/examples/check-image/images/1", name: "checklist.png" }] }
    ], formValues: { ...formValues } },
    models: [{ key: "balanced", label: "Balanced reasoning" }, { key: "detailed", label: "Detailed reasoning" }],
    defaultModel: "detailed",
    limits: { maxImages: 5, maxImageBytes: 10485760, maxTotalBytes: 26214400, maxQuestionCharacters: 4000 },
    ready: true, readinessMessage: "Ready", ...options
  };
}
function answer(options = {}) {
  return {
    outcome: "answered",
    answer: {
      outcome: "answered",
      overview: [{ text: "The owner must confirm approval before work starts.", references: ["r1"] }],
      summaries: [{ documentId: "POL-001", text: "The policy requires a recorded approval.", references: ["r1"] }],
      quotations: [{ text: "Record the approval before work starts.", reference: "r1" }],
      images: [{ image: 1, description: "The checklist includes an approval field.", uncertainty: "The image does not show whether an owner approved the request." }],
      limitations: ["The policy does not specify a completion date."]
    },
    sources: [{
      refId: "r1", chunkId: "c1", documentId: "POL-001", title: "Request Review Policy", revision: "1.0",
      status: "current", section: "Approval", text: "# Approval\n\nRecord the **approval** before work starts.\n\n| Step | Owner |\n| --- | --- |\n| Review | Request owner |\n\n```mermaid\nflowchart TD\nA[Request] --> B{Approved?}\nB -- Yes --> C[Proceed]\n```",
      sourceHash: "D4E5F6", imagePath: "", documentUrl: "/documents/pol-001"
    }],
    messages: [], issues: [], usage: { inputTokens: 240, outputTokens: 82 },
    traceId: "trace-1042", profileDigest: "A1B2C3", model: "detailed", verification: "passed", ...options
  };
}
function record(options = {}) {
  const config = configuration();
  return {
    id: "record-1042", profileId: "operations", profileDigest: config.digest, profileVersion: "1.0",
    process: config.process, values: { ...formValues }, assessment: answer(), recordedAt: "2026-10-14T08:00:00Z", ...options
  };
}
module.exports = { configuration, answer, record };
