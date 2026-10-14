"use strict";

function renderSourcePassage(raw) {
  const allowedTags = ["p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li",
    "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "blockquote",
    "pre", "code", "strong", "em", "del", "br", "hr", "span", "figure", "figcaption"];
  const renderer = new marked.Renderer();
  renderer.image = ({ text, title }) => {
    const caption = document.createElement("p");
    caption.textContent = `AI figure description: ${title || text || "Read the figure in the original PDF."}`;
    return caption.outerHTML;
  };
  function formatted(markdown) {
    const fragment = DOMPurify.sanitize(marked.parse(markdown.replace(/<!--/g, "&lt;!--"), {
      renderer, gfm: true, async: false
    }), {
      ALLOWED_TAGS: allowedTags, ALLOWED_ATTR: ["colspan", "rowspan"],
      ALLOW_DATA_ATTR: false, ALLOW_ARIA_ATTR: false, RETURN_DOM_FRAGMENT: true
    });
    fragment.querySelectorAll("h1,h2,h3,h4").forEach(heading => {
      const replacement = document.createElement(heading.tagName === "H1" ? "h5" : "h6");
      replacement.append(...heading.childNodes); heading.replaceWith(replacement);
    });
    fragment.querySelectorAll("table").forEach(table => {
      const wrapper = document.createElement("div");
      wrapper.className = "source-table";
      wrapper.tabIndex = 0;
      wrapper.setAttribute("role", "region");
      wrapper.setAttribute("aria-label", "Extracted table");
      table.replaceWith(wrapper); wrapper.append(table);
    });
    fragment.querySelectorAll("figure").forEach(figure => {
      const label = document.createElement("p");
      label.textContent = "AI figure description";
      figure.prepend(label);
    });
    fragment.querySelectorAll("pre > code").forEach(code => {
      if (isExtractedMermaid(code.textContent))
        code.parentElement.replaceWith(renderExtractedDiagram(code.textContent));
    });
    return fragment;
  }

  const notes = [];
  const markdown = raw.replace(/<!--([\s\S]*?)-->/g, (_, content) => {
    notes.push(content.trim()); return "\n\n";
  });
  const container = document.createElement("div");
  container.className = "source-passage";
  const passage = document.createElement("div");
  passage.className = "source-markdown";
  passage.append(formatted(markdown));
  if (!passage.textContent.trim()) {
    const notice = document.createElement("p");
    notice.textContent = "This passage contains extraction metadata. Read the notes or raw extract.";
    passage.append(notice);
  }
  container.append(passage);
  if (notes.length) {
    const details = document.createElement("details"), summary = document.createElement("summary");
    summary.textContent = `Extraction notes (${notes.length})`;
    const list = document.createElement("ol");
    list.className = "extraction-notes source-markdown";
    notes.forEach(note => { const item = document.createElement("li"); item.append(formatted(note)); list.append(item); });
    details.append(summary, list); container.append(details);
  }
  const details = document.createElement("details"), summary = document.createElement("summary"), pre = document.createElement("pre");
  summary.textContent = "Raw extract";
  pre.className = "raw-extract"; pre.textContent = raw;
  details.append(summary, pre); container.append(details);
  return container;
}
