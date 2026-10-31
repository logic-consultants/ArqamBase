SELECT DISTINCTROW Trans_head.TransactionDate AS Expr1, Sum(Transactions.Debit) AS SumOfDebit, Sum(Transactions.Credit) AS SumOfCredit, Sum([debit]-[credit]) AS Bal
FROM Trans_head, Transactions
GROUP BY Trans_head.TransactionDate, Month([TransactionDate]), Transactions.AccountID, Year([TransactionDate]), Day([TransactionDate])
HAVING (((Year([TransactionDate]))=2008) And ((Trans_head.TransactionDate)>=#6/1/2008#))
ORDER BY Month([TransactionDate]) DESC , Trans_head.TransactionDate DESC;

