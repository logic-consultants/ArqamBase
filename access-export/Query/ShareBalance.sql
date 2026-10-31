SELECT Sum([Debit])-Sum([Credit]) AS Balance, Accounts.AccountID AS Expr1, AccountTypes.AccountType AS Expr2, Investors.SirName AS Expr3, Investors.Phone AS Expr4, Investors.Mobile AS Expr5, Investors.DateofInv AS Expr6, Investors.Reference AS Expr7, Investors.Address AS Expr8
FROM AccountTypes, Accounts, Investors, Transactions
GROUP BY Accounts.AccountID, AccountTypes.AccountType, Investors.SirName, Investors.Phone, Investors.Mobile, Investors.DateofInv, Investors.Reference, Investors.Address
HAVING (((AccountTypes.AccountType) Like ("*Capital*")));

