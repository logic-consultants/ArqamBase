PARAMETERS StartDate DateTime, EndDate DateTime;
SELECT Family.FatherName, Family.Mobile, Students.CC, Students.[DS#], Students.FamilyID, Students.Name, Students.CurrentClass, Students.CurrentSection, Students.Status, Feerecipt.IssueDate, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.ProspectusFee, Feerecipt.Fine, Feerecipt.Other, Feerecipt.ReceivedAmount, Feerecipt.RecipNo, Students.AdmissionDate
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC]
WHERE (((Students.AdmissionDate)="Between [startDate] And [EndDate]"));

