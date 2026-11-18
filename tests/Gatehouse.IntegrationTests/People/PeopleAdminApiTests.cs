using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>authorization.md §11 people/role admin routes, domain-model.md §2.10 first admin and employees.</summary>
public sealed class PeopleAdminApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Employees = new("/api/v1/admin/people/employees", UriKind.Relative);
    private static readonly Uri Roles = new("/api/v1/admin/role-assignments", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Create_employee_is_authoritative_and_open_ended()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var managerId = await PersonIdAsync(DemoUser.Erik.Email, ct);

        using var response = await freja.PostAsJsonAsync(Employees, new CreateEmployeeRequest("New.Hire@Nordlys.test", "Nina", "Hansen", managerId, "Europe/Copenhagen"), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var person = await PersonAsync("new.hire@nordlys.test", ct);
        Assert.Equal((PersonSource.Authoritative, PersonType.Employee, PersonStatus.Active), (person.Source, person.Type, person.Status));
        Assert.Null(person.Eligibility.End);
        Assert.Equal(managerId, person.ManagerId?.Value);

        using var duplicate = await freja.PostAsJsonAsync(Employees, new CreateEmployeeRequest("new.hire@nordlys.test", "Nina", "Hansen", null, null), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("person.email_taken", await duplicate.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_dry_run_saves_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        const string Csv = "email,given_name,family_name\ndry.one@nordlys.test,Dry,One\ndry.two@nordlys.test,Dry,Two\n";

        using var response = await ImportAsync(freja, Csv, dryRun: true, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PeopleImportResultDto>(ApiJson.Options, ct))!;
        Assert.True(result.DryRun);
        Assert.Equal(2, result.Created);
        Assert.Empty(result.Errors);
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        Assert.False(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AnyAsync(p => p.Email.StartsWith("dry."), ct));
    }

    [Fact]
    public async Task Import_upserts_by_email_and_links_managers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        // The manager appears after the report; the last valid row renames a person this test created first
        // (never a demo user: other classes share the nordlys database).
        using (var seed = await ImportAsync(freja, "email,given_name,family_name\r\nrename.me@nordlys.test,Ren,Larsen\r\n", dryRun: false, ct))
        {
            Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        }

        var csv = "﻿email,given_name,family_name,manager_email\r\n"
            + "report.one@nordlys.test,Rita,Report,boss.one@nordlys.test\r\n"
            + "boss.one@nordlys.test,Bo,Boss,\r\n"
            + "rename.me@nordlys.test,Ren,Larsen-Berg,\r\n"
            + "\"no-at-sign, really\",Q,Q,\r\n";

        using var response = await ImportAsync(freja, csv, dryRun: false, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PeopleImportResultDto>(ApiJson.Options, ct))!;
        Assert.Equal((2, 1), (result.Created, result.Updated));
        var report = await PersonAsync("report.one@nordlys.test", ct);
        var boss = await PersonAsync("boss.one@nordlys.test", ct);
        Assert.Equal(boss.Id, report.ManagerId);
        Assert.Equal("Larsen-Berg", (await PersonAsync("rename.me@nordlys.test", ct)).FamilyName);
        Assert.Contains(result.Errors, e => e.Line == 5 && e.Code == "person.invalid_email");

        using var again = await ImportAsync(freja, csv, dryRun: false, ct);
        var second = (await again.Content.ReadFromJsonAsync<PeopleImportResultDto>(ApiJson.Options, ct))!;
        Assert.Equal((0, 0, 3), (second.Created, second.Updated, second.Unchanged));
    }

    [Fact]
    public async Task Import_rejects_formula_cells()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        const string Csv = "email,given_name,family_name\nevil@nordlys.test,=HYPERLINK(\"http://x\"),Evil\nok.person@nordlys.test,Ok,Person\n";

        using var response = await ImportAsync(freja, Csv, dryRun: false, ct);

        var result = (await response.Content.ReadFromJsonAsync<PeopleImportResultDto>(ApiJson.Options, ct))!;
        var error = Assert.Single(result.Errors);
        Assert.Equal((2, "people_import.invalid_file", "given_name"), (error.Line, error.Code, error.Field));
        Assert.Equal(1, result.Created);
    }

    [Fact]
    public async Task Import_over_limit_is_413()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var rows = new StringBuilder("email,given_name,family_name\n");
        for (var i = 0; i < 5001; i++)
        {
            rows.Append(System.Globalization.CultureInfo.InvariantCulture, $"bulk{i}@nordlys.test,B,{i}\n");
        }

        using var tooManyRows = await ImportAsync(freja, rows.ToString(), dryRun: true, ct);
        using var tooBig = await ImportAsync(freja, "email,given_name,family_name\n" + new string('x', 1024 * 1024), dryRun: true, ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooManyRows.StatusCode);
        Assert.Contains("people_import.too_large", await tooManyRows.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
    }

    [Fact]
    public async Task Assign_security_staff_scoped_to_site()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var anna = await PersonIdAsync(DemoUser.Anna.Email, ct);
        Guid siteId;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            siteId = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Sites.AsNoTracking().Select(s => s.Id).FirstAsync(ct)).Value;
        }

        using var response = await freja.PostAsJsonAsync(Roles, new AssignRoleRequest(anna, AssignedRole.SecurityStaff, siteId), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<RoleAssignmentDto>(ApiJson.Options, ct))!;
        Assert.Equal((siteId, AssignedRole.SecurityStaff), (dto.SiteId!.Value, dto.Role));
        Assert.NotNull(dto.SiteName);
        using var duplicate = await freja.PostAsJsonAsync(Roles, new AssignRoleRequest(anna, AssignedRole.SecurityStaff, siteId), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var scopedAdmin = await freja.PostAsJsonAsync(Roles, new AssignRoleRequest(anna, AssignedRole.TenantAdmin, siteId), ApiJson.Options, ct);
        Assert.Contains("role.tenant_admin_cannot_be_site_scoped", await scopedAdmin.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        var list = (await freja.GetFromJsonAsync<List<RoleAssignmentDto>>(Roles, ApiJson.Options, ct))!;
        Assert.Contains(list, r => r.Id == dto.Id);
        using var remove = await freja.DeleteAsync(new Uri($"/api/v1/admin/role-assignments/{dto.Id}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
    }

    [Fact]
    public async Task Concurrent_removals_cannot_remove_the_last_two_admins()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await _api.CreateTenantScopeAsync("nordlys");
        var setupDb = setup.ServiceProvider.GetRequiredService<TenantDbContext>();
        var admins = await setupDb.RoleAssignments.Where(r => r.Role == AssignedRole.TenantAdmin).OrderBy(r => r.Id).ToListAsync(ct);
        if (admins.Count < 2)
        {
            var other = await setupDb.Persons.FirstAsync(p => p.Type == PersonType.Employee && p.Status == PersonStatus.Active && !admins.Select(a => a.PersonId).Contains(p.Id), ct);
            setupDb.RoleAssignments.Add(RoleAssignment.Create(other.Id, AssignedRole.TenantAdmin, null).Value);
            await setupDb.SaveChangesAsync(ct);
            admins = await setupDb.RoleAssignments.Where(r => r.Role == AssignedRole.TenantAdmin).OrderBy(r => r.Id).ToListAsync(ct);
        }

        setupDb.RoleAssignments.RemoveRange(admins.Skip(2));
        await setupDb.SaveChangesAsync(ct);

        // Both requests passed the "another admin remains" check before either saved.
        await using var first = await _api.CreateTenantScopeAsync("nordlys");
        await using var second = await _api.CreateTenantScopeAsync("nordlys");
        var firstDb = first.ServiceProvider.GetRequiredService<TenantDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<TenantDbContext>();
        firstDb.RoleAssignments.Remove(await firstDb.RoleAssignments.SingleAsync(r => r.Id == admins[0].Id, ct));
        secondDb.RoleAssignments.Remove(await secondDb.RoleAssignments.SingleAsync(r => r.Id == admins[1].Id, ct));

        await firstDb.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<Gatehouse.Application.People.Admin.LastTenantAdminException>(() => secondDb.SaveChangesAsync(ct));

        await using var check = await _api.CreateTenantScopeAsync("nordlys");
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<TenantDbContext>().RoleAssignments.CountAsync(r => r.Role == AssignedRole.TenantAdmin, ct));
    }

    [Fact]
    public async Task Last_tenant_admin_cannot_be_removed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var list = (await freja.GetFromJsonAsync<List<RoleAssignmentDto>>(Roles, ApiJson.Options, ct))!;
        var admins = list.Where(r => r.Role == AssignedRole.TenantAdmin).ToList();
        var frejaId = await PersonIdAsync(DemoUser.Freja.Email, ct);
        foreach (var other in admins.Where(a => a.Person.PersonId != frejaId))
        {
            using var _ = await freja.DeleteAsync(new Uri($"/api/v1/admin/role-assignments/{other.Id}", UriKind.Relative), ct);
        }

        var own = admins.Single(a => a.Person.PersonId == frejaId);
        using var response = await freja.DeleteAsync(new Uri($"/api/v1/admin/role-assignments/{own.Id}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("role.last_tenant_admin", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roles_only_for_employees()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        Guid external;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            external = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking()
                .Where(p => p.Type != PersonType.Employee).Select(p => p.Id).FirstAsync(ct)).Value;
        }

        using var response = await freja.PostAsJsonAsync(Roles, new AssignRoleRequest(external, AssignedRole.SecurityStaff, null), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("role.person_not_employee", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_admin_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var create = await anna.PostAsJsonAsync(Employees, new CreateEmployeeRequest("x@nordlys.test", "X", "Y", null, null), ApiJson.Options, ct);
        using var list = await anna.GetAsync(Roles, ct);
        using var import = await ImportAsync(anna, "email,given_name,family_name\n", dryRun: true, ct);

        Assert.All([create, list, import], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, string csv, bool dryRun, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "people.csv");
        return await client.PostAsync(new Uri($"/api/v1/admin/people/import?dryRun={(dryRun ? "true" : "false")}", UriKind.Relative), form, ct);
    }

    private async Task<Guid> PersonIdAsync(string email, CancellationToken ct) => (await PersonAsync(email, ct)).Id.Value;

    private async Task<Person> PersonAsync(string email, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking().SingleAsync(p => p.Email == email, ct);
    }
}
