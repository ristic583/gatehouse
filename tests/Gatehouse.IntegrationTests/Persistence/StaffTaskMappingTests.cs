using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class StaffTaskMappingTests(PostgresFixture postgres)
{
    private static StaffTask Open(Guid subject) =>
        StaffTask.Open(StaffTaskKind.FulfillmentFailed, null, "Fulfillment", subject, MappingData.Now);

    [Fact]
    public async Task Task_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var task = Open(Guid.NewGuid());
        var person = PersonId.New();
        task.Claim(person, MappingData.Now);
        await using (var db = testDb.NewContext())
        {
            db.StaffTasks.Add(task);
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var loaded = await read.StaffTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        Assert.Equal(StaffTaskStatus.InProgress, loaded.Status);
        Assert.Equal(person, loaded.AssignedToId);
        Assert.Equal(StaffTaskKind.FulfillmentFailed, loaded.Kind);
        Assert.Null(loaded.SiteId);
    }

    [Fact]
    public async Task Second_open_task_for_same_subject_violates_unique_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var subject = Guid.NewGuid();
        db.StaffTasks.Add(Open(subject));
        db.StaffTasks.Add(Open(subject));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Resolved_task_allows_a_new_open_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var subject = Guid.NewGuid();
        var first = Open(subject);
        first.Resolve(null, StaffTaskResolutionAction.AutoResolved, null, MappingData.Now);
        db.StaffTasks.Add(first);
        db.StaffTasks.Add(Open(subject));
        await db.SaveChangesAsync(ct);
        Assert.Equal(2, await db.StaffTasks.CountAsync(t => t.SubjectId == subject, ct));
    }
}
