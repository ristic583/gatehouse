namespace Gatehouse.Application.Common.Validation;

/// <summary>A validator whose field errors are reported under this <c>code</c> instead of <c>validation.failed</c>.</summary>
public interface IValidationProblemCode
{
    public string ProblemCode { get; }
}
