namespace Gatehouse.Application.Audit.Dtos;

public sealed record AuditVerificationDto(
    bool Verified,
    long EntryCount,
    long? LastSequence,
    long? FirstInvalidSequence,
    DateTimeOffset VerifiedAt);
