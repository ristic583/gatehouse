import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { DashboardDto } from "@/lib/api/types";
import { DashboardView } from "./dashboard-view";

const fetchMock = vi.fn<typeof fetch>();
const dto: DashboardDto = {
  generatedAt: "2026-11-10T12:00:00Z",
  pendingApprovals: 4,
  openAlarms: 2,
  openTasksByKind: { FulfillmentFailed: 1, ReconciliationFinding: 3 },
  openFindingTasks: 3,
  openDriftAlerts: 0,
  unconfirmedFulfillmentsOverOneHour: 1,
  reconciliation: [
    { pacsInstanceId: "p-1", name: "Warehouse-Aarhus", comparedAt: null, comparedRecords: null, inSync: null, findings: null },
  ],
  connectors: { online: 1, offline: 1, neverSeen: 0 },
};

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(() => Promise.resolve(new Response(JSON.stringify(dto), { status: 200 })));
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("DashboardView", () => {
  it("shows counts that link to the console", async () => {
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <DashboardView />
      </QueryClientProvider>,
    );

    expect(await screen.findByTestId("tile-tasks")).toHaveTextContent("4");
    expect(screen.getByTestId("tile-tasks")).toHaveAttribute("href", "/console?tab=tasks");
    expect(screen.getByTestId("tile-alarms")).toHaveAttribute("href", "/live");
    expect(screen.getByTestId("dashboard-connectors")).toHaveTextContent("1 online · 1 offline");
    expect(screen.getByTestId("dashboard-coverage")).toHaveTextContent("never");
  });
});
