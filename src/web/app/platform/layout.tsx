import { connection } from "next/server";
import { Suspense } from "react";
import { PlatformBar } from "@/features/platform/components/platform-bar";
import { getCurrentPlatformSession } from "@/lib/auth/current-session";

async function SignedInBar() {
  await connection();
  const session = await getCurrentPlatformSession();
  return <PlatformBar operatorName={session?.data.name ?? null} />;
}

/** Operator panel shell: same tokens as the tenant workspace, but always labelled "Platform" (ADR-009). */
export default function PlatformLayout({ children }: LayoutProps<"/platform">) {
  return (
    <div className="gh-platform flex flex-1 flex-col" data-testid="platform-shell">
      <Suspense fallback={null}>
        <SignedInBar />
      </Suspense>
      {children}
    </div>
  );
}
