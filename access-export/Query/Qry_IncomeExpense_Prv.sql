SELECT DISTINCTROW ACTypeHeads.ACTypeHeadID AS Expr1, ACTypeHeads.ACTypeHeadName AS Expr2, AccountTypes.AccountTypeID AS Expr3, AccountTypes.AccountType AS Expr4, Accounts.AccountID AS Expr5, Accounts.AccountName AS Expr6, Sum([Debit])-Sum([Credit]) AS Balance
FROM ACTypeHeads, AccountTypes, Accounts, Trans_head, Transactions
WHERE (((Trans_head.TransactionDate)>=Forms!frmDateRange_InExpC!txtP_Fdate And (Trans_head.TransactionDate)<=Forms!frmDateRange_InExpC!txtP_tDate))
GROUP BY ACTypeHeads.ACTypeHeadID, ACTypeHeads.ACTypeHeadName, AccountTypes.AccountTypeID, AccountTypes.AccountType, Accounts.AccountID, Accounts.AccountName
HAVING (((ACTypeHeads.ACTypeHeadName)="Income" Or (ACTypeHeads.ACTypeHeadName)="Expenses"));

