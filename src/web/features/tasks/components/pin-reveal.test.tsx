import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PinReveal } from "./pin-reveal";

const fetchMock = vi.fn<typeof fetch>();

function renderReveal() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const view = render(
    <QueryClientProvider client={client}>
      <PinReveal taskId="t-1" />
    </QueryClientProvider>,
  );
  return { client, ...view };
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  fetchMock.mockReset();
  fetchMock.mockImplementation(() =>
    Promise.resolve(
      new Response(JSON.stringify({ pin: "73019", maskAfterSeconds: 30 }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    ),
  );
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("PinReveal", () => {
  it("shows the PIN and masks it again after 30 seconds", async () => {
    const { client } = renderReveal();

    fireEvent.click(screen.getByTestId("pin-reveal"));

    expect(await screen.findByText("73019")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/api/proxy/v1/tasks/t-1/pin-reveal", expect.objectContaining({ method: "POST" }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(29_000);
    });
    expect(screen.getByTestId("pin-value")).toHaveTextContent("73019");
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000);
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
    expect(screen.getByTestId("pin-value")).toHaveTextContent("••••");
    expect(document.body.textContent).not.toContain("73019");
    expect(
      JSON.stringify(
        client
          .getQueryCache()
          .getAll()
          .map((q) => q.state.data),
      ),
    ).not.toContain("73019");
    expect(
      JSON.stringify(
        client
          .getMutationCache()
          .getAll()
          .map((m) => m.state.data),
      ),
    ).not.toContain("73019");
  });

  it("leaves no value in the DOM after unmount", async () => {
    const { unmount } = renderReveal();
    fireEvent.click(screen.getByTestId("pin-reveal"));
    expect(await screen.findByText("73019")).toBeInTheDocument();

    unmount();

    expect(document.body.textContent).not.toContain("73019");
  });
});
