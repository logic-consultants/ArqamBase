SELECT DISTINCTROW Trans_head.TransactionDate AS Expr1, Transactions.Debit AS Expr2, Transactions.Credit AS Expr3, [Debit]-[Credit] AS Balance, Day([TransactionDate]) AS [Date]
FROM Trans_head, Transactions
GROUP BY Trans_head.TransactionDate, Transactions.Debit, Transactions.Credit, [Debit]-[Credit], Day([TransactionDate]), Transactions.AccountID, Year([TransactionDate]), Month([TransactionDate])
HAVING (((Year([TransactionDate]))=2008) And ((Month([TransactionDate]))=6) And ((Transactions.AccountID)=58));

