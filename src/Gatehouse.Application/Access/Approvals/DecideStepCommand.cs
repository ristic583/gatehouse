namespace Gatehouse.Application.Access.Approvals;

public sealed record DecideStepCommand(Guid StepId, string? Comment);
