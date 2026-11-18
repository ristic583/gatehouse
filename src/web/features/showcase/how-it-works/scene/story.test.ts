import { describe, expect, it } from "vitest";
import { DOOR_LANE, GOVERNANCE_LANE } from "../flows";
import { LINKS, NODES, STEP_SECONDS, sceneAt } from "./story";

describe("story", () => {
  it("has one timed script step per flow step", () => {
    expect(STEP_SECONDS.governance).toHaveLength(GOVERNANCE_LANE.length);
    expect(STEP_SECONDS.door).toHaveLength(DOOR_LANE.length);
    GOVERNANCE_LANE.forEach((_, i) => expect(() => sceneAt("governance", i, 0.5)).not.toThrow());
    DOOR_LANE.forEach((_, i) => expect(() => sceneAt("door", i, 0.5)).not.toThrow());
  });

  it("turns the map green only after the PACS ack", () => {
    for (let i = 0; i < GOVERNANCE_LANE.length - 1; i++) {
      expect(sceneAt("governance", i, 1).annaScreen).not.toBe("HasAccess");
    }
    expect(sceneAt("governance", GOVERNANCE_LANE.length - 1, 1).annaScreen).toBe("HasAccess");
  });

  it("approves in order: manager first, then the area owner", () => {
    const approve = GOVERNANCE_LANE.findIndex((s) => s.id === "approve");
    expect(sceneAt("governance", approve, 0.4)).toMatchObject({
      managerScreen: "approved",
      ownerScreen: "idle",
    });
    expect(sceneAt("governance", approve, 1)).toMatchObject({
      managerScreen: "approved",
      ownerScreen: "approved",
    });
  });

  it("decides at the door without Gatehouse: only reader and controller are active, the cloud is dimmed", () => {
    const decide = DOOR_LANE.findIndex((s) => s.id === "decide");
    for (const t of [0.1, 0.5, 0.9]) {
      const s = sceneAt("door", decide, t);
      expect(s.cloudDimmed).toBe(true);
      expect([...s.active].sort()).toEqual(["controller", "labReader"]);
      s.packets.forEach((p) => expect(p.link).toBe("readerController"));
    }
    expect(sceneAt("door", decide, 1)).toMatchObject({ readerLed: "granted" });
  });

  it("the agent only calls out: no link starts at the gateway towards the agent", () => {
    const links: { from: string; to: string }[] = Object.values(LINKS);
    expect(links.some((l) => l.from === "gateway" && l.to === "agent")).toBe(false);
    expect(LINKS.agentGateway.from).toBe("agent");
    expect(NODES.keycloak).toBeDefined();
    expect(links.some((l) => l.from === "keycloak" || l.to === "keycloak")).toBe(false);
  });
});
