SELECT DISTINCTROW ACTypeHeads.ACTypeHeadName AS Expr1, Sum(Transactions.Debit) AS [Sum Of Debit], Sum(Transactions.Credit) AS [Sum Of Credit], Sum([Debit])-Sum([Credit]) AS Balance
FROM ACTypeHeads, AccountTypes, Accounts, Trans_head, Transactions
WHERE (((Trans_head.TransactionDate)>=Forms!frmDateRange_InExpC!txtFDate And (Trans_head.TransactionDate)<=Forms!frmDateRange_InExpC!txtTDate))
GROUP BY ACTypeHeads.ACTypeHeadName
HAVING (((ACTypeHeads.ACTypeHeadName)="Income" Or (ACTypeHeads.ACTypeHeadName)="Expenses"));

