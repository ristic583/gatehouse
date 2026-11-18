using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.UnitTests.Architecture;

/// <summary>
/// conventions.md §2: one handler per use case; a Service is a workflow shared by two or more handlers. §3: request shape is
/// checked by a registered validator.
/// </summary>
public sealed class NamingRuleTests
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Assembly Application = typeof(ITenantDb).Assembly;
    private static readonly Assembly Infrastructure = typeof(TenantDbContext).Assembly;

    [Fact]
    public void No_handlers_classes_outside_allow_list() =>
        AllowList.AssertCovers(AllowList.HandlersClasses, HandlersClasses());

    [Fact]
    public void Services_are_shared_by_two_handlers_or_allow_listed() =>
        AllowList.AssertCovers(AllowList.SingleUseServices, SingleUseServices());

    [Fact]
    public void Write_commands_have_validators() =>
        AllowList.AssertCovers(AllowList.CommandsWithoutValidators, CommandsWithoutValidators());

    /// <summary>
    /// <c>*Command</c> types with a public property other than ids and no registered <see cref="IValidator{T}"/> for the command, for one of
    /// its property types (the request it carries) or from its use-case namespace (the request the endpoint binds).
    /// </summary>
    internal static IReadOnlyCollection<string> CommandsWithoutValidators()
    {
        var services = new ServiceCollection();
        foreach (var add in Application.GetTypes().Where(t => t.IsAbstract && t.IsSealed)
                     .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                     .Where(m => m.Name.StartsWith("Add", StringComparison.Ordinal)
                         && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(IServiceCollection)))
        {
            add.Invoke(null, [services]);
        }

        var validators = services
            .Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(d => (Validated: d.ServiceType.GetGenericArguments()[0], d.ImplementationType?.Namespace))
            .ToList();

        return Named(Application, "Command")
            .Where(c => c.GetProperties(BindingFlags.Public | BindingFlags.Instance) is { Length: > 0 } properties
                && !properties.All(p => IsIdentifier(p.PropertyType)))
            .Where(c => !validators.Any(v => v.Validated == c || v.Namespace == c.Namespace
                || c.GetProperties().Any(p => p.PropertyType == v.Validated)))
            .Select(c => c.FullName!)
            .ToList();
    }

    /// <summary>A route id (<see cref="Guid"/> or a strongly typed <c>*Id</c>) is not request shape: existence is the handler's.</summary>
    private static bool IsIdentifier(Type type) =>
        type == typeof(Guid) || (type.IsValueType && type.Name.EndsWith("Id", StringComparison.Ordinal));

    internal static IReadOnlyCollection<string> HandlersClasses() =>
        Named(Application, "Handlers").Select(t => t.FullName!).ToList();

    /// <summary>Concrete <c>*Service</c> types of Application that fewer than two <c>*Handler</c>/<c>*Runner</c> types take in their constructor.</summary>
    internal static IReadOnlyCollection<string> SingleUseServices()
    {
        var consumers = Named(Application, "Handler").Concat(Named(Application, "Runner")).Concat(Named(Infrastructure, "Runner"))
            .Select(t => t.GetConstructors(Instance).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToHashSet())
            .ToList();

        return Named(Application, "Service")
            .Where(s => !s.IsInterface && !s.IsAbstract)
            .Where(s => consumers.Count(deps => deps.Any(d => d == s || (d.IsInterface && d.IsAssignableFrom(s)
                && d.Namespace?.StartsWith("Gatehouse.", StringComparison.Ordinal) == true))) < 2)
            .Select(s => s.FullName!)
            .ToList();
    }

    private static IEnumerable<Type> Named(Assembly assembly, string suffix) =>
        assembly.GetTypes().Where(t => t.IsClass && t.Name.EndsWith(suffix, StringComparison.Ordinal)
            && !t.IsDefined(typeof(CompilerGeneratedAttribute)) && !t.Name.Contains('<', StringComparison.Ordinal));
}
