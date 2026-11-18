import { apiClient } from "@/lib/api/client";
import type { OnboardingStatusDto, OnboardingStep } from "@/lib/api/types";

export const onboardingKeys = { status: ["admin", "onboarding"] as const };

export const onboardingApi = {
  status: () => apiClient.get<OnboardingStatusDto>("/v1/admin/onboarding"),
  skip: (step: OnboardingStep) => apiClient.post<OnboardingStatusDto>(`/v1/admin/onboarding/steps/${step}/skip`),
  complete: () => apiClient.post<OnboardingStatusDto>("/v1/admin/onboarding/complete"),
};

/** Contract order (`domain-model.md` §2.10) with UI labels; Review is the client-side final panel. */
export const STEP_ORDER: readonly OnboardingStep[] = ["IdentityProvider", "Sites", "People", "Pacs", "Connector", "Staff"];
export const STEP_LABEL: Record<OnboardingStep, string> = {
  IdentityProvider: "Identity provider",
  Sites: "Sites & floor plans",
  People: "People & roles",
  Pacs: "PACS",
  Connector: "Connector",
  Staff: "Staff",
};

export function remainingSteps(status: OnboardingStatusDto): number {
  return status.steps.filter((s) => s.state !== "Complete" && s.state !== "Skipped").length;
}
