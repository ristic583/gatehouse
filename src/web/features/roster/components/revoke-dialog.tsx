"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api/client";
import { rosterApi } from "../api";

export interface RevokeTarget {
  entitlementId: string;
  personName: string;
  emergency: boolean;
}

export function RevokeDialog({ target, onClose }: { target: RevokeTarget | null; onClose: () => void }) {
  return (
    <Dialog open={target !== null} onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent>{target !== null ? <RevokeBody key={target.entitlementId} target={target} onClose={onClose} /> : null}</DialogContent>
    </Dialog>
  );
}

function RevokeBody({ target, onClose }: { target: RevokeTarget; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const title = target.emergency ? "Emergency revoke" : "Revoke access";

  const revoke = useMutation({
    mutationFn: () => rosterApi.revoke(target.entitlementId, reason.trim(), target.emergency),
    onSuccess: async () => {
      toast.success(target.emergency ? "Emergency revocation started" : "Access revoked");
      await Promise.all([["area-roster"], ["access-map"], ["fulfillments"]].map((queryKey) => queryClient.invalidateQueries({ queryKey })));
      onClose();
    },
    onError: (e: unknown) => setError(e instanceof ApiError ? e.detail : "The revocation failed."),
  });

  const valid = reason.trim().length >= 1 && reason.length <= 500;
  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    if (valid) {
      revoke.mutate();
    }
  };

  return (
    <>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{target.personName}</DialogDescription>
      </DialogHeader>
      <form onSubmit={onSubmit} className="flex flex-col gap-3" aria-label={title}>
        <label htmlFor="revoke-reason" className="text-sm">
          Reason
        </label>
        <textarea
          id="revoke-reason"
          maxLength={500}
          className="rounded-md border px-2 py-1"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        {error !== null ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
        <Button type="submit" variant={target.emergency ? "destructive" : "default"} disabled={!valid || revoke.isPending}>
          {title}
        </Button>
      </form>
    </>
  );
}
