"use client";

import { Button } from "@/components/ui/button";
import { redo, undo } from "../draft";
import type { EditorTool } from "../plan-editor-ops";
import type { PlanEditorState } from "../hooks/use-plan-editor";

const TOOLS: { tool: EditorTool; label: string }[] = [
  { tool: "select", label: "Select" },
  { tool: "area", label: "Draw area" },
  { tool: "door", label: "Place door" },
  { tool: "reader", label: "Place reader" },
];

/** Tool buttons, snap toggle, undo/redo, finish area and save. */
export function PlanEditorToolbar({ editor, hasProblems }: { editor: PlanEditorState; hasProblems: boolean }) {
  const { tool, drawing, history, saving } = editor;
  return (
    <div role="toolbar" aria-label="Editor tools" className="flex flex-wrap items-center gap-2">
      {TOOLS.map(({ tool: t, label }) => (
        <Button
          key={t}
          type="button"
          size="sm"
          variant={tool === t ? "default" : "outline"}
          aria-pressed={tool === t}
          onClick={() => editor.pickTool(t)}
        >
          {label}
        </Button>
      ))}
      <label className="flex items-center gap-1 text-sm">
        <input type="checkbox" checked={editor.snap} onChange={(e) => editor.setSnap(e.target.checked)} /> Snap to grid
      </label>
      <Button type="button" size="sm" variant="outline" onClick={() => editor.setHistory(undo)} disabled={history.past.length === 0}>
        Undo
      </Button>
      <Button type="button" size="sm" variant="outline" onClick={() => editor.setHistory(redo)} disabled={history.future.length === 0}>
        Redo
      </Button>
      {tool === "area" && drawing.length >= 3 && (
        <Button type="button" size="sm" onClick={() => editor.finishArea(drawing)}>
          Finish area
        </Button>
      )}
      <Button type="button" size="sm" onClick={() => void editor.save()} disabled={saving || hasProblems} className="ml-auto">
        {saving ? "Saving…" : "Save plan"}
      </Button>
    </div>
  );
}
