SELECT PaymentsHead.AccountID AS Expr1, Sum(PaymentsHead.Balance) AS SumOfBalance, PaymentsHead.AccountName AS Expr2
FROM PaymentsHead
WHERE (((PaymentsHead.TransactionDate)>=Forms!frmDateRange_PayRec!txtFDate And (PaymentsHead.TransactionDate)<=Forms!frmDateRange_PayRec!txtTDate))
GROUP BY PaymentsHead.AccountID, PaymentsHead.AccountName
HAVING (((Sum(PaymentsHead.Balance))>0));

