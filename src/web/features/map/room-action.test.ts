import { describe, expect, it } from "vitest";
import type { AreaAccessDto } from "@/lib/api/types";
import { roomAction } from "./room-action";
const area: AreaAccessDto = {
  areaId: "a",
  name: "Lab",
  code: "LAB",
  polygon: [],
  securityLevelName: "High",
  state: "NoAccess",
  reason: "NoEntitlement",
  cardInactive: false,
  since: null,
  doors: [],
};
describe("contextual room actions", () => {
  it("distinguishes requests, tracking, and assigned access", () => {
    expect(roomAction(area)).toMatchObject({ kind: "request", label: "Request access" });
    expect(roomAction({ ...area, state: "Pending", reason: "AwaitingApproval" })).toMatchObject({ kind: "track", label: "Track request" });
    expect(roomAction({ ...area, state: "Pending", reason: "AwaitingFulfillment" }).description).toMatch(/confirmation/);
    expect(roomAction({ ...area, state: "HasAccess", cardInactive: true }).description).toMatch(/card is inactive/);
  });
  it("does not invite impossible or duplicate requests for unmapped and scheduled access", () => {
    expect(roomAction({ ...area, state: "Unknown", reason: "AreaNotMapped" }).kind).toBe("view");
    expect(roomAction({ ...area, reason: "ScheduledStart" }).kind).toBe("view");
    expect(roomAction({ ...area, reason: "RevocationPending" }).description).toMatch(/removal/);
    expect(roomAction({ ...area, state: "Unknown", reason: "ConnectorOffline" }).description).toMatch(/cannot be verified/);
  });
});
