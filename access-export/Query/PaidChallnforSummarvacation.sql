SELECT Students.CC, Students.Name, Family.FatherName, Feerecipt.RecipNo, Feerecipt.period, Feerecipt.ExamsFee, Feerecipt.Paid, Classes.ClassID, Classes.DeptNo, Students.CurrentSection
FROM Family INNER JOIN ((Classes INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID
WHERE (((Feerecipt.period)="JUN/19") AND ((Feerecipt.Paid)=True) AND ((Classes.DeptNo)<>4));

