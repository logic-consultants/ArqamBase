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

    /// <summary>The period as "Oct 2026" when it is written like "OCT/26" or "NOV-26"; otherwise as saved.</summary>
    public string PeriodText
    {
        get
        {
            var p = Period?.Trim() ?? "";
            var parts = p.Split('/', '-', ' ');
            if (parts.Length == 2 && parts[0].Length >= 3 && int.TryParse(parts[1], out var year)
                && DateTime.TryParseExact(parts[0][..3], "MMM", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var month))
                return $"{month:MMM} {(year < 100 ? 2000 + year : year)}";
            return p;
        }
    }
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
