import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AreaAccessDto, AreaDetailsDto } from "@/lib/api/types";
import { RequestAccessDialog } from "./request-access-dialog";

const details: AreaDetailsDto = {
  areaId: "a1",
  name: "Server Room",
  code: "CPH-SERVER",
  floorId: "f",
  siteId: "s",
  securityLevel: { securityLevelId: "l", name: "High", approverChain: "Both", maxDurationDays: 182 },
  owners: [{ personId: "p", displayName: "Sofie Berg" }],
};
const me = {
  subject: "s",
  email: "e",
  displayName: "Sofie",
  tenantId: "t",
  tenantName: "T",
  person: { personId: "p", displayName: "Sofie Berg", type: "Employee" },
  roles: ["Employee"],
};
function area(state: AreaAccessDto["state"]): AreaAccessDto {
  return {
    areaId: "a1",
    name: "Server Room",
    code: "CPH-SERVER",
    polygon: [],
    securityLevelName: "High",
    state,
    reason: state === "HasAccess" ? "Confirmed" : "NoEntitlement",
    cardInactive: false,
    since: null,
    doors: [],
  };
}
function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}
function wrap(node: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{node}</QueryClientProvider>);
}
const fetchMock = vi.fn<typeof fetch>();
let people: unknown[] = [];
const activeCard = {
  cardId: "k1",
  personId: "p",
  cardNumberMasked: "••12",
  state: "Active",
  stateChangedAt: "2026-11-01T00:00:00Z",
  pacsStatus: null,
};
let cards: Record<string, { status: number; body: unknown }> = {};
const contractor = {
  personId: "c1",
  displayName: "Carl Contractor",
  email: "c@x.test",
  type: "External",
  source: "Local",
  status: "Active",
  eligibilityStart: "2026-11-01T00:00:00Z",
  eligibilityEnd: "2026-12-01T00:00:00Z",
  sponsor: { personId: "p", displayName: "Sofie Berg" },
  manager: null,
};
function mockResponses(postResponse = () => json({ entitlementId: "e" }, 201)) {
  fetchMock.mockImplementation((input, init) =>
    Promise.resolve(
      String(input).endsWith("/cards")
        ? json(cards[String(input)]?.body ?? [activeCard], cards[String(input)]?.status ?? 200)
        : String(input).endsWith("/v1/me")
          ? json(me)
          : String(input).includes("/v1/people")
            ? json(people)
            : String(input).endsWith("/v1/access-requests")
              ? init?.method === "POST"
                ? postResponse()
                : json([])
              : String(input).endsWith("/v1/theme")
                ? json({ terms: {} })
                : json(details),
    ),
  );
}
function posts() {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === "POST");
}
async function continueToDates() {
  const next = await screen.findByRole("button", { name: "Continue" });
  await waitFor(() => expect(next).toBeEnabled());
  fireEvent.click(next);
}
async function fillAndReview() {
  await continueToDates();
  fireEvent.change(screen.getByLabelText("Justification"), { target: { value: "  Needed for the migration project  " } });
  fireEvent.click(screen.getByRole("button", { name: "Review request" }));
}
beforeEach(() => {
  people = [];
  cards = {};
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  mockResponses();
});
afterEach(() => vi.unstubAllGlobals());

describe("RequestAccessDialog", () => {
  it("shows the person, policy and location before dates", async () => {
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={() => {}} location={{ siteName: "HQ", floorName: "Second floor" }} />);
    expect(await screen.findByText(/max 182 days/)).toBeInTheDocument();
    expect(screen.getByText("Sofie Berg")).toBeInTheDocument();
    expect(screen.getByText("HQ · Second floor")).toBeInTheDocument();
    expect(screen.queryByLabelText("Justification")).not.toBeInTheDocument();
    expect(posts()).toHaveLength(0);
  });
  it("requires review, preserves edits on Back and posts only after final confirmation", async () => {
    const onClose = vi.fn();
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={onClose} />);
    await fillAndReview();
    expect(posts()).toHaveLength(0);
    expect(screen.getByText("Needed for the migration project")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByLabelText("Justification")).toHaveValue("  Needed for the migration project  ");
    fireEvent.click(screen.getByRole("button", { name: "Review request" }));
    fireEvent.click(screen.getByRole("button", { name: "Send request" }));
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
    expect(posts()).toHaveLength(1);
    expect(JSON.parse(String(posts()[0]?.[1]?.body))).toMatchObject({
      personId: null,
      areaId: "a1",
      justification: "Needed for the migration project",
    });
  });
  it("requests on behalf of a sponsored person", async () => {
    people = [contractor];
    const onClose = vi.fn();
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={onClose} />);
    fireEvent.change(await screen.findByRole("combobox", { name: "Who needs access" }), { target: { value: "c1" } });
    await fillAndReview();
    expect(screen.getByText("Carl Contractor")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Send request" }));
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
    expect(JSON.parse(String(posts()[0]?.[1]?.body))).toMatchObject({ personId: "c1", areaId: "a1" });
  });
  it("does not advance with a whitespace justification or an excessive window", async () => {
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={() => {}} />);
    await continueToDates();
    fireEvent.change(screen.getByLabelText("Justification"), { target: { value: "             " } });
    expect(screen.getByRole("button", { name: "Review request" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Justification"), { target: { value: "Valid purpose for the project" } });
    const future = new Date();
    future.setFullYear(future.getFullYear() + 1);
    fireEvent.change(screen.getByLabelText("End date"), { target: { value: `${future.getFullYear()}-12-30` } });
    expect(screen.getByRole("status")).toHaveTextContent("maximum of 182 days");
    expect(screen.getByRole("button", { name: "Review request" })).toBeDisabled();
    expect(posts()).toHaveLength(0);
  });
  it("keeps review open with a useful server error", async () => {
    mockResponses(() => json({ code: "request.exceeds_max_duration", title: "Bad", detail: "Too long for this area." }, 400));
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={() => {}} />);
    await fillAndReview();
    fireEvent.click(screen.getByRole("button", { name: "Send request" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Too long for this area.");
    expect(screen.getByText("Needed for the migration project")).toBeInTheDocument();
  });
  it("existing access shows status and history instead of a new request", async () => {
    wrap(<RequestAccessDialog area={area("HasAccess")} onClose={() => {}} />);
    expect(await screen.findByTestId("area-current-state")).toHaveTextContent("Has access");
    expect(screen.queryByRole("button", { name: "Continue" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "See request history" })).toHaveAttribute("href", "/requests?area=a1");
  });
  it("owner retains the roster link", async () => {
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={() => {}} />);
    expect(await screen.findByRole("link", { name: "View roster" })).toHaveAttribute("href", "/areas/a1/roster");
  });
  it("blocks progression when the policy cannot be loaded", async () => {
    fetchMock.mockImplementation((input) =>
      Promise.resolve(String(input).endsWith("/v1/me") ? json(me) : json({ detail: "Unavailable" }, 503)),
    );
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={() => {}} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load this space’s policy");
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
    expect(posts()).toHaveLength(0);
  });

  it("warns when the subject has no active card", async () => {
    people = [contractor];
    cards = {
      "/api/proxy/v1/me/cards": { status: 200, body: [{ ...activeCard, state: "ActivationPending" }] },
      "/api/proxy/v1/people/c1/cards": { status: 403, body: { title: "Forbidden", code: "auth.forbidden" } },
    };
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={() => {}} />);

    expect(await screen.findByTestId("no-active-card-hint")).toHaveTextContent(
      "No active card: access applies once a card is issued and activated",
    );

    // A managed/sponsored subject whose cards the requester may not read (403) gets no hint.
    fireEvent.change(await screen.findByRole("combobox", { name: "Who needs access" }), { target: { value: "c1" } });
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith("/v1/people/c1/cards"))).toBe(true));
    await waitFor(() => expect(screen.queryByTestId("no-active-card-hint")).not.toBeInTheDocument());
  });

  it("shows no card hint when the subject has an active card", async () => {
    wrap(<RequestAccessDialog area={area("NoAccess")} onClose={() => {}} />);
    await screen.findByText(/max 182 days/);
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith("/v1/me/cards"))).toBe(true));
    expect(screen.queryByTestId("no-active-card-hint")).not.toBeInTheDocument();
  });
});
