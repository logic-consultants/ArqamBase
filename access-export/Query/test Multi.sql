SELECT Sum(Transaction.Debit)-Sum(Transaction.Credit) AS BF, Transaction.AccountID AS Expr1
FROM [Transaction]
WHERE (((Transaction.TransactionDate)<Forms![Report Date Range Lst]![Beginning Trans Date]))
GROUP BY Transaction.AccountID;

