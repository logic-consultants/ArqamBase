SELECT DISTINCTROW Accounts.AccountID AS Expr1, Accounts.AccountName AS Expr2, Sum(Transactions.Credit) AS WithdrawalAmount, Sum(Transactions.Debit) AS DepositAmount, [DepositAmount]-[WithdrawalAmount] AS Balance
FROM AccountTypes, Accounts, Transactions
GROUP BY Accounts.AccountID, Accounts.AccountName
ORDER BY Accounts.AccountID;

