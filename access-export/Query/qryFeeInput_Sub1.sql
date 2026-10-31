SELECT Family.FormID, Students.CC, Students.Name, Family.FatherName, Students.CurrentClass, Students.CurrentSection, Feerecipt.IssueDate, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.ProspectusFee, Feerecipt.Fine, Feerecipt.RecipNo, Feerecipt.Other, Feerecipt.ReceivedAmount, Feerecipt.Paid, Feerecipt.Description, Feerecipt.DueDate, Feerecipt.DRCR, [TuitionFee]+[ExamsFee]+[Mis_fee]+[AdmissionFee]+[ProspectusFee]+[Fine]+[Other] AS Total, Feerecipt.DateEdited
FROM Family INNER JOIN (Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID
WHERE (((Feerecipt.Paid)=False));

