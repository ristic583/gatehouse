import { describe, expect, it } from "vitest";
import type { ThemeDto } from "@/lib/api/types";
import { contrastPairs, contrastRatio } from "./contrast";
import { themeCss } from "./theme-css";

const theme: ThemeDto = {
  primary: "#7f1d1d",
  primaryForeground: "#ffffff",
  accent: "#fef2f2",
  accentForeground: "#7f1d1d",
  background: "#ffffff",
  foreground: "#1c1917",
  radiusPx: 4,
  terms: { Area: "Zone" },
};

describe("contrast", () => {
  it("matches the WCAG reference ratios", () => {
    expect(contrastRatio("#000000", "#ffffff")).toBeCloseTo(21, 2);
    expect(contrastRatio("#777777", "#777777")).toBeCloseTo(1, 2);
  });

  it("passes the bricks theme and flags a weak pair", () => {
    expect(contrastPairs(theme).every((p) => p.passes)).toBe(true);
    const weak = contrastPairs({ ...theme, primary: "#ffff00" });
    expect(weak.find((p) => p.name === "primary")?.passes).toBe(false);
  });

  it("writes only theme tokens and drops invalid values", () => {
    const css = themeCss({ ...theme, accent: "red;}body{display:none" });
    expect(css).toContain("--gh-theme-primary:#7f1d1d;");
    expect(css).toContain("--gh-theme-radius:4px;");
    expect(css).not.toContain("gh-state");
    expect(css).not.toContain("display:none");
    expect(css).not.toContain("--gh-theme-accent:");
  });
});
