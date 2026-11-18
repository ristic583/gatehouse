namespace Gatehouse.Domain.Reconciliation;

/// <summary>
/// A PACS record staged by the connector gateway for a reconciliation (domain-model.md §2.7), deleted with its report.
/// <see cref="CardNumber"/> is the full number: never put it in DTOs, messages, logs or audit.
/// </summary>
public sealed class ReconciliationExportRecord
{
    public ReconciliationExportRecord(
        ReconciliationReportId reportId,
        int chunkIndex,
        int ordinal,
        string recordType,
        string pacsRef,
        string? personRef,
        string? cardNumber,
        string? vendorAccessLevelRef,
        DateTimeOffset? validFrom,
        DateTimeOffset? validTo,
        bool isActive)
    {
        ReportId = reportId;
        ChunkIndex = chunkIndex;
        Ordinal = ordinal;
        RecordType = recordType;
        PacsRef = pacsRef;
        PersonRef = personRef;
        CardNumber = cardNumber;
        VendorAccessLevelRef = vendorAccessLevelRef;
        ValidFrom = validFrom;
        ValidTo = validTo;
        IsActive = isActive;
    }

    // EF Core
    private ReconciliationExportRecord()
    {
        RecordType = string.Empty;
        PacsRef = string.Empty;
    }

    public ReconciliationReportId ReportId { get; private set; }

    public int ChunkIndex { get; private set; }

    public int Ordinal { get; private set; }

    public string RecordType { get; private set; }

    public string PacsRef { get; private set; }

    public string? PersonRef { get; private set; }

    [Common.Sensitive]
    public string? CardNumber { get; private set; }

    public string? VendorAccessLevelRef { get; private set; }

    public DateTimeOffset? ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }

    public bool IsActive { get; private set; }
}
