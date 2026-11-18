namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record EnrollRequest(string EnrollmentToken, string CsrPem, string AgentVersion);
