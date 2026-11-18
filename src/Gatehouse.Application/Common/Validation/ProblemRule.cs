using FluentValidation;

namespace Gatehouse.Application.Common.Validation;

/// <summary>
/// A rule that fails as one problem (<c>code</c> + title, no <c>errors</c>), the same 400 a handler or Domain
/// <c>Error.Validation</c> returns. The first failed rule wins, so rules are declared in the order the checks ran before.
/// </summary>
public static class ProblemRule
{
    /// <summary>Marks a failure as a single problem; read by the API validation filter.</summary>
    public static object State { get; } = new();

    public static IRuleBuilderOptions<T, TProperty> WithProblem<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule, string code, string message) =>
        rule.WithErrorCode(code).WithMessage(message).WithState(_ => State);
}
