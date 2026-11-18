namespace Gatehouse.Domain.Theming;

/// <summary>A foreground/background pair and its WCAG contrast ratio.</summary>
public sealed record ContrastPair(string Name, string Foreground, string Background, double Ratio);
