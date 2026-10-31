UPDATE Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC SET Feerecipt.Other = [Forms]![Feeupdate]![CboExamFee]
WHERE (((Feerecipt.Other)=0) AND ((Students.CurrentClass)=[Forms]![Feeupdate]![CboClasses]) AND ((Feerecipt.Period)=[Forms]![Feeupdate]![Period]) AND ((Feerecipt.IssueDate)>[Forms]![Feeupdate]![IssueDate]) AND ((Students.Status)=True));

