SELECT Family.FormID, Family.FatherName, Students.Name, Feerecipt.RecipNo, Feerecipt.ReceivedAmount, Feerecipt.CC, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.ProspectusFee, Feerecipt.Fine, Feerecipt.Other, Feerecipt.Paid, Feerecipt.ReDate
FROM Family INNER JOIN (Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Family.FormID = Students.FamilyID;

