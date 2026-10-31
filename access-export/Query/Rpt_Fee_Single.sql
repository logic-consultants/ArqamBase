SELECT Students.CC, Students.[DS#], Family.FormID, Students.Name, Students.CurrentClass, Students.CurrentSection, Family.FatherName, Feerecipt.IssueDate, [TuitionFee]+[Mis_fee]+[AdmissionFee]+[Fine]+[other]+[ProspectusFee] AS TOTAL, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.Paid, Feerecipt.Fine, Students.Status, Feerecipt.ProspectusFee, Feerecipt.other, Feerecipt.[Sr#], Feerecipt.Fine
FROM Family INNER JOIN ((Departments INNER JOIN (Classes INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) ON Departments.DeptNo = Classes.DeptNo) INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID
WHERE (((Family.FormID)=Forms!Classselect!Combo7) And ((Feerecipt.Paid)=False) And ((Students.Status)=True))
ORDER BY Students.CurrentClass;

