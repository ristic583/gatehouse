import type { ThemeDto } from "@/lib/api/types";

/** WCAG AA for normal text; same formula as Gatehouse.Domain.Theming.ContrastChecker. */
export const AA_NORMAL_TEXT = 4.5;

export const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

function channel(hex: string, offset: number): number {
  const c = Number.parseInt(hex.slice(1 + offset, 3 + offset), 16) / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function luminance(hex: string): number {
  return 0.2126 * channel(hex, 0) + 0.7152 * channel(hex, 2) + 0.0722 * channel(hex, 4);
}

export function contrastRatio(a: string, b: string): number {
  const la = luminance(a);
  const lb = luminance(b);
  const [lighter, darker] = la >= lb ? [la, lb] : [lb, la];
  return (lighter + 0.05) / (darker + 0.05);
}

export interface ContrastResult {
  name: "primary" | "accent" | "background";
  ratio: number;
  passes: boolean;
}

/** The three required pairs; invalid colors count as failing. */
export function contrastPairs(theme: ThemeDto): ContrastResult[] {
  const pairs: [ContrastResult["name"], string, string][] = [
    ["primary", theme.primaryForeground, theme.primary],
    ["accent", theme.accentForeground, theme.accent],
    ["background", theme.foreground, theme.background],
  ];
  return pairs.map(([name, fg, bg]) => {
    const valid = HEX_COLOR.test(fg) && HEX_COLOR.test(bg);
    const ratio = valid ? contrastRatio(fg, bg) : 0;
    return { name, ratio, passes: valid && ratio >= AA_NORMAL_TEXT };
  });
}
