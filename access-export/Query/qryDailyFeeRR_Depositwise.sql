SELECT Family.FormID, Students.CC, Students.[DS#], Students.Name, Students.CurrentClass, Students.CurrentSection, Students.Status, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.Paid, Feerecipt.Fine, Feerecipt.RecipNo, Feerecipt.Other, [TuitionFee]+[ExamsFee]+[Mis_fee]+[AdmissionFee]+[Fine]+[Other]+[ProspectusFee] AS [Fee on Slip], Feerecipt.ReceivedAmount, Feerecipt.ProspectusFee, Feerecipt.CollectionType, Feerecipt.Remarks
FROM Family INNER JOIN (Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID
WHERE (((Feerecipt.ReDate)>=CStr(forms![Report Date Range]![Beginning Trans Date]) And (Feerecipt.ReDate)<=CStr(Forms![Report Date Range]![Ending Trans Date])) And ((Feerecipt.CollectionType)=Forms![Report Date Range]!cboCollectionType))
ORDER BY Students.[DS#];

