import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LiveConnection } from "@/features/live/types";
import type { LiveUpdateDto } from "@/lib/api/types";
import { HowItWorksView } from "./how-it-works-view";

vi.mock("../scene/building-scene", () => ({
  default: () => <div data-testid="scene" />,
}));

const fetchMock = vi.fn<typeof fetch>();

class FakeConnection {
  handler: ((u: LiveUpdateDto) => void) | null = null;
  on(_name: string, handler: (u: LiveUpdateDto) => void) {
    this.handler = handler;
  }
  start = () => Promise.resolve();
  stop = () => Promise.resolve();
  invoke = () => Promise.resolve();
  onreconnecting() {}
  onreconnected() {}
  onclose() {}
}

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation((input) =>
    Promise.resolve(
      new Response(
        JSON.stringify(
          String(input).endsWith("/v1/me")
            ? {
                subject: "s",
                email: "j@x.test",
                displayName: "J",
                tenantId: "nordlys",
                tenantName: "N",
                person: null,
                roles: ["Employee", "SecurityStaff"],
              }
            : [
                {
                  siteId: "s-1",
                  name: "HQ Copenhagen",
                  code: "CPH-HQ",
                  timeZoneId: "Europe/Copenhagen",
                  floors: [],
                },
              ],
        ),
        { status: 200 },
      ),
    ),
  );
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("HowItWorksView", () => {
  it("pulses the door lane on a real door event", async () => {
    const connection = new FakeConnection();
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <HowItWorksView connectionFactory={() => connection as unknown as LiveConnection} />
      </QueryClientProvider>,
    );

    expect(screen.getByTestId("lane-governance")).toBeInTheDocument();
    expect(screen.getByTestId("sign-in-note")).toHaveTextContent("never takes part in a door decision");
    const site = await screen.findByLabelText("Live site");
    await screen.findByRole("option", { name: "HQ Copenhagen" });
    fireEvent.change(site, { target: { value: "s-1" } });
    act(() => {
      connection.handler?.({
        kind: "DoorEvent",
        siteId: "s-1",
        doorEvent: null,
        doorState: null,
        alarm: null,
        connector: null,
      });
    });

    expect(screen.getByTestId("lane-door-pulse")).toHaveAttribute("data-count", "1");
    expect(screen.getByTestId("step-badge")).toHaveAttribute("aria-current", "step");
    expect(screen.getByTestId("step-card")).toHaveTextContent("At the door · step 1 of 6");
  });

  it("steps through the governance track and wraps to the door track", () => {
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <HowItWorksView connectionFactory={() => new FakeConnection() as unknown as LiveConnection} />
      </QueryClientProvider>,
    );

    fireEvent.click(screen.getByTestId("step-ack"));
    expect(screen.getByTestId("step-card")).toHaveTextContent("Only the PACS ack turns the map green");
    fireEvent.click(screen.getByRole("button", { name: "Next step" }));
    expect(screen.getByTestId("step-badge")).toHaveAttribute("aria-current", "step");
  });
});
