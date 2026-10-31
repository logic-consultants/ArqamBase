SELECT AccountTypes.AccountTypeID AS Expr1, AccountTypes.AccountType AS Expr2, ACTypeHeads.ACTypeHeadID AS Expr3, Accounts.AccountName AS Expr4, Sum([debit]-[credit]) AS Balance, ACTypeHeads.ACTypeHeadName AS Expr5
FROM AL, ACTypeHeads, AccountTypes, Accounts, Trans_head, Transactions
WHERE (((Trans_head.TransactionDate)<=Forms!frmDateRange_BalanceSheet!txtTDate))
GROUP BY AccountTypes.AccountTypeID, AccountTypes.AccountType, ACTypeHeads.ACTypeHeadID, Accounts.AccountName, ACTypeHeads.ACTypeHeadName
HAVING (((Sum([debit]-[credit]))<>0) And ((ACTypeHeads.ACTypeHeadID)=1 Or (ACTypeHeads.ACTypeHeadID)=2));

