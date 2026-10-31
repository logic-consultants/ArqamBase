SELECT Branches.BrID, Students.CurrentClass, Sum(FeeDefinitation.TuitionFee) AS SumOfTuitionFee, Avg(FeeDefinitation.TuitionFee) AS AvgOfTuitionFee, Count(Students.cc) AS Strenght
FROM ((Branches INNER JOIN Departments ON Branches.BrID = Departments.Br_ID) INNER JOIN Classes ON Departments.DeptNo = Classes.DeptNo) INNER JOIN (Students INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC) ON Classes.ClassID = Students.CurrentClass
WHERE (((Students.Status)=True))
GROUP BY Branches.BrID, Students.CurrentClass;

