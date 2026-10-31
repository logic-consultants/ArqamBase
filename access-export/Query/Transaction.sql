SELECT Transactions.TransactionID AS Expr1, Transactions.Trans_Head AS Expr2, Trans_head.TransactionDate AS Expr3, Transactions.TransactionDescription AS Expr4, Transactions.AccountID AS Expr5, Transactions.Credit AS Expr6, Transactions.Debit AS Expr7, [Debit]-[Credit] AS Balance, Transactions.OK AS Expr8, Transactions.DW AS Expr9
FROM Trans_head, Transactions;

