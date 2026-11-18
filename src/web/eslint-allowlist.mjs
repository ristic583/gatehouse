// Allow-lists for the size rules of conventions.md §7 (M11-28). One entry per line, sorted, each with the task that
// removes it. The lists can only shrink: loading the ESLint config fails when an entry no longer violates its rule.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const MAX_LINES = 250;
const MAX_LEN = 140;

// Component/page files over 250 lines (max-lines is not applied to them).
export const maxLinesAllowlist = [];

// Files with lines over 140 characters that ESLint's max-len skips because they are string literals.
// Kept (no removing task): shadcn/ui-generated Tailwind class strings; regenerating the component restores them.
export const longLineAllowlist = [
  "components/ui/badge.tsx",
  "components/ui/button.tsx",
  "components/ui/card.tsx",
  "components/ui/dialog.tsx",
  "components/ui/input.tsx",
  "components/ui/label.tsx",
  "components/ui/select.tsx",
  "components/ui/textarea.tsx",
];

function lines(path) {
  return readFileSync(fileURLToPath(new URL(path, import.meta.url)), "utf8").split("\n");
}

export function staleAllowlistEntries() {
  const stale = [];
  for (const path of maxLinesAllowlist) {
    if (lines(path).filter((l) => l.trim() !== "").length <= MAX_LINES) stale.push(`${path} (max-lines)`);
  }
  for (const path of longLineAllowlist) {
    if (!lines(path).some((l) => Buffer.byteLength(l) > MAX_LEN)) stale.push(`${path} (long lines)`);
  }
  return stale;
}
