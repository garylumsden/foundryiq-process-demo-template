const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const { JSDOM } = require("jsdom");
const assets = path.resolve(__dirname, "..", "..", "wwwroot");

function render(raw) {
  const dom = new JSDOM("<!doctype html><html><head></head><body></body></html>", { runScripts: "dangerously" });
  for (const name of [path.join("vendor", "marked.umd.js"), path.join("vendor", "purify.min.js"), "mermaid-passage.js", "source-passage.js"]) {
    const script = dom.window.document.createElement("script");
    script.textContent = fs.readFileSync(path.join(assets, name), "utf8");
    dom.window.document.head.append(script);
  }
  const result = dom.window.renderSourcePassage(raw);
  dom.window.document.body.append(result);
  assert.equal(dom.window.compromised, undefined);
  dom.window.close();
  return result;
}

test("formats headings, lists and Markdown tables while retaining exact evidence", () => {
  const raw = "# Requirements\n\nA **supported** statement.\n\n- First item\n- Second item\n\n| Item | Limit |\n| --- | --- |\n| A | 3 |\n";
  const result = render(raw);
  assert.equal(result.querySelector("h5").textContent, "Requirements");
  assert.equal(result.querySelector("strong").textContent, "supported");
  assert.equal(result.querySelectorAll(".source-markdown li").length, 2);
  assert.equal(result.querySelector(".source-table").getAttribute("aria-label"), "Extracted table");
  assert.equal(result.querySelector("td").textContent, "A");
  assert.equal(result.querySelector(".raw-extract").textContent, raw);
});

test("keeps page comments in expandable notes and preserves status statements", () => {
  const raw = '<!-- PageHeader: Superseded with effect from 01/01/2027 -->\n# Content\n\nText.\n<!-- PageFooter: Publisher -->\n<!-- PageBreak -->';
  const result = render(raw);
  assert.ok(!result.querySelector(".source-markdown").textContent.includes("PageHeader"));
  assert.ok(result.querySelector(".extraction-notes").textContent.includes("Superseded with effect from 01/01/2027"));
  assert.equal(result.querySelector(".extraction-notes").children.length, 3);
  assert.equal(result.querySelector("summary").textContent, "Extraction notes (3)");
  assert.equal(result.querySelector(".raw-extract").textContent, raw);
});

test("sanitises HTML and disables untrusted images, links and event handlers", () => {
  const raw = '# Extract\n<script>window.compromised=true</script><img src="https://evil.invalid/a" onerror="alert(1)"><svg onload="alert(1)"></svg><iframe src="https://evil.invalid"></iframe><a href="javascript:alert(1)">Visible words</a>\n\n[Another link](https://evil.invalid)\n\n![Figure](figures/1.1 "A review checklist.")';
  const result = render(raw);
  assert.equal(result.querySelectorAll("script,img,svg,iframe,a").length, 0);
  assert.equal(result.querySelectorAll("[onerror],[onload],[href],[src]").length, 0);
  assert.ok(result.querySelector(".source-markdown").textContent.includes("Visible words"));
  assert.ok(result.querySelector(".source-markdown").textContent.includes("AI figure description: A review checklist."));
  assert.equal(result.querySelector(".raw-extract").textContent, raw);
});

test("preserves native HTML table structure but removes source formatting attributes", () => {
  const result = render('<table style="position:fixed" onclick="alert(1)"><tr><th>Heading</th><td colspan="2">Value</td></tr></table>');
  assert.equal(result.querySelector("td").getAttribute("colspan"), "2");
  assert.equal(result.querySelectorAll("[style],[onclick]").length, 0);
});

test("retains an incomplete comment at a chunk boundary instead of hiding the passage", () => {
  const raw = "<!-- PageHeader: incomplete\n\nRemaining extracted words.";
  const result = render(raw);
  assert.ok(result.querySelector(".source-markdown").textContent.includes("Remaining extracted words."));
  assert.equal(result.querySelector(".raw-extract").textContent, raw);
});

test("explains metadata-only passages without fabricating source text", () => {
  const result = render("<!-- PageNumber: 10 -->");
  assert.ok(result.querySelector(".source-markdown").textContent.includes("extraction metadata"));
  assert.equal(result.querySelector(".raw-extract").textContent, "<!-- PageNumber: 10 -->");
});

test("browser assets match the pinned installed libraries", () => {
  const modules = path.resolve(__dirname, "..", "node_modules");
  assert.deepEqual(fs.readFileSync(path.join(assets, "vendor", "marked.umd.js")), fs.readFileSync(path.join(modules, "marked", "lib", "marked.umd.js")));
  assert.deepEqual(fs.readFileSync(path.join(assets, "vendor", "purify.min.js")), fs.readFileSync(path.join(modules, "dompurify", "dist", "purify.min.js")));
});
