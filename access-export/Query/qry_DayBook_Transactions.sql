SELECT DISTINCTROW Transactions.TransactionID AS Expr1, Trans_head.TransactionDate AS Expr2, Transactions.Trans_Head AS Expr3, Transactions.AccountID AS Expr4, Transactions.DW AS Expr5, Transactions.TransactionDescription AS Expr6, Transactions.Debit AS Expr7, Transactions.Credit AS Expr8, Transactions.VehicleID AS Expr9, Trans_head.VoucherNo AS Expr10, Day([TransactionDate]) AS Expr1
FROM Trans_head, Transactions
WHERE (((Day([TransactionDate]))=[Date]) And ((Transactions.AccountID)=58) And ((Year([TransactionDate]))=2008) And ((Month([TransactionDate]))=6))
ORDER BY Trans_head.TransactionDate;

