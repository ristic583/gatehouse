import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PacsAdmin } from "./pacs-admin";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  usePathname: () => "/admin/pacs",
  useSearchParams: () => new URLSearchParams(""),
}));

const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(() =>
    Promise.resolve(new Response(JSON.stringify([]), { status: 200, headers: { "Content-Type": "application/json" } })),
  );
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("PacsAdmin states", () => {
  it("shows empty state", async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <PacsAdmin />
      </QueryClientProvider>,
    );
    expect(await screen.findByText("No PACS instances yet.")).toBeInTheDocument();
    expect(screen.getByTestId("page-state")).toHaveAttribute("data-kind", "empty");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
