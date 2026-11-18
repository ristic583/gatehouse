namespace Gatehouse.Domain.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Failure,
}
