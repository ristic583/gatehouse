import { describe, expect, it } from "vitest";
import { safeReturnTo } from "./return-to";

describe("safeReturnTo", () => {
  it("rejects absolute returnTo URLs", () => {
    expect(safeReturnTo("https://evil.example/")).toBe("/");
    expect(safeReturnTo("//evil.example/")).toBe("/");
    expect(safeReturnTo("/\\evil.example")).toBe("/");
    expect(safeReturnTo("/requests?tab=mine")).toBe("/requests?tab=mine");
  });
});
