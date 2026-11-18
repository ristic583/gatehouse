using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Credentials.SetPin;

/// <summary>Body of <c>PUT /api/v1/me/pin</c>; the PIN is never logged.</summary>
public sealed record SetPinRequest([property: Sensitive] string? Pin);
