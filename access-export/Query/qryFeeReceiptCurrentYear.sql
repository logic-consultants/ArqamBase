SELECT Feerecipt.[Sr#], Feerecipt.IssueDate, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.CC, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.ProspectusFee, Feerecipt.Fine, Feerecipt.RecipNo, Feerecipt.Other, Feerecipt.ReceivedAmount, Feerecipt.Paid, Feerecipt.DateEdited, Feerecipt.DRCR, Feerecipt.Description, Feerecipt.DueDate
FROM Feerecipt
WHERE (((Feerecipt.IssueDate)>=#6/20/2017#));

