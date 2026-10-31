SELECT Family.FormID, Family.FatherName, Students.CC, Students.Name, Students.CurrentClass, Students.CurrentSection, Sum([TuitionFee]+[ExamsFee]+[Mis_fee]+[AdmissionFee]+[ProspectusFee]+[Fine]) AS TotalFee, Count(*) AS Total
FROM Family INNER JOIN (Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID
WHERE (((Feerecipt.Paid)=False) AND ((Students.Status)=True))
GROUP BY Family.FormID, Family.FatherName, Students.CC, Students.Name, Students.CurrentClass, Students.CurrentSection;

