import { IdentityProviderPanel } from "@/features/admin/identity/components/identity-provider-form";

export default function AdminIdentityPage() {
  return (
    <>
      <div className="gh-page-heading">
        <div>
          <p className="gh-eyebrow">IDENTITY</p>
          <h1>Sign-in for your organization</h1>
          <p>Connect your OIDC or SAML identity provider. Test the connection before saving.</p>
        </div>
      </div>
      <IdentityProviderPanel />
    </>
  );
}
