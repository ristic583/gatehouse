import { SitesAdmin } from "@/features/admin/sites/components/sites-admin";

export default function AdminSitesPage() {
  return (
    <>
      <div className="gh-page-heading">
        <div>
          <p className="gh-eyebrow">SITES</p>
          <h1>Sites, floors and areas</h1>
          <p>Model your buildings: the map, the live view and the 3D scene all use these plans.</p>
        </div>
      </div>
      <SitesAdmin />
    </>
  );
}
