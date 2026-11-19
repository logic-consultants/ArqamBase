namespace ArqamBase.Data;

/// <summary>Assets, Liabilities, Income or Expenses (table acc_AccountTypes). NormalSide is Dr or Cr.</summary>
public class AccountType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalSide { get; set; } = "Dr";
    public int SortOrder { get; set; }
}

/// <summary>A group within a type, e.g. Cash &amp; banks under Assets (table acc_SubTypes).</summary>
public class AccountSubType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? AccountTypeId { get; set; }
}

/// <summary>What kind of account it is: cash and bank accounts are offered first when picking an account for money.</summary>
public enum AccountKind : byte { Ordinary = 0, Cash = 1, Bank = 2 }

/// <summary>One account in the chart of accounts (table acc_Accounts). Numbers and names come from the Access accounts database.</summary>
public class Account
{
    public int Id { get; set; }
    public string AccountNo { get; set; } = "";
    public string Name { get; set; } = "";
    public int AccountTypeId { get; set; }
    public int? SubTypeId { get; set; }
    public AccountKind Kind { get; set; }
    public string? BankAccountNo { get; set; }
    public string? CareOf { get; set; }
    public string? Mobile { get; set; }
    public string? Mobile2 { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A financial year, July to June (table acc_FinancialYears).</summary>
public class FinancialYear
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    /// <summary>Vouchers on or before this day are locked: only an Admin changes them.</summary>
    public DateTime? LockedUpTo { get; set; }
    public bool IsClosed { get; set; }
}

/// <summary>A voucher (table acc_Vouchers): Daily (one cash sheet a day), Bank, Journal or Opening.</summary>
public class Voucher
{
    public int Id { get; set; }
    public int YearId { get; set; }
    public int VoucherNo { get; set; }
    public string VoucherType { get; set; } = "Daily";
    public DateTime VoucherDate { get; set; }
    public int? Folio { get; set; }
    public string? Narration { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<VoucherLine> Lines { get; set; } = [];
}

/// <summary>
/// One line of a voucher (table acc_VoucherLines). Debit is Access's "D" (DepositAmount), Credit its "W" (WithdrawalAmount).
/// A line imported from Access may have no voucher (one such line in 2026-27).
/// </summary>
public class VoucherLine
{
    public int Id { get; set; }
    public int? VoucherId { get; set; }
    public int LineNumber { get; set; }
    public int AccountId { get; set; }
    public string? Description { get; set; }
    public string? BillNo { get; set; }
    public string? ChequeNo { get; set; }
    public DateTime? ChequeDate { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    /// <summary>The line has been checked.</summary>
    public bool IsOk { get; set; }
    /// <summary>The cheque or bank entry has gone through the bank.</summary>
    public bool IsCleared { get; set; }
    /// <summary>Typed, Fees (fee posting), Auto (cash in hand b/f and c/f) or Import (from Access).</summary>
    public string Source { get; set; } = "Typed";
    public int? AccessId { get; set; }
    /// <summary>The user who typed the line, and when it was first saved (empty for lines imported from Access).</summary>
    public string? EnteredBy { get; set; }
    public DateTime? EnteredOn { get; set; }
    /// <summary>The user who last changed the line after it was first saved.</summary>
    public string? ChangedBy { get; set; }
    public DateTime? ChangedOn { get; set; }
}
