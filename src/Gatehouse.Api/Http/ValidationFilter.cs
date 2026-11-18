using FluentValidation;
using FluentValidation.Results;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Domain.Common;

namespace Gatehouse.Api.Http;

/// <summary>
/// conventions.md §3: validates every endpoint argument whose type has a registered <see cref="IValidator{T}"/> before the
/// handler runs. Failures marked by <see cref="ProblemRule"/> become a single problem; others a 400 with <c>errors</c>.
/// </summary>
public static class ValidationFilter
{
    public static RouteGroupBuilder WithRequestValidation(this RouteGroupBuilder group) => group.AddEndpointFilterFactory(Create);

    private static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var validators = context.MethodInfo.GetParameters()
            .Select((p, index) => (Index: index, Validator: ValidatorFor(context.ApplicationServices, p.ParameterType)))
            .Where(v => v.Validator is not null)
            .ToArray();
        if (validators.Length == 0)
        {
            return next;
        }

        return async invocation =>
        {
            foreach (var (index, validator) in validators)
            {
                if (invocation.Arguments[index] is not { } argument)
                {
                    continue;
                }

                var result = await validator!.ValidateAsync(new ValidationContext<object>(argument), invocation.HttpContext.RequestAborted);
                if (!result.IsValid)
                {
                    return ToProblem(result, validator);
                }
            }

            return await next(invocation);
        };
    }

    private static IValidator? ValidatorFor(IServiceProvider services, Type type) =>
        type.IsByRef || type.IsPointer || type.ContainsGenericParameters
            ? null
            : services.GetService(typeof(IValidator<>).MakeGenericType(type)) as IValidator;

    private static IResult ToProblem(ValidationResult result, IValidator validator)
    {
        var first = result.Errors[0];
        if (ReferenceEquals(first.CustomState, ProblemRule.State))
        {
            return Error.Validation(first.ErrorCode, first.ErrorMessage).ToProblem();
        }

        var code = validator is IValidationProblemCode custom ? custom.ProblemCode : ResultExtensions.ValidationFailedCode;
        return result.ToDictionary().ToValidationProblem(code);
    }
}
