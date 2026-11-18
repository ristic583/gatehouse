"use client";

import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import {
  ArrowUpRight,
  Building2,
  CheckCheck,
  CircleHelp,
  Fingerprint,
  LayoutDashboard,
  Map,
  Radio,
  Rocket,
  Send,
  Settings2,
  ShieldCheck,
  SlidersHorizontal,
  Users,
  type LucideIcon,
} from "lucide-react";
import { AppTitle } from "./app-title";
import { SignOutButton } from "./sign-out-button";

import { ADMIN_HOME, ONBOARDING_HOME, APPROVER_ROLES, OPERATIONS_ROLES, homeForRoles } from "@/features/workspace/navigation";
const PEOPLE_ROLES = ["Employee", "TenantAdmin", "Manager", "Sponsor"];

export interface AppHeaderProps {
  tenantName: string;
  displayName: string;
  roles: readonly string[];
  preview?: boolean;
  /** Remaining onboarding steps while onboarding is pending (tenant admins). */ onboardingRemaining?: number;
}

export function AppHeader({ tenantName, displayName, roles, preview = false, onboardingRemaining }: AppHeaderProps) {
  const pathname = usePathname();
  const params = useSearchParams();
  if (pathname === "/login") return null;
  const active = preview ? `/${params.get("view") ?? "map"}` : pathname?.startsWith("/admin/") === true ? ADMIN_HOME : pathname;
  const canApprove = roles.some((role) => APPROVER_ROLES.includes(role));
  const canUseConsole = roles.some((role) => OPERATIONS_ROLES.includes(role));
  const canSeePeople = roles.some((role) => PEOPLE_ROLES.includes(role));
  const isAdmin = roles.includes("TenantAdmin");
  const link = (href: string) => (preview ? `/design-preview?view=${href.slice(1)}` : href);
  function item(href: string, label: string, Icon: LucideIcon, accessibleName = label) {
    if (preview && !["/map", "/requests", "/approvals", "/dashboard"].includes(href)) return null;
    return (
      <Link
        href={link(href)}
        className={`gh-nav-item ${active === href ? "is-active" : ""}`}
        aria-label={accessibleName}
        aria-current={active === href ? "page" : undefined}
      >
        <Icon size={18} strokeWidth={1.6} />
        <span>{label}</span>
        {active === href && <span className="gh-nav-dot" />}
      </Link>
    );
  }
  return (
    <aside className="gh-sidebar">
      <Link href={link(homeForRoles(roles))} aria-label="Gatehouse home">
        <AppTitle />
      </Link>
      {!preview && (
        <details className="gh-mobile-account">
          <summary>Account · {displayName}</summary>
          <p>{tenantName}</p>
          <Link href="/me">My badge and profile</Link>
          <SignOutButton />
        </details>
      )}
      <div className="gh-organization">
        <span className="gh-org-icon">
          <Building2 size={19} />
        </span>
        <div>
          <strong data-testid="current-tenant">{tenantName}</strong>
          <span>Access workspace</span>
        </div>
        <ShieldCheck size={15} />
      </div>
      <nav aria-label="Main">
        <p className="gh-nav-label">WORKSPACE</p>
        {canUseConsole && item("/dashboard", "Overview", LayoutDashboard, "Dashboard")}
        {canApprove && item("/approvals", "Approvals", CheckCheck)}
        {item("/map", "Spaces", Map, "Map")}
        {item("/requests", "My requests", Send)}
        {item("/me", "My badge", Fingerprint)}
        {canSeePeople && item("/people", "People", Users)}
        {canUseConsole && !preview && (
          <>
            <p className="gh-nav-label gh-nav-section">OPERATIONS</p>
            {item("/live", "Live building", Radio, "Live")}
            {item("/console", "Control center", Settings2, "Console")}
          </>
        )}
        {isAdmin && !preview && (
          <>
            <p className="gh-nav-label gh-nav-section">ADMIN</p>
            {onboardingRemaining !== undefined && item(ONBOARDING_HOME, `Onboarding (${onboardingRemaining} left)`, Rocket, "Onboarding")}
            {item(ADMIN_HOME, "Administration", SlidersHorizontal)}
          </>
        )}
      </nav>
      <div className="gh-sidebar-bottom">
        {!preview && (
          <div className="gh-sidebar-note">
            <span className="gh-note-orbit">
              <ShieldCheck size={24} />
            </span>
            <strong>Access is a decision.</strong>
            <p>Every request has a purpose. Every boundary has an owner.</p>
            <Link href={link("/how-it-works")}>
              How it works <ArrowUpRight size={14} />
            </Link>
          </div>
        )}
        {!preview && (
          <Link className="gh-nav-item gh-help-link" href={link("/how-it-works")}>
            <CircleHelp size={18} /> Workspace guide
          </Link>
        )}
        <div className="gh-profile">
          <span className="gh-avatar">
            {displayName
              .split(" ")
              .map((n) => n[0])
              .slice(0, 2)
              .join("")}
          </span>
          <div>
            <strong>{displayName}</strong>
            <span>{roles.filter((r) => r !== "Employee").join(" · ") || "Employee"}</span>
          </div>
        </div>
        {!preview && <SignOutButton />}
      </div>
    </aside>
  );
}
