import type { ReactNode } from "react";
import { ArrowUpRight, ShieldCheck } from "lucide-react";
import Link from "next/link";

export function PageHeading({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow: string;
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <>
      <div className="gh-topline">
        <span>
          Workspace <span>/</span> {eyebrow}
        </span>
        <span>
          <ShieldCheck size={14} /> Governed access
        </span>
      </div>
      <div className="gh-page-heading">
        <div>
          <p className="gh-eyebrow">{eyebrow}</p>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
        {action}
      </div>
    </>
  );
}
export function NewRequestLink({ href = "/map" }: { href?: string }) {
  return (
    <Link href={href} className="gh-primary-link">
      Request access <ArrowUpRight size={17} />
    </Link>
  );
}
