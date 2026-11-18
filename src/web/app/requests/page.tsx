import { PageHeading } from "@/components/page-heading";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { MyRequestsTable } from "@/features/requests/components/my-requests-table";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInRequests() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/requests");
  }
  return <MyRequestsTable />;
}

export default function RequestsPage() {
  return (
    <div className="gh-page">
      <PageHeading
        eyebrow="ACCESS GOVERNANCE"
        title="Every request. One clear view."
        description="Follow your access from the first request to final confirmation."
      />
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInRequests />
      </Suspense>
    </div>
  );
}
