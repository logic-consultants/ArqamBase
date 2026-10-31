SELECT AccountTypes.AccountTypeID AS Expr1, AccountTypes.AccountType AS Expr2, Sum([debit]-[credit]) AS Balance, ACTypeHeads.ACTypeHeadID AS Expr3, ACTypeHeads.ACTypeHeadName AS Expr4
FROM AL, ACTypeHeads, AccountTypes, Accounts, Trans_head, Transactions
WHERE (((Trans_head.TransactionDate)<=Forms!frmDateRange_BalanceSheetFinal!txtFDate))
GROUP BY AccountTypes.AccountTypeID, AccountTypes.AccountType, ACTypeHeads.ACTypeHeadID, ACTypeHeads.ACTypeHeadName
HAVING (((ACTypeHeads.ACTypeHeadID)=1 Or (ACTypeHeads.ACTypeHeadID)=2));

