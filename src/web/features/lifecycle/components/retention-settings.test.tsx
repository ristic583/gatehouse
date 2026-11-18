import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { RetentionSettings } from "./retention-settings";

const fetchMock = vi.fn<typeof fetch>();
const settings = { photoDaysAfterTermination: 30, personalDataDaysAfterTermination: 365, doorEventDays: 90 };

function json(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
}

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation((input, init) =>
    String(input).endsWith("/run")
      ? json({ photosDeleted: 2, peopleAnonymized: 1 })
      : init?.method === "PUT"
        ? json({ ...settings, doorEventDays: 60 })
        : json(settings),
  );
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function renderSettings() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <RetentionSettings />
    </QueryClientProvider>,
  );
}

describe("RetentionSettings", () => {
  it("validates ranges before saving", async () => {
    renderSettings();
    const rows = await screen.findAllByTestId("retention-setting");
    const personal = rows[1]!;

    fireEvent.change(within(personal).getByRole("textbox"), { target: { value: "10" } });

    expect(within(personal).getByRole("button", { name: "Save" })).toBeDisabled();
    expect(within(personal).getByRole("alert")).toHaveTextContent("30-3650 days");
  });

  it("saves a value and runs retention", async () => {
    renderSettings();
    const rows = await screen.findAllByTestId("retention-setting");
    fireEvent.change(within(rows[2]!).getByRole("textbox"), { target: { value: "60" } });
    fireEvent.click(within(rows[2]!).getByRole("button", { name: "Save" }));
    fireEvent.click(screen.getByRole("button", { name: "Run retention now" }));

    expect(await screen.findByTestId("retention-result")).toHaveTextContent("2 photos deleted · 1 people anonymized");
    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.some(([, init]) => init?.method === "PUT" && String(init.body).includes('"days":60'))).toBe(true),
    );
  });
});
