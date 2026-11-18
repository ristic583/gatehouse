"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/lib/api/client";
import type { TermKey, ThemeDto } from "@/lib/api/types";
import { AA_NORMAL_TEXT, contrastPairs } from "../contrast";
import { THEME_KEY, themeApi, useTheme } from "../theme";

const COLORS: { field: keyof Omit<ThemeDto, "radiusPx" | "terms">; label: string }[] = [
  { field: "primary", label: "Primary" },
  { field: "primaryForeground", label: "Primary text" },
  { field: "accent", label: "Accent" },
  { field: "accentForeground", label: "Accent text" },
  { field: "background", label: "Background" },
  { field: "foreground", label: "Text" },
];
const TERMS: TermKey[] = ["Area", "Site", "Floor", "Door"];

/** Console tab "Theme" (TenantAdmin). Map state colors, icons and labels are fixed and not editable here. */
export function ThemePanel() {
  const theme = useTheme();
  if (theme.isError) {
    return <p role="alert">Could not load the theme.</p>;
  }
  return theme.data === undefined ? <p>Loading…</p> : <ThemeForm initial={theme.data} />;
}

export function ThemeForm({ initial }: { initial: ThemeDto }) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<ThemeDto>(initial);
  const [saved, setSaved] = useState(false);
  const pairs = contrastPairs(draft);
  const allPass = pairs.every((p) => p.passes);
  const save = useMutation({
    mutationFn: themeApi.save,
    onSuccess: (theme) => {
      queryClient.setQueryData(THEME_KEY, theme);
      setSaved(true);
    },
  });
  const error = save.error instanceof ApiError ? `${save.error.code ?? "error"}: ${save.error.detail}` : save.error?.message;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Theme</CardTitle>
        <p className="text-sm text-muted-foreground">Map state colors, icons and labels are fixed and cannot be themed.</p>
      </CardHeader>
      <CardContent>
        <form
          className="grid gap-4 md:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            setSaved(false);
            if (allPass) {
              save.mutate(draft);
            }
          }}
        >
          <fieldset className="flex flex-col gap-2">
            <legend className="font-semibold">Colors</legend>
            {COLORS.map((c) => (
              <div key={c.field} className="flex items-center gap-2 text-sm">
                <span className="w-28">{c.label}</span>
                <input
                  type="color"
                  aria-label={`${c.label} picker`}
                  value={draft[c.field]}
                  onChange={(e) => setDraft({ ...draft, [c.field]: e.target.value })}
                />
                <Input
                  aria-label={c.label}
                  className="w-28 font-mono"
                  value={draft[c.field]}
                  onChange={(e) => setDraft({ ...draft, [c.field]: e.target.value })}
                />
              </div>
            ))}
            <div className="flex items-center gap-2 text-sm">
              <span className="w-28">Radius (px)</span>
              <Input
                aria-label="Radius"
                type="number"
                min={0}
                max={24}
                className="w-24"
                value={draft.radiusPx}
                onChange={(e) => setDraft({ ...draft, radiusPx: Number(e.target.value) })}
              />
            </div>
          </fieldset>
          <fieldset className="flex flex-col gap-2">
            <legend className="font-semibold">Terminology</legend>
            {TERMS.map((key) => (
              <div key={key} className="flex items-center gap-2 text-sm">
                <span className="w-28">{key}</span>
                <Input
                  aria-label={`Term for ${key}`}
                  placeholder={key}
                  maxLength={30}
                  value={draft.terms[key] ?? ""}
                  onChange={(e) => {
                    const terms = { ...draft.terms };
                    if (e.target.value.trim() === "") {
                      delete terms[key];
                    } else {
                      terms[key] = e.target.value;
                    }
                    setDraft({ ...draft, terms });
                  }}
                />
              </div>
            ))}
          </fieldset>
          <table className="text-sm md:col-span-2" data-testid="contrast-table">
            <thead>
              <tr>
                <th className="text-left">Pair</th>
                <th className="text-left">Ratio</th>
                <th className="text-left">WCAG AA ({AA_NORMAL_TEXT}:1)</th>
              </tr>
            </thead>
            <tbody>
              {pairs.map((p) => (
                <tr key={p.name} data-testid={`contrast-${p.name}`} data-passes={p.passes}>
                  <td>{p.name}</td>
                  <td>{p.ratio.toFixed(2)}:1</td>
                  <td>{p.passes ? "✓ Pass" : "✕ Fails"}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="flex items-center gap-3 md:col-span-2">
            <Button type="submit" disabled={!allPass || save.isPending}>
              Save theme
            </Button>
            {!allPass ? <span role="alert">Fix the failing pairs before saving.</span> : null}
            {saved ? <span role="status">Theme saved. Reload to apply it everywhere.</span> : null}
            {error !== undefined ? <span role="alert">{error}</span> : null}
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
