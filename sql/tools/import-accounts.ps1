<#
.SYNOPSIS
    Writes a SQL script that loads one year of the Access accounts database into the acc_ tables.

.DESCRIPTION
    Reads the Access back end read-only (DAO; Access or the Access Database Engine must be installed)
    and writes INSERT statements for acc_Accounts, acc_Vouchers and acc_VoucherLines. IDs are kept:
    AccountID, the voucher ID (Trans_head.ID) and, on each line, AccessID = table1.TransactionID.
    Run sql/accounts_install.sql first. The script it writes refuses to run if vouchers already exist.

    Run it again on the newest Access file just before switching over, so nothing typed in Access is lost.

.EXAMPLE
    .\import-accounts.ps1 -BackEnd 'Z:\Acc_Toba_2026-27\ACC_Toba_be_26-27.accdb' -Year '2026-27' -Out ..\accounts_import_2026_27.sql
#>
param(
    [Parameter(Mandatory)] [string] $BackEnd,
    [string] $Year = '2026-27',
    [string] $Out = (Join-Path $PSScriptRoot "..\accounts_import_$($Year -replace '-', '_').sql")
)
$ErrorActionPreference = 'Stop'

function Q([object] $v) {
    if ($null -eq $v -or $v -is [DBNull]) { return 'NULL' }
    $s = "$v"
    if ($s.Trim().Length -eq 0) { return 'NULL' }
    return "N'" + $s.Replace("'", "''") + "'"
}
function Num([object] $v) { if ($null -eq $v -or $v -is [DBNull]) { '0' } else { ([decimal]$v).ToString([Globalization.CultureInfo]::InvariantCulture) } }
function Dt([object] $v) { if ($null -eq $v -or $v -is [DBNull]) { 'NULL' } else { "'" + ([datetime]$v).ToString('yyyy-MM-dd') + "'" } }
function Bit([object] $v) { if ($v -eq $true) { '1' } else { '0' } }

$dbe = New-Object -ComObject DAO.DBEngine.120
$db = $dbe.OpenDatabase((Resolve-Path $BackEnd).Path, $false, $true)
$sb = [System.Text.StringBuilder]::new()
function W([string] $line) { [void]$sb.AppendLine($line) }

function Rows([string] $sql) {
    $rs = $db.OpenRecordset($sql)
    $list = [System.Collections.Generic.List[object]]::new()
    while (-not $rs.EOF) {
        $row = @{}
        foreach ($f in $rs.Fields) { $row[$f.Name] = $f.Value }
        $list.Add($row)
        $rs.MoveNext()
    }
    $rs.Close()
    return , $list
}

function Batches([System.Collections.Generic.List[string]] $values, [string] $insert) {
    for ($i = 0; $i -lt $values.Count; $i += 500) {
        W $insert
        W ('    ' + (($values.GetRange($i, [Math]::Min(500, $values.Count - $i))) -join ",`r`n    ") + ';')
    }
}

$accounts = Rows 'SELECT AccountID, AccountNumber, AccountName, AccountTypeID, SubTypeCode, CareOf, Moble, Mobile2, PTCL, PTCL2, Address FROM Accounts ORDER BY AccountID'
$heads = Rows 'SELECT ID, TransactionDate, Type, Folio FROM Trans_head ORDER BY ID'
$lines = Rows 'SELECT TransactionID, Trans_Head, AccountID, TransactionDescription, DepositAmount, WithdrawalAmount, BillNo, DueDays, chqDate, OK, Chk FROM table1 ORDER BY Trans_Head, TransactionID'
$headIds = @{}
foreach ($h in $heads) { $headIds[[int]$h.ID] = $true }

W "/* Accounts $Year imported from $([IO.Path]::GetFileName($BackEnd)) on $(Get-Date -Format 'dd/MM/yyyy HH:mm')."
W "   $($accounts.Count) accounts, $($heads.Count) vouchers, $($lines.Count) lines. Made by sql/tools/import-accounts.ps1; run accounts_install.sql first. */"
W 'SET NOCOUNT ON;'
W 'SET XACT_ABORT ON;'
W "IF EXISTS (SELECT 1 FROM dbo.acc_Vouchers) OR EXISTS (SELECT 1 FROM dbo.acc_VoucherLines)"
W "BEGIN RAISERROR('Vouchers already exist: the import runs only into empty accounts tables.', 16, 1); SET NOEXEC ON; END"
W 'GO'
W 'BEGIN TRANSACTION;'
W "DECLARE @year int = (SELECT YearID FROM dbo.acc_FinancialYears WHERE Name = '$Year');"
W "IF @year IS NULL BEGIN RAISERROR('Financial year $Year is missing.', 16, 1); ROLLBACK; RETURN; END"
W ''

# Accounts: kept by ID. Cash & banks become Cash (Cash in Hand) or Bank (the rest).
W 'DELETE dbo.acc_Accounts;'
W 'SET IDENTITY_INSERT dbo.acc_Accounts ON;'
$values = [System.Collections.Generic.List[string]]::new()
foreach ($a in $accounts) {
    $sub = if ($a.SubTypeCode -is [DBNull] -or $null -eq $a.SubTypeCode) { 'NULL' } else { "$($a.SubTypeCode)" }
    $kind = if ("$($a.SubTypeCode)" -eq '5') { if ("$($a.AccountName)" -match 'cash in hand') { 1 } else { 2 } } else { 0 }
    $values.Add("($($a.AccountID), $(Q $a.AccountNumber), $(Q $a.AccountName), $($a.AccountTypeID), $sub, $kind, $(Q $a.CareOf), $(Q $a.Moble), $(Q $a.Mobile2), $(Q $a.PTCL), $(Q $a.PTCL2), $(Q $a.Address), 1, N'Access import', SYSDATETIME())")
}
Batches $values 'INSERT dbo.acc_Accounts (AccountID, AccountNo, Name, AccountTypeID, SubTypeID, Kind, CareOf, Mobile, Mobile2, Phone, Phone2, Address, IsActive, CreatedBy, CreatedOn) VALUES'
W 'SET IDENTITY_INSERT dbo.acc_Accounts OFF;'
W ''

# Vouchers: Access has one "Cash" voucher per day; the first one holds the opening balances.
$first = ($heads | Select-Object -First 1).ID
W 'SET IDENTITY_INSERT dbo.acc_Vouchers ON;'
$values = [System.Collections.Generic.List[string]]::new()
foreach ($h in $heads) {
    $opening = $h.ID -eq $first
    $type = if ($opening) { 'Opening' } else { 'Daily' }
    $date = [datetime]$h.TransactionDate
    $narr = if ($opening) { 'Opening balances' } else { "Daily cash sheet $($date.ToString('dd/MM/yyyy'))" }
    $folio = if ($h.Folio -is [DBNull] -or $null -eq $h.Folio) { 'NULL' } else { "$($h.Folio)" }
    $values.Add("($($h.ID), @year, $($h.ID), '$type', $(Dt $h.TransactionDate), $folio, N'$narr', N'Access import', SYSDATETIME())")
}
Batches $values 'INSERT dbo.acc_Vouchers (VoucherID, YearID, VoucherNo, VoucherType, VoucherDate, Folio, Narration, CreatedBy, CreatedOn) VALUES'
W 'SET IDENTITY_INSERT dbo.acc_Vouchers OFF;'
W ''

# Lines, numbered within each voucher. DueDays held the cheque number. A line whose voucher is missing keeps no voucher.
$values = [System.Collections.Generic.List[string]]::new()
$no = @{}
foreach ($l in $lines) {
    $head = if ($l.Trans_Head -is [DBNull] -or $null -eq $l.Trans_Head -or -not $headIds.ContainsKey([int]$l.Trans_Head)) { $null } else { [int]$l.Trans_Head }
    $key = if ($null -eq $head) { 0 } else { $head }
    $no[$key] = 1 + [int]$no[$key]
    $voucher = if ($null -eq $head) { 'NULL' } else { "$head" }
    $chq = if ($l.DueDays -is [DBNull] -or $null -eq $l.DueDays) { 'NULL' } else { "N'$($l.DueDays)'" }
    $values.Add("($voucher, $($no[$key]), $($l.AccountID), $(Q $l.TransactionDescription), $(Q $l.BillNo), $chq, $(Dt $l.chqDate), $(Num $l.DepositAmount), $(Num $l.WithdrawalAmount), $(Bit $l.OK), $(Bit $l.Chk), 'Import', $($l.TransactionID))")
}
Batches $values 'INSERT dbo.acc_VoucherLines (VoucherID, LineNumber, AccountID, Description, BillNo, ChequeNo, ChequeDate, Debit, Credit, IsOk, IsCleared, Source, AccessID) VALUES'
W ''
W 'COMMIT;'
W "DECLARE @a int = (SELECT COUNT(*) FROM dbo.acc_Accounts), @v int = (SELECT COUNT(*) FROM dbo.acc_Vouchers), @l int = (SELECT COUNT(*) FROM dbo.acc_VoucherLines);"
W "PRINT CONCAT('Imported: ', @a, ' accounts, ', @v, ' vouchers, ', @l, ' lines.');"
W 'GO'
W 'SET NOEXEC OFF;'
W 'GO'

$db.Close()
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Out), $sb.ToString(), [Text.UTF8Encoding]::new($true))
"Wrote $Out : $($accounts.Count) accounts, $($heads.Count) vouchers, $($lines.Count) lines."
