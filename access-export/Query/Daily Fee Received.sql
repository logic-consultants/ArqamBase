SELECT Family.FormID, Students.CC, Students.[DS#], Students.Name, Students.CurrentClass, Students.CurrentSection, Students.Status, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.Paid, Feerecipt.Fine, Feerecipt.RecipNo, Feerecipt.Other, [TuitionFee]+[ExamsFee]+[Mis_fee]+[AdmissionFee]+[Fine]+[Other]+[ProspectusFee] AS [Fee on Slip], Feerecipt.ReceivedAmount, Feerecipt.ProspectusFee, [ReceivedAmount]-[Fee on slip] AS Expr1, Feerecipt.CollectionType
FROM Family INNER JOIN (Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID
WHERE Feerecipt.ReDate>=cstr([forms]![Report Date Range]![Beginning Trans Date]) And Feerecipt.ReDate <= cstr([Forms]![Report Date Range]![Ending Trans Date])
ORDER BY Students.[DS#];

