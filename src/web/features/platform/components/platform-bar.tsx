import Link from "next/link";
import { ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { AppTitle } from "@/features/shell/components/app-title";

export function PlatformBar({ operatorName }: { operatorName: string | null }) {
  return (
    <header className="flex items-center gap-4 border-b px-6 py-3">
      <Link href="/platform" aria-label="Platform home">
        <AppTitle />
      </Link>
      <span
        className="inline-flex items-center gap-1 rounded-full border px-3 py-1 text-xs font-medium uppercase tracking-wide"
        data-testid="platform-badge-header"
      >
        <ShieldCheck size={14} /> Platform
      </span>
      {operatorName !== null && (
        <nav aria-label="Platform" className="flex flex-1 items-center gap-4 text-sm">
          <Link href="/platform">Tenants</Link>
          <Link href="/platform/tenants/new">New tenant</Link>
          <span className="ml-auto text-muted-foreground" data-testid="platform-operator">
            {operatorName}
          </span>
          <form action="/platform/auth/logout" method="post">
            <Button type="submit" variant="outline">
              Sign out
            </Button>
          </form>
        </nav>
      )}
    </header>
  );
}
