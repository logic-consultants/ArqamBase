SELECT STDMain.CC AS Expr1, STDMain.RegNo AS Expr2, STDMain.FamID AS Expr3, STDMain.Name AS Expr4, STDMain.ClassID AS Expr5, STDMain.SEC AS Expr6, Family.F_Name AS Expr7, Feerecipt.IssueDate, [T_fee]+[Build_F]+[Mis_fee]+[Comp_F]+[other]+[fine] AS TOTAL, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.T_fee, Feerecipt.Build_F, Feerecipt.Mis_fee, Feerecipt.Comp_F, Feerecipt.Piad AS Expr8, Feerecipt.Fine
FROM Family, Departments, Classes, STDMain, Feerecipt
WHERE (((Feerecipt.Piad)=0) And ((Departments.DeptNo)=2) And ((STDMain.Status)=-1))
ORDER BY STDMain.ClassID;

