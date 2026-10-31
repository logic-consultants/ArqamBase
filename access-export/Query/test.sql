SELECT Sum(Transaction.Debit)-Sum(Transaction.Credit) AS BF
FROM [Transaction]
WHERE (((Transaction.TransactionDate)<Forms![Report Date Range]![Beginning Trans Date]) And ((Transaction.AccountID)=Forms![Report Date Range]!Combo16));

