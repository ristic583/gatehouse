using FluentValidation;

namespace Gatehouse.Application.Access.RequestAccess;

/// <summary>Request shape only; business rules live in <c>AccessRequestPolicy</c>.</summary>
public sealed class RequestAccessValidator : AbstractValidator<RequestAccessCommand>
{
    public RequestAccessValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.AreaId).NotEmpty().When(c => c.Request is not null);
        RuleFor(c => c.Request.Justification).NotNull().When(c => c.Request is not null);
    }
}
