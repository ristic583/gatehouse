using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Credentials.RevealPin;

/// <summary>Response of <c>POST /api/v1/tasks/{taskId}/pin-reveal</c>; never logged.</summary>
public sealed record RevealedPinDto([property: Sensitive] string Pin, int MaskAfterSeconds);
