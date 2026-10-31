SELECT STDMain.CC AS Expr1, STDMain.RegNo AS Expr2, STDMain.FamID AS Expr3, STDMain.Name AS Expr4, STDMain.ClassID AS Expr5, STDMain.SEC AS Expr6, Family.F_Name AS Expr7, Family.Adress1 AS Expr8, Family.Ph AS Expr9, FeeDef.T_fee AS Expr10, FeeDef.Build_F AS Expr11, FeeDef.Mis_fee AS Expr12, FeeDef.Comp_F AS Expr13, [T_Fee]+[Build_F]+[Mis_fee]+[Comp_F] AS Total, Family.PH2 AS Expr14, Family.PH3 AS Expr15
FROM Family, Departments, Classes, STDMain, FeeDef
WHERE (((Departments.DeptNo)=2) And ((STDMain.Status)=-1))
ORDER BY STDMain.CC, STDMain.ClassID;

