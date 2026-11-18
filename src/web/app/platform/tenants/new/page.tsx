import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { CreateTenantWizard } from "@/features/platform/components/create-tenant-wizard";
import { PlatformHeading } from "@/features/platform/components/platform-heading";
import { getCurrentPlatformSession } from "@/lib/auth/current-session";

async function SignedIn() {
  await connection();
  if ((await getCurrentPlatformSession()) === null) {
    redirect("/platform/login?returnTo=/platform/tenants/new");
  }
  return <CreateTenantWizard />;
}

export default function NewTenantPage() {
  return (
    <div className="gh-page">
      <PlatformHeading
        eyebrow="NEW TENANT"
        title="Onboard a customer"
        description="Creates the tenant database, the sign-in organization and invites the first admin."
      />
      <Suspense fallback={<p className="gh-loading">Loading…</p>}>
        <SignedIn />
      </Suspense>
    </div>
  );
}
