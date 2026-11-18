using Gatehouse.Application.Access.Dtos;

namespace Gatehouse.Application.Access.RequestAccess;

public sealed record RequestAccessCommand(CreateAccessRequestDto Request);
