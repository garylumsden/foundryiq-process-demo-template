"use strict";

(async () => {
  const W = window.Workspace;
  try {
    const config = await W.init();
    const question = document.getElementById("question");
    const model = document.getElementById("chat-model");
    const ask = document.getElementById("ask-button");
    const askError = document.getElementById("ask-error");
    const conversation = document.getElementById("conversation");
    const uploader = W.upload(document.getElementById("chat-images"));
    const conversationImageUrls = new Set();
    function releaseImages() {
      conversationImageUrls.forEach(url => URL.revokeObjectURL(url));
      conversationImageUrls.clear();
    }
    window.addEventListener("pagehide", event => {
      if (!event.persisted) { releaseImages(); uploader.clear(); }
    });
    let previousQuestions = [];
    let pending = false;
    let exampleGeneration = 0;
    let exampleLoading = false;
    W.models(model);
    question.maxLength = config.limits.maxQuestionCharacters;
    function length() {
      document.getElementById("question-length").textContent = `${question.value.length} / ${config.limits.maxQuestionCharacters}`;
    }
    question.addEventListener("input", length); length();
    const groups = new Map();
    for (const prompt of config.examples.prompts || []) {
      if (!groups.has(prompt.group)) {
        const group = W.element("section", null, "prompt-group");
        group.append(W.element("h3", prompt.group));
        groups.set(prompt.group, group);
        document.getElementById("prompt-examples").append(group);
      }
      const control = W.button(prompt.title, async () => {
        if (pending) return;
        const generation = ++exampleGeneration;
        question.value = prompt.question; length();
        W.visibleError(askError, null);
        uploader.clear();
        exampleLoading = Boolean(prompt.images?.length);
        W.busy(ask, exampleLoading, "ask");
        if (prompt.images?.length) {
          try {
            const images = await Promise.all(prompt.images.map(async image => {
              const url = W.safeUrl(image.url, true);
              if (!url) throw new Error(W.ui("validationError"));
              const response = await fetch(url, { credentials: "same-origin", redirect: "error" });
              if (!response.ok) throw new Error(`${response.status}: ${W.ui("requestFailed")}`);
              const blob = await response.blob();
              return new File([blob], image.name, { type: blob.type });
            }));
            if (generation === exampleGeneration) uploader.replace(images);
          } catch (error) { if (generation === exampleGeneration) W.visibleError(askError, error); }
          finally {
            if (generation === exampleGeneration) { exampleLoading = false; W.busy(ask, false, "ask"); }
          }
        }
        question.focus();
      }, "prompt-button");
      control.dataset.featured = String(Boolean(prompt.featured));
      groups.get(prompt.group).append(control);
    }
    document.getElementById("new-conversation").addEventListener("click", () => {
      if (pending) return;
      ++exampleGeneration;
      releaseImages();
      previousQuestions = []; conversation.replaceChildren(); question.value = ""; uploader.clear();
      ask.disabled = !W.config.ready; W.visibleError(askError, null); length(); question.focus();
    });
    document.getElementById("ask-form").addEventListener("submit", async event => {
      event.preventDefault();
      if (pending || exampleLoading || !W.config.ready) return;
      const text = question.value.trim();
      if (!text || text.length > config.limits.maxQuestionCharacters) {
        W.visibleError(askError, new Error(W.ui("validationError"))); question.focus(); return;
      }
      pending = true;
      ++exampleGeneration;
      W.busy(ask, true, "ask"); W.visibleError(askError, null);
      const exchange = W.element("section", null, "exchange");
      const prompt = W.element("div", null, "question-message");
      prompt.append(W.element("h2", W.ui("questionLabel")), W.element("p", text));
      exchange.append(prompt);
      conversation.append(exchange);
      const submittedFiles = uploader.files;
      try {
        const result = await W.post("/api/ask", W.multipart({ question: text, model: model.value, previousQuestions: previousQuestions.slice(-4) }, submittedFiles));
        const submittedImages = W.canRenderAnswer(result) && result.answer.images?.length
          ? submittedFiles.map(file => {
            const url = URL.createObjectURL(file);
            conversationImageUrls.add(url);
            return { url, name: file.name };
          }) : [];
        exchange.append(W.renderResult(result, "answerHeading", { submittedImages }));
        previousQuestions.push(text);
        question.value = ""; uploader.clear(); length();
        void W.refreshStatus();
      } catch (error) {
        W.visibleError(askError, error);
        exchange.append(W.element("p", `${W.ui("requestFailed")} ${error.message}`, "error"));
      } finally {
        pending = false; W.busy(ask, false, "ask"); question.focus();
      }
    });
    const searchInput = document.getElementById("search-question");
    const searchButton = document.getElementById("search-button");
    const searchError = document.getElementById("search-error");
    const pages = document.getElementById("search-pages");
    const previous = document.getElementById("previous-page");
    const next = document.getElementById("next-page");
    const params = new URLSearchParams(location.search);
    searchInput.maxLength = config.limits.maxQuestionCharacters;
    searchInput.value = history.state?.sourceSearch?.question || "";
    let currentPage = Math.max(1, Number(params.get("page")) || 1);
    let searchQuestion = "";
    let searching = false;
    async function search(page) {
      if (searching || !W.config.ready || !searchInput.value.trim()) return;
      searching = true;
      searchQuestion = searchInput.value.trim();
      W.busy(searchButton, true, "search"); W.visibleError(searchError, null);
      previous.disabled = next.disabled = true;
      try {
        const result = await W.post("/api/search", { question: searchQuestion, page, pageSize: 5 });
        currentPage = result.page;
        const url = new URL(location.href);
        url.searchParams.delete("q"); url.searchParams.set("page", currentPage);
        history.replaceState({ ...history.state, sourceSearch: { question: searchQuestion } }, "", url);
        const container = document.getElementById("search-results");
        container.replaceChildren(W.element("h2", `${W.ui("sourcesHeading")} · ${result.total}`));
        if (!result.documents.length) container.append(W.element("p", W.ui("insufficientEvidence"), "empty-state"));
        result.documents.forEach((document, index) => container.append(W.evidence(document, `search-evidence-${currentPage}-${index}`, false)));
        pages.hidden = false;
        document.getElementById("page-number").textContent = `${W.ui("pageLabel")} ${currentPage}`;
        previous.disabled = currentPage <= 1; next.disabled = !result.hasNext;
        void W.refreshStatus();
      } catch (error) { W.visibleError(searchError, error); pages.hidden = true; }
      finally { searching = false; W.busy(searchButton, false, "search"); }
    }
    document.getElementById("search-form").addEventListener("submit", event => { event.preventDefault(); void search(1); });
    previous.addEventListener("click", () => { searchInput.value = searchQuestion; void search(currentPage - 1); });
    next.addEventListener("click", () => { searchInput.value = searchQuestion; void search(currentPage + 1); });
    await W.refreshStatus();
  } catch (error) { W.startupError(error); }
})();
