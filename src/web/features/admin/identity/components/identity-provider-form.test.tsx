import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IdentityProviderDto, IdpTestResultDto } from "@/lib/api/types";
import { IdentityProviderForm } from "./identity-provider-form";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const saved: IdentityProviderDto = {
  kind: "Oidc",
  alias: "acme-oidc",
  displayName: "Acme Entra",
  discoveryUrl: "https://login.acme.test/.well-known/openid-configuration",
  issuer: "https://login.acme.test",
  clientId: "gatehouse",
  hasClientSecret: true,
  samlEntityId: null,
  samlSsoUrl: null,
  signingCertificateThumbprint: null,
  emailDomain: "acme.test",
  brokerRedirectUri: "http://localhost:8080/realms/gatehouse/broker/acme-oidc/endpoint",
  spEntityId: null,
  savedAt: "2026-11-14T10:00:00Z",
  lastTestedAt: null,
  lastTestOk: null,
};

const passing: IdpTestResultDto = {
  ok: true,
  checks: [
    { name: "discovery", ok: true, code: null, detail: null },
    { name: "jwks", ok: true, code: null, detail: null },
  ],
};
const failing: IdpTestResultDto = {
  ok: false,
  checks: [{ name: "discovery", ok: false, code: "idp.discovery_failed", detail: "timeout" }],
};

function setup(provider: IdentityProviderDto | null, results: IdpTestResultDto[]) {
  const onTest = vi.fn(() => Promise.resolve(results.shift() ?? passing));
  const onSave = vi.fn(() => Promise.resolve(saved));
  render(<IdentityProviderForm provider={provider} onTest={onTest} onSave={onSave} onRemove={vi.fn(() => Promise.resolve())} />);
  return { onTest, onSave };
}

describe("IdentityProviderForm", () => {
  it("save is disabled until the test passes", async () => {
    const { onTest, onSave } = setup(null, [failing, passing]);
    fireEvent.change(screen.getByLabelText("Display name"), { target: { value: "Acme" } });
    fireEvent.change(screen.getByLabelText("Discovery URL"), {
      target: { value: "https://login.acme.test/.well-known/openid-configuration" },
    });
    fireEvent.change(screen.getByLabelText("Client id"), { target: { value: "gatehouse" } });
    fireEvent.change(screen.getByLabelText("Client secret"), { target: { value: "s3cret" } });
    const save = screen.getByRole("button", { name: "Save" });
    expect(save).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "Test connection" }));
    await waitFor(() => expect(screen.getByTestId("idp-test-result")).toHaveAttribute("data-ok", "false"));
    expect(screen.getAllByTestId("idp-test-check")).toHaveLength(1);
    expect(save).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "Test connection" }));
    await waitFor(() => expect(screen.getByTestId("idp-test-result")).toHaveAttribute("data-ok", "true"));
    expect(save).toBeEnabled();

    // Editing after a passing test requires a new test.
    fireEvent.change(screen.getByLabelText("Client id"), { target: { value: "other" } });
    expect(save).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Client id"), { target: { value: "gatehouse" } });
    fireEvent.click(save);
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onTest).toHaveBeenCalledTimes(2);
    expect(onSave.mock.calls[0]).toEqual([
      expect.objectContaining({ kind: "Oidc", oidc: expect.objectContaining({ clientSecret: "s3cret" }) }),
    ]);
  });

  it("secret is never prefilled", () => {
    setup(saved, []);
    const secret = screen.getByLabelText("Client secret");
    expect(secret).toHaveAttribute("type", "password");
    expect(secret).toHaveValue("");
    expect(screen.getByLabelText("Client id")).toHaveValue("gatehouse");
    expect(screen.getByTestId("idp-broker-redirect-uri")).toHaveTextContent(saved.brokerRedirectUri);
  });
});
