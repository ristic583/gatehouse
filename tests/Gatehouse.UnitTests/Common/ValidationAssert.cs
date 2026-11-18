using FluentValidation.Results;
using Gatehouse.Application.Common.Validation;

namespace Gatehouse.UnitTests.Common;

internal static class ValidationAssert
{
    /// <summary>The code of the first failure, which must be a single problem (<see cref="ProblemRule"/>).</summary>
    public static string FirstProblemCode(ValidationResult result)
    {
        Assert.False(result.IsValid);
        Assert.Same(ProblemRule.State, result.Errors[0].CustomState);
        return result.Errors[0].ErrorCode;
    }
}
