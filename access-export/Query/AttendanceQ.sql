INSERT INTO Attendance ( CC, [Date], Status )
SELECT STDMain.CC AS Expr1, Date() AS [Date], "P" AS Statu
FROM STDMain
WHERE (((STDMain.Status)=-1));

