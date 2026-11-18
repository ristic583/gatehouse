import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { CardDto, MeDto } from "@/lib/api/types";
import { MyBadge } from "./my-badge";

const fetchMock = vi.fn<typeof fetch>();

const card: CardDto = {
  cardId: "c-1",
  personId: "p-1",
  cardNumberMasked: "••••2045",
  state: "Assigned",
  stateChangedAt: "2026-11-10T00:00:00Z",
  pacsStatus: null,
};

function me(type: string): MeDto {
  return {
    subject: "sub",
    email: "x@nordlys.test",
    displayName: "X",
    tenantId: "nordlys",
    tenantName: "Nordlys A/S",
    person: { personId: "p-1", displayName: "X", type },
    roles: [type],
    onboardingPending: false,
  };
}

function json(body: unknown, status = 200) {
  return Promise.resolve(
    new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }),
  );
}

function respond(type: string) {
  fetchMock.mockImplementation((input, init) => {
    const url = String(input);
    if (init?.method === "PUT" && url.endsWith("/v1/me/pin")) {
      return json(null, 204);
    }
    if (init?.method === "POST" && url.endsWith("/v1/me/pin/reveal-requests")) {
      return json({ taskId: "t-1" }, 202);
    }
    if (url.endsWith("/v1/me/cards")) {
      return json([card]);
    }
    return json(me(type));
  });
}

function renderBadge() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MyBadge />
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

describe("MyBadge", () => {
  it("shows activate for employees", async () => {
    respond("Employee");
    renderBadge();

    expect(await screen.findByTestId("my-card")).toHaveTextContent("Assigned, not active yet");
    expect(await screen.findByRole("button", { name: "Activate" })).toBeInTheDocument();
  });

  it("shows no activate for externals", async () => {
    respond("External");
    renderBadge();

    await screen.findByTestId("my-card");
    expect(await screen.findByText("Your sponsor or manager activates your card.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Activate" })).not.toBeInTheDocument();
  });

  it("sets a PIN without echoing it", async () => {
    respond("Employee");
    renderBadge();
    const input = screen.getByLabelText("New PIN (4-8 digits)");

    expect(input).toHaveAttribute("type", "password");
    fireEvent.change(input, { target: { value: "4711" } });
    fireEvent.click(screen.getByRole("button", { name: "Set PIN" }));

    expect(await screen.findByText("PIN saved.")).toBeInTheDocument();
    expect(input).toHaveValue("");
    expect(document.body.textContent).not.toContain("4711");
  });

  it("asks security staff for help", async () => {
    respond("Employee");
    renderBadge();

    fireEvent.click(screen.getByRole("button", { name: "I forgot my PIN" }));

    expect(await screen.findByText("Security staff will contact you.")).toBeInTheDocument();
  });
});
