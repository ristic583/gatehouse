import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReconciliationInstanceDto, ReconciliationReportSummaryDto } from "@/lib/api/types";
import { ReconciliationPanel } from "./reconciliation-panel";

const fetchMock = vi.fn<typeof fetch>();

function instance(overrides: Partial<ReconciliationInstanceDto>): ReconciliationInstanceDto {
  return {
    pacsInstanceId: "i-1",
    name: "HQ-Copenhagen",
    vendor: "LegacyPacs",
    fulfillmentMode: "Connector",
    siteNames: ["Copenhagen HQ"],
    canRun: true,
    blockedReason: null,
    lastReport: null,
    ...overrides,
  };
}

const requested: ReconciliationReportSummaryDto = {
  reportId: "r-1",
  pacsInstanceId: "i-1",
  pacsInstanceName: "HQ-Copenhagen",
  status: "Requested",
  requestedAt: "2026-11-10T08:00:00Z",
  requestedBy: { personId: "p-1", displayName: "Jonas Berg" },
  capturedAt: null,
  comparedAt: null,
  findingCount: null,
  comparedRecordCount: null,
  inSyncCount: null,
};

function json(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
}

function respond(instances: ReconciliationInstanceDto[], onRun?: () => Promise<Response>) {
  let reports: ReconciliationReportSummaryDto[] = [];
  fetchMock.mockImplementation((input, init) => {
    const url = String(input);
    if (init?.method === "POST" && url.endsWith("/v1/reconciliation/reports")) {
      if (onRun !== undefined) {
        return onRun();
      }
      reports = [requested];
      return json(requested, 202);
    }
    if (url.includes("/v1/reconciliation/instances")) {
      return json(instances);
    }
    return json(reports);
  });
}

function renderPanel(onSelect = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ReconciliationPanel onSelect={onSelect} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ReconciliationPanel", () => {
  it("shows run button only when can run", async () => {
    respond([instance({}), instance({ pacsInstanceId: "i-2", name: "Warehouse-Aarhus", canRun: false })]);
    renderPanel();

    const cards = await screen.findAllByTestId("recon-instance");
    expect(cards).toHaveLength(2);
    expect(within(cards[0]!).getByRole("button", { name: "Run reconciliation" })).toBeInTheDocument();
    expect(within(cards[1]!).queryByRole("button", { name: "Run reconciliation" })).not.toBeInTheDocument();
  });

  it("starts a run and shows requested status", async () => {
    respond([instance({})]);
    const onSelect = vi.fn();
    renderPanel(onSelect);

    fireEvent.click(await screen.findByRole("button", { name: "Run reconciliation" }));

    const row = await screen.findByTestId("recon-report-row");
    expect(within(row).getByText("Requested")).toBeInTheDocument();
    fireEvent.click(row);
    expect(onSelect).toHaveBeenCalledWith("r-1");
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === "POST");
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({ pacsInstanceId: "i-1" });
  });

  it("shows blocked reason", async () => {
    respond([instance({ canRun: false, blockedReason: "reconciliation.export_unsupported" })]);
    renderPanel();

    expect(await screen.findByText("This PACS cannot export its records.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Run reconciliation" })).not.toBeInTheDocument();
  });

  it("shows the label of a 409 from run", async () => {
    respond([instance({})], () => json({ code: "reconciliation.already_running", detail: "Running" }, 409));
    renderPanel();

    fireEvent.click(await screen.findByRole("button", { name: "Run reconciliation" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("A reconciliation of this PACS instance is already running.");
  });
});
