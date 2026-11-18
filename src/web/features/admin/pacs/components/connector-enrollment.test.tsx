import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ConnectorEnrollmentDto, PacsInstanceAdminDto } from "@/lib/api/types";
import { installSnippet } from "../api";
import { ConnectorPanel } from "./connector-panel";

const enrollment: ConnectorEnrollmentDto = {
  connectorId: "c1",
  enrollmentToken: "one-time-secret-token",
  expiresAt: "2026-11-15T10:00:00Z",
  gatewayUrl: "https://gateway.acme.test:5443",
  caCertificatePem: "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----",
  vendor: "CloudPacs",
  agentImage: null,
};

const none: Pick<PacsInstanceAdminDto, "connector" | "health" | "fulfillmentMode"> = {
  connector: null,
  health: null,
  fulfillmentMode: "Manual",
};
const pending: typeof none = {
  ...none,
  connector: {
    connectorId: "c1",
    status: "PendingEnrollment",
    createdAt: "2026-11-14T10:00:00Z",
    enrollmentTokenExpiresAt: "2026-11-15T10:00:00Z",
    certificateNotAfter: null,
    lastSeenAt: null,
  },
};

describe("Connector enrollment", () => {
  it("token is shown once", async () => {
    const onRegister = vi.fn(() => Promise.resolve(enrollment));
    const { rerender } = render(<ConnectorPanel instance={none} onRegister={onRegister} onRevoke={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Register connector" }));
    expect(await screen.findByTestId("enrollment-token")).toHaveTextContent("one-time-secret-token");

    // A refetch (now PendingEnrollment) must not hide or re-fetch the token while the dialog is open...
    rerender(<ConnectorPanel instance={pending} onRegister={onRegister} onRevoke={vi.fn()} />);
    const close = screen.getByRole("button", { name: "Close" });
    expect(close).toBeDisabled();
    fireEvent.click(screen.getByLabelText("I have stored it"));
    fireEvent.click(close);

    // ...and once closed it is gone for good.
    expect(screen.queryByTestId("enrollment-token")).not.toBeInTheDocument();
    expect(screen.queryByText(/one-time-secret-token/)).not.toBeInTheDocument();
    rerender(<ConnectorPanel instance={pending} onRegister={onRegister} onRevoke={vi.fn()} />);
    expect(screen.queryByText(/one-time-secret-token/)).not.toBeInTheDocument();
    expect(screen.getByTestId("connector-status")).toHaveTextContent("Pending enrollment");
    expect(screen.queryByRole("button", { name: "Register connector" })).not.toBeInTheDocument();
    expect(onRegister).toHaveBeenCalledTimes(1);
  });

  it("install snippet contains the gateway url", async () => {
    render(<ConnectorPanel instance={none} onRegister={() => Promise.resolve(enrollment)} onRevoke={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Register connector" }));
    const snippet = await screen.findByTestId("install-snippet");
    expect(snippet).toHaveTextContent("Agent__GatewayUrl=https://gateway.acme.test:5443");
    expect(snippet).toHaveTextContent("Agent__EnrollmentToken=one-time-secret-token");
    expect(snippet).toHaveTextContent("Agent__Vendor=CloudPacs");
    expect(snippet).toHaveTextContent("Agent__CloudPacsApiKey=");
    expect(installSnippet({ ...enrollment, vendor: "LegacyPacs" })).toContain("Agent__LegacyPacsConnectionString=");
    expect(screen.getByTestId("ca-download")).toHaveAttribute("download", "gatehouse-connector-ca.pem");
  });

  it("snippet uses the configured agent image", async () => {
    const image = "ghcr.io/acme/gatehouse-agent:1.2.3";
    render(<ConnectorPanel instance={none} onRegister={() => Promise.resolve({ ...enrollment, agentImage: image })} onRevoke={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Register connector" }));
    const snippet = (await screen.findByTestId("install-snippet")).textContent ?? "";
    expect(snippet.trimEnd().endsWith(image)).toBe(true);
    expect(snippet).not.toContain("docker build");
    expect(snippet).not.toContain("<gatehouse-agent-image>");
  });

  it("snippet shows build instructions when no agent image is configured", () => {
    const snippet = installSnippet(enrollment);
    expect(snippet).toContain("docker build -f deploy/Dockerfile --target agent -t gatehouse-agent:local .");
    expect(snippet.trimEnd().endsWith("gatehouse-agent:local")).toBe(true);
    expect(snippet).not.toContain("<gatehouse-agent-image>");
  });
});
