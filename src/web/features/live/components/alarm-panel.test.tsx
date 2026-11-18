import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { AlarmDto } from "@/lib/api/types";
import { AlarmPanel } from "./alarm-panel";
import { renderWithClient } from "./test-utils";

function alarm(kind: AlarmDto["kind"], id: string): AlarmDto {
  return {
    alarmId: id,
    doorId: "d-1",
    doorName: "Vault door",
    areaId: "ar-1",
    areaName: "Vault",
    siteId: "s-1",
    kind,
    raisedAt: "2026-11-10T10:00:00Z",
    doorEventId: "e-1",
    acknowledgedAt: null,
    acknowledgedByName: null,
    comment: null,
  };
}

describe("AlarmPanel", () => {
  it("labels unauthorized access alarms", () => {
    renderWithClient(<AlarmPanel alarms={[alarm("UnauthorizedAccess", "a-1"), alarm("Forced", "a-2")]} canAct={false} />);

    const rows = screen.getAllByTestId("alarm-row");
    const drift = rows.find((r) => r.getAttribute("data-kind") === "UnauthorizedAccess")!;
    expect(within(drift).getByText("Access without entitlement")).toBeInTheDocument();
    expect(drift.querySelector("svg")).not.toBeNull();
    const forced = rows.find((r) => r.getAttribute("data-kind") === "Forced")!;
    expect(within(forced).getByText("Forced open")).toBeInTheDocument();
  });
});
