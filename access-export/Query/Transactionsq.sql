SELECT Accounts.AccountID AS Expr1, Accounts.AccountNumber AS Expr1, Accounts.AccountName AS Expr2, Transactions.TransactionNumber AS Expr1, Transactions.TransactionDate AS Expr2, Transactions.TransactionDescription AS Expr3, Transactions.WithdrawalAmount AS Expr3, Transactions.DepositAmount AS Expr4, [WithdrawalAmount]-[DepositAmount] AS balance1
FROM Accounts, Transactions
WHERE (((Accounts.AccountNumber)="1") And (((Transactions.TransactionDate)>=#8/1/2004#)<=#9/15/2004#));

