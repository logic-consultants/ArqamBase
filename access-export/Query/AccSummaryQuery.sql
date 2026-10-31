SELECT AccSummary.AccountID AS Expr1, AccSummary.AccountName AS Expr2, AccSummary.WithdrawalAmount AS Expr3, AccSummary.DepositAmount AS Expr4, AccSummary.Balance AS Expr5
FROM AccSummary
WHERE (((AccSummary.Balance)<>0));

