import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { PlatformHeading } from "@/features/platform/components/platform-heading";
import { TenantDetail } from "@/features/platform/components/tenant-detail";
import { getCurrentPlatformSession } from "@/lib/auth/current-session";

async function SignedIn({ params }: { params: Promise<{ tenantId: string }> }) {
  await connection();
  const { tenantId } = await params;
  if ((await getCurrentPlatformSession()) === null) {
    redirect(`/platform/login?returnTo=${encodeURIComponent(`/platform/tenants/${tenantId}`)}`);
  }
  return <TenantDetail tenantId={tenantId} />;
}

export default function TenantPage({ params }: PageProps<"/platform/tenants/[tenantId]">) {
  return (
    <div className="gh-page">
      <PlatformHeading eyebrow="TENANT" title="Tenant" description="Provisioning, statistics, connectors and alerts." />
      <Suspense fallback={<p className="gh-loading">Loading…</p>}>
        <SignedIn params={params} />
      </Suspense>
    </div>
  );
}
