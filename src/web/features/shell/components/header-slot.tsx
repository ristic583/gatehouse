import { connection } from "next/server";
import { ApiError, apiGet, NotSignedInError } from "@/lib/api/server-fetch";
import type { MeDto, OnboardingStatusDto } from "@/lib/api/types";
import { AppHeader } from "./app-header";

/** Server component: the header for signed-in users, just the title otherwise. */
export async function HeaderSlot() {
  await connection();
  let me: MeDto | null = null;
  try {
    me = await apiGet<MeDto>("/v1/me");
  } catch (error) {
    if (!(error instanceof NotSignedInError || error instanceof ApiError)) {
      throw error;
    }
  }

  if (me === null) {
    return null;
  }

  let onboardingRemaining: number | undefined;
  if (me.onboardingPending && me.roles.includes("TenantAdmin")) {
    try {
      const status = await apiGet<OnboardingStatusDto>("/v1/admin/onboarding");
      onboardingRemaining = status.steps.filter((s) => s.state !== "Complete" && s.state !== "Skipped").length;
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
    }
  }

  return (
    <AppHeader
      tenantName={me.tenantName}
      displayName={me.person?.displayName ?? me.displayName}
      roles={me.roles}
      onboardingRemaining={onboardingRemaining}
    />
  );
}
