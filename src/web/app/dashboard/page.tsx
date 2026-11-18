import { PageHeading } from "@/components/page-heading";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { DashboardView } from "@/features/dashboard/components/dashboard-view";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInDashboard() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/dashboard");
  }
  return <DashboardView />;
}

export default function DashboardPage() {
  return (
    <div className="gh-page">
      <PageHeading
        eyebrow="WORKSPACE OVERVIEW"
        title="A connected perspective."
        description="Your buildings, access operations, and the things that need attention."
      />
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInDashboard />
      </Suspense>
    </div>
  );
}
