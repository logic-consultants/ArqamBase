SELECT DISTINCTROW Prv_BalanceSheet.AccountType AS Expr1, Sum(Prv_BalanceSheet.Balance) AS SumBalance
FROM Prv_BalanceSheet
GROUP BY Prv_BalanceSheet.AccountType;

