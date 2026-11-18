/** DOM integration check of the exported UI; this does not replace browser visual QA. */
import { JSDOM, VirtualConsole } from "jsdom";
import fs from "node:fs/promises";
import assert from "node:assert/strict";
const errors = [];
const vc = new VirtualConsole();
vc.on("jsdomError", (e) => {
  if (!e.message.includes("CSS")) errors.push(e.message);
});
vc.on("error", (e) => errors.push(String(e)));
const html = await fs.readFile(new URL("../.design-preview/gatehouse-design-preview.html", import.meta.url), "utf8");
function createDom(compact = false) {
  return new JSDOM(html, {
    runScripts: "dangerously",
    url: "https://offline-preview.test/#/design-preview",
    pretendToBeVisual: true,
    virtualConsole: vc,
    beforeParse(w) {
      w.fetch = () => Promise.reject(new Error("Unexpected external fetch"));
      w.Response = Response;
      w.Request = Request;
      w.Headers = Headers;
      w.matchMedia = (query) => ({
        matches: compact && query.includes("max-width"),
        addListener() {},
        removeListener() {},
        addEventListener() {},
        removeEventListener() {},
      });
      w.ResizeObserver = class {
        observe() {}
        disconnect() {}
        unobserve() {}
      };
      w.scrollTo = () => {};
      w.HTMLElement.prototype.scrollIntoView = () => {};
    },
  });
}
const dom = createDom();
const w = dom.window;
const d = w.document;
async function until(fn) {
  for (let i = 0; i < 150; i++) {
    if (fn()) return;
    await new Promise((r) => setTimeout(r, 20));
  }
  throw new Error("Timed out: " + fn.toString());
}
function button(text) {
  return [...d.querySelectorAll("button")].find((b) => b.textContent.trim() === text);
}
function change(el, value) {
  const proto =
    el.tagName === "TEXTAREA"
      ? w.HTMLTextAreaElement.prototype
      : el.tagName === "SELECT"
        ? w.HTMLSelectElement.prototype
        : w.HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(proto, "value").set.call(el, value);
  el.dispatchEvent(new w.Event("input", { bubbles: true }));
  el.dispatchEvent(new w.Event("change", { bubbles: true }));
}
await until(() => d.querySelector('[data-testid="spatial-area-GROUND-2"]'));
assert.match(d.body.textContent, /Every space. Under control/);
assert.equal(button("Isolate floor").getAttribute("aria-pressed"), "true");
change(d.querySelector('[aria-label="Search this building"]'), "Research lab");
await until(() => d.querySelector(".gh-search-results button"));
d.querySelector(".gh-search-results button").click();
await until(() => d.querySelector(".gh-area-detail h3")?.textContent === "Research lab");
assert.equal(d.querySelector('[aria-label="Floor"]').value, "first");
assert.match(d.querySelector(".gh-area-detail").textContent, /Approval policy/);
change(d.querySelector('[aria-label="Floor"]'), "ground");
await until(() => d.querySelector('[data-testid="spatial-area-GROUND-2"]'));
button("Building").click();
await until(() => button("Building").getAttribute("aria-pressed") === "true");
button("Isolate floor").click();
await until(() => d.querySelectorAll('[aria-label^="Select floor "]').length === 1);
button("Exploded").click();
await until(() => d.querySelectorAll('[aria-label^="Select floor "]').length === 3);
d.querySelector('[aria-label="Scene settings"]').click();
await until(() => d.querySelector('[role="switch"]'));
const labels = [...d.querySelectorAll('[role="switch"]')].find((b) => b.textContent === "Room labels");
labels.click();
await until(() => labels.getAttribute("aria-checked") === "false");
assert.equal(d.querySelectorAll(".gh-room-label").length, 0);
labels.click();
await until(() => d.querySelectorAll(".gh-room-label").length > 0);
d.querySelector('[aria-label="Close scene settings"]').click();
d.querySelector('[aria-label="Zoom in"]').click();
await until(() => d.querySelector(".gh-zoom-tools").textContent.includes("115%"));
d.querySelector('[aria-label="Reset view"]').click();
await until(() => d.querySelector(".gh-zoom-tools").textContent.includes("100%"));

d.querySelector('[data-testid="spatial-area-GROUND-2"]').dispatchEvent(new w.MouseEvent("click", { bubbles: true }));
await until(() => d.querySelector(".gh-area-detail h3")?.textContent === "Meeting suite");
button("Request access").click();
await until(() => button("Continue") && !button("Continue").disabled);
button("Continue").click();
await until(() => d.querySelector("#request-justification"));
await until(() => d.querySelector('[data-slot="dialog-description"]')?.textContent.includes("max 30 days"));
change(d.querySelector("#request-justification"), "Project workshop with the design team.");
await new Promise((r) => setTimeout(r, 30));
button("Review request").click();
await until(() => button("Send request"));
assert.match(d.querySelector('[role="dialog"]').textContent, /Project workshop/);
button("Send request").click();
await until(() => !d.querySelector("#request-justification"));
await until(() => d.querySelector(".gh-area-detail .gh-pill")?.textContent === "Pending");
const track = [...d.querySelectorAll("a")].find((a) => a.textContent.trim() === "Track request");
assert.match(track.getAttribute("href"), /area=ground-1/);
w.location.hash = track.getAttribute("href");
await until(() => d.querySelector('[data-testid="request-preview-3"]'));
assert.match(d.querySelector('[data-testid="request-preview-3"]').textContent, /Awaiting approval/);
const row = d.querySelector('[data-testid="request-preview-3"]');
[...row.querySelectorAll("button")].find((b) => b.textContent === "Cancel request").click();
await until(() => button("Confirm cancel"));
button("Confirm cancel").click();
await until(() => d.querySelector('[data-testid="request-preview-3"]')?.textContent.includes("Cancelled"));
w.location.hash = "/design-preview?view=approvals";
await until(() => d.querySelectorAll(".gh-approval-card").length === 2);
[...d.querySelectorAll("button")].find((b) => b.textContent.includes("Approve")).click();
await until(() => button("Confirm approval"));
assert.equal(d.querySelectorAll(".gh-approval-card").length, 2);
button("Confirm approval").click();
await until(() => d.querySelectorAll(".gh-approval-card").length === 1);
[...d.querySelectorAll("button")].find((b) => b.textContent.includes("Reject")).click();
await until(() => d.querySelector("#reject-comment"));
assert.equal(button("Confirm reject").disabled, true);
change(d.querySelector("#reject-comment"), "Access is not needed for this project.");
await until(() => !button("Confirm reject").disabled);
button("Confirm reject").click();
await until(() => d.body.textContent.includes("Nothing waits for your decision."));
w.location.hash = "/login";
await until(() => d.querySelector("#login-email"));
assert.equal(d.querySelector("#login-email").type, "email");
assert.equal(d.querySelector("form").method, "post");
assert.equal(errors.length, 0, errors.join("\n"));
console.log(
  "PASS: offline bundle mounts; floor-first view; building search; scene controls and SVG fallback; room selection; request wizard and review; filtered tracking; pending map state; cancellation; approval; rejection with required comment; login form.",
);
dom.window.close();

const mobile = createDom(true);
const md = mobile.window.document;
await until(
  () => [...md.querySelectorAll("button")].find((b) => b.textContent.trim() === "Room list")?.getAttribute("aria-pressed") === "true",
);
assert.equal(md.querySelector(".gh-webgl-stage"), null, "Mobile starts without a GPU scene");
assert.ok(md.querySelector('[aria-label="Floor"]'));
const three = [...md.querySelectorAll("button")].find((b) => b.textContent.trim() === "3D model");
three.click();
await until(() => md.querySelector(".gh-webgl-stage"));
assert.equal(errors.length, 0, errors.join("\n"));
console.log("PASS: mobile defaults to a room list; floor selection is available; 3D is an explicit choice.");
mobile.window.close();
