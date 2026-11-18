import { connection } from "next/server";
import { Suspense } from "react";
import { Button } from "@/components/ui/button";

interface GuestPageProps {
  searchParams: Promise<{ token?: string; error?: string }>;
}

/** Magic link landing: a button posts the token, so link scanners that only GET the page never consume it. */
async function GuestSignIn({ searchParams }: GuestPageProps) {
  await connection();
  const { token, error } = await searchParams;
  return (
    <div className="flex max-w-md flex-col gap-4">
      {error === "invalid" ? (
        <p role="alert" className="text-destructive">
          This link is invalid or has expired.
        </p>
      ) : null}
      {typeof token === "string" && token !== "" ? (
        <form method="post" action="/auth/guest" className="flex flex-col gap-3">
          <p>You were invited as a guest. The session lasts 30 minutes.</p>
          <input type="hidden" name="token" value={token} />
          <Button type="submit" className="self-start">
            Continue as guest
          </Button>
        </form>
      ) : error === "invalid" ? (
        <p>Ask your host for a new link.</p>
      ) : (
        <p>Open the link your host sent you.</p>
      )}
    </div>
  );
}

export default function GuestPage(props: GuestPageProps) {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">Guest sign-in</h1>
      <Suspense fallback={<p>Loading…</p>}>
        <GuestSignIn {...props} />
      </Suspense>
    </div>
  );
}
