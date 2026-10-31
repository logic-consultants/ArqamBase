SELECT Family.FormID, Family.FatherName, Students.CC AS Students_CC, Students.Name, Students.CurrentClass, Students.Status, Feerecipt.[Sr#], Feerecipt.IssueDate, Feerecipt.ReDate, Feerecipt.Period, Feerecipt.TuitionFee, Feerecipt.ExamsFee, Feerecipt.Mis_fee, Feerecipt.AdmissionFee, Feerecipt.ProspectusFee, Feerecipt.Fine, Feerecipt.RecipNo, Feerecipt.Other, Feerecipt.ReceivedAmount, Feerecipt.Paid, Feerecipt.DateEdited, Feerecipt.DRCR, Feerecipt.Description
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC]
WHERE (((Feerecipt.IssueDate)>#7/31/2014#) AND ((Feerecipt.RecipNo)<=0) AND ((Feerecipt.Paid)=True)) OR (((Feerecipt.RecipNo) Is Null));

