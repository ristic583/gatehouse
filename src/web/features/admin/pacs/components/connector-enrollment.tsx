"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { ConnectorEnrollmentDto } from "@/lib/api/types";
import { installSnippet } from "../api";

function copy(text: string) {
  void navigator.clipboard?.writeText(text);
}

/** One-time view of the enrollment token: it lives only in this component's props and is gone once closed. */
export function EnrollmentDialog({ enrollment, onClose }: { enrollment: ConnectorEnrollmentDto; onClose: () => void }) {
  const [stored, setStored] = useState(false);
  const snippet = installSnippet(enrollment);
  const caUrl = `data:application/x-pem-file;charset=utf-8,${encodeURIComponent(enrollment.caCertificatePem)}`;
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Connector enrollment"
      data-testid="enrollment-dialog"
      className="gh-panel flex flex-col gap-3"
    >
      <h3 className="font-medium">Connector registered</h3>
      <p className="text-sm">This enrollment token is shown only once. Store it now; it cannot be shown again.</p>
      <div className="flex items-center gap-2">
        <code className="flex-1 break-all rounded bg-muted px-2 py-1 text-xs" data-testid="enrollment-token">
          {enrollment.enrollmentToken}
        </code>
        <Button type="button" size="sm" variant="outline" onClick={() => copy(enrollment.enrollmentToken)}>
          Copy token
        </Button>
      </div>
      <p className="text-xs text-muted-foreground" data-testid="enrollment-expires">
        Expires {new Date(enrollment.expiresAt).toLocaleString()}
      </p>
      <span className="text-sm">Install the agent next to your PACS:</span>
      <pre className="overflow-x-auto rounded bg-muted p-2 text-xs" data-testid="install-snippet">
        {snippet}
      </pre>
      <div className="flex flex-wrap gap-2">
        <Button type="button" size="sm" variant="outline" onClick={() => copy(snippet)}>
          Copy command
        </Button>
        <a
          className="inline-flex items-center px-2 text-sm underline"
          href={caUrl}
          download="gatehouse-connector-ca.pem"
          data-testid="ca-download"
        >
          Download CA certificate
        </a>
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={stored} onChange={(e) => setStored(e.target.checked)} /> I have stored it
      </label>
      <Button type="button" disabled={!stored} onClick={onClose}>
        Close
      </Button>
    </div>
  );
}
