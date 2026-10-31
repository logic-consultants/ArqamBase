SELECT DISTINCTROW Transaction.*, Accounts.AccountName AS Expr1, Transaction.AccountID AS Expr2, [Debit]-[Credit] AS Runbal
FROM Accounts, [Transaction]
WHERE (((Transaction.AccountID)=Forms![Report Date Range]!Combo16) And ((Transaction.TransactionDate)>=forms![Report Date Range]![Beginning Trans Date] And (Transaction.TransactionDate)<=Forms![Report Date Range]![Ending Trans Date]));

