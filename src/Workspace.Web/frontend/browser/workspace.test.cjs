const assert = require("node:assert/strict");
const { test, before, after } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const http = require("node:http");
const { chromium } = require("playwright");
const { configuration, answer, record } = require("../test/fixtures.cjs");
const assets = path.resolve(__dirname, "..", "..", "wwwroot");
const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j12cAAAAASUVORK5CYII=", "base64");
let server;
let browser;
let base;

before(async () => {
  if (process.env.WORKSPACE_BASE_URL) base = process.env.WORKSPACE_BASE_URL.replace(/\/$/, "");
  else {
    server = http.createServer((request, response) => {
      const pathname = new URL(request.url, "http://127.0.0.1").pathname;
      const file = path.resolve(assets, `.${decodeURIComponent(pathname === "/" ? "/index.html" : pathname)}`);
      if (!file.startsWith(assets + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
        response.writeHead(404); response.end(); return;
      }
      const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8" };
      response.writeHead(200, {
        "Content-Type": types[path.extname(file)] || "application/octet-stream",
        "Content-Security-Policy": "default-src 'self'; img-src 'self' blob:; style-src 'self' 'report-sample'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'",
        "X-Content-Type-Options": "nosniff"
      });
      fs.createReadStream(file).pipe(response);
    });
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    base = `http://127.0.0.1:${server.address().port}`;
  }
  const edges = [
    path.join(process.env["ProgramFiles(x86)"] || "", "Microsoft", "Edge", "Application", "msedge.exe"),
    path.join(process.env.ProgramFiles || "", "Microsoft", "Edge", "Application", "msedge.exe"),
    path.join(process.env.LOCALAPPDATA || "", "Microsoft", "Edge", "Application", "msedge.exe")
  ];
  const executablePath = process.env.BROWSER_EXECUTABLE || edges.find(file => fs.existsSync(file));
  browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
});
after(async () => {
  await browser?.close();
  if (server) await new Promise(resolve => server.close(resolve));
});

async function pageFor(t, options = {}) {
  const config = options.config || configuration();
  const context = await browser.newContext({ viewport: { width: options.width || 1440, height: 1000 }, reducedMotion: "reduce" });
  const page = await context.newPage();
  const failures = [];
  const external = [];
  const calls = [];
  let currentRecord = options.record || record();
  const apiErrors = options.errors || {};
  t.after(async () => {
    try {
      assert.deepEqual(external, [], "The application must not request external assets.");
      const violations = await page.evaluate(() => window.workspaceCspViolations || []);
      assert.deepEqual([...new Set(violations)].slice(0, 6), [], "The application must comply with the content policy.");
      assert.deepEqual([...new Set(failures)].slice(0, 3), [], "The application must not produce browser errors.");
    } finally { await context.close(); }
  });
  await page.addInitScript(() => {
    window.workspaceCspViolations = [];
    document.addEventListener("securitypolicyviolation", event => {
      window.workspaceCspViolations.push(`${event.violatedDirective}: ${event.sample} (${event.sourceFile}:${event.lineNumber})`);
    });
  });
  if (options.trackImageUrls) await page.addInitScript(() => {
    window.workspaceImageUrls = { created: [], revoked: [] };
    const create = URL.createObjectURL.bind(URL);
    const revoke = URL.revokeObjectURL.bind(URL);
    URL.createObjectURL = blob => {
      const url = create(blob);
      window.workspaceImageUrls.created.push({ url, name: blob.name, blob });
      return url;
    };
    URL.revokeObjectURL = url => {
      window.workspaceImageUrls.revoked.push(url); revoke(url);
    };
  });
  page.on("pageerror", error => failures.push(error.message));
  page.on("console", message => {
    if (message.type() === "error" && /Content Security Policy|Refused to|violates the following/.test(message.text()))
      failures.push(message.text());
  });
  await context.route("**/*", async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.origin !== new URL(base).origin) { external.push(url.href); await route.abort(); return; }
    if (url.pathname === "/theme.css") {
      const tokens = Object.entries(config.theme.tokens).map(([key, value]) => `--${key}:${value}`).join(";");
      const fonts = { system: "system-ui, sans-serif", humanist: "Verdana, sans-serif", serif: "Georgia, serif" };
      await route.fulfill({ contentType: "text/css", body: `:root{${tokens};--font-family:${fonts[config.theme.font]};--text-size:${config.theme.textSize}px}` }); return;
    }
    if (!url.pathname.startsWith("/api/")) { await route.continue(); return; }
    calls.push({ path: url.pathname, method: request.method(), headers: request.headers(), body: request.postData() });
    if (apiErrors[url.pathname]) {
      await route.fulfill({ status: apiErrors[url.pathname].status || 503, json: { error: apiErrors[url.pathname].message } }); return;
    }
    let json;
    switch (url.pathname) {
      case "/api/config": json = config; break;
      case "/api/status": json = { ready: config.ready, readinessMessage: config.readinessMessage, requests: 3, failures: 0, inputTokens: 240, outputTokens: 82 }; break;
      case "/api/token": json = { token: "browser-token" }; break;
      case "/api/ask": json = options.answer || answer(); break;
      case "/api/search": {
        const body = JSON.parse(request.postData());
        json = { documents: answer().sources, page: body.page, pageSize: body.pageSize, total: 6, hasNext: body.page === 1 }; break;
      }
      case "/api/process-records":
        if (request.method() === "POST") { currentRecord = options.savedRecord || currentRecord; json = currentRecord; }
        else json = { records: options.records || [currentRecord] };
        break;
      case "/api/feedback":
        await route.fulfill({ status: 200, body: "" }); return;
      case "/api/logo":
      case "/api/examples/check-image/images/1":
        await route.fulfill({ contentType: "image/png", body: png }); return;
      default:
        if (url.pathname.startsWith("/api/process-records/")) json = currentRecord;
        else { await route.fulfill({ status: 404, json: { error: "The requested resource is unavailable." } }); return; }
    }
    await route.fulfill({ json });
  });
  await page.goto(`${base}${options.path || "/"}`);
  if (apiErrors["/api/config"]) await page.waitForSelector('#startup[role="alert"]');
  else {
    await page.waitForSelector("#application:not([hidden])");
    await page.waitForFunction(() => !document.getElementById("usage").textContent.includes("Loading"));
  }
  return { page, config, calls };
}

async function noOverflow(page) {
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, "The page must fit the viewport.");
}

async function screenshot(page, name) {
  if (process.env.WORKSPACE_SCREENSHOTS !== "1") return;
  const directory = path.join(__dirname, "artifacts");
  fs.mkdirSync(directory, { recursive: true });
  await page.screenshot({ path: path.join(directory, `${name}.png`), fullPage: true });
}

for (const width of [390, 1440]) {
  test(`chat page renders configured content at ${width}px without a logo or external assets`, async t => {
    const { page, config, calls } = await pageFor(t, { width });
    assert.equal(await page.locator("h1").textContent(), config.ui.chatHeading);
    assert.equal(await page.locator("#brand-logo").isVisible(), false);
    assert.equal(await page.locator("#chat-model").inputValue(), "detailed");
    assert.equal(await page.locator(".top-banner").textContent(), "DEMO");
    assert.equal(await page.locator(".prompt-button").count(), 2);
    await page.getByRole("button", { name: "Check approval requirements" }).click();
    assert.equal(await page.locator("#question").inputValue(), config.examples.prompts[0].question);
    assert.equal(calls.filter(call => call.path === "/api/ask").length, 0);
    await noOverflow(page);
    await screenshot(page, `chat-${width}`);
    await page.keyboard.press("Tab");
    assert.equal(await page.evaluate(() => document.activeElement !== document.body), true);
  });
  test(`process page uses all configured field types and logo at ${width}px`, async t => {
    const config = configuration(); config.theme.logo = "assets\\workspace.png"; config.theme.layout = "compact";
    const { page, calls } = await pageFor(t, { config, width, path: "/business-process.html" });
    assert.equal(await page.locator("#brand-logo").isVisible(), true);
    assert.ok(calls.some(call => call.path === "/api/logo"));
    await page.getByRole("button", { name: config.ui.createRecord }).click();
    await page.getByRole("button", { name: config.ui.fillExample }).click();
    assert.equal(await page.getByLabel("Request reference", { exact: false }).inputValue(), "REQ-1042");
    assert.equal(await page.locator("#field-context").getAttribute("maxlength"), "2000");
    assert.equal(await page.locator("#field-state").inputValue(), "In review");
    assert.deepEqual(await page.locator("#field-areas").evaluate(node => [...node.selectedOptions].map(option => option.value)), ["Operations", "Service delivery"]);
    assert.equal(await page.locator("#field-required-by").inputValue(), "2027-01-15");
    assert.equal(await page.locator("#field-effort").inputValue(), "8.5");
    assert.equal(await page.locator("#field-approval").isChecked(), false);
    assert.equal(await page.locator("#field-approval").getAttribute("required"), null);
    assert.equal(await page.locator("#process-form").evaluate(form => form.checkValidity()), true);
    await noOverflow(page);
    await screenshot(page, `process-${width}`);
    await page.getByRole("button", { name: config.ui.submitProcess }).click();
    await page.waitForSelector("#record-view:not([hidden])");
    const post = calls.find(call => call.path === "/api/process-records" && call.method === "POST");
    assert.equal(post.headers["x-csrf-token"], "browser-token");
    assert.match(post.body, /name="values"/);
    assert.match(post.body, /"effort":8.5/);
    assert.match(post.body, /"approval":false/);
    assert.doesNotMatch(post.body, /name="assessment"/);
    assert.equal(await page.locator(".record-summary h3").textContent(), "Request review");
    assert.equal(await page.locator(".answer-prose").count(), 1);
    await noOverflow(page);
  });
}

test("verified chat answers retain citations, safe source formatting, diagrams, images, and feedback", async t => {
  const { page, config, calls } = await pageFor(t);
  await page.locator("#question").fill("Which evidence is required before work starts?");
  await page.locator("#chat-images-input").setInputFiles({ name: "checklist.png", mimeType: "image/png", buffer: png });
  assert.equal(await page.locator(".image-preview").count(), 1);
  await page.getByRole("button", { name: config.ui.ask, exact: true }).click();
  await page.waitForSelector('.result[data-verified="true"]');
  const ask = calls.find(call => call.path === "/api/ask");
  assert.match(ask.body, /name="images"; filename="checklist.png"/);
  assert.match(ask.body, /name="previousQuestions"\r?\n\r?\n\[\]/);
  assert.equal(await page.locator(".answer-prose img").count(), 1);
  assert.equal(await page.locator(".image-observation img").getAttribute("alt"), "checklist.png");
  await page.waitForFunction(() => {
    const image = document.querySelector(".image-observation img");
    return image.complete && image.naturalWidth > 0;
  });
  assert.equal(await page.locator(".image-preview").count(), 0);
  assert.ok((await page.locator(".image-observation").textContent()).includes(config.ui.imageUncertainty));
  assert.equal(await page.locator(".citations a").count(), 3);
  await page.locator(".citations a").first().click();
  assert.equal(await page.locator(".evidence-pane").getAttribute("open"), "");
  assert.equal(await page.locator(".source-table th").first().textContent(), "Step");
  await page.waitForSelector('.source-diagram[data-render-state="rendered"]', { timeout: 30000 });
  assert.equal(await page.locator(".diagram-frame svg").count(), 1);
  assert.equal(await page.locator(".diagram-frame [style]").count(), 0);
  assert.equal(await page.evaluate(() => document.adoptedStyleSheets.length), 0);
  assert.equal(await page.locator(".diagram-render-stage").count(), 0);
  assert.ok(await page.locator(".diagram-frame svg").evaluate(svg => svg.viewBox.baseVal.width > 100 && svg.viewBox.baseVal.height > 100));
  await page.locator(".diagram-opener").focus(); await page.keyboard.press("Enter");
  await page.waitForSelector("dialog[open]");
  await page.getByRole("button", { name: "Actual size", exact: true }).click();
  await page.getByRole("button", { name: "Fit width", exact: true }).click();
  await page.keyboard.press("Escape");
  await page.waitForSelector("dialog", { state: "detached" });
  assert.equal(await page.locator("dialog").count(), 0);
  await page.getByRole("button", { name: config.ui.feedbackUseful, exact: true }).click();
  await page.waitForFunction(() => document.querySelector(".feedback [role=status]").textContent === "Saved");
  const feedback = calls.find(call => call.path === "/api/feedback");
  assert.deepEqual(JSON.parse(feedback.body), { traceId: "trace-1042", category: "useful" });
  await page.locator("#question").fill("Which owner records the approval?");
  await page.getByRole("button", { name: config.ui.ask, exact: true }).click();
  await page.waitForFunction(() => document.querySelectorAll(".result").length === 2);
  const second = calls.filter(call => call.path === "/api/ask")[1];
  assert.match(second.body, /\["Which evidence is required before work starts\?"\]/);
  await page.getByRole("button", { name: config.ui.newConversation }).click();
  assert.equal(await page.locator(".result").count(), 0);
});

test("chat submits only the four most recent previous questions after seven exchanges", async t => {
  const { page, config, calls } = await pageFor(t, { answer: answer({ sources: [] }) });
  const questions = Array.from({ length: 7 }, (_, index) => `Review requirement ${index + 1}.`);
  for (let index = 0; index < questions.length; index++) {
    await page.locator("#question").fill(questions[index]);
    await page.getByRole("button", { name: config.ui.ask, exact: true }).click();
    await page.waitForFunction(count => document.querySelectorAll(".result").length === count, index + 1);
    const request = calls.filter(call => call.path === "/api/ask")[index];
    const history = JSON.parse(request.body.match(/name="previousQuestions"\r?\n\r?\n([^\r\n]*)/)[1]);
    assert.deepEqual(history, questions.slice(Math.max(0, index - 4), index));
    assert.ok(history.length <= 4);
  }
  assert.equal(calls.filter(call => call.path === "/api/ask").length, 7);
});

test("chat keeps actual submitted images beside accepted observations and releases their URLs on cleanup", async t => {
  const result = answer({ sources: [] });
  result.answer.images = [
    { image: 2, description: "The second image shows a review field.", uncertainty: "Its status is unclear." },
    { image: 1, description: "The first image shows an owner field.", uncertainty: "Its name is unclear." }
  ];
  const { page, config } = await pageFor(t, { answer: result, trackImageUrls: true, width: 390 });
  await page.locator("#chat-images-input").setInputFiles([
    { name: "owner.png", mimeType: "image/png", buffer: png },
    { name: "review.png", mimeType: "image/png", buffer: png }
  ]);
  await page.locator("#question").fill("Review the fields shown in these images.");
  await page.getByRole("button", { name: config.ui.ask, exact: true }).click();
  await page.waitForSelector('.result[data-verified="true"]');
  const observations = page.locator(".image-observation");
  assert.equal(await observations.count(), 2);
  const retainedUrls = [];
  for (const [index, name] of [[0, "review.png"], [1, "owner.png"]]) {
    const observation = observations.nth(index);
    const image = observation.locator(":scope > img");
    assert.equal(await image.getAttribute("alt"), name);
    assert.ok((await observation.locator(".image-observation-text").textContent()).includes(result.answer.images[index].description));
    await image.evaluate(async node => { await node.decode(); });
    const url = await image.getAttribute("src");
    retainedUrls.push(url);
    const state = await page.evaluate(() => window.workspaceImageUrls);
    assert.ok(state.created.some(entry => entry.url === url && entry.name === name));
    assert.ok(!state.revoked.includes(url));
    assert.deepEqual(await page.evaluate(async source => {
      const entry = window.workspaceImageUrls.created.find(image => image.url === source);
      return [...new Uint8Array(await entry.blob.arrayBuffer())];
    }, url), [...png]);
  }
  assert.equal(await page.locator(".image-preview").count(), 0);
  await noOverflow(page);
  await page.getByRole("button", { name: config.ui.newConversation }).click();
  const cleared = await page.evaluate(() => window.workspaceImageUrls);
  assert.ok(retainedUrls.every(url => cleared.revoked.includes(url)));
  assert.equal(await page.locator(".image-observation").count(), 0);
  await page.locator("#chat-images-input").setInputFiles({ name: "owner.png", mimeType: "image/png", buffer: png });
  await page.locator("#question").fill("Review the owner field.");
  await page.getByRole("button", { name: config.ui.ask, exact: true }).click();
  await page.waitForSelector(".image-observation img");
  await page.evaluate(() => dispatchEvent(new PageTransitionEvent("pagehide", { persisted: false })));
  const cleaned = await page.evaluate(() => window.workspaceImageUrls);
  assert.ok(cleaned.created.every(entry => cleaned.revoked.includes(entry.url)));
});

test("chat and saved record views retain safe source evidence while withholding rejected factual prose", async t => {
  for (const changes of [{ verification: "failed" }, { verification: "pending" }, { answer: null },
    { outcome: "insufficient_evidence" }, { outcome: "clarification_required" }, { outcome: "out_of_scope" }]) {
    const blocked = answer(changes);
    blocked.sources[0].text = "# Approval\n\nRecord the approval before work starts.";
    const chat = await pageFor(t, { answer: blocked });
    await chat.page.locator("#question").fill("Review the approval requirement.");
    await chat.page.getByRole("button", { name: chat.config.ui.ask, exact: true }).click();
    await chat.page.waitForSelector(".result");
    assert.equal(await chat.page.locator(".answer-prose,.verified").count(), 0);
    await chat.page.locator(".evidence-pane > summary").click();
    assert.equal(await chat.page.locator(".evidence-card").isVisible(), true);
    assert.match(await chat.page.locator(".source-passage").textContent(), /Record the approval/);
    assert.ok((await chat.page.locator(".result").textContent()).includes(chat.config.ui.noAnswer));
    const saved = await pageFor(t, { record: record({ assessment: blocked }), path: "/business-process.html?record=record-1042" });
    await saved.page.waitForSelector("#record-view:not([hidden])");
    assert.equal(await saved.page.locator(".answer-prose,.verified").count(), 0);
    await saved.page.locator(".evidence-pane > summary").click();
    assert.equal(await saved.page.locator(".evidence-card").isVisible(), true);
    assert.ok((await saved.page.locator(".evidence-pane").textContent()).includes(saved.config.ui.evidenceAtTime));
    assert.equal(await saved.page.locator(".image-observation img").count(), 0);
    assert.equal(await saved.page.locator(".result").getAttribute("data-verified"), "false");
  }
});

test("saved assessments use the captured process and evidence rather than current definitions", async t => {
  const saved = record();
  saved.process.name = "Earlier request review";
  saved.process.fields[0].label = "Earlier reference label";
  saved.assessment.sources[0].revision = "0.9";
  saved.assessment.sources[0].text = "The earlier policy requires an owner approval.";
  const { page, config } = await pageFor(t, { record: saved, path: "/business-process.html?record=record-1042" });
  await page.waitForSelector("#record-view:not([hidden])");
  assert.equal(await page.locator(".record-summary h3").textContent(), "Earlier request review");
  assert.ok((await page.locator(".record-details").first().textContent()).includes("Earlier reference label"));
  await page.locator(".citations a").first().click();
  assert.ok((await page.locator(".evidence-pane").textContent()).includes(config.ui.evidenceAtTime));
  assert.ok((await page.locator(".evidence-meta").textContent()).includes("0.9"));
  assert.ok((await page.locator(".source-passage").textContent()).includes("earlier policy"));
  assert.equal(await page.locator(".image-observation img").count(), 0);
});

test("preview mode disables inference but retains examples, editable forms, and saved records", async t => {
  const config = configuration({ ready: false, readinessMessage: "Review the configuration before connecting the services." });
  const { page, calls } = await pageFor(t, { config });
  assert.equal(await page.locator("#ask-button").isDisabled(), true);
  assert.equal(await page.locator("#search-button").isDisabled(), true);
  await page.getByRole("button", { name: "Review an attached checklist" }).click();
  await page.waitForSelector(".image-preview");
  assert.ok(calls.some(call => call.path === "/api/examples/check-image/images/1"));
  assert.equal(calls.filter(call => call.method === "POST").length, 0);
  const process = await pageFor(t, { config, path: "/business-process.html" });
  await process.page.getByRole("button", { name: config.ui.createRecord }).click();
  await process.page.getByRole("button", { name: config.ui.fillExample }).click();
  assert.equal(await process.page.locator("#submit-process").isDisabled(), true);
  assert.equal(await process.page.locator("#field-reference").inputValue(), "REQ-1042");
  await process.page.getByRole("button", { name: config.ui.back }).click();
  await process.page.getByRole("link", { name: "REQ-1042", exact: true }).click();
  await process.page.waitForSelector("#record-view:not([hidden])");
  assert.equal(await process.page.locator(".answer-prose").count(), 1);
});

test("source search keeps questions out of URLs while paginating safe raw extracts", async t => {
  const { page, config, calls } = await pageFor(t);
  await page.locator("#search-question").fill("Approval requirements");
  await page.getByRole("button", { name: config.ui.search, exact: true }).click();
  await page.waitForSelector("#search-results .evidence-card");
  assert.equal(await page.locator("#search-results .source-passage").count(), 0);
  assert.ok((await page.locator("#search-results .raw-extract").textContent()).includes("**approval**"));
  assert.equal(await page.locator("#previous-page").isDisabled(), true);
  await page.getByRole("button", { name: config.ui.nextPage }).click();
  await page.waitForFunction(() => document.getElementById("page-number").textContent === "Page 2");
  assert.equal(await page.locator("#next-page").isDisabled(), true);
  assert.equal(new URL(page.url()).searchParams.get("page"), "2");
  assert.equal(new URL(page.url()).searchParams.has("q"), false);
  assert.ok(!decodeURIComponent(page.url()).includes("Approval requirements"));
  assert.equal(await page.evaluate(() => history.state.sourceSearch.question), "Approval requirements");
  assert.deepEqual(calls.filter(call => call.path === "/api/search").map(call => JSON.parse(call.body).page), [1, 2]);
  assert.ok(calls.filter(call => call.path === "/api/search").every(call => JSON.parse(call.body).question === "Approval requirements"));
  await page.reload();
  await page.waitForSelector("#application:not([hidden])");
  assert.equal(await page.locator("#search-question").inputValue(), "Approval requirements");
  assert.ok(!decodeURIComponent(page.url()).includes("Approval requirements"));
});

test("server errors remain visible in chat, search, feedback, register, and form views", async t => {
  const errors = {
    "/api/ask": { message: "The answer service is unavailable." },
    "/api/search": { message: "The source service is unavailable." }
  };
  const chat = await pageFor(t, { errors });
  await chat.page.locator("#question").fill("Review the request.");
  await chat.page.getByRole("button", { name: chat.config.ui.ask, exact: true }).click();
  await chat.page.waitForSelector("#ask-error:not([hidden])");
  assert.match(await chat.page.locator("#ask-error").textContent(), /answer service is unavailable/);
  await chat.page.locator("#search-question").fill("Approval");
  await chat.page.getByRole("button", { name: chat.config.ui.search, exact: true }).click();
  await chat.page.waitForSelector("#search-error:not([hidden])");
  const feedback = await pageFor(t, { errors: { "/api/feedback": { message: "Feedback could not be recorded." } } });
  await feedback.page.locator("#question").fill("Review the approval.");
  await feedback.page.getByRole("button", { name: feedback.config.ui.ask, exact: true }).click();
  await feedback.page.waitForSelector(".result");
  await feedback.page.getByRole("button", { name: feedback.config.ui.feedbackIncorrect, exact: true }).click();
  await feedback.page.waitForSelector(".feedback .error:not([hidden])");
  const process = await pageFor(t, { path: "/business-process.html", errors: { "/api/process-records": { message: "The record store is unavailable." } } });
  await process.page.waitForSelector("#process-error:not([hidden])");
  await process.page.getByRole("button", { name: process.config.ui.createRecord }).click();
  await process.page.getByRole("button", { name: process.config.ui.fillExample }).click();
  await process.page.getByRole("button", { name: process.config.ui.submitProcess }).click();
  await process.page.waitForSelector("#form-error:not([hidden])");
  assert.equal(await process.page.locator("#new-record").isVisible(), true);
  assert.match(await process.page.locator("#form-error").textContent(), /record store is unavailable/);
});

test("image limits reject invalid files visibly and image removal operates from the keyboard", async t => {
  const { page, config, calls } = await pageFor(t);
  await page.locator("#chat-images-input").setInputFiles({ name: "unsupported.gif", mimeType: "image/gif", buffer: png });
  assert.equal(await page.locator("#chat-images .error").isVisible(), true);
  assert.equal(await page.locator(".image-preview").count(), 0);
  await page.locator("#chat-images-input").setInputFiles({ name: "checklist.png", mimeType: "image/png", buffer: png });
  assert.equal(await page.locator(".image-preview").count(), 1);
  await page.getByRole("button", { name: `${config.ui.removeImage}: checklist.png` }).focus();
  await page.keyboard.press("Enter");
  assert.equal(await page.locator(".image-preview").count(), 0);
  await page.locator("#chat-images-input").setInputFiles(Array.from({ length: 6 }, (_, i) => ({ name: `file-${i}.png`, mimeType: "image/png", buffer: png })));
  assert.equal(await page.locator("#chat-images .error").isVisible(), true);
  assert.equal(await page.locator(".image-preview").count(), 0);
  await page.locator("#chat-images-input").setInputFiles({ name: "oversized.png", mimeType: "image/png", buffer: Buffer.alloc(10485761) });
  assert.equal(await page.locator("#chat-images .error").isVisible(), true);
  assert.equal(await page.locator(".image-preview").count(), 0);
  await page.locator("#chat-images-input").setInputFiles(Array.from({ length: 3 }, (_, i) => ({ name: `large-${i}.png`, mimeType: "image/png", buffer: Buffer.alloc(9 * 1048576) })));
  assert.equal(await page.locator("#chat-images .error").isVisible(), true);
  assert.equal(await page.locator(".image-preview").count(), 0);
  assert.equal(calls.filter(call => call.method === "POST").length, 0);
});

test("both pages change their branding, layout, palette, font, and text size from configuration", async t => {
  for (const pathname of ["/", "/business-process.html"]) {
    const config = configuration();
    config.ui.title = "Document Control Workspace";
    config.ui.chatHeading = "Review document requirements";
    config.ui.processHeading = "Document review register";
    config.theme.font = "serif"; config.theme.textSize = 18; config.theme.layout = "compact";
    config.theme.tokens.primary = "#644332"; config.theme.logo = "assets\\workspace.png";
    const { page } = await pageFor(t, { config, path: pathname, width: 390 });
    assert.equal(await page.title(), config.ui.title);
    assert.equal(await page.locator(".brand strong").textContent(), config.ui.title);
    assert.equal(await page.locator("h1").textContent(), pathname === "/" ? config.ui.chatHeading : config.ui.processHeading);
    assert.equal(await page.locator("#brand-logo").isVisible(), true);
    const styles = await page.evaluate(() => {
      const root = getComputedStyle(document.documentElement);
      return { font: root.fontFamily, size: root.fontSize, primary: root.getPropertyValue("--primary").trim(), layout: document.documentElement.dataset.layout };
    });
    assert.ok(styles.font.includes("Georgia")); assert.equal(styles.size, "18px");
    assert.equal(styles.primary, "#644332"); assert.equal(styles.layout, "compact");
    await noOverflow(page);
  }
});

test("configuration and logo failures remain visible without rendering scenario content prematurely", async t => {
  const startup = await pageFor(t, { errors: { "/api/config": { message: "The configuration is unavailable." } } });
  assert.equal(await startup.page.locator("#application").isVisible(), false);
  assert.match(await startup.page.locator("#startup").textContent(), /configuration is unavailable/);
  const config = configuration(); config.theme.logo = "assets\\workspace.png";
  const logo = await pageFor(t, { config, errors: { "/api/logo": { message: "The logo is unavailable." } } });
  await logo.page.waitForSelector("#service-error:not([hidden])");
  assert.equal(await logo.page.locator("#brand-logo").isVisible(), false);
  assert.match(await logo.page.locator("#service-error").textContent(), /api\/logo/);
  await logo.page.locator("#question").fill("Review the request.");
  await logo.page.getByRole("button", { name: config.ui.ask, exact: true }).click();
  await logo.page.waitForSelector(".result");
  assert.equal(await logo.page.locator("#service-error").isVisible(), true);
});

test("invalid process values focus their fields and never submit a client assessment", async t => {
  const { page, config, calls } = await pageFor(t, { path: "/business-process.html", records: [] });
  await page.waitForSelector("#empty-register:not([hidden])");
  await page.getByRole("button", { name: config.ui.createRecord }).click();
  await page.getByRole("button", { name: config.ui.submitProcess }).click();
  assert.equal(await page.locator("#field-reference").getAttribute("aria-invalid"), "true");
  assert.equal(await page.locator("#field-reference-error").isVisible(), true);
  assert.equal(await page.evaluate(() => document.activeElement.id), "field-reference");
  await page.getByRole("button", { name: config.ui.fillExample }).click();
  await page.locator("#field-effort").fill("101");
  await page.getByRole("button", { name: config.ui.submitProcess }).click();
  assert.equal(await page.locator("#field-effort-error").isVisible(), true);
  assert.equal(calls.filter(call => call.path === "/api/process-records" && call.method === "POST").length, 0);
});

test("failed process assessments have no successful label after the server records them", async t => {
  const failed = record({ assessment: answer({ verification: "failed", messages: ["The approval claim could not be verified."] }) });
  const { page, config } = await pageFor(t, { path: "/business-process.html", savedRecord: failed });
  await page.getByRole("button", { name: config.ui.createRecord }).click();
  await page.getByRole("button", { name: config.ui.fillExample }).click();
  await page.getByRole("button", { name: config.ui.submitProcess }).click();
  await page.waitForSelector("#record-view:not([hidden])");
  assert.equal(await page.locator("#record-view .answer-prose,#record-view .verified").count(), 0);
  assert.equal(await page.locator("#record-view .source-passage").count(), 1);
  assert.ok((await page.locator("#record-view .evidence-pane").textContent()).includes(config.ui.evidenceAtTime));
  assert.ok((await page.locator(".result").textContent()).includes(config.ui.verificationFailed));
  assert.ok(!(await page.locator(".result").textContent()).includes(config.ui.saved));
});

test("unsafe diagram content stays in a raw source view and never requests external content", async t => {
  const result = answer();
  result.sources[0].text = '```mermaid\nflowchart TD\nA[Review] --> B[Approve]\nclick B "https://blocked.invalid"\n```';
  const { page, config } = await pageFor(t, { answer: result });
  await page.locator("#question").fill("Review the process.");
  await page.getByRole("button", { name: config.ui.ask, exact: true }).click();
  await page.waitForSelector(".result");
  await page.locator(".citations a").first().click();
  await page.waitForSelector('.source-diagram[data-render-state="unavailable"]');
  assert.ok((await page.locator(".source-diagram").textContent()).includes("External, interactive, or oversized"));
  assert.equal(await page.locator('script[src="/vendor/mermaid.min.js"]').count(), 0);
});

test("sequence and class diagrams retain safe formatting under the content policy", async t => {
  const result = answer();
  result.sources[0].text = "```mermaid\nsequenceDiagram\nRequestor->>Owner: Request review\nOwner-->>Requestor: Confirm approval\n```\n\n```mermaid\nclassDiagram\nRequest --> Approval\nRequest : reference\nApproval : owner\n```";
  const { page, config } = await pageFor(t, { answer: result });
  await page.locator("#question").fill("Review the process relationships.");
  await page.getByRole("button", { name: config.ui.ask, exact: true }).click();
  await page.waitForSelector(".result");
  await page.locator(".citations a").first().click();
  await page.waitForFunction(() => document.querySelectorAll('.source-diagram[data-render-state="rendered"]').length === 2, null, { timeout: 30000 });
  assert.equal(await page.locator(".diagram-frame svg").count(), 2);
  assert.equal(await page.locator(".diagram-frame [style],.diagram-frame image,.diagram-frame a").count(), 0);
  await noOverflow(page);
});
