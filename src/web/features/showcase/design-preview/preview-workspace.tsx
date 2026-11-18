"use client";
import { useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import Link from "next/link";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AppHeader } from "@/features/shell/components/app-header";
import { PageHeading } from "@/components/page-heading";
import { MapView } from "@/features/map/components/map-view";
import { MyRequestsTable } from "@/features/requests/components/my-requests-table";
import { ApprovalInbox } from "@/features/approvals/components/approval-inbox";
import { DashboardView } from "@/features/dashboard/components/dashboard-view";
import type { AccessRequestDto, CreateAccessRequestDto } from "@/lib/api/types";
import {
  createSampleInbox,
  createSampleMaps,
  createSampleRequests,
  sampleDetails,
  sampleMe,
  sampleSites,
} from "@/lib/fixtures/sample-workspace";

const headings: Record<string, [string, string, string]> = {
  map: [
    "SPATIAL COMMAND",
    "Every space. Under control.",
    "Inspect your building. Understand every boundary. Govern access with precision.",
  ],
  requests: ["ACCESS GOVERNANCE", "Every request. One clear view.", "Follow your access from the first request to final confirmation."],
  approvals: ["DECISION CENTER", "Good access starts with you.", "Review the context. Make a confident decision. Keep work moving."],
  dashboard: ["WORKSPACE OVERVIEW", "A connected perspective.", "Your buildings, access operations, and the things that need attention."],
};

/** This fixture transport exists only inside /design-preview in development; it cannot authenticate a user. */
export function PreviewWorkspace() {
  const params = useSearchParams();
  const view = params.get("view") ?? "map";
  const heading = headings[view] ?? headings.map!;
  const [client] = useState(
    () => new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } } }),
  );
  const [ready, setReady] = useState(false);
  useEffect(() => {
    const original = window.fetch;
    const maps = createSampleMaps();
    const requests = createSampleRequests();
    let inbox = createSampleInbox();
    const json = (data: unknown, status = 200) =>
      Promise.resolve(new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } }));
    window.fetch = (input, init) => {
      const url = String(input);
      if (!url.startsWith("/api/proxy/")) return original(input, init);
      const path = url.slice("/api/proxy".length);
      const method = init?.method ?? "GET";
      if (path === "/v1/me") return json(sampleMe);
      if (path === "/v1/theme") return json({ terms: { Area: "Area", Floor: "Floor", Site: "Site", Door: "Door" } });
      if (path === "/v1/sites") return json(sampleSites);
      if (path.includes("/access-map")) return json(maps.find((m) => path.includes(`/floors/${m.floorId}/`)));
      if (path.startsWith("/v1/areas/")) return json(sampleDetails(maps, path.split("/").at(-1) ?? ""));
      if (path === "/v1/access-requests" && method === "GET") return json(requests);
      if (path === "/v1/access-requests" && method === "POST") {
        const body = JSON.parse(String(init?.body)) as CreateAccessRequestDto;
        const details = sampleDetails(maps, body.areaId);
        const map = maps.find((m) => m.floorId === details?.floorId);
        const area = map?.areas.find((a) => a.areaId === body.areaId);
        if (!details || !map || !area) return json({ detail: "Space not found." }, 404);
        if (area.state === "Pending" || area.state === "HasAccess")
          return json({ detail: "You already have an open request or access." }, 409);
        const id = `preview-${requests.length + 1}`;
        const request: AccessRequestDto = {
          entitlementId: id,
          subject: sampleMe.person!,
          requestedBy: sampleMe.person!,
          area: {
            areaId: body.areaId,
            name: details.name,
            siteName: sampleSites.find((s) => s.siteId === map.siteId)?.name ?? "",
            floorName: map.floorName,
          },
          requestedStart: body.requestedStart,
          requestedEnd: body.requestedEnd,
          effectiveEnd: body.requestedEnd,
          cappedToEligibility: false,
          justification: body.justification,
          status: "PendingApproval",
          steps: [
            {
              stepId: `${id}-1`,
              order: 1,
              approverKind: "Manager",
              status: "Pending",
              assignedApprover: { personId: "mikkel", displayName: "Mikkel Holm" },
              decidedBy: null,
              decidedAt: null,
              comment: null,
            },
            ...(details.securityLevel.approverChain === "Both"
              ? [
                  {
                    stepId: `${id}-2`,
                    order: 2,
                    approverKind: "AreaOwner" as const,
                    status: "Waiting" as const,
                    assignedApprover: null,
                    decidedBy: null,
                    decidedAt: null,
                    comment: null,
                  },
                ]
              : []),
          ],
          fulfillmentStatus: null,
          createdAt: new Date().toISOString(),
          revocation: null,
          revocationFulfillmentStatus: null,
        };
        requests.unshift(request);
        area.state = "Pending";
        area.reason = "AwaitingApproval";
        return json(request, 201);
      }
      if (path.endsWith("/cancel") && method === "POST") {
        const request = requests.find((r) => path.includes(`/${r.entitlementId}/`));
        if (request) {
          request.status = "Cancelled";
          request.steps.forEach((s) => {
            if (s.status === "Pending" || s.status === "Waiting") s.status = "Closed";
          });
          const a = maps.flatMap((m) => m.areas).find((a) => a.areaId === request.area.areaId);
          if (a) {
            a.state = "NoAccess";
            a.reason = "NoEntitlement";
          }
        }
        return json(request);
      }
      if (path === "/v1/approvals/inbox") return json(inbox);
      if (path.startsWith("/v1/approvals/") && method === "POST") {
        inbox = inbox.filter((i) => !path.includes(`/${i.stepId}/`));
        return json({});
      }
      if (path === "/v1/dashboard")
        return json({
          pendingApprovals: inbox.length,
          openAlarms: 2,
          openTasksByKind: { Review: 4, PinReveal: 1 },
          openFindingTasks: 3,
          openDriftAlerts: 1,
          unconfirmedFulfillmentsOverOneHour: 0,
          connectors: { online: 2, offline: 1, neverSeen: 0 },
          reconciliation: [
            {
              pacsInstanceId: "pacs",
              name: "HQ Copenhagen",
              comparedAt: new Date().toISOString(),
              inSync: 148,
              comparedRecords: 151,
              findings: 3,
            },
          ],
          generatedAt: new Date().toISOString(),
        });
      return json({ detail: "This operation is not part of the isolated design preview." }, 404);
    };
    const readyTimer = window.setTimeout(() => setReady(true), 0);
    return () => {
      window.clearTimeout(readyTimer);
      window.fetch = original;
      client.clear();
    };
  }, [client]);
  return (
    <QueryClientProvider client={client}>
      <AppHeader tenantName={sampleMe.tenantName} displayName={sampleMe.displayName} roles={sampleMe.roles} preview />
      <div className="gh-preview-main">
        <div className="gh-preview-banner">
          <span>DESIGN PREVIEW · Fictional workspace · Changes reset on refresh</span>
          <Link href="/login">Preview sign-in ↗</Link>
        </div>
        <div className="gh-page">
          <PageHeading eyebrow={heading[0]} title={heading[1]} description={heading[2]} />
          {!ready ? (
            <div className="gh-loading">Preparing your workspace…</div>
          ) : view === "approvals" ? (
            <ApprovalInbox />
          ) : view === "requests" ? (
            <MyRequestsTable />
          ) : view === "dashboard" ? (
            <DashboardView />
          ) : (
            <MapView />
          )}
        </div>
      </div>
    </QueryClientProvider>
  );
}
