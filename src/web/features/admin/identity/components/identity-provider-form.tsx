"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { PageState, QueryErrorState } from "@/components/ui/page-state";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/lib/api/client";
import type { IdentityProviderDto, IdpTestResultDto, SaveIdentityProviderRequest } from "@/lib/api/types";
import { identityApi, identityKeys } from "../api";
import { IdpTestResult, IdpValues } from "./idp-values";

type Kind = "Oidc" | "Saml";

export interface IdentityProviderFormProps {
  provider: IdentityProviderDto | null;
  onTest: (request: SaveIdentityProviderRequest) => Promise<IdpTestResultDto>;
  onSave: (request: SaveIdentityProviderRequest) => Promise<unknown>;
  onRemove: () => Promise<unknown>;
}

function errorText(error: unknown): string {
  if (error instanceof ApiError) return error.code !== undefined ? `${error.detail} (${error.code})` : error.detail;
  return "The request failed.";
}

/** IdP setup: a passing Test connection for exactly the current values is required before Save enables. */
export function IdentityProviderForm({ provider, onTest, onSave, onRemove }: IdentityProviderFormProps) {
  const [kind, setKind] = useState<Kind>(provider?.kind === "Saml" ? "Saml" : "Oidc");
  const [displayName, setDisplayName] = useState(provider?.displayName ?? "");
  const [discoveryUrl, setDiscoveryUrl] = useState(provider?.discoveryUrl ?? "");
  const [clientId, setClientId] = useState(provider?.clientId ?? "");
  // Never prefilled: the stored secret stays in Keycloak; empty on update = keep.
  const [clientSecret, setClientSecret] = useState("");
  const [scopes, setScopes] = useState("openid profile email");
  const [samlSource, setSamlSource] = useState<"url" | "xml">("url");
  const [metadataUrl, setMetadataUrl] = useState("");
  const [metadataXml, setMetadataXml] = useState("");
  const [testResult, setTestResult] = useState<IdpTestResultDto | null>(null);
  const [testedKey, setTestedKey] = useState<string | null>(null);
  const [busy, setBusy] = useState<"test" | "save" | "remove" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmRemove, setConfirmRemove] = useState(false);

  const configured = provider !== null && provider.kind !== "Local";

  function request(): SaveIdentityProviderRequest {
    return {
      kind,
      displayName: displayName.trim(),
      oidc:
        kind === "Oidc"
          ? {
              discoveryUrl: discoveryUrl.trim(),
              clientId: clientId.trim(),
              clientSecret: clientSecret === "" ? null : clientSecret,
              scopes: scopes.split(/[\s,]+/).filter((s) => s !== ""),
            }
          : null,
      saml:
        kind === "Saml"
          ? { metadataUrl: samlSource === "url" ? metadataUrl.trim() : null, metadataXml: samlSource === "xml" ? metadataXml : null }
          : null,
    };
  }

  const currentKey = JSON.stringify(request());
  const testPassed = testResult?.ok === true && testedKey === currentKey;

  async function run(action: "test" | "save" | "remove") {
    setBusy(action);
    setError(null);
    try {
      if (action === "test") {
        const body = request();
        const result = await onTest(body);
        setTestResult(result);
        setTestedKey(JSON.stringify(body));
      } else if (action === "save") {
        await onSave(request());
        setClientSecret("");
        setTestResult(null);
        setTestedKey(null);
      } else {
        await onRemove();
        setConfirmRemove(false);
      }
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <section className="gh-panel flex flex-col gap-3" aria-label="Identity provider">
        <div role="radiogroup" aria-label="Protocol" className="flex gap-2">
          {(["Oidc", "Saml"] as const).map((value) => (
            <Button
              key={value}
              type="button"
              role="radio"
              aria-checked={kind === value}
              variant={kind === value ? "default" : "outline"}
              onClick={() => setKind(value)}
            >
              {value === "Oidc" ? "OIDC" : "SAML"}
            </Button>
          ))}
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="idp-display-name">Display name</Label>
          <Input id="idp-display-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Acme Entra ID" />
        </div>
        {kind === "Oidc" ? (
          <div className="grid gap-3 md:grid-cols-2">
            <div className="flex flex-col gap-1 md:col-span-2">
              <Label htmlFor="idp-discovery-url">Discovery URL</Label>
              <Input
                id="idp-discovery-url"
                value={discoveryUrl}
                onChange={(e) => setDiscoveryUrl(e.target.value)}
                placeholder="https://login.example.com/.well-known/openid-configuration"
              />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="idp-client-id">Client id</Label>
              <Input id="idp-client-id" value={clientId} onChange={(e) => setClientId(e.target.value)} />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="idp-client-secret">Client secret</Label>
              <Input
                id="idp-client-secret"
                type="password"
                autoComplete="new-password"
                value={clientSecret}
                onChange={(e) => setClientSecret(e.target.value)}
                placeholder={provider?.hasClientSecret ? "Stored - leave empty to keep" : ""}
              />
            </div>
            <div className="flex flex-col gap-1 md:col-span-2">
              <Label htmlFor="idp-scopes">Scopes</Label>
              <Input id="idp-scopes" value={scopes} onChange={(e) => setScopes(e.target.value)} />
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            <div role="radiogroup" aria-label="Metadata source" className="flex gap-4 text-sm">
              <label className="flex items-center gap-1">
                <input type="radio" name="saml-source" checked={samlSource === "url"} onChange={() => setSamlSource("url")} /> Metadata URL
              </label>
              <label className="flex items-center gap-1">
                <input type="radio" name="saml-source" checked={samlSource === "xml"} onChange={() => setSamlSource("xml")} /> Paste XML
              </label>
            </div>
            {samlSource === "url" ? (
              <div className="flex flex-col gap-1">
                <Label htmlFor="idp-metadata-url">Metadata URL</Label>
                <Input id="idp-metadata-url" value={metadataUrl} onChange={(e) => setMetadataUrl(e.target.value)} />
              </div>
            ) : (
              <div className="flex flex-col gap-1">
                <Label htmlFor="idp-metadata-xml">Metadata XML</Label>
                <Textarea id="idp-metadata-xml" rows={6} value={metadataXml} onChange={(e) => setMetadataXml(e.target.value)} />
              </div>
            )}
          </div>
        )}

        {testResult !== null && <IdpTestResult result={testResult} />}
        {error !== null && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" onClick={() => void run("test")} disabled={busy !== null}>
            {busy === "test" ? "Testing…" : "Test connection"}
          </Button>
          <Button type="button" onClick={() => void run("save")} disabled={!testPassed || busy !== null}>
            {busy === "save" ? "Saving…" : "Save"}
          </Button>
          {configured && (
            <a
              className="inline-flex items-center px-3 text-sm underline"
              href={`/auth/login?login_hint=${encodeURIComponent(`test@${provider.emailDomain}`)}`}
              target="_blank"
              rel="noreferrer"
            >
              Try sign-in
            </a>
          )}
          {configured && !confirmRemove && (
            <Button type="button" variant="outline" onClick={() => setConfirmRemove(true)} disabled={busy !== null}>
              Remove
            </Button>
          )}
        </div>
        {confirmRemove && (
          <div role="alertdialog" aria-label="Remove identity provider" className="flex items-center gap-2 text-sm">
            <span>Remove the identity provider? Users fall back to local sign-in.</span>
            <Button type="button" variant="destructive" onClick={() => void run("remove")} disabled={busy !== null}>
              Remove
            </Button>
            <Button type="button" variant="outline" onClick={() => setConfirmRemove(false)}>
              Cancel
            </Button>
          </div>
        )}
      </section>

      {provider !== null && <IdpValues provider={provider} />}
    </div>
  );
}

export function IdentityProviderPanel() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: identityKeys.provider, queryFn: identityApi.get });
  const refresh = () => queryClient.invalidateQueries({ queryKey: identityKeys.provider });
  const save = useMutation({
    mutationFn: identityApi.save,
    onSuccess: async () => {
      toast.success("Identity provider saved");
      await refresh();
    },
  });
  const remove = useMutation({
    mutationFn: identityApi.remove,
    onSuccess: async () => {
      toast.success("Identity provider removed");
      await refresh();
    },
  });

  if (query.isPending) return <PageState kind="loading" message="Loading identity provider…" />;
  if (query.isError) {
    return <QueryErrorState error={query.error} message="The identity provider could not be loaded." onRetry={() => query.refetch()} />;
  }
  return (
    <IdentityProviderForm
      key={query.data?.savedAt ?? "none"}
      provider={query.data}
      onTest={identityApi.test}
      onSave={(body) => save.mutateAsync(body)}
      onRemove={() => remove.mutateAsync()}
    />
  );
}
