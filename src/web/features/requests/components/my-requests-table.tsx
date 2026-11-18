"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { ArrowUpRight, Search, ShieldCheck } from "lucide-react";
import { EmptyState } from "@/components/flow-steps";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useMe } from "@/lib/api/me";
import type { AccessRequestDto, ApprovalStepDto } from "@/lib/api/types";
import { formatAccessTime } from "../request-window";
import { MY_REQUESTS_KEY, requestsApi } from "../api";

const STEP_LABEL: Record<ApprovalStepDto["approverKind"], string> = { Manager: "Manager", AreaOwner: "Area owner" };
const STEP_MARK: Record<ApprovalStepDto["status"], string> = {
  Approved: "✓",
  Pending: "⏳",
  Waiting: "…",
  Rejected: "✕",
  Closed: "–",
};

/** Status text for a request: approved access is "active" only once every grant is confirmed. */
export function statusText(request: AccessRequestDto, now = new Date()): string {
  switch (request.status) {
    case "PendingApproval":
      return "Awaiting approval";
    case "Approved":
      if (request.fulfillmentStatus === "Confirmed") {
        if (Date.parse(request.effectiveEnd) <= now.getTime()) return "Window ended";
        if (Date.parse(request.requestedStart) > now.getTime()) return "Scheduled";
        return "Active";
      }
      if (request.fulfillmentStatus === "Failed") {
        return "Approved - applying failed";
      }
      return "Approved - not yet applied";
    case "Cancelled":
      return "Cancelled";
    case "Expired":
      // A request whose window ended while pending is expired by the lifecycle run with its open steps closed.
      return request.steps.some((s) => s.status === "Closed") ? "Expired before a decision" : "Expired";
    case "Revoked":
      return request.revocationFulfillmentStatus === null || request.revocationFulfillmentStatus === "Confirmed"
        ? "Revoked"
        : "Revoked - removal pending";
    default:
      return request.status;
  }
}

function formatDate(iso: string): string {
  return formatAccessTime(iso);
}

export function MyRequestsTableView({
  requests,
  currentPersonId = null,
  onCancel,
}: {
  requests: AccessRequestDto[];
  currentPersonId?: string | null;
  onCancel?: (request: AccessRequestDto) => void;
}) {
  if (requests.length === 0) {
    return <EmptyState title="No requests to show." description="Choose another filter or explore spaces to request access." />;
  }
  return (
    <div className="gh-table-wrap">
      <table className="w-full text-left text-sm">
        <thead>
          <tr>
            <th>Area</th>
            <th>Window</th>
            <th>Status</th>
            <th>Approvals</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {requests.map((r) => (
            <tr key={r.entitlementId} data-testid={`request-${r.entitlementId}`} className="border-t">
              <td data-label="Space">
                {r.area.name}
                <div className="text-xs text-muted-foreground">
                  {r.area.siteName} · {r.area.floorName}
                </div>
              </td>
              <td data-label="Access window">
                {formatDate(r.requestedStart)} – {formatDate(r.effectiveEnd)}
                {r.cappedToEligibility ? (
                  <Badge variant="outline" className="ml-2">
                    capped to eligibility
                  </Badge>
                ) : null}
              </td>
              <td data-label="Status">
                <span className="gh-pill" data-status={r.status}>
                  {statusText(r)}
                </span>
              </td>
              <td data-label="Approvals" className="gh-steps-cell">
                {r.steps.map((s) => (
                  <Badge key={s.stepId} variant="outline">
                    {STEP_LABEL[s.approverKind]} {STEP_MARK[s.status]}
                  </Badge>
                ))}
              </td>
              <td>
                {r.status === "PendingApproval" && currentPersonId !== null && r.requestedBy.personId === currentPersonId ? (
                  <Button size="sm" variant="outline" onClick={() => onCancel?.(r)}>
                    Cancel request
                  </Button>
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function MyRequestsTable() {
  const pathname = usePathname();
  const areaFilter = useSearchParams().get("area");
  const query = useQuery({ queryKey: MY_REQUESTS_KEY, queryFn: requestsApi.mine });
  const me = useMe();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("All");
  const [target, setTarget] = useState<AccessRequestDto | null>(null);
  const cancel = useMutation({
    mutationFn: (id: string) => requestsApi.cancel(id),
    onSuccess: async () => {
      toast.success("Request cancelled");
      setTarget(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: MY_REQUESTS_KEY }),
        queryClient.invalidateQueries({ queryKey: ["access-map"] }),
      ]);
    },
    onError: () => toast.error("The request could not be cancelled."),
  });
  if (query.isError) {
    return <p role="alert">Could not load your requests.</p>;
  }
  if (query.data === undefined) {
    return <p>Loading…</p>;
  }
  return (
    <>
      {areaFilter && (
        <div className="gh-filter-notice">
          Showing your requests for {query.data.find((r) => r.area.areaId === areaFilter)?.area.name ?? "the selected space"}.{" "}
          <Link href={pathname === "/design-preview" ? "/design-preview?view=requests" : "/requests"}>Show all requests</Link>
        </div>
      )}
      <div className="gh-filterbar">
        <div className="gh-segmented">
          {["All", "Pending", "Approved", "Closed"].map((f) => (
            <button key={f} aria-pressed={filter === f} onClick={() => setFilter(f)}>
              {f}
            </button>
          ))}
        </div>
        <div className="gh-room-search">
          <Search size={13} />
          <input aria-label="Search requests" placeholder="Search spaces…" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
      </div>
      <MyRequestsTableView
        requests={query.data.filter(
          (r) =>
            (!areaFilter || (r.area.areaId === areaFilter && r.subject.personId === me.data?.person?.personId)) &&
            `${r.area.name} ${r.area.siteName}`.toLowerCase().includes(search.toLowerCase()) &&
            (filter === "All" ||
              (filter === "Pending" && r.status === "PendingApproval") ||
              (filter === "Approved" && r.status === "Approved") ||
              (filter === "Closed" && !["PendingApproval", "Approved"].includes(r.status))),
        )}
        currentPersonId={me.data?.person?.personId ?? null}
        onCancel={setTarget}
      />
      <p className="gh-request-summary">
        <ShieldCheck size={15} /> {"Approval needs access-system confirmation. "}
        {"Check the access window and card status before using a space."}{" "}
        <Link
          href={pathname === "/design-preview" ? "/design-preview?view=map" : "/map"}
          className="ml-auto inline-flex items-center gap-1"
        >
          Explore spaces <ArrowUpRight size={13} />
        </Link>
      </p>
      <Dialog open={target !== null} onOpenChange={(open) => (open ? undefined : setTarget(null))}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Cancel request</DialogTitle>
            <DialogDescription>{target?.area.name}</DialogDescription>
          </DialogHeader>
          <Button variant="destructive" disabled={cancel.isPending} onClick={() => target !== null && cancel.mutate(target.entitlementId)}>
            Confirm cancel
          </Button>
        </DialogContent>
      </Dialog>
    </>
  );
}
