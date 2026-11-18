"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArchitecturalMark } from "@/components/architectural-mark";
import { ArrowUpRight } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { apiClient } from "@/lib/api/client";
import type { DashboardDto } from "@/lib/api/types";

function Tile({ label, value, href, testId }: { label: string; value: number; href?: string; testId: string }) {
  const body = (
    <>
      <div className="text-3xl font-semibold">{value}</div>
      <div className="text-sm text-muted-foreground">{label}</div>
    </>
  );
  return href === undefined ? (
    <div data-testid={testId} className="rounded-lg border p-4">
      {body}
    </div>
  ) : (
    <Link href={href} data-testid={testId} className="rounded-lg border p-4 hover:bg-muted">
      {body}
    </Link>
  );
}

/** Counts the app already tracks; each tile opens the console view that lists the same items. */
export function DashboardView() {
  const dashboard = useQuery({
    queryKey: ["dashboard"],
    queryFn: () => apiClient.get<DashboardDto>("/v1/dashboard"),
    refetchInterval: 30_000,
  });
  if (dashboard.isError) {
    return <p role="alert">Could not load the dashboard.</p>;
  }
  const d = dashboard.data;
  if (d === undefined) {
    return <p>Loading…</p>;
  }
  const openTasks = Object.values(d.openTasksByKind).reduce((a, b) => a + b, 0);
  return (
    <div className="flex flex-col gap-6">
      <section className="gh-dashboard-hero">
        <div>
          <p className="gh-eyebrow">A WINDOW INTO YOUR WORKSPACE</p>
          <h2>Every building tells a story.</h2>
          <p>See the spaces behind your access decisions. Explore the atlas to understand who can go where.</p>
          <Link href="/map" className="gh-primary-link">
            Explore building atlas <ArrowUpRight size={15} />
          </Link>
        </div>
        <ArchitecturalMark />
      </section>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
        <Tile label="Requests awaiting approval (tenant)" value={d.pendingApprovals} testId="tile-approvals" />
        <Tile label="Open alarms" value={d.openAlarms} href="/live" testId="tile-alarms" />
        <Tile label="Open staff tasks" value={openTasks} href="/console?tab=tasks" testId="tile-tasks" />
        <Tile label="Open reconciliation findings" value={d.openFindingTasks} href="/console?tab=tasks" testId="tile-findings" />
        <Tile label="Live drift alerts" value={d.openDriftAlerts} href="/console?tab=tasks" testId="tile-drift" />
        <Tile
          label="Fulfillments unconfirmed > 1 h"
          value={d.unconfirmedFulfillmentsOverOneHour}
          href="/console?tab=fulfillment"
          testId="tile-fulfillments"
        />
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Connector health</CardTitle>
          </CardHeader>
          <CardContent className="text-sm" data-testid="dashboard-connectors">
            {d.connectors.online} online · {d.connectors.offline} offline · {d.connectors.neverSeen} never seen ·{" "}
            <Link className="text-primary underline" href="/live">
              Live view
            </Link>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Reconciliation coverage</CardTitle>
          </CardHeader>
          <CardContent>
            <table className="w-full text-left text-sm" data-testid="dashboard-coverage">
              <thead>
                <tr>
                  <th>PACS</th>
                  <th>Last compared</th>
                  <th>In sync</th>
                  <th>Findings</th>
                </tr>
              </thead>
              <tbody>
                {d.reconciliation.map((r) => (
                  <tr key={r.pacsInstanceId} className="border-t">
                    <td>
                      <Link className="text-primary underline" href="/console?tab=reconciliation">
                        {r.name}
                      </Link>
                    </td>
                    <td>{r.comparedAt === null ? "never" : new Date(r.comparedAt).toLocaleString()}</td>
                    <td>{r.inSync === null || r.comparedRecords === null ? "–" : `${r.inSync}/${r.comparedRecords}`}</td>
                    <td>{r.findings ?? "–"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </CardContent>
        </Card>
      </div>
      <p className="text-xs text-muted-foreground">
        Generated {new Date(d.generatedAt).toLocaleString()}. Alarms and fulfillments follow your site scope.
      </p>
    </div>
  );
}
