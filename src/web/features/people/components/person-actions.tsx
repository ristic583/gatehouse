"use client";

import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { MagicLinkIssuedDto } from "@/lib/api/types";
import { peopleApi } from "../api";
import { formatDateTime, messageOf } from "../labels";

export function RevokeEntitlement({
  entitlementId,
  action,
  onDone,
}: {
  entitlementId: string;
  action: "revoke" | "emergency-revoke";
  onDone: () => unknown;
}) {
  const [reason, setReason] = useState<string | null>(null);
  const revoke = useMutation({
    mutationFn: (why: string) => peopleApi.revokeEntitlement(entitlementId, action, why),
    onSuccess: () => {
      setReason(null);
      return onDone();
    },
  });
  const error = messageOf(revoke.error);
  if (reason === null) {
    return (
      <Button size="sm" variant="outline" onClick={() => setReason("")}>
        Revoke access
      </Button>
    );
  }
  return (
    <form
      className="flex flex-wrap items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        revoke.mutate(reason.trim());
      }}
    >
      <Input aria-label="Revocation reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
      <Button size="sm" type="submit" variant="destructive" disabled={revoke.isPending || reason.trim() === ""}>
        Confirm revoke
      </Button>
      <Button size="sm" type="button" variant="outline" onClick={() => setReason(null)}>
        Cancel
      </Button>
      {error !== undefined ? (
        <p role="alert" className="w-full text-destructive">
          {error}
        </p>
      ) : null}
    </form>
  );
}

export function TerminateAction({ personId, onDone }: { personId: string; onDone: () => unknown }) {
  const [confirming, setConfirming] = useState(false);
  const terminate = useMutation({ mutationFn: () => peopleApi.terminate(personId), onSuccess: onDone });
  const error = messageOf(terminate.error);
  return (
    <section className="flex flex-col gap-2">
      {confirming ? (
        <span role="alertdialog" aria-label="Terminate" className="flex flex-wrap items-center gap-2">
          Terminate this employee now? All access and cards are revoked in every PACS.
          <Button size="sm" variant="destructive" disabled={terminate.isPending} onClick={() => terminate.mutate()}>
            Yes, terminate
          </Button>
          <Button size="sm" variant="outline" onClick={() => setConfirming(false)}>
            Cancel
          </Button>
        </span>
      ) : (
        <Button size="sm" variant="outline" className="self-start" onClick={() => setConfirming(true)}>
          Terminate
        </Button>
      )}
      {error !== undefined ? (
        <p role="alert" className="text-destructive">
          {error}
        </p>
      ) : null}
    </section>
  );
}

export function SponsorshipActions({ personId, onDone }: { personId: string; onDone: () => unknown }) {
  const [end, setEnd] = useState("");
  const [confirming, setConfirming] = useState(false);
  const extend = useMutation({ mutationFn: (iso: string) => peopleApi.extend(personId, iso), onSuccess: onDone });
  const endSponsorship = useMutation({ mutationFn: () => peopleApi.endSponsorship(personId), onSuccess: onDone });
  const error = messageOf(extend.error ?? endSponsorship.error);

  return (
    <section className="flex flex-col gap-2">
      <form
        className="flex flex-wrap items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          extend.mutate(new Date(`${end}T00:00:00`).toISOString());
        }}
      >
        <Input
          aria-label="New end date"
          type="date"
          required
          className="max-w-[12rem]"
          value={end}
          onChange={(e) => setEnd(e.target.value)}
        />
        <Button size="sm" type="submit" disabled={extend.isPending || end === ""}>
          Extend
        </Button>
        {confirming ? (
          <span role="alertdialog" aria-label="End sponsorship" className="flex items-center gap-2">
            End the sponsorship now? Access and cards are revoked.
            <Button
              size="sm"
              type="button"
              variant="destructive"
              disabled={endSponsorship.isPending}
              onClick={() => endSponsorship.mutate()}
            >
              Yes, end it
            </Button>
            <Button size="sm" type="button" variant="outline" onClick={() => setConfirming(false)}>
              Keep
            </Button>
          </span>
        ) : (
          <Button size="sm" type="button" variant="outline" onClick={() => setConfirming(true)}>
            End sponsorship
          </Button>
        )}
      </form>
      {error !== undefined ? (
        <p role="alert" className="text-destructive">
          {error}
        </p>
      ) : null}
    </section>
  );
}

export function MagicLinkSection({ personId }: { personId: string }) {
  const [issued, setIssued] = useState<MagicLinkIssuedDto | null>(null);
  const [copied, setCopied] = useState(false);
  const issue = useMutation({ mutationFn: () => peopleApi.issueMagicLink(personId), onSuccess: (link) => setIssued(link) });
  const error = messageOf(issue.error);

  return (
    <section className="flex flex-col gap-2">
      <h3 className="font-semibold">Guest sign-in</h3>
      {issued === null ? (
        <Button size="sm" className="self-start" disabled={issue.isPending} onClick={() => issue.mutate()}>
          Create magic link
        </Button>
      ) : (
        <div data-testid="magic-link" className="flex flex-col gap-1">
          <p>This link is shown once. It works one time until {formatDateTime(issued.expiresAt)}.</p>
          <div className="flex items-center gap-2">
            <Input readOnly aria-label="Magic link" value={issued.url} />
            <Button
              size="sm"
              onClick={() => {
                void navigator.clipboard?.writeText(issued.url);
                setCopied(true);
              }}
            >
              {copied ? "Copied" : "Copy"}
            </Button>
            <Button size="sm" variant="outline" onClick={() => setIssued(null)}>
              Done
            </Button>
          </div>
        </div>
      )}
      {error !== undefined ? (
        <p role="alert" className="text-destructive">
          {error}
        </p>
      ) : null}
    </section>
  );
}
