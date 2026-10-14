const path = require("node:path");
const fs = require("node:fs");
const esbuild = require("esbuild");

const vendor = path.join(__dirname, "..", "wwwroot", "vendor");
fs.mkdirSync(vendor, { recursive: true });
for (const [module, file] of [["marked", "lib/marked.umd.js"], ["dompurify", "dist/purify.min.js"]]) {
  fs.copyFileSync(path.join(__dirname, "node_modules", module, file), path.join(vendor, path.basename(file)));
}

function replaceRequired(source, before, after) {
  if (!source.includes(before)) throw new Error("The pinned Mermaid CSP adapter no longer matches its source.");
  return source.replace(before, after);
}

// Adapt the pinned Mermaid and D3 sources during bundling. Keep their license notices.
// CSSOM styles avoid inline-style CSP violations during SVG measurement.
const cspAdapter = {
  name: "mermaid-csp",
  setup(build) {
    build.onLoad({ filter: /mermaid[\\/]dist[\\/]mermaid\.core\.mjs$/ }, args => {
      let source = fs.readFileSync(args.path, "utf8");
      source = "let workspaceDiagramSheet;\n" + source;
      source = replaceRequired(source,
        "  const rules = createUserStyles(config, diagramType, diagramClassDefs, idSelector);",
        "  let rules = createUserStyles(config, diagramType, diagramClassDefs, idSelector);");
      source = replaceRequired(source,
        '  const style1 = document.createElement("style");\n  style1.innerHTML = rules;\n  svg.insertBefore(style1, firstChild);',
        "  workspaceDiagramSheet = new CSSStyleSheet();\n  workspaceDiagramSheet.replaceSync(rules);\n  document.adoptedStyleSheets = [...document.adoptedStyleSheets, workspaceDiagramSheet];");
      source = replaceRequired(source, "    svg: svgCode,", "    svg: svgCode,\n    workspaceStyles: rules,");
      source = replaceRequired(source,
        "    let code2 = root.select(enclosingDivID_selector).node().innerHTML;",
        `    [svg, ...svg.querySelectorAll("[style]")].filter(node => node.hasAttribute("style")).forEach((node, index) => {
      const className = id38 + "-inline-" + index;
      rules += "\\n." + className + " { " + node.getAttribute("style") + " }";
      node.classList.add(className);
      node.removeAttribute("style");
    });
    let code2 = root.select(enclosingDivID_selector).node().innerHTML;`);
      source = replaceRequired(source, '    setDiagramConfigScope(void 0);\n  }\n}, "render");',
        '    if (workspaceDiagramSheet) {\n      document.adoptedStyleSheets = document.adoptedStyleSheets.filter(sheet => sheet !== workspaceDiagramSheet);\n      workspaceDiagramSheet = undefined;\n    }\n    setDiagramConfigScope(void 0);\n  }\n}, "render");');
      return { contents: source, loader: "js" };
    });
    build.onLoad({ filter: /d3-selection[\\/]src[\\/]selection[\\/]attr\.js$/ }, args => {
      let source = fs.readFileSync(args.path, "utf8");
      source = replaceRequired(source, "    this.setAttribute(name, value);",
        '    if (name === "style") this.style.cssText = value;\n    else this.setAttribute(name, value);');
      source = replaceRequired(source, "    else this.setAttribute(name, v);",
        '    else if (name === "style") this.style.cssText = v;\n    else this.setAttribute(name, v);');
      return { contents: source, loader: "js" };
    });
    build.onLoad({ filter: /mermaid[\\/]dist[\\/]chunks[\\/]mermaid\.core[\\/].*\.mjs$/ }, args => {
      const source = fs.readFileSync(args.path, "utf8").replace(
        /(\b[\w.]+)\.setAttribute\("style", ([^\n]+)\);/g, "$1.style.cssText = $2;");
      return { contents: source, loader: "js" };
    });
  }
};

esbuild.build({
  entryPoints: [path.join(__dirname, "mermaid-entry.mjs")],
  outfile: path.join(__dirname, "..", "wwwroot", "vendor", "mermaid.min.js"),
  bundle: true,
  minify: true,
  platform: "browser",
  format: "iife",
  target: "es2022",
  plugins: [cspAdapter],
  legalComments: "eof"
}).catch(error => { console.error(error.message); process.exitCode = 1; });
