"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { auditApi } from "../api";

export function AuditPanel() {
  const entries = useQuery({ queryKey: ["audit-entries"], queryFn: auditApi.entries });
  const verify = useMutation({ mutationFn: auditApi.verify });

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-3">
        <Button size="sm" disabled={verify.isPending} onClick={() => verify.mutate()}>
          Verify audit chain
        </Button>
        {verify.data !== undefined ? (
          <p data-testid="audit-status">
            {verify.data.verified
              ? `Audit chain verified ✓ (${verify.data.entryCount} entries)`
              : `Audit chain broken at entry #${verify.data.firstInvalidSequence ?? "?"} ✕`}
          </p>
        ) : null}
        {verify.isError ? <p role="alert">Verification failed.</p> : null}
      </div>
      {entries.isError ? <p role="alert">Could not load audit entries.</p> : null}
      {entries.data === undefined ? (
        entries.isError ? null : (
          <p>Loading…</p>
        )
      ) : (
        <table className="w-full text-left text-sm">
          <thead>
            <tr>
              <th>#</th>
              <th>Time</th>
              <th>Actor</th>
              <th>Action</th>
              <th>Subject</th>
              <th>Hash</th>
            </tr>
          </thead>
          <tbody>
            {entries.data.map((entry) => (
              <tr key={entry.sequence} className="border-t">
                <td>{entry.sequence}</td>
                <td>{new Date(entry.occurredAt).toLocaleString()}</td>
                <td>{entry.actorName ?? entry.actorId}</td>
                <td>{entry.action}</td>
                <td>{entry.subjectType}</td>
                <td className="font-mono">{entry.hash.slice(0, 12)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
