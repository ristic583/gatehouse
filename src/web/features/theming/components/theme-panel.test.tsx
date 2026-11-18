import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ThemeDto } from "@/lib/api/types";
import { ThemeForm } from "./theme-panel";

const fetchMock = vi.fn<typeof fetch>();
const initial: ThemeDto = {
  primary: "#171717",
  primaryForeground: "#fafafa",
  accent: "#f5f5f5",
  accentForeground: "#171717",
  background: "#ffffff",
  foreground: "#0a0a0a",
  radiusPx: 10,
  terms: {},
};

function renderForm() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <ThemeForm initial={initial} />
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

describe("ThemeForm", () => {
  it("blocks saving while a pair fails AA", () => {
    renderForm();

    fireEvent.change(screen.getByLabelText("Primary"), { target: { value: "#ffff00" } });
    fireEvent.change(screen.getByLabelText("Primary text"), { target: { value: "#ffffff" } });

    expect(screen.getByTestId("contrast-primary")).toHaveAttribute("data-passes", "false");
    expect(screen.getByRole("button", { name: "Save theme" })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent("Fix the failing pairs");
  });

  it("saves a passing theme with terms", async () => {
    fetchMock.mockImplementation((_input, init) =>
      Promise.resolve(new Response(String(init?.body), { status: 200, headers: { "Content-Type": "application/json" } })),
    );
    renderForm();

    fireEvent.change(screen.getByLabelText("Primary"), { target: { value: "#1e3a8a" } });
    fireEvent.change(screen.getByLabelText("Primary text"), { target: { value: "#ffffff" } });
    fireEvent.change(screen.getByLabelText("Term for Area"), { target: { value: "Zone" } });
    fireEvent.click(screen.getByRole("button", { name: "Save theme" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Theme saved");
    const body = JSON.parse(String(fetchMock.mock.calls[0]![1]!.body)) as ThemeDto;
    expect(body.primary).toBe("#1e3a8a");
    expect(body.terms.Area).toBe("Zone");
  });
});
