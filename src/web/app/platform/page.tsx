import Link from "next/link";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { AlertList } from "@/features/platform/components/alert-list";
import { PlatformHeading } from "@/features/platform/components/platform-heading";
import { TenantList } from "@/features/platform/components/tenant-list";
import { getCurrentPlatformSession } from "@/lib/auth/current-session";

async function SignedIn() {
  await connection();
  if ((await getCurrentPlatformSession()) === null) {
    redirect("/platform/login?returnTo=/platform");
  }
  return (
    <>
      <section className="gh-panel">
        <h2 className="gh-panel-title">Alerts</h2>
        <AlertList />
      </section>
      <div className="gh-section-heading">
        <h2>Tenants</h2>
      </div>
      <TenantList />
    </>
  );
}

export default function PlatformPage() {
  return (
    <div className="gh-page">
      <PlatformHeading
        eyebrow="TENANTS"
        title="Customer tenants"
        description="Provision, inspect and suspend tenants. Counts only: tenant data stays in each tenant."
        action={
          <Link className="gh-primary-link" href="/platform/tenants/new">
            New tenant
          </Link>
        }
      />
      <Suspense fallback={<p className="gh-loading">Loading…</p>}>
        <SignedIn />
      </Suspense>
    </div>
  );
}
