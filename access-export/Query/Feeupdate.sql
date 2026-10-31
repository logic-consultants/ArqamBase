SELECT STDMain.CC AS Expr1, FeeDef.T_fee AS Expr2, FeeDef.Build_F AS Expr3, FeeDef.Mis_fee AS Expr4, FeeDef.Comp_F AS Expr5, #9/1/2005# AS IssueDate, "SEP/05" AS Period
FROM STDMain, FeeDef
WHERE (((STDMain.Status)=-1))
ORDER BY STDMain.CC;

