SELECT Students.CC, Students.[DS#], Students.FamilyID, Students.Name, Students.CurrentClass, Students.CurrentSection, Students.Status, Feerecipt.IssueDate, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.ProspectusFee, Feerecipt.Fine, Feerecipt.Other, Feerecipt.ReceivedAmount, Feerecipt.Paid, Feerecipt.RecipNo
FROM Students INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC];

