import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { OnboardingStatusDto, OnboardingStepDto } from "@/lib/api/types";
import { OnboardingFlowView } from "./onboarding-flow";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), refresh: vi.fn() }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/features/admin/identity/api", () => ({
  identityKeys: { provider: ["admin", "identity-provider"] },
  identityApi: { get: vi.fn().mockResolvedValue(null), save: vi.fn(), remove: vi.fn(), test: vi.fn() },
}));

const step = (s: OnboardingStepDto["step"], state: OnboardingStepDto["state"], skippable = false): OnboardingStepDto => ({
  step: s,
  state,
  skippable,
  detail: `${s} detail`,
  href: `/admin/${s.toLowerCase()}`,
});

function status(overrides: Partial<OnboardingStatusDto> = {}): OnboardingStatusDto {
  return {
    // Deliberately shuffled: the UI uses the contract order.
    steps: [
      step("Staff", "NotStarted", true),
      step("Sites", "InProgress"),
      step("IdentityProvider", "Complete"),
      step("Connector", "NotStarted", true),
      step("Pacs", "NotStarted"),
      step("People", "NotStarted"),
    ],
    canComplete: false,
    completedAt: null,
    ...overrides,
  };
}

describe("OnboardingFlowView", () => {
  it("shows steps in contract order", () => {
    render(<OnboardingFlowView tenantName="Acme" status={status()} onSkip={vi.fn()} onFinish={vi.fn()} />);
    const flow = screen.getByRole("list", { name: "Onboarding steps" });
    expect(
      within(flow)
        .getAllByRole("listitem")
        .map((li) => li.querySelector("strong")?.textContent),
    ).toEqual(["Identity provider", "Sites & floor plans", "People & roles", "PACS", "Connector", "Staff", "Review"]);
    expect(screen.getByTestId("onboarding-step-Sites")).toHaveTextContent("Sites detail");
    expect(within(screen.getByTestId("onboarding-step-Sites")).getByRole("link")).toHaveAttribute("href", "/admin/sites");
  });

  it("skip offered only for connector and staff", () => {
    const onSkip = vi.fn();
    render(<OnboardingFlowView tenantName="Acme" status={status()} onSkip={onSkip} onFinish={vi.fn()} />);
    const skips = screen.getAllByRole("button", { name: /^Skip/ });
    expect(skips.map((b) => b.textContent)).toEqual(["Skip Connector", "Skip Staff"]);
    fireEvent.click(screen.getByRole("button", { name: "Skip Staff" }));
    expect(onSkip).toHaveBeenCalledWith("Staff");
  });

  it("finish disabled until required steps complete", () => {
    const onFinish = vi.fn();
    const { rerender } = render(<OnboardingFlowView tenantName="Acme" status={status()} onSkip={vi.fn()} onFinish={onFinish} />);
    expect(screen.getByRole("button", { name: "Finish onboarding" })).toBeDisabled();

    const ready = status({
      canComplete: true,
      steps: [
        step("IdentityProvider", "Complete"),
        step("Sites", "Complete"),
        step("People", "Complete"),
        step("Pacs", "Complete"),
        step("Connector", "Skipped", true),
        step("Staff", "Complete", true),
      ],
    });
    rerender(<OnboardingFlowView tenantName="Acme" status={ready} onSkip={vi.fn()} onFinish={onFinish} />);
    fireEvent.click(screen.getByRole("button", { name: "Finish onboarding" }));
    expect(onFinish).toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /^Skip/ })).not.toBeInTheDocument();
  });

  it("identity step embeds the provider form", async () => {
    const fresh = status({
      steps: [
        step("IdentityProvider", "NotStarted"),
        step("Sites", "NotStarted"),
        step("People", "NotStarted"),
        step("Pacs", "NotStarted"),
        step("Connector", "NotStarted", true),
        step("Staff", "NotStarted", true),
      ],
    });
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <OnboardingFlowView tenantName="Acme" status={fresh} onSkip={vi.fn()} onFinish={vi.fn()} />
      </QueryClientProvider>,
    );

    const identity = screen.getByTestId("onboarding-step-IdentityProvider");
    // The form is inline in the step panel, not a link to /admin/identity.
    expect(within(identity).queryByRole("link")).not.toBeInTheDocument();
    const panel = within(identity).getByTestId("onboarding-panel-IdentityProvider");
    expect(await within(panel).findByLabelText("Display name")).toBeInTheDocument();
    expect(within(panel).getByRole("radiogroup", { name: "Protocol" })).toBeInTheDocument();

    // Only one embedded panel at a time; hiding it leaves the step's server state untouched.
    expect(screen.queryByTestId("onboarding-panel-People")).not.toBeInTheDocument();
    fireEvent.click(within(identity).getByRole("button", { name: "Hide Identity provider" }));
    expect(screen.queryByTestId("onboarding-panel-IdentityProvider")).not.toBeInTheDocument();
    expect(identity).toHaveAttribute("data-state", "NotStarted");
  });
});
