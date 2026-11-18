import { PacsAdmin } from "@/features/admin/pacs/components/pacs-admin";

export default function AdminPacsPage() {
  return (
    <>
      <div className="gh-page-heading">
        <div>
          <p className="gh-eyebrow">PACS &amp; CONNECTORS</p>
          <h1>Access control systems</h1>
          <p>Register each PACS, map areas to vendor access levels and enroll a connector agent.</p>
        </div>
      </div>
      <PacsAdmin />
    </>
  );
}
