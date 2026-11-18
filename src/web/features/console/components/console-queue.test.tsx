import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { FORBIDDEN_TEXT } from "@/components/ui/page-state";
import { ConsoleView } from "./console-view";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn() }),
  usePathname: () => "/console",
  useSearchParams: () => new URLSearchParams(""),
}));

const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ConsoleView states", () => {
  it("shows not permitted on 403", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ title: "Forbidden", detail: "Forbidden", code: "auth.forbidden" }), {
        status: 403,
        headers: { "Content-Type": "application/problem+json" },
      }),
    );
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ConsoleView />
      </QueryClientProvider>,
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(FORBIDDEN_TEXT);
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
  });
});
