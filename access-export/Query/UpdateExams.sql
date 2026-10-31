UPDATE Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC SET Feerecipt.ExamsFee = [Forms]![Feeupdate]![CboExamFee]
WHERE (((Feerecipt.ExamsFee)=0) AND ((Students.CurrentClass)=[Forms]![Feeupdate]![CboClasses]) AND ((Feerecipt.Period)=[Forms]![Feeupdate]![Period]) AND ((Feerecipt.IssueDate)=[Forms]![Feeupdate]![IssueDate]) AND ((Students.Status)=True));

