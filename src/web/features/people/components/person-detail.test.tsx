import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PersonDetailDto } from "@/lib/api/types";
import { PersonDetailView } from "./person-detail";

const fetchMock = vi.fn<typeof fetch>();

function detail(overrides: Partial<PersonDetailDto> = {}, type: PersonDetailDto["summary"]["type"] = "External"): PersonDetailDto {
  return {
    summary: {
      personId: "p-1",
      displayName: "Gus Guest",
      email: "gus@x.test",
      type,
      source: type === "Employee" ? "Authoritative" : "Local",
      status: "Active",
      eligibilityStart: "2026-11-10T00:00:00Z",
      eligibilityEnd: "2026-11-11T00:00:00Z",
      sponsor: { personId: "p-9", displayName: "Mikkel Holm" },
      manager: null,
    },
    cards: [
      {
        cardId: "c-1",
        personId: "p-1",
        cardNumberMasked: "••••2045",
        state: "Assigned",
        stateChangedAt: "2026-11-10T00:00:00Z",
        pacsStatus: null,
      },
      {
        cardId: "c-2",
        personId: "p-1",
        cardNumberMasked: "••••7781",
        state: "Active",
        stateChangedAt: "2026-11-10T00:00:00Z",
        pacsStatus: null,
      },
    ],
    canManage: false,
    canActivateCards: false,
    canIssueMagicLink: false,
    entitlements: [],
    ...overrides,
  };
}

function json(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
}

function renderDetail(person: PersonDetailDto) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <PersonDetailView person={person} />
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

describe("PersonDetailView", () => {
  it("hides card and sponsorship actions without rights", () => {
    renderDetail(detail());

    expect(screen.queryByRole("button", { name: "Activate" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Deactivate" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Extend" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create magic link" })).not.toBeInTheDocument();
  });

  it("shows activate with canActivateCards and deactivate, extend and end with canManage", () => {
    renderDetail(detail({ canActivateCards: true, canManage: true }));

    expect(screen.getByRole("button", { name: "Activate" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deactivate" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Extend" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "End sponsorship" }));
    expect(screen.getByRole("alertdialog", { name: "End sponsorship" })).toBeInTheDocument();
  });

  it("asks for a reason before deactivating", () => {
    renderDetail(detail({ canManage: true }));

    fireEvent.click(screen.getByRole("button", { name: "Deactivate" }));

    expect(screen.getByLabelText("Deactivation reason")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm deactivate" })).toBeDisabled();
  });

  it("shows a magic link once with copy and expiry", async () => {
    fetchMock.mockImplementation((input) =>
      String(input).endsWith("/v1/me")
        ? json({
            subject: "s",
            email: "m@x.test",
            displayName: "M",
            tenantId: "nordlys",
            tenantName: "Nordlys",
            person: null,
            roles: ["Employee"],
          })
        : json({ url: "http://localhost:3000/guest?token=abc.def", expiresAt: "2026-11-10T12:30:00Z" }, 201),
    );
    renderDetail(detail({ canManage: true, canIssueMagicLink: true }, "Guest"));

    fireEvent.click(screen.getByRole("button", { name: "Create magic link" }));

    expect(await screen.findByLabelText("Magic link")).toHaveValue("http://localhost:3000/guest?token=abc.def");
    expect(screen.getByRole("button", { name: "Copy" })).toBeInTheDocument();
    expect(screen.getByTestId("magic-link")).toHaveTextContent("shown once");
    expect(screen.queryByRole("button", { name: "Create magic link" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Done" }));
    expect(screen.queryByLabelText("Magic link")).not.toBeInTheDocument();
  });

  it("offers no magic link for non-guests", () => {
    renderDetail(detail({ canManage: true, canIssueMagicLink: true }, "External"));

    expect(screen.queryByRole("button", { name: "Create magic link" })).not.toBeInTheDocument();
  });

  it("lets security staff deactivate without canManage", () => {
    renderDetail(detail({ canDeactivateCards: true }));

    expect(screen.getByRole("button", { name: "Deactivate" })).toBeInTheDocument();
  });

  it("offers issue card only when the person has no usable card", () => {
    renderDetail(detail({ canIssueCards: true, cards: [] }));

    expect(screen.getByRole("button", { name: "Issue card" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Card number"), { target: { value: "100001" } });
    expect(screen.getByRole("button", { name: "Issue card" })).toBeEnabled();
  });

  it("hides issue card while a card exists", () => {
    renderDetail(detail({ canIssueCards: true }));

    expect(screen.queryByRole("button", { name: "Issue card" })).not.toBeInTheDocument();
  });

  it("revokes one approved entitlement with a reason", async () => {
    fetchMock.mockImplementation((input) =>
      String(input).endsWith("/v1/me")
        ? json({
            subject: "s",
            email: "j@x.test",
            displayName: "J",
            tenantId: "nordlys",
            tenantName: "Nordlys",
            person: null,
            roles: ["SecurityStaff"],
          })
        : json({}),
    );
    renderDetail(
      detail({
        entitlements: [
          {
            entitlementId: "e-1",
            areaName: "Vault",
            status: "Approved",
            effectiveEnd: "2026-12-01T00:00:00Z",
            revokeFulfillmentStatuses: [],
            revokeAction: "emergency-revoke",
          },
          {
            entitlementId: "e-2",
            areaName: "Lobby",
            status: "Expired",
            effectiveEnd: "2026-11-01T00:00:00Z",
            revokeFulfillmentStatuses: [],
            revokeAction: null,
          },
        ],
      }),
    );

    fireEvent.click(screen.getByRole("button", { name: "Revoke access" }));
    fireEvent.change(screen.getByLabelText("Revocation reason"), { target: { value: "Lost badge" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm revoke" }));

    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.some((call) => String(call[0]).includes("/v1/access-requests/e-1/emergency-revoke"))).toBe(true),
    );
    expect(await screen.findAllByRole("button", { name: "Revoke access" })).toHaveLength(1);
  });

  it("asks before terminating an employee", () => {
    renderDetail(detail({ canTerminate: true }, "Employee"));

    fireEvent.click(screen.getByRole("button", { name: "Terminate" }));

    expect(screen.getByRole("alertdialog", { name: "Terminate" })).toBeInTheDocument();
  });
});
