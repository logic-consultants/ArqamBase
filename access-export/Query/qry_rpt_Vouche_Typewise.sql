SELECT DISTINCTROW [vouchertypecode] & "-" & [VoucherNo] AS vno, Trans_head.TransactionDate AS Expr1, Transactions.AccountID AS Expr2, Transactions.TransactionDescription AS Expr3, Transactions.Debit AS Expr4, Transactions.Credit AS Expr5, Transactions.Depriciation AS Expr6, Transactions.TransactionID AS Expr7, [debit]-[credit] AS bal1, VoucherType.VoucherTypeCode AS Expr8, VoucherType.VoucherTypeID AS Expr9, VoucherType.VoucherType AS Expr10, Trans_head.VoucherNo AS Expr11
FROM VoucherType, Trans_head, AccountTypes, Accounts, Transactions
WHERE (((Trans_head.TransactionDate)>=Forms!frmDateRange_Vouchers!txtFdate And (Trans_head.TransactionDate)<=Forms!frmDateRange_Vouchers!txtTdate) And ((VoucherType.VoucherTypeID)=Forms!frmDateRange_Vouchers!cboAccounts));

