using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_result_exposes_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_result_throws_on_value()
    {
        var result = Result.Failure<int>(Error.NotFound("x.not_found", "missing"));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Implicit_conversion_from_error_creates_failure()
    {
        var error = Error.Conflict("x.conflict", "conflict");

        Result<string> typed = error;
        Result untyped = error;

        Assert.True(typed.IsFailure);
        Assert.Same(error, typed.Error);
        Assert.True(untyped.IsFailure);
        Assert.Equal(ErrorKind.Conflict, untyped.Error.Kind);
    }
}
