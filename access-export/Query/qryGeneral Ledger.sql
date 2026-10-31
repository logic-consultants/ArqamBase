SELECT DISTINCTROW Transactions.TransactionID AS Expr1, Trans_head.TransactionDate AS Expr2, Trans_head.VoucherNo AS Expr3, [vouchertypecode] & "-" & [voucherno] AS VNo, Transactions.AccountID AS Expr4, Transactions.TransactionDescription AS Expr5, Transactions.DW AS Expr6, Transactions.Debit AS Expr7, Transactions.Credit AS Expr8, [debit]-[credit] AS Bal1, Transactions.VehicleID AS Expr9, VehicleTransactioned.EngineNo AS Expr1, VehicleTransactioned.ChassisNo AS Expr2, VehicleTransactioned.DONo AS Expr3
FROM VehicleTransactioned, VoucherType, Trans_head, Transactions
WHERE (((Transactions.AccountID)=Forms!frmDateRange_Accounts!cboAccounts))
ORDER BY Trans_head.TransactionDate;

