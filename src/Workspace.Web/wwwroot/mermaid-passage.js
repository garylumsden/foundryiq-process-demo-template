"use strict";

let diagramLibrary;
let diagramNumber = 0;

function isExtractedMermaid(source) {
  return /^(?:flowchart|graph)\s+(?:TD|TB|BT|LR|RL)\b|^(?:sequenceDiagram|classDiagram|stateDiagram(?:-v2)?|erDiagram|gantt|pie|journey|mindmap|timeline|gitGraph|quadrantChart|xychart(?:-beta)?|sankey-beta)\b/.test(source.trim());
}

function quoteExtractedFlowchartLabels(source) {
  if (!/^(?:flowchart|graph)\b/.test(source.trim())) return source;
  return source
    .replace(/(\b[A-Za-z_][\w-]*)\{([^{}]*)\}/g, (match, id, label) =>
      /^\s*["']/.test(label) ? match : `${id}{${JSON.stringify(label.trim())}}`)
    .replace(/--\s+([^"\n][^\n]*?)\s+-->/g, (_, label) => `-- ${JSON.stringify(label.trim())} -->`);
}

function loadDiagramLibrary() {
  if (!diagramLibrary) diagramLibrary = new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "/vendor/mermaid.min.js";
    script.onload = () => {
      if (!window.mermaid) { reject(new Error("The diagram library did not load.")); return; }
      window.mermaid.initialize({
        startOnLoad: false, securityLevel: "strict", suppressErrorRendering: true, htmlLabels: false,
        maxTextSize: 20000, maxEdges: 300, theme: "neutral",
        themeVariables: { fontFamily: "Arial", fontSize: "16px" },
        flowchart: { htmlLabels: false, useMaxWidth: false },
        secure: ["securityLevel", "startOnLoad", "suppressErrorRendering", "maxTextSize", "maxEdges", "htmlLabels", "flowchart", "themeCSS"]
      });
      resolve(window.mermaid);
    };
    script.onerror = () => reject(new Error("The diagram library is unavailable."));
    document.head.append(script);
  });
  return diagramLibrary;
}

function renderExtractedDiagram(source) {
  const figure = document.createElement("figure");
  figure.className = "source-diagram"; figure.dataset.renderState = "pending";
  const caption = document.createElement("figcaption");
  caption.textContent = "AI-extracted diagram. Check the original PDF.";
  const frame = document.createElement("div");
  frame.className = "diagram-frame"; frame.tabIndex = 0;
  frame.setAttribute("role", "region"); frame.setAttribute("aria-label", "Extracted diagram");
  const status = document.createElement("p");
  status.setAttribute("role", "status"); status.textContent = "Rendering diagram…";
  frame.append(status);
  const details = document.createElement("details"), summary = document.createElement("summary"), code = document.createElement("pre");
  summary.textContent = "Diagram source"; code.textContent = source; code.className = "raw-extract";
  details.append(summary, code); figure.append(caption, frame, details);
  (async () => {
    try {
      if (source.length > 20000 || /%%\s*\{|https?:|javascript:|data:|@import|url\s*\(|@\{[^}]*\bimg\s*:|^\s*click\s|<\s*(?:script|img|image|iframe|svg|foreignObject)\b/im.test(source))
        throw new Error("External, interactive, or oversized diagram content is not supported.");
      const library = await loadDiagramLibrary();
      let input = source;
      try { await library.parse(input); }
      catch {
        input = quoteExtractedFlowchartLabels(source);
        if (input === source) throw new Error("The indexed diagram is incomplete or invalid.");
        await library.parse(input);
      }
      // Measure diagram text outside collapsed evidence panels.
      const stage = document.createElement("div");
      stage.className = "diagram-render-stage"; stage.setAttribute("aria-hidden", "true");
      document.body.append(stage);
      let rendered;
      try { rendered = await library.render(`indexed-diagram-${++diagramNumber}`, input, stage); }
      finally { stage.remove(); }
      const { svg, workspaceStyles } = rendered;
      const parsed = new DOMParser().parseFromString(svg, "image/svg+xml");
      let css = workspaceStyles || [...parsed.querySelectorAll("style")].map(style => style.textContent).join("\n");
      parsed.querySelectorAll("[style]").forEach((node, index) => {
        const className = `diagram-${diagramNumber}-style-${index}`;
        css += `\n.${className} { ${node.getAttribute("style")} }`;
        node.classList.add(className); node.removeAttribute("style");
      });
      const nonLocalCss = css.replace(/url\(\s*(['"]?)#[\w-]+\1\s*\)/gi, "");
      if (/@import|url\s*\(|\\/i.test(nonLocalCss))
        throw new Error("External diagram styles are not supported.");
      const fragment = DOMPurify.sanitize(new XMLSerializer().serializeToString(parsed), {
        USE_PROFILES: { svg: true, svgFilters: true },
        FORBID_TAGS: ["style", "script", "foreignObject", "image", "a"],
        FORBID_ATTR: ["href", "xlink:href", "style"], RETURN_DOM_FRAGMENT: true
      });
      const image = fragment.querySelector("svg");
      if (!image) throw new Error("The renderer did not return a diagram.");
      image.setAttribute("role", "img"); image.setAttribute("aria-label", "Diagram from the indexed passage");
      const sheet = new CSSStyleSheet();
      sheet.replaceSync(css + "\nsvg { display: block; width: 100%; height: auto; max-width: none !important; }\n@media (prefers-reduced-motion: reduce) { * { animation: none !important; transition: none !important; } }");
      frame.replaceChildren();
      const shadow = frame.attachShadow({ mode: "open" });
      shadow.adoptedStyleSheets = [sheet]; shadow.append(fragment);
      const expand = document.createElement("button");
      expand.type = "button"; expand.textContent = "Expand diagram";
      const openDiagram = opener => {
        const dialog = document.createElement("dialog"); dialog.className = "diagram-dialog";
        dialog.setAttribute("aria-label", "Full extracted diagram");
        const toolbar = document.createElement("div"); toolbar.className = "diagram-toolbar";
        const fit = document.createElement("button"), actual = document.createElement("button"), close = document.createElement("button");
        fit.type = actual.type = close.type = "button";
        fit.textContent = "Fit width"; actual.textContent = "Actual size"; close.textContent = "Close";
        const full = document.createElement("div"); full.className = "diagram-frame";
        full.tabIndex = 0; full.setAttribute("role", "region"); full.setAttribute("aria-label", "Full extracted diagram");
        const fullShadow = full.attachShadow({ mode: "open" }), clone = image.cloneNode(true);
        fullShadow.adoptedStyleSheets = [sheet]; fullShadow.append(clone);
        fit.onclick = () => { clone.style.width = "100%"; clone.style.height = "auto"; };
        actual.onclick = () => { clone.style.width = `${image.viewBox.baseVal.width}px`; clone.style.height = "auto"; };
        close.onclick = () => dialog.close();
        dialog.addEventListener("close", () => { dialog.remove(); opener.focus(); });
        toolbar.append(fit, actual, close); dialog.append(toolbar, full);
        document.body.append(dialog); dialog.showModal();
      };
      expand.onclick = () => openDiagram(expand);
      frame.setAttribute("role", "button");
      frame.setAttribute("aria-label", "Open diagram in full view");
      frame.setAttribute("aria-haspopup", "dialog");
      frame.title = "Open diagram in full view";
      frame.classList.add("diagram-opener");
      frame.onclick = () => { frame.focus(); openDiagram(frame); };
      frame.onkeydown = event => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault(); openDiagram(frame);
        }
      };
      figure.insertBefore(expand, details);
      figure.dataset.renderState = "rendered";
    } catch (error) {
      figure.dataset.renderState = "unavailable";
      status.textContent = `Diagram unavailable from this extract. ${error.message} Read the original PDF or diagram source.`;
      frame.replaceChildren(status); details.open = true;
    }
  })();
  return figure;
}
