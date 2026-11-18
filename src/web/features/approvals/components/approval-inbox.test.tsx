import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ApprovalInboxItemDto } from "@/lib/api/types";
import { ApprovalInbox } from "./approval-inbox";

const item: ApprovalInboxItemDto = {
  entitlementId: "e1",
  stepId: "s1",
  stepOrder: 1,
  approverKind: "AreaOwner",
  subject: { personId: "a", displayName: "Anna Larsen" },
  requestedBy: { personId: "a", displayName: "Anna Larsen" },
  area: { areaId: "x", name: "Lab", siteName: "HQ Copenhagen", floorName: "First floor" },
  securityLevelName: "Medium",
  requestedStart: "2026-11-10T00:00:00Z",
  requestedEnd: "2026-12-10T00:00:00Z",
  effectiveEnd: "2026-12-10T00:00:00Z",
  cappedToEligibility: false,
  justification: "Running experiments",
  createdAt: "2026-11-09T00:00:00Z",
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const fetchMock = vi.fn<typeof fetch>();

function renderInbox() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ApprovalInbox />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => vi.unstubAllGlobals());

describe("ApprovalInbox", () => {
  it("lists items", async () => {
    fetchMock.mockResolvedValue(json([item]));
    renderInbox();
    expect(await screen.findByText("Anna Larsen")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Lab" })).toBeInTheDocument();
    expect(screen.getByText("Area owner approval")).toBeInTheDocument();
  });

  it("approval requires review before posting to the step endpoint", async () => {
    fetchMock.mockImplementation((input) => Promise.resolve(String(input).includes("/approve") ? json({}) : json([item])));
    renderInbox();
    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
    expect(screen.getByRole("dialog")).toHaveTextContent("Anna Larsen");
    expect(screen.getByRole("dialog")).toHaveTextContent("Lab");
    fireEvent.click(screen.getByRole("button", { name: "Confirm approval" }));
    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([url, init]) => url === "/api/proxy/v1/approvals/s1/approve" && init?.method === "POST")).toBe(
        true,
      ),
    );
  });

  it("reject requires a comment", async () => {
    fetchMock.mockImplementation((input) => Promise.resolve(String(input).includes("/reject") ? json({}) : json([item])));
    renderInbox();
    fireEvent.click(await screen.findByRole("button", { name: "Reject" }));
    const confirm = await screen.findByRole("button", { name: "Confirm reject" });
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Comment"), { target: { value: "Not this quarter" } });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    await waitFor(() => {
      const call = fetchMock.mock.calls.find(([url]) => url === "/api/proxy/v1/approvals/s1/reject");
      expect(JSON.parse(String(call?.[1]?.body))).toEqual({ comment: "Not this quarter" });
    });
  });

  it("shows empty state", async () => {
    fetchMock.mockResolvedValue(json([]));
    renderInbox();
    expect(await screen.findByText("Nothing waits for your decision.")).toBeInTheDocument();
  });
});
