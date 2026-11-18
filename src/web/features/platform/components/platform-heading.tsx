import type { ReactNode } from "react";

/** Page heading of the operator panel; the topline says "Platform" so it is never mistaken for a tenant workspace. */
export function PlatformHeading({
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
          Platform <span>/</span> {eyebrow}
        </span>
        <span data-testid="platform-badge">Platform operator</span>
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
