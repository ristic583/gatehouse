import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReconciliationFindingDto, ReconciliationReportDto } from "@/lib/api/types";
import { ReconciliationReportView } from "./reconciliation-report-view";

const fetchMock = vi.fn<typeof fetch>();

function finding(kind: ReconciliationFindingDto["kind"], overrides: Partial<ReconciliationFindingDto> = {}): ReconciliationFindingDto {
  return {
    findingId: `f-${kind}`,
    kind,
    person: { personId: "p-1", displayName: "Anna Holm" },
    personRef: "p-1",
    areaId: "a-1",
    areaName: "Vault",
    vendorAccessLevelRef: "LVL-VAULT",
    pacsRef: "pacs-1",
    differences: [],
    expected: null,
    actual: null,
    staffTaskId: `t-${kind}`,
    taskStatus: "Open",
    resolutionAction: null,
    ...overrides,
  };
}

function report(status: ReconciliationReportDto["summary"]["status"], findings: ReconciliationFindingDto[] = []): ReconciliationReportDto {
  const done = status === "Completed";
  return {
    summary: {
      reportId: "r-1",
      pacsInstanceId: "i-1",
      pacsInstanceName: "HQ-Copenhagen",
      status,
      requestedAt: "2026-11-10T08:00:00Z",
      requestedBy: null,
      capturedAt: status === "Requested" ? null : "2026-11-10T08:01:00Z",
      comparedAt: done ? "2026-11-10T08:02:00Z" : null,
      findingCount: done ? findings.length : null,
      comparedRecordCount: done ? 12 : null,
      inSyncCount: done ? 10 : null,
    },
    coverage: {
      sites: [{ id: "s-1", name: "Copenhagen HQ" }],
      areas: [{ id: "a-1", name: "Vault" }],
      recordTypes: ["AccessAssignment"],
      exportedRecordCount: 15,
      comparedRecordCount: 12,
      inSyncCount: 10,
      skippedInFlightCount: 1,
      ignoredInactiveCount: 2,
    },
    failureCode: null,
    failureMessage: null,
    findings,
  };
}

function respond(body: ReconciliationReportDto) {
  fetchMock.mockImplementation(() =>
    Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } })),
  );
}

function renderView() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ReconciliationReportView reportId="r-1" />
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

describe("ReconciliationReportView", () => {
  it("shows captured compared and coverage", async () => {
    respond(report("Completed"));
    renderView();

    await screen.findByTestId("recon-report");
    expect(screen.getByTestId("recon-captured-at")).not.toHaveTextContent("–");
    expect(screen.getByTestId("recon-compared-at")).not.toHaveTextContent("–");
    const coverage = screen.getByTestId("recon-coverage");
    expect(coverage).toHaveTextContent("Copenhagen HQ");
    expect(coverage).toHaveTextContent("AccessAssignment");
    expect(coverage).toHaveTextContent("12 of 15 exported records compared");
    expect(coverage).toHaveTextContent("1 skipped");
    expect(coverage).toHaveTextContent("2 ignored");
  });

  it("in sync line only for completed reports", async () => {
    respond(report("Comparing"));
    const { unmount } = renderView();
    await screen.findByTestId("recon-coverage");
    expect(screen.queryByTestId("recon-in-sync")).not.toBeInTheDocument();
    unmount();

    respond(report("Completed"));
    renderView();
    expect(await screen.findByTestId("recon-in-sync")).toHaveTextContent("10 of 12 compared records in sync");
  });

  it("renders a finding per kind with label", async () => {
    respond(
      report("Completed", [
        finding("MissingInPacs"),
        finding("ExtraInPacs", { person: null, personRef: "unknown-7" }),
        finding("Mismatch", {
          differences: ["ValidTo"],
          expected: { cardNumberMasked: null, validFrom: null, validTo: "2026-12-01T00:00:00Z" },
          actual: { cardNumberMasked: "************4444", validFrom: null, validTo: "2027-01-01T00:00:00Z" },
        }),
        finding("ExpiredStillPresent"),
      ]),
    );
    renderView();

    const items = await screen.findAllByTestId("recon-finding");
    expect(items.map((i) => i.getAttribute("data-kind"))).toEqual(["MissingInPacs", "ExtraInPacs", "Mismatch", "ExpiredStillPresent"]);
    expect(within(items[0]!).getByText("Missing in PACS")).toBeInTheDocument();
    expect(within(items[1]!).getByText("Extra in PACS")).toBeInTheDocument();
    expect(within(items[1]!).getByText("unknown-7")).toBeInTheDocument();
    expect(within(items[2]!).getByText("Mismatch")).toBeInTheDocument();
    expect(items[2]).toHaveTextContent("************4444");
    expect(within(items[3]!).getByText("Expired still present")).toBeInTheDocument();
  });

  it("links finding to its task", async () => {
    respond(report("Completed", [finding("MissingInPacs")]));
    renderView();

    const item = await screen.findByTestId("recon-finding");
    expect(within(item).getByRole("link", { name: "Open task" })).toHaveAttribute("href", "/console?tab=tasks&item=t-MissingInPacs");
  });

  it("shows failure code and message of a failed report", async () => {
    respond({ ...report("Failed"), coverage: null, failureCode: "reconciliation.timed_out", failureMessage: "Did not finish" });
    renderView();

    expect(await screen.findByRole("alert")).toHaveTextContent("reconciliation.timed_out: Did not finish");
  });
});
