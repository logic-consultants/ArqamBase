namespace ArqamBase.Data;

/// <summary>
/// One fee challan of a student (table Feerecipt): what is charged for a period and, once paid, the receipt.
/// A challan is always paid in full. One receipt number can pay several challans, also of brothers and sisters.
/// </summary>
public class FeeChallan : ISoftDelete
{
    public int Id { get; set; }
    public int? StudentId { get; set; }
    /// <summary>e.g. "OCT/26".</summary>
    public string? Period { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int? TuitionFee { get; set; }
    /// <summary>Exam fee and Annual Fund.</summary>
    public int? ExamsFee { get; set; }
    /// <summary>Registration fee (the old Misc column).</summary>
    public int? RegistrationFee { get; set; }
    public int? AdmissionFee { get; set; }
    public int? ProspectusFee { get; set; }
    public int? Fine { get; set; }
    public int? Other { get; set; }
    public bool Paid { get; set; }
    /// <summary>Date the fee was received (column ReDate).</summary>
    public DateTime? ReceivedOn { get; set; }
    public int? ReceiptNo { get; set; }
    public int? ReceivedAmount { get; set; }
    public int? CollectionTypeId { get; set; }
    public DateTime? DateEdited { get; set; }
    /// <summary>Who received or last changed it (column Description).</summary>
    public string? EditedBy { get; set; }
    public string? DrCr { get; set; }
    public string? Remarks { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }

    /// <summary>Everything charged on the challan.</summary>
    public int Total => (TuitionFee ?? 0) + (ExamsFee ?? 0) + (RegistrationFee ?? 0) + (AdmissionFee ?? 0) + (ProspectusFee ?? 0) + (Fine ?? 0) + (Other ?? 0);
}

/// <summary>
/// What a student is charged (table FeeDefinitation). The periodic fee update copies it onto each month's challan:
/// tuition → Tuition, exams → Exams/AF, misc → Registration, lab → Prospectus, other → Other.
/// A/C (parking) and admission fees are not charged monthly. If a student has more than one, the newest counts.
/// </summary>
public class FeeDefinition : ISoftDelete
{
    public int Id { get; set; }
    public int? StudentId { get; set; }
    public int? DsNo { get; set; }
    public int? TuitionFee { get; set; }
    public int? ExamsFee { get; set; }
    /// <summary>Column MiscFee; charged as Registration.</summary>
    public int? RegistrationFee { get; set; }
    /// <summary>Column LabFee; charged as Prospectus.</summary>
    public int? ProspectusFee { get; set; }
    /// <summary>A/C charges (column ParkingFee).</summary>
    public int? AcFee { get; set; }
    public int? Other { get; set; }
    public int? AdmissionFee { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? RevisionDate { get; set; }
    public int? RevisionNo { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>A monthly fee increase of one student (table tblFeeUpdateList).</summary>
public class FeeIncrease
{
    public int Id { get; set; }
    public int? StudentId { get; set; }
    public decimal? CurrentFee { get; set; }
    public decimal? IncreasedAmount { get; set; }
    public decimal? NewFee { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// The bank or online deposit slip of a receipt (table tblFeeReceiptFiles), for Online Deposit Girls / Boys.
/// A receipt is known by its collection type, receipt number and date; the file is in the FeeReceipts folder of the shared attachments folder.
/// </summary>
public class FeeReceiptFile : ISoftDelete
{
    public int Id { get; set; }
    public int CollectionTypeId { get; set; }
    public int ReceiptNo { get; set; }
    public DateTime ReceivedOn { get; set; }
    public string FilePath { get; set; } = "";
    public string? OriginalFileName { get; set; }
    public int? FileSizeKB { get; set; }
    public string? CreatedBy { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>How a fee was collected (table tblCollectionTypes), e.g. Cash Receipt Girls, Online Deposit Boys.</summary>
public class CollectionType : ISoftDelete
{
    public int Id { get; set; }
    public string? Name { get; set; }
    /// <summary>Short code, e.g. CR, CRB, ODG (column Rem).</summary>
    public string? Code { get; set; }
    public bool? Active { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}
