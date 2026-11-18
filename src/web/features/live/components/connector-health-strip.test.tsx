import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { ConnectorHealthDto } from "@/lib/api/types";
import { ConnectorHealthStrip } from "./connector-health-strip";

const NOW = Date.parse("2026-11-10T10:00:00Z");

function connector(online: boolean): ConnectorHealthDto {
  return {
    pacsInstanceId: "p-1",
    name: "LegacyPACS CPH",
    vendor: "LegacyPacs",
    fulfillmentMode: "Connector",
    online,
    lastHeartbeatAt: "2026-11-10T09:59:30Z",
    agentVersion: "1.0.0",
    agentQueueDepth: 0,
    queuedCommands: 3,
    lastAckLatencyMs: 120,
    pacsReachable: true,
    capabilityGaps: [],
  };
}

describe("ConnectorHealthStrip", () => {
  it("offline badge", () => {
    render(<ConnectorHealthStrip connectors={[connector(false)]} now={NOW} />);
    const item = screen.getByTestId("connector-health");
    expect(item).toHaveAttribute("data-online", "false");
    expect(screen.getByTestId("connector-badge")).toHaveTextContent("Offline");
    expect(item).toHaveTextContent("heartbeat 30s ago · queue 3 · ack 120 ms");
  });

  it("online badge", () => {
    render(<ConnectorHealthStrip connectors={[connector(true)]} now={NOW} />);
    expect(screen.getByTestId("connector-badge")).toHaveTextContent("Online");
  });

  it("shows capability gaps per connector", () => {
    render(<ConnectorHealthStrip connectors={[{ ...connector(true), capabilityGaps: ["NativeValidity", "Lockdown"] }]} now={NOW} />);
    expect(screen.getAllByTestId("capability-gap").map((li) => li.getAttribute("data-gap"))).toEqual(["NativeValidity", "Lockdown"]);
  });
});
