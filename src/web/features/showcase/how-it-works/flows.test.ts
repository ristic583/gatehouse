import { describe, expect, it } from "vitest";
import { DOOR_LANE, GOVERNANCE_LANE, SIGN_IN_NOTE } from "./flows";

describe("flows", () => {
  it("governance lane runs from the map to the PACS ack", () => {
    expect(GOVERNANCE_LANE.map((s) => s.id)).toEqual(["request", "route", "approve", "outbox", "dispatch", "poll", "adapter", "ack"]);
    expect(GOVERNANCE_LANE.find((s) => s.id === "route")?.detail).toContain("Nobody approves their own request");
  });

  it("door lane has the controller deciding and no Keycloak", () => {
    expect(DOOR_LANE.map((s) => s.id)).toEqual(["badge", "decide", "pump", "events", "ingest", "signalr"]);
    expect(DOOR_LANE.find((s) => s.id === "decide")?.detail).toContain("not in the decision path");
    expect(JSON.stringify(DOOR_LANE).toLowerCase()).not.toContain("keycloak");
    expect(SIGN_IN_NOTE.component).toContain("Keycloak");
  });
});
