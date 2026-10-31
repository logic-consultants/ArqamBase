SELECT Students.CC, Feerecipt.IssueDate, [TuitionFee]+[ExamsFee]+[Mis_fee]+[AdmissionFee]+[Fine]+[Other] AS TOTAL, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.Paid, Feerecipt.Fine, Feerecipt.Other
FROM Family INNER JOIN ((Sections INNER JOIN (Classes INNER JOIN Students ON Classes.ClassID=Students.CurrentClass) ON Sections.SectionID=Students.CurrentSection) INNER JOIN Feerecipt ON Students.CC=Feerecipt.CC) ON Family.FormID=Students.FamilyID
WHERE (((Feerecipt.Paid)=0));

