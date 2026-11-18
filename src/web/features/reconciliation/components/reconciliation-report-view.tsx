"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { FindingValues, ReconciliationCoverageDto, ReconciliationFindingDto } from "@/lib/api/types";
import { RUNNING_REFETCH_MS, RUNNING_STATUSES, reconciliationApi, reconciliationKeys } from "../api";
import { FINDING_KIND_LABELS, reportStatusLabel } from "../labels";

function formatDateTime(iso: string | null): string {
  return iso === null ? "–" : new Date(iso).toLocaleString();
}

function formatValues(values: FindingValues | null): string {
  if (values === null) {
    return "–";
  }
  const parts = [
    values.cardNumberMasked === null ? null : `card ${values.cardNumberMasked}`,
    values.validFrom === null ? null : `from ${formatDateTime(values.validFrom)}`,
    values.validTo === null ? null : `to ${formatDateTime(values.validTo)}`,
  ].filter((p) => p !== null);
  return parts.length === 0 ? "–" : parts.join(", ");
}

export function ReconciliationReportView({ reportId }: { reportId: string }) {
  const query = useQuery({
    queryKey: reconciliationKeys.report(reportId),
    queryFn: () => reconciliationApi.report(reportId),
    refetchInterval: (q) => (q.state.data !== undefined && RUNNING_STATUSES.has(q.state.data.summary.status) ? RUNNING_REFETCH_MS : false),
  });

  if (query.data === undefined) {
    return query.isError ? <p role="alert">Could not load the reconciliation report.</p> : <p>Loading…</p>;
  }

  const { summary, coverage, findings } = query.data;
  const completed = summary.status === "Completed";

  return (
    <Card data-testid="recon-report">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          {summary.pacsInstanceName}
          <Badge variant={summary.status === "Failed" ? "destructive" : "secondary"}>{reportStatusLabel(summary.status)}</Badge>
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 text-sm">
        <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1">
          <dt>Requested</dt>
          <dd>
            {formatDateTime(summary.requestedAt)} · {summary.requestedBy?.displayName ?? "scheduled"}
          </dd>
          <dt>Captured at</dt>
          <dd data-testid="recon-captured-at">{formatDateTime(summary.capturedAt)}</dd>
          <dt>Compared at</dt>
          <dd data-testid="recon-compared-at">{formatDateTime(summary.comparedAt)}</dd>
        </dl>
        {summary.status === "Failed" ? (
          <p role="alert">
            {query.data.failureCode ?? "reconciliation.failed"}: {query.data.failureMessage ?? "The reconciliation failed."}
          </p>
        ) : null}
        {coverage !== null ? <CoverageBlock coverage={coverage} completed={completed} /> : null}
        {completed ? (
          findings.length === 0 ? (
            <p>No findings: every compared record matches.</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {findings.map((finding) => (
                <FindingItem key={finding.findingId} finding={finding} />
              ))}
            </ul>
          )
        ) : null}
      </CardContent>
    </Card>
  );
}

function CoverageBlock({ coverage, completed }: { coverage: ReconciliationCoverageDto; completed: boolean }) {
  return (
    <section data-testid="recon-coverage" className="flex flex-col gap-1">
      <h3 className="font-medium">Coverage</h3>
      <p>Sites: {coverage.sites.map((s) => s.name).join(", ") || "–"}</p>
      <p>Areas: {coverage.areas.map((a) => a.name).join(", ") || "–"}</p>
      <p>Record types: {coverage.recordTypes.join(", ") || "–"}</p>
      <p>
        {coverage.comparedRecordCount} of {coverage.exportedRecordCount} exported records compared
      </p>
      {completed ? (
        <p data-testid="recon-in-sync">
          {coverage.inSyncCount} of {coverage.comparedRecordCount} compared records in sync
        </p>
      ) : null}
      <p>{coverage.skippedInFlightCount} skipped (change in flight)</p>
      <p>{coverage.ignoredInactiveCount} ignored (inactive in PACS)</p>
    </section>
  );
}

function FindingItem({ finding }: { finding: ReconciliationFindingDto }) {
  return (
    <li data-testid="recon-finding" data-kind={finding.kind} className="flex flex-col gap-1 rounded-md border p-3">
      <div className="flex items-center gap-2">
        <Badge variant="outline">{FINDING_KIND_LABELS[finding.kind] ?? finding.kind}</Badge>
        <span>{finding.person?.displayName ?? finding.personRef ?? "Unknown person"}</span>
        <span className="text-muted-foreground">
          {finding.areaName ?? "Unmapped area"} · {finding.vendorAccessLevelRef}
        </span>
      </div>
      {finding.differences.length > 0 ? <p>Differences: {finding.differences.join(", ")}</p> : null}
      {finding.expected !== null || finding.actual !== null ? (
        <p>
          Expected: {formatValues(finding.expected)} · Actual: {formatValues(finding.actual)}
        </p>
      ) : null}
      <div className="flex items-center gap-2">
        <Badge variant="secondary">{finding.taskStatus}</Badge>
        {finding.resolutionAction !== null ? <span>{finding.resolutionAction}</span> : null}
        <Link className="underline" href={`/console?tab=tasks&item=${finding.staffTaskId}`}>
          Open task
        </Link>
      </div>
    </li>
  );
}
