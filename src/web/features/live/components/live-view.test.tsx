import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { LiveView } from "./live-view";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn() }),
  usePathname: () => "/live",
  useSearchParams: () => new URLSearchParams(""),
}));
vi.mock("../hooks/use-live-site", () => ({ useLiveSite: () => "disconnected" }));

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("LiveView states", () => {
  it("shows error with retry", async () => {
    let siteCalls = 0;
    fetchMock.mockImplementation((input) => {
      const url = String(input);
      if (url.includes("/v1/me")) return Promise.resolve(json({ roles: ["SecurityStaff"] }));
      if (url.includes("/v1/sites")) {
        siteCalls += 1;
        return Promise.resolve(json({ title: "Server error", detail: "Boom" }, 500));
      }
      return Promise.resolve(json([]));
    });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <LiveView />
      </QueryClientProvider>,
    );
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load sites.");
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    await vi.waitFor(() => expect(siteCalls).toBe(2));
  });
});
