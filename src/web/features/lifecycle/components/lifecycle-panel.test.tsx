import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LifecycleSettingsDto } from "@/lib/api/types";
import { LifecyclePanel } from "./lifecycle-panel";

const fetchMock = vi.fn<typeof fetch>();
const settings: LifecycleSettingsDto = {
  maxSponsorshipDays: 365,
  maxVisitorDays: 7,
  renewalEmployeeDays: 365,
  renewalExternalDays: 90,
  renewalGuestDays: 1,
  renewalLeadDays: 14,
};

function json(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
}

function respond(hrFeed: boolean) {
  fetchMock.mockImplementation((input, init) => {
    const url = String(input);
    if (url.endsWith("/v1/admin/hr-sync")) {
      return hrFeed
        ? json({ created: 2, updated: 1, unchanged: 45, skipped: 0 })
        : json({ code: "hr_sync.feed_unavailable", title: "No feed", status: 409 }, 409);
    }
    if (url.endsWith("/v1/admin/lifecycle/run")) {
      return json({ terminated: 1, expired: 3, renewalsRequested: 2 });
    }
    if (init?.method === "PUT") {
      const body = JSON.parse(String(init.body)) as { key: string; days: number };
      return json({ ...settings, renewalLeadDays: body.key === "lifecycle.renewal_lead_days" ? body.days : settings.renewalLeadDays });
    }
    return json(settings);
  });
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <LifecyclePanel />
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

describe("LifecyclePanel", () => {
  it("runs HR sync and lifecycle and shows the counts", async () => {
    respond(true);
    renderPanel();

    fireEvent.click(screen.getByRole("button", { name: "Run HR sync" }));
    fireEvent.click(screen.getByRole("button", { name: "Run lifecycle now" }));

    expect(await screen.findByTestId("hr-sync-result")).toHaveTextContent("2 created · 1 updated · 45 unchanged · 0 skipped");
    expect(await screen.findByTestId("lifecycle-result")).toHaveTextContent("1 terminated · 3 expired · 2 renewals requested");
  });

  it("explains a missing HR feed", async () => {
    respond(false);
    renderPanel();

    fireEvent.click(screen.getByRole("button", { name: "Run HR sync" }));

    expect(await screen.findByText("No HR feed for this tenant")).toBeInTheDocument();
  });

  it("shows six settings and saves one", async () => {
    respond(true);
    renderPanel();

    const rows = await screen.findAllByTestId("lifecycle-setting");
    expect(rows).toHaveLength(6);
    const lead = rows[5]!;
    const input = within(lead).getByRole("textbox");
    expect(input).toHaveValue("14");
    fireEvent.change(input, { target: { value: "21" } });
    fireEvent.click(within(lead).getByRole("button", { name: "Save" }));

    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.some(([, init]) => init?.method === "PUT" && String(init.body).includes('"days":21'))).toBe(true),
    );
  });
});
