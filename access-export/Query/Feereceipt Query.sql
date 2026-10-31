SELECT STDMain.CC AS Expr1, STDMain.RegNo AS Expr2, STDMain.FamID AS Expr3, STDMain.Name AS Expr4, STDMain.ClassID AS Expr5, STDMain.SEC AS Expr6, STDMain.Status AS Expr7, Feerecipt.[Sr#], Feerecipt.IssueDate, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.T_fee, Feerecipt.Build_F, Feerecipt.Mis_fee, Feerecipt.Comp_F, Feerecipt.Piad AS Expr8, Feerecipt.Fine, [T_fee]+[Build_F]+[Mis_fee]+[Comp_F]+[Other] AS Total, Family.F_Name AS Expr9, Feerecipt.RecipNo, Feerecipt.Other
FROM Family, STDMain, Feerecipt
WHERE (((Feerecipt.IssueDate)<=#11/1/2004#));

