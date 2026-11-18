import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StaffTaskDto } from "@/lib/api/types";
import { StaffTaskList } from "./staff-task-list";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const task: StaffTaskDto = {
  taskId: "t1",
  kind: "RevocationUnconfirmed",
  status: "Open",
  siteId: "s1",
  siteName: "HQ Copenhagen",
  subjectType: "Fulfillment",
  subjectId: "f1",
  summary: "Revoke for Anna Larsen is unconfirmed",
  assignedTo: null,
  resolutionAction: null,
  resolutionNote: null,
  resolvedBy: null,
  resolvedAt: null,
  createdAt: "2026-11-09T10:00:00Z",
};

const fetchMock = vi.fn<typeof fetch>();

const extraFinding: StaffTaskDto = {
  ...task,
  taskId: "t2",
  kind: "ReconciliationFinding",
  subjectType: "ReconciliationFinding",
  subjectId: "rf1",
  summary: "Extra in PACS: unknown-7 · Vault",
  findingKind: "ExtraInPacs",
};

function renderList(canHandle: boolean, listed: StaffTaskDto = task) {
  fetchMock.mockImplementation(() =>
    Promise.resolve(new Response(JSON.stringify([listed]), { status: 200, headers: { "Content-Type": "application/json" } })),
  );
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <StaffTaskList canHandle={canHandle} />
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

describe("StaffTaskList", () => {
  it("lists open tasks with summary", async () => {
    renderList(true);
    const row = await screen.findByTestId("task-t1");
    expect(row).toHaveTextContent("Revocation not confirmed");
    expect(row).toHaveTextContent("Revoke for Anna Larsen is unconfirmed");
    expect(row).toHaveTextContent("HQ Copenhagen");
  });

  it("resolve requires an action", async () => {
    renderList(true);
    fireEvent.click(await screen.findByRole("button", { name: "Resolve" }));
    const confirm = await screen.findByRole("button", { name: "Confirm resolve" });
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Resolution"), { target: { value: "Dismissed" } });
    expect(confirm).toBeEnabled();
  });

  it("read-only without canHandle", async () => {
    renderList(false);
    await screen.findByTestId("task-t1");
    expect(screen.queryByRole("button", { name: "Claim" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Resolve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Open fulfillment" })).not.toBeInTheDocument();
  });

  it("offers remove from pacs for extra findings", async () => {
    renderList(true, extraFinding);
    const row = await screen.findByTestId("task-t2");
    expect(row).toHaveTextContent("Extra in PACS");
    fireEvent.click(screen.getByRole("button", { name: "Resolve" }));
    const select = await screen.findByLabelText("Resolution");
    const options = Array.from((select as HTMLSelectElement).options).map((o) => o.value);
    expect(options).toEqual(["", "RemovedFromPacs", "Dismissed"]);
    expect(screen.getByRole("option", { name: "Remove from PACS" })).toBeInTheDocument();
  });

  it("requires a note to dismiss", async () => {
    renderList(true, extraFinding);
    fireEvent.click(await screen.findByRole("button", { name: "Resolve" }));
    const confirm = await screen.findByRole("button", { name: "Confirm resolve" });
    fireEvent.change(screen.getByLabelText("Resolution"), { target: { value: "Dismissed" } });
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Note (required)"), { target: { value: "Vendor badge, removed by vendor" } });
    expect(confirm).toBeEnabled();
  });
});
