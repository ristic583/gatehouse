import { describe, expect, it } from "vitest";
import { entryFor, homeForRoles } from "./navigation";
describe("role-aware entry", () => {
  it("prioritizes employees' spaces, approvers' inboxes and operational work", () => {
    expect(homeForRoles(["Employee"])).toBe("/map");
    for (const role of ["Manager", "AreaOwner", "Sponsor"]) expect(homeForRoles(["Employee", role])).toBe("/approvals");
    expect(homeForRoles(["SecurityStaff", "Manager"])).toBe("/dashboard");
    expect(homeForRoles(["TenantAdmin"])).toBe("/dashboard");
    expect(homeForRoles([])).toBe("/map");
  });
});
describe("onboarding entry", () => {
  it("pending admin lands on onboarding", () => {
    expect(entryFor({ roles: ["Employee", "TenantAdmin"], onboardingPending: true })).toBe("/onboarding");
    expect(entryFor({ roles: ["Employee", "TenantAdmin"], onboardingPending: false })).toBe("/dashboard");
    expect(entryFor({ roles: ["Employee"], onboardingPending: true })).toBe("/map");
    expect(entryFor({ roles: ["SecurityStaff"], onboardingPending: true })).toBe("/dashboard");
  });
});
