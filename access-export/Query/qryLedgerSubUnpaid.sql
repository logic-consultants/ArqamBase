SELECT Students.CC, Feerecipt.IssueDate, [TuitionFee]+[ExamsFee]+[Mis_fee]+[AdmissionFee]+[Fine]+[Other]+[ProspectusFee] AS TOTAL, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.Fine, Feerecipt.RecipNo, Feerecipt.Other, Feerecipt.Paid, Feerecipt.ReceivedAmount, Feerecipt.Description, Feerecipt.ProspectusFee, Feerecipt.DateEdited, Feerecipt.DueDate, Feerecipt.Remarks, Feerecipt.CollectionType, Students.Status, Feerecipt.DeletedOn AS NotInclude_uptill
FROM Family INNER JOIN ((Classes INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID
WHERE (((Feerecipt.Paid)=False) AND ((Students.Status)=True))
ORDER BY Feerecipt.IssueDate DESC;

