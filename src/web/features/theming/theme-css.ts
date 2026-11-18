import type { ThemeDto } from "@/lib/api/types";
import { HEX_COLOR } from "./contrast";

/** CSS for the tenant theme tokens only (`--gh-theme-*`); never touches `--gh-state-*`. Invalid values are dropped. */
export function themeCss(theme: ThemeDto): string {
  const vars: [string, string][] = [
    ["--gh-theme-primary", theme.primary],
    ["--gh-theme-primary-foreground", theme.primaryForeground],
    ["--gh-theme-accent", theme.accent],
    ["--gh-theme-accent-foreground", theme.accentForeground],
    ["--gh-theme-background", theme.background],
    ["--gh-theme-foreground", theme.foreground],
  ];
  const declarations = vars.filter(([, v]) => HEX_COLOR.test(v)).map(([k, v]) => `${k}:${v.toLowerCase()};`);
  if (Number.isInteger(theme.radiusPx) && theme.radiusPx >= 0 && theme.radiusPx <= 24) {
    declarations.push(`--gh-theme-radius:${theme.radiusPx}px;`);
  }
  return `:root{${declarations.join("")}}`;
}
