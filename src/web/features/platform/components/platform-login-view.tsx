"use client";

import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { LockKeyhole } from "lucide-react";
import { Button } from "@/components/ui/button";

/** Operators sign in on the platform realm (password + OTP); tenant accounts cannot sign in here. */
export function PlatformLoginView() {
  const params = useSearchParams();
  const [submitting, setSubmitting] = useState(false);
  const returnTo = params.get("returnTo") ?? "/platform";
  return (
    <div className="gh-page flex items-center justify-center py-16">
      <section className="gh-panel flex max-w-md flex-col gap-4 text-center">
        <span className="mx-auto">
          <LockKeyhole size={28} strokeWidth={1.5} />
        </span>
        <p className="gh-eyebrow">GATEHOUSE PLATFORM</p>
        <h1 className="text-2xl font-medium">Operator sign-in</h1>
        <p className="text-sm text-muted-foreground">
          For Gatehouse platform operators only. A one-time code from your authenticator app is required.
        </p>
        <form action="/platform/auth/login" method="post" onSubmit={() => setSubmitting(true)}>
          <input type="hidden" name="returnTo" value={returnTo} />
          <Button type="submit" disabled={submitting}>
            {submitting ? "Connecting securely…" : "Sign in as operator"}
          </Button>
        </form>
        {params.get("error") !== null && (
          <p role="alert" className="text-sm text-destructive">
            The platform sign-in service is unavailable. Please try again shortly.
          </p>
        )}
      </section>
    </div>
  );
}
