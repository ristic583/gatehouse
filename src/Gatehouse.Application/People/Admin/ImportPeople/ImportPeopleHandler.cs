using System.Text;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Admin.ImportPeople;

/// <summary>
/// <c>POST /api/v1/admin/people/import?dryRun=</c> (<c>domain-model.md</c> §2.10): CSV of authoritative, open-ended employees,
/// matched by email like HR sync; audit <c>people.imported</c> unless dry run.
/// </summary>
public sealed class ImportPeopleHandler(ITenantDb db, PeopleAdminAudit audit, TimeProvider time)
{
    public const int MaxImportBytes = 1024 * 1024;
    public const int MaxImportRows = 5000;
    private static readonly string[] RequiredColumns = ["email", "given_name", "family_name"];

    public static Error TooLarge { get; } = Error.Validation("people_import.too_large", "The file may have at most 1 MiB and 5000 rows.");

    public async Task<Result<PeopleImportResultDto>> HandleAsync(ImportPeopleCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var file = command.File;
        var dryRun = command.DryRun;
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length > MaxImportBytes)
        {
            return TooLarge;
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(file);
        }
        catch (DecoderFallbackException)
        {
            return InvalidFile("The file is not UTF-8.");
        }

        text = text.TrimStart('﻿');
        var rows = CsvReader.Read(text);
        if (rows.Count == 0)
        {
            return InvalidFile("The file is empty.");
        }

        var header = rows[0].Cells.Select(c => c.Trim().ToLowerInvariant()).ToList();
        if (RequiredColumns.Any(c => !header.Contains(c)))
        {
            return InvalidFile("The header must contain email, given_name and family_name.");
        }

        if (rows.Count - 1 > MaxImportRows)
        {
            return TooLarge;
        }

        int Col(string name) => header.IndexOf(name);
        var errors = new List<PeopleImportErrorDto>();
        var records = new List<(int Line, string Email, string Given, string Family, string? Manager)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, cells) in rows.Skip(1))
        {
            string Cell(string name) => Col(name) is var i and >= 0 && i < cells.Count ? cells[i].Trim() : string.Empty;
            var formula = header.Select((name, i) => (name, value: i < cells.Count ? cells[i].TrimStart() : string.Empty))
                .FirstOrDefault(c => c.value.Length > 0 && "=+-@".Contains(c.value[0], StringComparison.Ordinal));
            if (formula.name is not null)
            {
                errors.Add(new(line, "people_import.invalid_file", formula.name));
                continue;
            }

            var email = Cell("email").ToLowerInvariant();
            if (email.Length is 0 or > 254 || !email.Contains('@', StringComparison.Ordinal))
            {
                errors.Add(new(line, "person.invalid_email", "email"));
                continue;
            }

            if (!seen.Add(email))
            {
                errors.Add(new(line, "person.email_taken", "email"));
                continue;
            }

            var manager = Col("manager_email") >= 0 && Cell("manager_email").Length > 0 ? Cell("manager_email").ToLowerInvariant() : null;
            records.Add((line, email, Cell("given_name"), Cell("family_name"), manager));
        }

        var emails = records.Select(r => r.Email).Concat(records.Select(r => r.Manager).OfType<string>()).Distinct().ToList();
        var people = (await db.Persons.Where(p => emails.Contains(p.Email)).ToListAsync(ct))
            .ToDictionary(p => p.Email, StringComparer.Ordinal);
        var now = time.GetUtcNow();
        var zone = await PeopleAdminDefaults.TimeZoneAsync(db, ct);
        int created = 0, updated = 0, unchanged = 0;
        var pending = new List<(int Line, Person Person, string Given, string Family, string? Manager, bool IsNew)>();
        foreach (var r in records)
        {
            if (r.Manager is not null && !people.ContainsKey(r.Manager) && records.All(x => x.Email != r.Manager))
            {
                errors.Add(new(r.Line, "person.not_found", "manager_email"));
                continue;
            }

            if (people.TryGetValue(r.Email, out var existing))
            {
                if (existing.Source != PersonSource.Authoritative || existing.Status == PersonStatus.Terminated
                    || existing.AnonymizedAt is not null)
                {
                    errors.Add(new(r.Line, "person.email_taken", "email"));
                    continue;
                }

                pending.Add((r.Line, existing, r.Given, r.Family, r.Manager, false));
                continue;
            }

            var eligibility = Eligibility.Create(now, null, zone);
            var person = Person.Create(
                PersonSource.Authoritative, PersonType.Employee, r.Given, r.Family, r.Email, eligibility.Value, null, null, now);
            if (person.IsFailure)
            {
                errors.Add(new(r.Line, person.Error.Code, person.Error.Code == "person.invalid_name" ? "given_name" : null));
                continue;
            }

            people[r.Email] = person.Value;
            pending.Add((r.Line, person.Value, r.Given, r.Family, r.Manager, true));
        }

        // Managers resolve after all rows, so a manager may appear later in the file.
        foreach (var (line, person, given, family, manager, isNew) in pending)
        {
            var managerId = manager is not null && people.TryGetValue(manager, out var m) ? m.Id : (PersonId?)null;
            if (isNew)
            {
                if (managerId is not null && person.SetManager(managerId).IsFailure)
                {
                    errors.Add(new(line, "person.self_reference", "manager_email"));
                    continue;
                }

                if (!dryRun)
                {
                    db.Persons.Add(person);
                }

                created++;
                continue;
            }

            var changed = person.UpdateFromHr(given, family, managerId ?? person.ManagerId, person.Eligibility, now);
            if (changed.IsFailure)
            {
                errors.Add(new(line, changed.Error.Code, null));
            }
            else if (changed.Value)
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        if (!dryRun)
        {
            await audit.RecordAsync("people.imported", "Tenant", "people", new { created, updated, unchanged, errors = errors.Count }, ct);
            await db.SaveChangesAsync(ct);
        }

        return new PeopleImportResultDto(dryRun, created, updated, unchanged, errors.OrderBy(e => e.Line).ToList());
    }

    private static Error InvalidFile(string message) => Error.Validation("people_import.invalid_file", message);
}
