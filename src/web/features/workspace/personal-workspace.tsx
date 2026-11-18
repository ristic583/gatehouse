"use client";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { useMe } from "@/lib/api/me";
import { MY_REQUESTS_KEY, requestsApi } from "@/features/requests/api";
import { approvalsApi, INBOX_KEY } from "@/features/approvals/api";
import { APPROVER_ROLES, OPERATIONS_ROLES } from "./navigation";

export function PersonalWorkspace({ preview = false }: { preview?: boolean }) {
  const me = useMe();
  const canApprove = (me.data?.roles ?? []).some((r) => APPROVER_ROLES.includes(r));
  const canOperate = (me.data?.roles ?? []).some((r) => OPERATIONS_ROLES.includes(r));
  const requests = useQuery({ queryKey: MY_REQUESTS_KEY, queryFn: requestsApi.mine, enabled: !!me.data?.person });
  const inbox = useQuery({ queryKey: INBOX_KEY, queryFn: approvalsApi.inbox, enabled: canApprove });
  const own = (requests.data ?? []).filter((r) => r.subject.personId === me.data?.person?.personId);
  const waiting = own.filter(
    (r) => r.status === "PendingApproval" || (r.status === "Approved" && r.fulfillmentStatus !== "Confirmed"),
  ).length;
  const href = (view: string) => (preview ? `/design-preview?view=${view}` : `/${view}`);
  return (
    <section className="gh-personal-workspace" aria-label="Your workspace">
      <div>
        <p className="gh-eyebrow">YOUR WORKSPACE</p>
        <strong>Find a space. Get the access you need.</strong>
        <p>Select a room to see your access and its requirements.</p>
      </div>
      <div className="gh-personal-actions">
        <Link href={href("requests")}>
          My requests <span>{requests.isError ? "Unavailable" : requests.data ? `${waiting} in progress` : "Open"}</span>
        </Link>
        {canApprove && (
          <Link href={href("approvals")}>
            Review approvals <span>{inbox.isError ? "Unavailable" : inbox.data ? `${inbox.data.length} awaiting you` : "Open inbox"}</span>
          </Link>
        )}
        {canOperate && (
          <Link href={href("dashboard")}>
            Operations overview <span>Health and follow-ups</span>
          </Link>
        )}
      </div>
    </section>
  );
}
