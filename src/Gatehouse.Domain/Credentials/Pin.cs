using System.Text.RegularExpressions;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Credentials;

/// <summary>A PIN value (4-8 digits). <see cref="ToString"/> is always masked; only <see cref="Reveal"/> returns the digits.</summary>
public sealed partial class Pin
{
    private readonly string _digits;

    private Pin(string digits) => _digits = digits;

    public static Result<Pin> Create(string? digits) =>
        digits is not null && Pattern().IsMatch(digits)
            ? new Pin(digits)
            : Error.Validation("pin.invalid", "A PIN must be 4-8 digits.");

    /// <summary>The plain value; used only by the PIN vault, the reveal handler and the <c>SetPin</c> command builder.</summary>
    public string Reveal() => _digits;

    public override string ToString() => "****";

    [GeneratedRegex("^[0-9]{4,8}$")]
    private static partial Regex Pattern();
}
