SELECT STDMain.RegNo AS Expr1, STDMain.CC AS Expr2, STDMain.Name AS Expr3, Family.F_Name AS Expr4, STDMain.ClassID AS Expr5, STDMain.FamID AS Expr6, Feerecipt.T_fee, Feerecipt.Build_F, Feerecipt.Mis_fee, Feerecipt.Comp_F, [T_Fee]+[BUILD_F]+[MIS_Fee]+[Comp_F]+[Fine] AS Total, STDMain.Status AS Expr7, Family.Adress1 AS Expr8, Family.Ph AS Expr9, Feerecipt.Period, Feerecipt.Fine, Feerecipt.Piad AS Expr10, STDMain.SEC AS Expr11, Feerecipt.IssueDate
FROM Family, Classes, STDMain, Feerecipt
WHERE (((STDMain.Status)=-1))
ORDER BY STDMain.RegNo, STDMain.CC, STDMain.ClassID;

