import Link from "next/link";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { ApiError, apiGet, NotSignedInError } from "@/lib/api/server-fetch";
import type { MeDto } from "@/lib/api/types";

const ADMIN_SECTIONS = [
  { href: "/admin/identity", label: "Identity" },
  { href: "/admin/people", label: "People & roles" },
  { href: "/admin/sites", label: "Sites" },
  { href: "/admin/pacs", label: "PACS & connectors" },
  { href: "/onboarding", label: "Onboarding" },
] as const;

/** TenantAdmin only; everyone else goes to `/` (the API enforces `Policies.TenantAdmin` regardless). */
async function AdminGate({ children }: { children: React.ReactNode }) {
  await connection();
  let me: MeDto;
  try {
    me = await apiGet<MeDto>("/v1/me");
  } catch (error) {
    if (error instanceof NotSignedInError) redirect("/login?returnTo=/admin/identity");
    if (error instanceof ApiError) redirect("/");
    throw error;
  }
  if (!me.roles.includes("TenantAdmin")) redirect("/");
  return <>{children}</>;
}

export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return (
    <div className="gh-page">
      <div className="gh-topline">
        <span>
          Workspace <span>/</span> Administration
        </span>
      </div>
      <nav aria-label="Administration" className="mb-4 flex flex-wrap gap-2 text-sm" data-testid="admin-nav">
        {ADMIN_SECTIONS.map((s) => (
          <Link key={s.href} href={s.href} className="rounded-full border px-3 py-1">
            {s.label}
          </Link>
        ))}
      </nav>
      <Suspense fallback={<p className="gh-loading">Loading…</p>}>
        <AdminGate>{children}</AdminGate>
      </Suspense>
    </div>
  );
}
