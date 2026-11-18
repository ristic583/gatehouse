import { PageHeading } from "@/components/page-heading";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { ApprovalInbox } from "@/features/approvals/components/approval-inbox";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInApprovals() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/approvals");
  }
  return <ApprovalInbox />;
}

export default function ApprovalsPage() {
  return (
    <div className="gh-page">
      <PageHeading
        eyebrow="DECISION CENTER"
        title="Good access starts with you."
        description="Review the context. Make a confident decision. Keep work moving."
      />
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInApprovals />
      </Suspense>
    </div>
  );
}
