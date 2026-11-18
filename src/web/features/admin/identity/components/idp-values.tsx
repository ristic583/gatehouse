"use client";

import { Button } from "@/components/ui/button";
import type { IdentityProviderDto, IdpTestResultDto } from "@/lib/api/types";

function CopyValue({ label, value, testId }: { label: string; value: string; testId: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <div className="flex items-center gap-2">
        <code className="flex-1 break-all rounded bg-muted px-2 py-1 text-xs" data-testid={testId}>
          {value}
        </code>
        <Button type="button" variant="outline" size="sm" onClick={() => void navigator.clipboard?.writeText(value)}>
          Copy
        </Button>
      </div>
    </div>
  );
}

/** Values the tenant copies into its IdP (redirect URI, SP entity id) and the last test outcome. */
export function IdpValues({ provider }: { provider: IdentityProviderDto }) {
  return (
    <section className="gh-panel flex flex-col gap-3" aria-label="Values for your IdP" data-testid="idp-values">
      <h3 className="gh-panel-title">Values for your IdP</h3>
      <CopyValue label="Redirect URI" value={provider.brokerRedirectUri} testId="idp-broker-redirect-uri" />
      {provider.spEntityId !== null && <CopyValue label="SP entity id" value={provider.spEntityId} testId="idp-sp-entity-id" />}
      <p className="text-xs text-muted-foreground">
        Sign-in domain: @{provider.emailDomain}
        {provider.lastTestedAt !== null &&
          ` · last test ${new Date(provider.lastTestedAt).toLocaleString()} ${provider.lastTestOk ? "passed" : "failed"}`}
      </p>
    </section>
  );
}

/** Per-check outcome of the last Test connection. */
export function IdpTestResult({ result }: { result: IdpTestResultDto }) {
  return (
    <ul data-testid="idp-test-result" data-ok={result.ok} className="flex flex-col gap-1 text-sm">
      {result.checks.map((check) => (
        <li key={check.name} data-testid="idp-test-check" data-ok={check.ok}>
          {check.ok ? "✓" : "✕"} {check.name}
          {check.code !== null && ` - ${check.code}`}
          {check.detail !== null && `: ${check.detail}`}
        </li>
      ))}
    </ul>
  );
}
