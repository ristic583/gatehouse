import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { PageHeading } from "@/components/page-heading";
import { OnboardingFlow } from "@/features/onboarding/components/onboarding-flow";
import { ApiError, apiGet, NotSignedInError } from "@/lib/api/server-fetch";
import type { MeDto } from "@/lib/api/types";

async function SignedIn() {
  await connection();
  let me: MeDto;
  try {
    me = await apiGet<MeDto>("/v1/me");
  } catch (error) {
    if (error instanceof NotSignedInError) redirect("/login?returnTo=/onboarding");
    if (error instanceof ApiError) redirect("/");
    throw error;
  }
  if (!me.roles.includes("TenantAdmin")) redirect("/");
  return <OnboardingFlow tenantName={me.tenantName} />;
}

export default function OnboardingPage() {
  return (
    <div className="gh-page">
      <PageHeading
        eyebrow="ONBOARDING"
        title="Let's set up your workspace."
        description="A guided path through everything Gatehouse needs before your people can request access."
      />
      <Suspense fallback={<p className="gh-loading">Loading…</p>}>
        <SignedIn />
      </Suspense>
    </div>
  );
}
