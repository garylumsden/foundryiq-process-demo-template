"use strict";

(async () => {
  const W = window.Workspace;
  try {
    const config = await W.init();
    const register = document.getElementById("register");
    const formPanel = document.getElementById("new-record");
    const recordPanel = document.getElementById("record-view");
    const form = document.getElementById("process-form");
    const formError = document.getElementById("form-error");
    const pageError = document.getElementById("process-error");
    const model = document.getElementById("process-model");
    const submit = document.getElementById("submit-process");
    const controls = new Map();
    const uploader = W.upload(document.getElementById("process-images"));
    let records = [];
    let dirty = false;
    let pending = false;
    let viewGeneration = 0;
    W.models(model);
    document.getElementById("form-heading").textContent = config.process.name;
    document.getElementById("register-table").setAttribute("aria-label", W.ui("registerHeading"));
    for (const stage of config.process.stages) {
      const fieldset = W.element("fieldset", null, "process-stage");
      fieldset.append(W.element("legend", stage.label));
      const fields = W.element("div", null, "process-field-grid");
      for (const definition of config.process.fields.filter(field => field.stage === stage.id)) {
        const group = W.element("div", null, `field${definition.type === "multiline" ? " full-width" : ""}`);
        const id = `field-${definition.id}`;
        const label = W.element("label", definition.label);
        label.htmlFor = id;
        if (definition.required) {
          const marker = W.element("span", " *", "required-marker");
          marker.setAttribute("aria-hidden", "true"); label.append(marker);
        }
        let control;
        if (definition.type === "select" || definition.type === "multiselect") {
          control = W.element("select");
          if (definition.type === "multiselect") {
            control.multiple = true; control.size = Math.min(4, Math.max(2, definition.options.length));
          } else {
            const empty = W.element("option", definition.placeholder || "—");
            empty.value = ""; control.append(empty);
          }
          for (const value of definition.options) {
            const option = W.element("option", value); option.value = value; control.append(option);
          }
        } else if (definition.type === "multiline") {
          control = W.element("textarea"); control.rows = 4;
        } else {
          control = W.element("input");
          control.type = definition.type === "boolean" ? "checkbox" : definition.type;
        }
        control.id = id; control.name = definition.id; control.autocomplete = "off";
        control.required = definition.required && definition.type !== "boolean";
        if (["text", "multiline"].includes(definition.type)) control.maxLength = definition.maxLength ?? config.limits.maxQuestionCharacters;
        if (["text", "multiline", "number", "date"].includes(definition.type)) control.placeholder = definition.placeholder || "";
        if (definition.type === "number") {
          control.step = "any"; control.inputMode = "decimal";
          if (definition.min !== null && definition.min !== undefined) control.min = definition.min;
          if (definition.max !== null && definition.max !== undefined) control.max = definition.max;
        }
        const help = W.element("p", definition.help, "field-help"); help.id = `${id}-help`;
        const fieldError = W.element("p", null, "field-error");
        fieldError.id = `${id}-error`; fieldError.hidden = true;
        control.setAttribute("aria-describedby", `${help.id} ${fieldError.id}`);
        control.addEventListener("invalid", () => {
          control.setAttribute("aria-invalid", "true"); fieldError.textContent = control.validationMessage; fieldError.hidden = false;
        });
        control.addEventListener("input", () => {
          control.removeAttribute("aria-invalid"); fieldError.hidden = true;
        });
        if (definition.type === "boolean") {
          label.prepend(control); label.className = "checkbox-label"; group.append(label, help, fieldError);
        } else group.append(label, control, help, fieldError);
        controls.set(definition.id, { control, definition });
        fields.append(group);
      }
      fieldset.append(fields); document.getElementById("process-fields").append(fieldset);
    }
    function values() {
      const result = {};
      for (const [id, { control, definition }] of controls) {
        if (definition.type === "boolean") result[id] = control.checked;
        else if (definition.type === "multiselect") result[id] = [...control.selectedOptions].map(option => option.value);
        else if (definition.type === "number") {
          if (control.value !== "") result[id] = control.valueAsNumber;
        } else if (control.value !== "") result[id] = control.value;
      }
      return result;
    }
    function fill(valuesToFill) {
      for (const [id, { control, definition }] of controls) {
        const value = valuesToFill[id];
        control.removeAttribute("aria-invalid");
        document.getElementById(`${control.id}-error`).hidden = true;
        if (definition.type === "boolean") control.checked = value === true;
        else if (definition.type === "multiselect") {
          for (const option of control.options) option.selected = Array.isArray(value) && value.includes(option.value);
        } else control.value = value === null || value === undefined ? "" : String(value);
      }
    }
    function setView(view, recordId, replace = false) {
      register.hidden = view !== "register"; formPanel.hidden = view !== "new"; recordPanel.hidden = view !== "record";
      document.getElementById("create-record").hidden = view !== "register";
      const url = new URL(location.href);
      url.searchParams.delete("view"); url.searchParams.delete("record");
      if (view === "new") url.searchParams.set("view", "new");
      if (recordId) url.searchParams.set("record", recordId);
      history[replace ? "replaceState" : "pushState"](null, "", url);
    }
    function recordLink(record) {
      const reference = W.display(record.values[record.process.referenceField]);
      const link = W.element("a", reference === "—" ? record.id : reference);
      link.href = `/business-process.html?record=${encodeURIComponent(record.id)}`;
      link.addEventListener("click", event => {
        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault(); void openRecord(record.id);
      });
      return link;
    }
    function drawRegister() {
      const host = document.getElementById("register-table");
      host.replaceChildren();
      document.getElementById("record-count").textContent = new Intl.NumberFormat().format(records.length);
      document.getElementById("empty-register").hidden = records.length !== 0;
      if (!records.length) return;
      const table = W.element("table", null, "register-table");
      const caption = W.element("caption", W.ui("registerHeading"), "visually-hidden");
      const head = W.element("thead");
      const row = W.element("tr");
      const columns = config.process.registerColumns.filter(id => id !== config.process.referenceField);
      const headings = [W.ui("referenceColumn"), ...columns.map(id => config.process.fields.find(field => field.id === id).label),
        W.ui("outcomeColumn"), W.ui("recordedColumn")];
      for (const heading of headings) {
        const cell = W.element("th", heading); cell.scope = "col"; row.append(cell);
      }
      head.append(row);
      const body = W.element("tbody");
      for (const record of records) {
        const row = W.element("tr");
        const reference = W.element("th"); reference.scope = "row"; reference.append(recordLink(record)); row.append(reference);
        for (const id of columns) row.append(W.element("td", W.display(record.values[id])));
        const outcome = W.element("td");
        outcome.append(W.element("span", W.outcome(record.assessment), W.canRenderAnswer(record.assessment) ? "badge verified" : "badge warning"));
        const timestamp = W.element("time", W.date(record.recordedAt)); timestamp.dateTime = record.recordedAt;
        const recorded = W.element("td"); recorded.append(timestamp); row.append(outcome, recorded); body.append(row);
      }
      table.append(caption, head, body); host.append(table);
    }
    async function loadRegister() {
      W.visibleError(pageError, null);
      register.setAttribute("aria-busy", "true");
      try {
        const result = await W.request("/api/process-records");
        records = result.records; drawRegister();
      } catch (error) { W.visibleError(pageError, error); }
      finally { register.setAttribute("aria-busy", "false"); }
    }
    function detailList(process, data, fieldIds) {
      const list = W.element("dl", null, "record-details");
      for (const id of fieldIds) {
        const definition = process.fields.find(field => field.id === id);
        if (!definition) continue;
        const item = W.element("div");
        item.append(W.element("dt", definition.label), W.element("dd", W.display(data[id])));
        list.append(item);
      }
      return list;
    }
    function showRecord(record, replace = false) {
      const process = record.process;
      document.getElementById("record-heading").textContent = W.display(record.values[process.referenceField]);
      const content = document.getElementById("record-content");
      const overview = W.element("section", null, "panel record-summary");
      const head = W.element("div", null, "section-heading");
      head.append(W.element("h3", process.name), W.element("span", `${record.profileVersion} · ${process.version}`, "badge"));
      overview.append(head, detailList(process, record.values, process.summaryFields));
      const meta = W.element("p", `${W.ui("recordedColumn")}: ${W.date(record.recordedAt)}`, "section-help");
      overview.append(meta);
      const details = W.element("details", null, "panel record-all-details");
      details.append(W.element("summary", W.ui("detailsHeading")));
      for (const stage of process.stages) {
        details.append(W.element("h4", stage.label), detailList(process, record.values,
          process.fields.filter(field => field.stage === stage.id).map(field => field.id)));
      }
      content.replaceChildren(overview, W.renderResult(record.assessment, "assessmentHeading", { captured: true }), details);
      setView("record", record.id, replace);
      document.getElementById("record-heading").tabIndex = -1;
      document.getElementById("record-heading").focus();
    }
    async function openRecord(id, replace = false) {
      const generation = ++viewGeneration;
      W.visibleError(pageError, null);
      try {
        const record = await W.request(`/api/process-records/${encodeURIComponent(id)}`);
        if (generation === viewGeneration) showRecord(record, replace);
      } catch (error) { if (generation === viewGeneration) W.visibleError(pageError, error); }
    }
    function createRecord(replace = false) {
      ++viewGeneration;
      W.visibleError(formError, null); W.visibleError(pageError, null);
      setView("new", null, replace);
      document.getElementById("form-heading").tabIndex = -1;
      document.getElementById("form-heading").focus();
    }
    async function back(replace = false) {
      if (pending) return;
      ++viewGeneration; dirty = false; form.reset(); uploader.clear(); W.visibleError(formError, null);
      setView("register", null, replace);
      await loadRegister();
      document.getElementById("create-record").focus();
    }
    document.getElementById("create-record").addEventListener("click", () => createRecord());
    document.getElementById("fill-example").addEventListener("click", () => {
      if (pending) return;
      fill(config.examples.formValues); dirty = true; W.visibleError(formError, null);
    });
    document.getElementById("cancel-record").addEventListener("click", () => { void back(); });
    document.getElementById("back-register").addEventListener("click", () => { void back(); });
    form.addEventListener("input", () => { dirty = true; });
    form.addEventListener("change", () => { dirty = true; });
    window.addEventListener("beforeunload", event => {
      if (dirty && !formPanel.hidden) { event.preventDefault(); event.returnValue = ""; }
    });
    window.addEventListener("popstate", () => {
      if (pending) return;
      const query = new URLSearchParams(location.search);
      if (query.has("record")) void openRecord(query.get("record"), true);
      else if (query.get("view") === "new") createRecord(true);
      else void back(true);
    });
    form.addEventListener("submit", async event => {
      event.preventDefault();
      if (pending || !W.config.ready) return;
      if (!form.reportValidity()) return;
      pending = true;
      W.busy(submit, true, "submitProcess"); W.visibleError(formError, null);
      document.getElementById("fill-example").disabled = true;
      document.getElementById("cancel-record").disabled = true;
      try {
        const record = await W.post("/api/process-records", W.multipart({ values: values(), model: model.value }, uploader.files));
        dirty = false;
        form.reset(); uploader.clear();
        showRecord(record);
        void W.refreshStatus();
      } catch (error) { W.visibleError(formError, error); formError.tabIndex = -1; formError.focus(); }
      finally {
        pending = false; W.busy(submit, false, "submitProcess");
        document.getElementById("fill-example").disabled = false;
        document.getElementById("cancel-record").disabled = false;
      }
    });
    await loadRegister();
    const query = new URLSearchParams(location.search);
    if (query.has("record")) await openRecord(query.get("record"), true);
    else if (query.get("view") === "new") createRecord(true);
    await W.refreshStatus();
  } catch (error) { W.startupError(error); }
})();
