import { PeopleAdmin } from "@/features/admin/people/components/people-admin";

export default function AdminPeoplePage() {
  return (
    <>
      <div className="gh-page-heading">
        <div>
          <p className="gh-eyebrow">PEOPLE &amp; ROLES</p>
          <h1>Employees and administrators</h1>
          <p>Add employees one by one or import a CSV, and assign tenant admin or security staff roles.</p>
        </div>
      </div>
      <PeopleAdmin />
    </>
  );
}
