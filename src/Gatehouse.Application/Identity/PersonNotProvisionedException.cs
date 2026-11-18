namespace Gatehouse.Application.Identity;

/// <summary>Thrown by <see cref="ICurrentUser.RequirePersonIdAsync"/>; endpoints behind the LinkedPerson policy never see it.</summary>
public sealed class PersonNotProvisionedException : InvalidOperationException
{
    public const string Code = "identity.person_not_provisioned";

    public PersonNotProvisionedException()
        : base("The signed-in user has no active person in this tenant.")
    {
    }

    public PersonNotProvisionedException(string message)
        : base(message)
    {
    }

    public PersonNotProvisionedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
