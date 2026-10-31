SELECT BalanceSheetPrv.ACTypeHeadName AS Expr1, Sum(BalanceSheetPrv.Balance) AS SumOfBalance
FROM BalanceSheetPrv
GROUP BY BalanceSheetPrv.ACTypeHeadName;

