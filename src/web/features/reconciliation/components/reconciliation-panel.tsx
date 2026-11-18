"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ApiError } from "@/lib/api/client";
import type { ReconciliationInstanceDto, ReconciliationReportSummaryDto } from "@/lib/api/types";
import { RUNNING_REFETCH_MS, RUNNING_STATUSES, reconciliationApi, reconciliationKeys } from "../api";
import { blockedReasonLabel, reportStatusLabel } from "../labels";

export interface ReconciliationPanelProps {
  onSelect: (reportId: string) => void;
}

const anyRunning = (reports: (ReconciliationReportSummaryDto | null)[] | undefined) =>
  reports?.some((r) => r !== null && RUNNING_STATUSES.has(r.status)) ?? false;

export function ReconciliationPanel({ onSelect }: ReconciliationPanelProps) {
  const instances = useQuery({
    queryKey: reconciliationKeys.instances(),
    queryFn: reconciliationApi.instances,
    refetchInterval: (query) => (anyRunning(query.state.data?.map((i) => i.lastReport)) ? RUNNING_REFETCH_MS : false),
  });
  const reports = useQuery({
    queryKey: reconciliationKeys.reports(),
    queryFn: () => reconciliationApi.reports(),
    refetchInterval: (query) => (anyRunning(query.state.data) ? RUNNING_REFETCH_MS : false),
  });

  return (
    <div className="flex flex-col gap-6">
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">PACS instances</h2>
        {instances.isError ? <p role="alert">Could not load the PACS instances.</p> : null}
        {instances.data === undefined ? (
          instances.isError ? null : (
            <p>Loading…</p>
          )
        ) : instances.data.length === 0 ? (
          <p>No PACS instances to reconcile.</p>
        ) : (
          <div className="grid gap-3 md:grid-cols-2">
            {instances.data.map((instance) => (
              <InstanceCard key={instance.pacsInstanceId} instance={instance} />
            ))}
          </div>
        )}
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">Reports</h2>
        {reports.isError ? <p role="alert">Could not load the reconciliation reports.</p> : null}
        {reports.data === undefined ? (
          reports.isError ? null : (
            <p>Loading…</p>
          )
        ) : reports.data.length === 0 ? (
          <p>No reconciliation has run yet.</p>
        ) : (
          <table className="w-full text-left text-sm">
            <thead>
              <tr>
                <th>PACS</th>
                <th>Status</th>
                <th>Requested</th>
                <th>Findings</th>
              </tr>
            </thead>
            <tbody>
              {reports.data.map((report) => (
                <tr
                  key={report.reportId}
                  data-testid="recon-report-row"
                  className="cursor-pointer border-t hover:bg-muted"
                  onClick={() => onSelect(report.reportId)}
                >
                  <td>{report.pacsInstanceName}</td>
                  <td>
                    <Badge variant={report.status === "Failed" ? "destructive" : "secondary"}>{reportStatusLabel(report.status)}</Badge>
                  </td>
                  <td>
                    {new Date(report.requestedAt).toLocaleString()}
                    {report.requestedBy === null ? " · scheduled" : ` · ${report.requestedBy.displayName}`}
                  </td>
                  <td>{report.findingCount ?? "–"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function InstanceCard({ instance }: { instance: ReconciliationInstanceDto }) {
  const client = useQueryClient();
  const run = useMutation({
    mutationFn: () => reconciliationApi.run(instance.pacsInstanceId),
    onSuccess: () => client.invalidateQueries({ queryKey: reconciliationKeys.all }),
  });
  const runError =
    run.error instanceof ApiError ? blockedReasonLabel(run.error.code) : run.error === null ? null : blockedReasonLabel(null);

  return (
    <Card data-testid="recon-instance">
      <CardHeader>
        <CardTitle>{instance.name}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 text-sm">
        <p>
          {instance.vendor} · {instance.siteNames.join(", ")}
        </p>
        <p>
          Last report:{" "}
          {instance.lastReport === null
            ? "never"
            : `${reportStatusLabel(instance.lastReport.status)} · ${new Date(instance.lastReport.requestedAt).toLocaleString()}`}
        </p>
        {instance.blockedReason !== null ? <p className="text-muted-foreground">{blockedReasonLabel(instance.blockedReason)}</p> : null}
        {instance.canRun ? (
          <Button size="sm" className="self-start" disabled={run.isPending} onClick={() => run.mutate()}>
            Run reconciliation
          </Button>
        ) : null}
        {runError !== null ? <p role="alert">{runError}</p> : null}
      </CardContent>
    </Card>
  );
}
