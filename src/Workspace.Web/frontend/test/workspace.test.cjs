const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const { JSDOM } = require("jsdom");
const { configuration, answer } = require("./fixtures.cjs");
const assets = path.resolve(__dirname, "..", "..", "wwwroot");

async function workspace(config = configuration()) {
  const dom = new JSDOM(fs.readFileSync(path.join(assets, "index.html"), "utf8"), {
    url: "http://127.0.0.1:8080", runScripts: "outside-only"
  });
  const calls = [];
  dom.window.fetch = async (url, options = {}) => {
    calls.push({ url, options });
    const payload = url === "/api/config" ? config : url === "/api/token" ? { token: "fixture-token" } : {};
    return new Response(JSON.stringify(payload), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  for (const name of ["mermaid-passage.js", "workspace.js"]) {
    const exports = name === "mermaid-passage.js"
      ? "\nwindow.renderExtractedDiagram = renderExtractedDiagram; window.quoteExtractedFlowchartLabels = quoteExtractedFlowchartLabels; window.isExtractedMermaid = isExtractedMermaid;" : "";
    dom.window.eval(fs.readFileSync(path.join(assets, name), "utf8") + exports);
  }
  await dom.window.Workspace.init();
  return { dom, W: dom.window.Workspace, calls };
}

test("requires all three answer checks for factual prose while retaining backend source evidence", async t => {
  const { dom, W } = await workspace(); t.after(() => dom.window.close());
  let formatted = 0;
  dom.window.renderSourcePassage = text => {
    formatted++;
    const node = dom.window.document.createElement("pre"); node.textContent = text; return node;
  };
  for (const change of [{ outcome: "out_of_scope" }, { outcome: "insufficient_evidence" },
    { outcome: "clarification_required" }, { verification: "failed" }, { verification: "pending" }, { answer: null }]) {
    const result = answer(change);
    assert.equal(W.canRenderAnswer(result), false);
    const rendered = W.renderResult(result, "assessmentHeading", { captured: true });
    assert.equal(rendered.querySelector(".answer-prose"), null);
    assert.equal(rendered.querySelectorAll(".evidence-card").length, result.sources.length);
    assert.equal(rendered.querySelector(".evidence-card pre").textContent, result.sources[0].text);
    assert.ok(rendered.querySelector(".evidence-pane").textContent.includes(W.ui("evidenceAtTime")));
    assert.equal(rendered.querySelector(".verified"), null);
    assert.ok(rendered.textContent.includes(W.ui("noAnswer")));
  }
  assert.equal(formatted, 6);
  const good = W.renderResult(answer());
  assert.equal(formatted, 7);
  assert.ok(good.querySelector(".answer-prose").textContent.includes("confirm approval"));
  assert.equal(good.querySelectorAll(".citations a").length, 3);
});

test("pairs accepted image observations with one-based submitted images but never invents saved images", async t => {
  const { dom, W } = await workspace(); t.after(() => dom.window.close());
  const result = answer();
  result.answer.images = [
    { image: 2, description: "The second image shows a review field.", uncertainty: "Its status is unclear." },
    { image: 1, description: "The first image shows an owner field.", uncertainty: "Its name is unclear." }
  ];
  const submittedImages = [
    { name: "owner.png", url: "blob:http://127.0.0.1:8080/owner" },
    { name: "review.png", url: "blob:http://127.0.0.1:8080/review" }
  ];
  const view = W.renderResult(result, "answerHeading", { submittedImages });
  const observations = view.querySelectorAll(".image-observation");
  assert.equal(observations[0].querySelector("img").src, submittedImages[1].url);
  assert.equal(observations[0].querySelector("img").alt, "review.png");
  assert.ok(observations[0].querySelector(".image-observation-text").textContent.includes(result.answer.images[0].description));
  assert.equal(observations[1].querySelector("img").src, submittedImages[0].url);
  const saved = W.renderResult(result, "assessmentHeading", { captured: true });
  assert.equal(saved.querySelectorAll(".image-observation img").length, 0);
  const rejected = W.renderResult({ ...result, verification: "failed" }, "answerHeading", { submittedImages });
  assert.equal(rejected.querySelectorAll(".image-observation").length, 0);
});

test("renders model text literally and permits only safe original-document URLs", async t => {
  const { dom, W } = await workspace(); t.after(() => dom.window.close());
  const result = answer();
  result.answer.overview[0].text = '<img src="https://blocked.invalid/image" onerror="alert(1)"> **plain model text**';
  result.sources[0].documentUrl = "javascript:alert(1)";
  const view = W.renderResult(result);
  assert.equal(view.querySelectorAll("img,script").length, 0);
  assert.equal(view.querySelector(".evidence-card > a"), null);
  assert.ok(view.querySelector(".answer-prose").textContent.includes("<img"));
  for (const url of ["javascript:alert(1)", "data:text/html,test", "http://external.invalid", "https://user:pass@external.invalid"])
    assert.equal(W.safeUrl(url), null);
  assert.equal(W.safeUrl("https://external.invalid", true), null);
  assert.equal(W.safeUrl("/documents/a"), "http://127.0.0.1:8080/documents/a");
});

test("uses configured labels and supported theme choices without inline CSS", async t => {
  const config = configuration();
  config.ui.chatHeading = "Review supplier requirements";
  config.theme.layout = "compact"; config.theme.fontFamily = "Georgia"; config.theme.fontScale = 1.1;
  const { dom, W } = await workspace(config); t.after(() => dom.window.close());
  const doc = dom.window.document;
  assert.equal(doc.querySelector("h1").textContent, config.ui.chatHeading);
  assert.equal(doc.documentElement.dataset.layout, "compact");
  assert.equal(doc.documentElement.dataset.font, "georgia");
  assert.equal(doc.documentElement.dataset.scale, "1.1");
  assert.equal(doc.querySelectorAll("[style],style").length, 0);
  assert.equal(doc.getElementById("brand-logo").hidden, true);
  W.models(doc.getElementById("chat-model"));
  assert.equal(doc.getElementById("chat-model").value, "detailed");
});

test("shares a CSRF token across JSON and multipart posts and accepts an empty success response", async t => {
  const { dom, W, calls } = await workspace(); t.after(() => dom.window.close());
  const images = [];
  const data = W.multipart({ question: "A question", model: "balanced", previousQuestions: ["Earlier question"] }, images);
  assert.equal(data.get("previousQuestions"), '["Earlier question"]');
  await Promise.all([W.post("/api/ask", data), W.post("/api/search", { question: "A policy", page: 1, pageSize: 5 })]);
  assert.equal(calls.filter(call => call.url === "/api/token").length, 1);
  for (const call of calls.filter(call => call.options.method === "POST"))
    assert.equal(call.options.headers["X-CSRF-Token"], "fixture-token");
  dom.window.fetch = async () => new Response(null, { status: 200 });
  assert.equal(JSON.stringify(await W.post("/api/feedback", { traceId: "trace-1042", category: "useful" })), "{}");
});

test("does not hide request errors or non-JSON failures", async t => {
  const { dom, W } = await workspace(); t.after(() => dom.window.close());
  dom.window.fetch = async () => new Response(JSON.stringify({ error: "The source is unavailable." }), { status: 503 });
  await assert.rejects(W.request("/api/search"), /503: The source is unavailable/);
  dom.window.fetch = async () => new Response("<html>error</html>", { status: 500 });
  await assert.rejects(W.request("/api/search"), /500:/);
  const node = dom.window.document.getElementById("ask-error");
  W.visibleError(node, new Error("<script>failure</script>"));
  assert.equal(node.hidden, false);
  assert.equal(node.querySelector("script"), null);
  assert.ok(node.textContent.includes("<script>failure</script>"));
});

test("unsafe extracted diagrams remain visible as raw source without loading a library", async t => {
  const { dom } = await workspace(); t.after(() => dom.window.close());
  const source = "flowchart TD\nA[Input] --> B[Output]\nclick B \"https://external.invalid\"";
  const result = dom.window.renderExtractedDiagram(source);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(result.dataset.renderState, "unavailable");
  assert.equal(result.querySelector("pre").textContent, source);
  assert.equal(result.querySelector("details").open, true);
  assert.equal(dom.window.document.querySelector('script[src="/vendor/mermaid.min.js"]'), null);
});

test("recognises extracted diagrams and quotes incomplete flowchart labels without changing their words", async t => {
  const { dom } = await workspace(); t.after(() => dom.window.close());
  const raw = "flowchart TD\nA{Owner approval (signed)?} -- Ready (confirmed) --> B[Start]";
  assert.equal(dom.window.isExtractedMermaid(raw), true);
  assert.equal(dom.window.quoteExtractedFlowchartLabels(raw),
    'flowchart TD\nA{"Owner approval (signed)?"} -- "Ready (confirmed)" --> B[Start]');
  assert.equal(dom.window.isExtractedMermaid("Record an owner approval."), false);
  assert.equal(dom.window.quoteExtractedFlowchartLabels("sequenceDiagram\nA->>B: Request"), "sequenceDiagram\nA->>B: Request");
});
