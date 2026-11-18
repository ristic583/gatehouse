using Gatehouse.Domain.Common;
using Gatehouse.Domain.Theming;

namespace Gatehouse.Application.Theming;

/// <summary>Either the saved theme, a validation error, or the pairs that fail WCAG AA (<c>theme.contrast_failed</c>).</summary>
public sealed record SaveThemeResult(ThemeDto? Theme, Error? Error, IReadOnlyList<ContrastPair> FailingPairs);
