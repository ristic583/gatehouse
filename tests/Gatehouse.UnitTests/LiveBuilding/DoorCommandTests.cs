using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.LiveBuilding.Events;
using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.LiveBuilding;

public sealed class DoorCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, DoorCommandScope.Door, false)]
    [InlineData(0, DoorCommandScope.Door, false)]
    [InlineData(61, DoorCommandScope.Door, false)]
    [InlineData(10, DoorCommandScope.Area, false)]
    [InlineData(1, DoorCommandScope.Door, true)]
    [InlineData(60, DoorCommandScope.Door, true)]
    public void Momentary_unlock_requires_duration_1_to_60(int? duration, DoorCommandScope scope, bool ok)
    {
        var result = DoorCommand.Request(DoorCommandKind.MomentaryUnlock, scope, Guid.NewGuid(), PersonId.New(), "Delivery", duration, 1, Now);
        Assert.Equal(ok, result.IsSuccess);
        if (!ok)
        {
            Assert.Equal("door_command.invalid_duration", result.Error.Code);
        }
    }

    [Fact]
    public void Reason_required()
    {
        var result = DoorCommand.Request(DoorCommandKind.LockdownStart, DoorCommandScope.Floor, Guid.NewGuid(), PersonId.New(), " ", null, 3, Now);
        Assert.Equal("door_command.reason_required", result.Error.Code);

        var ok = DoorCommand.Request(DoorCommandKind.LockdownStart, DoorCommandScope.Floor, Guid.NewGuid(), PersonId.New(), "Intruder", null, 3, Now);
        Assert.IsType<DoorCommandRequested>(Assert.Single(ok.Value.DomainEvents));
        Assert.Equal("door_command.invalid_duration", DoorCommand.Request(DoorCommandKind.LockdownClear, DoorCommandScope.Site, Guid.NewGuid(), PersonId.New(), "x", 5, 3, Now).Error.Code);
    }
}
