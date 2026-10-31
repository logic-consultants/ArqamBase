SELECT ReceiptsHead.AccountID AS Expr1, Sum(ReceiptsHead.Balance) AS SumOfBalance, ReceiptsHead.AccountName AS Expr2
FROM ReceiptsHead
WHERE (((ReceiptsHead.TransactionDate)>=Forms!frmDateRange_PayRec!txtFDate And (ReceiptsHead.TransactionDate)<=Forms!frmDateRange_PayRec!txtTDate))
GROUP BY ReceiptsHead.AccountID, ReceiptsHead.AccountName
HAVING (((Sum(ReceiptsHead.Balance))<0));

