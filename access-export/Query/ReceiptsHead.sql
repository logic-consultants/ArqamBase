SELECT Transaction.TransactionID AS Expr1, Transaction.Trans_Head AS Expr2, Transaction.TransactionDate AS Expr3, Transaction.TransactionDescription AS Expr4, Transaction.AccountID AS Expr5, Transaction.Balance AS Expr6, Accounts.AccountName AS Expr7
FROM [Transaction], Accounts
WHERE (((Transaction.Balance)<0));

