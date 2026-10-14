"use strict";

window.Workspace = (() => {
  let config;
  let tokenPromise;
  let evidenceNumber = 0;
  const formStates = new WeakMap();
  const number = new Intl.NumberFormat();
  const dates = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
  const ui = key => {
    if (typeof config?.ui?.[key] !== "string") throw new Error(`Missing UI label: ${key}`);
    return config.ui[key];
  };
  function element(tag, text, className) {
    const node = document.createElement(tag);
    if (text !== undefined && text !== null) node.textContent = String(text);
    if (className) node.className = className;
    return node;
  }
  function button(label, action, className) {
    const node = element("button", label, className);
    node.type = "button";
    node.addEventListener("click", action);
    return node;
  }
  function visibleError(node, error) {
    node.textContent = error ? `${ui("requestFailed")} ${error.message || String(error)}` : "";
    node.hidden = !error;
  }
  function safeUrl(raw, localOnly = false) {
    if (typeof raw !== "string" || !raw.trim()) return null;
    try {
      const url = new URL(raw, location.origin);
      if (url.username || url.password) return null;
      if (url.origin === location.origin && ["http:", "https:"].includes(url.protocol)) return url.href;
      if (!localOnly && url.protocol === "https:" && !url.username && !url.password) return url.href;
    } catch { /* Invalid URLs remain plain text. */ }
    return null;
  }
  async function request(path, options = {}) {
    const response = await fetch(path, { ...options, credentials: "same-origin", redirect: "error" });
    let result;
    const text = await response.text();
    if (!text && response.ok) return {};
    try { result = text ? JSON.parse(text) : {}; }
    catch { throw new Error(`${response.status}: ${config ? ui("requestFailed") : "The server returned an invalid response."}`); }
    if (!response.ok) {
      const detail = typeof result.message === "string" ? result.message
        : typeof result.error === "string" ? result.error : config ? ui("requestFailed") : "The request failed.";
      throw new Error(`${response.status}: ${detail}`);
    }
    return result;
  }
  async function post(path, body) {
    if (!tokenPromise) tokenPromise = request("/api/token").then(value => {
      if (typeof value.token !== "string" || !value.token) throw new Error(ui("requestFailed"));
      return value.token;
    }).catch(error => { tokenPromise = undefined; throw error; });
    const headers = { "X-CSRF-Token": await tokenPromise };
    if (!(body instanceof FormData)) {
      headers["Content-Type"] = "application/json";
      body = JSON.stringify(body);
    }
    try { return await request(path, { method: "POST", headers, body }); }
    catch (error) {
      if (error.message.startsWith("403:")) tokenPromise = undefined;
      throw error;
    }
  }
  function multipart(values, images) {
    const data = new FormData();
    for (const [key, value] of Object.entries(values)) data.set(key, typeof value === "string" ? value : JSON.stringify(value));
    for (const file of images || []) data.append("images", file, file.name);
    return data;
  }
  function applyTheme(theme) {
    const root = document.documentElement;
    root.dataset.layout = theme.layout === "compact" ? "compact" : "comfortable";
    const fonts = {
      system: "system", "system-ui": "system", "sans-serif": "arial", Arial: "arial",
      Verdana: "verdana", serif: "georgia", Georgia: "georgia", monospace: "monospace"
    };
    const font = theme.fontFamily || theme.font;
    if (fonts[font]) root.dataset.font = fonts[font];
    const scale = theme.fontScale ?? theme.scale;
    if ([0.85, 0.9, 0.95, 1, 1.05, 1.1, 1.15, 1.2, 1.25].includes(scale)) root.dataset.scale = String(scale);
    if (theme.tokens?.canvas) {
      document.querySelector('meta[name="theme-color"]').content = theme.tokens.canvas;
      const hex = theme.tokens.canvas.slice(1);
      const brightness = 0.2126 * parseInt(hex.slice(0, 2), 16) + 0.7152 * parseInt(hex.slice(2, 4), 16) + 0.0722 * parseInt(hex.slice(4), 16);
      root.dataset.scheme = brightness < 110 ? "dark" : "light";
    }
  }
  function readiness(ready, message) {
    config.ready = Boolean(ready);
    const node = document.getElementById("readiness");
    node.replaceChildren(element("span", ui(config.ready ? "ready" : "preview"), "status-pill"));
    if (message && message !== ui(config.ready ? "ready" : "preview")) node.append(element("span", message));
    node.dataset.ready = String(config.ready);
    document.querySelectorAll("[data-inference]").forEach(control => {
      control.disabled = !config.ready || control.dataset.busy === "true";
    });
  }
  async function refreshStatus() {
    try {
      const status = await request("/api/status");
      if (typeof status.ready === "boolean") readiness(status.ready, status.readinessMessage || config.readinessMessage);
      const counts = status.usage || status;
      const parts = [];
      const questions = counts.questionCount ?? counts.questions ?? counts.requests;
      const sources = counts.sourceCount ?? counts.sources;
      const tokens = counts.tokens ?? ((counts.inputTokens || 0) + (counts.outputTokens || 0));
      for (const [label, value] of [["questionCount", questions], ["sourceCount", sources], ["tokenLabel", tokens]]) {
        if (typeof value === "number" && Number.isFinite(value) && value >= 0) parts.push(`${ui(label)}: ${number.format(value)}`);
      }
      const usage = document.getElementById("usage");
      usage.textContent = parts.length ? `${ui("usageLabel")} · ${parts.join(" · ")}` : "";
      usage.classList.remove("error");
    } catch (error) {
      const usage = document.getElementById("usage");
      usage.textContent = `${ui("requestFailed")} ${error.message}`;
      usage.classList.add("error");
    }
  }
  async function init() {
    config = await request("/api/config");
    if (!config.profile || !config.ui || !config.theme || !config.process || !config.limits)
      throw new Error("The workspace configuration is incomplete.");
    applyTheme(config.theme);
    document.querySelectorAll("[data-ui]").forEach(node => { node.textContent = ui(node.dataset.ui); });
    document.querySelectorAll("[data-placeholder]").forEach(node => { node.placeholder = ui(node.dataset.placeholder); });
    document.title = ui("title");
    document.querySelector(".brand").setAttribute("aria-label", ui("title"));
    document.getElementById("profile-version").textContent = config.profile.title === ui("title")
      ? config.profile.version : `${config.profile.title} · ${config.profile.version}`;
    document.getElementById("profile-purpose").textContent = config.profile.purpose;
    const logo = document.getElementById("brand-logo");
    if (config.theme.logo) {
      logo.alt = config.theme.logoAlt;
      logo.hidden = false;
      logo.addEventListener("error", () => {
        logo.hidden = true;
        visibleError(document.getElementById("service-error"), new Error("/api/logo"));
      }, { once: true });
      logo.src = "/api/logo";
    }
    readiness(config.ready, config.readinessMessage);
    document.getElementById("startup").hidden = true;
    document.getElementById("application").hidden = false;
    return config;
  }
  function startupError(error) {
    const startup = document.getElementById("startup");
    startup.hidden = false;
    startup.setAttribute("role", "alert");
    startup.textContent = `Unable to load the workspace. ${error.message}`;
  }
  function models(select) {
    select.replaceChildren();
    for (const model of config.models || []) {
      const option = element("option", model.label);
      option.value = model.key;
      select.append(option);
    }
    if (config.defaultModel) select.value = config.defaultModel;
  }
  function busy(control, active, label) {
    control.dataset.busy = String(active);
    control.disabled = active || (control.hasAttribute("data-inference") && !config.ready);
    control.textContent = ui(active ? "busy" : label);
    const form = control.closest("form");
    if (form) {
      form.setAttribute("aria-busy", String(active));
      if (active && !formStates.has(form)) {
        const states = [...form.querySelectorAll("input,textarea,select,button")].filter(node => node !== control)
          .map(node => [node, node.disabled]);
        formStates.set(form, states);
        states.forEach(([node]) => { node.disabled = true; });
      } else if (!active && formStates.has(form)) {
        formStates.get(form).forEach(([node, disabled]) => { node.disabled = disabled; });
        formStates.delete(form);
      }
    }
  }
  function upload(container) {
    const files = [];
    const urls = new Map();
    const field = element("div", null, "field");
    const input = element("input");
    input.type = "file"; input.multiple = true; input.accept = "image/png,image/jpeg";
    input.id = `${container.id}-input`; input.name = "images";
    const label = element("label", ui("imagesLabel")); label.htmlFor = input.id;
    const help = element("p", ui("imagesHelp"), "field-help"); help.id = `${input.id}-help`;
    const limits = config.limits;
    help.append(element("span", ` ${number.format(limits.maxImages)} · ${number.format(limits.maxImageBytes / 1048576)} MB / ${number.format(limits.maxTotalBytes / 1048576)} MB`));
    input.setAttribute("aria-describedby", help.id);
    const error = element("p", null, "error");
    error.setAttribute("role", "alert"); error.hidden = true;
    const previews = element("div", null, "image-previews");
    field.append(label, input, help, error, previews);
    container.append(field);
    function draw() {
      previews.replaceChildren();
      files.forEach((file, index) => {
        const card = element("figure", null, "image-preview");
        const image = element("img"); image.width = 120; image.height = 90; image.alt = file.name;
        if (!urls.has(file)) urls.set(file, URL.createObjectURL(file));
        image.src = urls.get(file);
        const caption = element("figcaption", file.name);
        const remove = button(ui("removeImage"), () => {
          URL.revokeObjectURL(urls.get(file)); urls.delete(file); files.splice(index, 1); draw(); input.focus();
        });
        remove.setAttribute("aria-label", `${ui("removeImage")}: ${file.name}`);
        card.append(image, caption, remove); previews.append(card);
      });
    }
    function validate(next) {
      if (next.length > limits.maxImages) throw new Error(`${ui("validationError")} ${ui("imagesLabel")}: ${limits.maxImages}`);
      if (next.some(file => !["image/png", "image/jpeg"].includes(file.type) || !file.size || file.size > limits.maxImageBytes))
        throw new Error(`${ui("validationError")} PNG / JPEG · ${number.format(limits.maxImageBytes / 1048576)} MB`);
      if (next.reduce((sum, file) => sum + file.size, 0) > limits.maxTotalBytes)
        throw new Error(`${ui("validationError")} ${number.format(limits.maxTotalBytes / 1048576)} MB`);
    }
    function replace(next) {
      validate(next);
      for (const url of urls.values()) URL.revokeObjectURL(url);
      urls.clear(); files.splice(0, files.length, ...next); error.hidden = true; input.value = ""; draw();
    }
    input.addEventListener("change", () => {
      try { validate([...files, ...input.files]); files.push(...input.files); error.hidden = true; draw(); }
      catch (problem) { error.textContent = problem.message; error.hidden = false; }
      input.value = "";
    });
    return { get files() { return [...files]; }, replace, clear: () => replace([]) };
  }
  function canRenderAnswer(result) {
    return Boolean(result?.outcome === "answered" && result.verification === "passed" && result.answer);
  }
  function outcome(result) {
    if (result?.outcome === "answered" && !canRenderAnswer(result)) return ui("verificationFailed");
    const labels = {
      answered: "answered", insufficient_evidence: "insufficientEvidence",
      clarification_required: "clarificationRequired", out_of_scope: "outOfScope",
      verification_failed: "verificationFailed", generation_unavailable: "unavailable", unavailable: "unavailable",
      error: "unavailable", failed: "unavailable"
    };
    return ui(labels[result?.outcome] || "noAnswer");
  }
  function date(value) {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? String(value || "") : dates.format(parsed);
  }
  function display(value) {
    if (Array.isArray(value)) return value.map(display).join(", ");
    if (value === undefined || value === null || value === "") return "—";
    if (typeof value === "object") return JSON.stringify(value);
    return String(value);
  }
  function evidence(source, id, formatted) {
    const item = element("article", null, "evidence-card");
    item.id = id;
    item.tabIndex = -1;
    item.append(element("h4", `${source.documentId || ""} · ${source.title || ""}`));
    item.append(element("p", [source.revision, source.status, source.section].filter(Boolean).join(" · "), "evidence-meta"));
    const url = safeUrl(source.documentUrl);
    if (url) {
      const link = element("a", ui("openDocument"));
      link.href = url; link.target = "_blank"; link.rel = "noopener noreferrer";
      item.append(link);
    }
    if (formatted && typeof renderSourcePassage === "function") item.append(renderSourcePassage(String(source.text || "")));
    else {
      const raw = element("details");
      raw.open = true;
      raw.append(element("summary", ui("rawEvidenceHelp")), element("pre", source.text || "", "raw-extract"));
      item.append(raw);
    }
    return item;
  }
  function references(node, refs, targets) {
    const group = element("span", null, "citations");
    for (const reference of new Set(refs || [])) {
      const target = targets.get(reference);
      if (!target) continue;
      const link = element("a", `[${reference}]`);
      link.href = `#${target.id}`;
      link.setAttribute("aria-label", `${ui("sourcesHeading")}: ${reference} · ${target.title}`);
      link.addEventListener("click", event => {
        event.preventDefault();
        const source = document.getElementById(target.id);
        source.closest("details")?.setAttribute("open", "");
        source.focus();
        source.scrollIntoView({ block: "nearest", behavior: "instant" });
      });
      group.append(link);
    }
    node.append(group);
  }
  function renderResult(result, headingKey = "answerHeading", options = {}) {
    const article = element("article", null, "result panel");
    article.dataset.outcome = result.outcome || "unavailable";
    article.dataset.verified = String(canRenderAnswer(result));
    const head = element("div", null, "section-heading");
    head.append(element("h3", ui(headingKey)), element("span", outcome(result), canRenderAnswer(result) ? "badge verified" : "badge warning"));
    article.append(head);
    for (const message of result.messages || []) article.append(element("p", message, "result-message"));
    if (result.issues?.length) {
      const issues = element("ul", null, "result-issues");
      for (const issue of result.issues) issues.append(element("li", `${issue.message}${issue.location ? ` (${issue.location})` : ""}`));
      article.append(issues);
    }
    // Backend source evidence remains available when the answer is withheld.
    const targets = new Map();
    const sourcePane = element("details", null, "evidence-pane");
    const sources = result.sources || [];
    sourcePane.append(element("summary", `${ui("sourcesHeading")} · ${number.format(sources.length)}`));
    if (options.captured) sourcePane.append(element("p", ui("evidenceAtTime"), "section-help"));
    sourcePane.append(element("p", ui("rawEvidenceHelp"), "field-help"));
    sources.forEach(source => {
      const id = `evidence-${++evidenceNumber}`;
      targets.set(source.refId, { id, title: source.title });
      sourcePane.append(evidence(source, id, true));
    });
    if (canRenderAnswer(result)) {
      const prose = element("div", null, "answer-prose");
      const answer = result.answer;
      for (const statement of answer.overview || []) {
        const paragraph = element("p", statement.text);
        references(paragraph, statement.references, targets); prose.append(paragraph);
      }
      for (const summary of answer.summaries || []) {
        const group = element("section", null, "document-summary");
        group.append(element("h4", summary.documentId));
        const paragraph = element("p", summary.text);
        references(paragraph, summary.references, targets); group.append(paragraph); prose.append(group);
      }
      for (const quotation of answer.quotations || []) {
        const quote = element("blockquote", quotation.text);
        references(quote, [quotation.reference], targets); prose.append(quote);
      }
      if (answer.images?.length) {
        const images = element("section");
        images.append(element("h4", ui("imagesHeading")));
        for (const image of answer.images) {
          const observation = element("div", null, "image-observation");
          const submitted = Number.isInteger(image.image) && image.image > 0 ? options.submittedImages?.[image.image - 1] : null;
          if (submitted) {
            const preview = element("img");
            preview.src = submitted.url; preview.alt = submitted.name;
            preview.width = 180; preview.height = 135;
            observation.classList.add("has-image"); observation.append(preview);
          }
          const description = element("div", null, "image-observation-text");
          description.append(element("strong", `${ui("imagesLabel")} ${image.image}`), element("p", image.description));
          description.append(element("p", `${ui("imageUncertainty")}: ${image.uncertainty || "—"}`, "section-help"));
          observation.append(description);
          images.append(observation);
        }
        prose.append(images);
      }
      if (answer.limitations?.length) {
        const limits = element("section");
        limits.append(element("h4", ui("limitsHeading")));
        const list = element("ul");
        answer.limitations.forEach(value => list.append(element("li", value))); limits.append(list); prose.append(limits);
      }
      article.append(prose);
    } else article.append(element("p", ui("noAnswer"), "empty-state"));
    if (sources.length || canRenderAnswer(result) || options.captured) article.append(sourcePane);
    const metadata = element("div", null, "result-meta");
    if (result.model) metadata.append(element("span", `${ui("modelLabel")}: ${result.model}`));
    if (result.usage) metadata.append(element("span", `${ui("tokenLabel")}: ${number.format((result.usage.inputTokens || 0) + (result.usage.outputTokens || 0))}`));
    if (result.traceId) {
      const trace = element("code", result.traceId); trace.title = "traceId"; metadata.append(trace);
    }
    if (result.profileDigest) {
      const digest = element("code", result.profileDigest, "digest"); digest.title = "profileDigest"; metadata.append(digest);
    }
    article.append(metadata);
    if (result.traceId) {
      const feedback = element("div", null, "feedback");
      const status = element("span"); status.setAttribute("role", "status");
      const error = element("div", null, "error"); error.setAttribute("role", "alert"); error.hidden = true;
      for (const [key, category] of [["feedbackUseful", "useful"], ["feedbackIncorrect", "incorrect"], ["feedbackUnsupported", "unsupported"]]) {
        const control = button(ui(key), async () => {
          feedback.querySelectorAll("button").forEach(node => { node.disabled = true; });
          visibleError(error, null); status.textContent = ui("busy");
          try {
            await post("/api/feedback", { traceId: result.traceId, category });
            feedback.querySelectorAll("button").forEach(node => node.setAttribute("aria-pressed", "false"));
            status.textContent = ui("saved"); control.setAttribute("aria-pressed", "true");
          } catch (problem) {
            status.textContent = ""; visibleError(error, problem);
          } finally { feedback.querySelectorAll("button").forEach(node => { node.disabled = false; }); }
        });
        control.setAttribute("aria-pressed", "false"); feedback.append(control);
      }
      feedback.append(status, error); article.append(feedback);
    }
    return article;
  }
  return { init, ui, element, button, request, post, multipart, safeUrl, models, busy, upload,
    readiness, refreshStatus, visibleError, startupError, canRenderAnswer, outcome, date, display, evidence, renderResult,
    get config() { return config; } };
})();
