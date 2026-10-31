SELECT Feerecipt.IssueDate, Feerecipt.CC, [tuitionFee]+[ExamsFee]+[Mis_fee]+[ProspectusFee]+[fine]+[other] AS Total, Feerecipt.ReceivedAmount, Feerecipt.RecipNo, [Total]-[ReceivedAmount] AS Diff, Feerecipt.ReDate
FROM Feerecipt
WHERE (((Feerecipt.IssueDate)>=#5/1/2015#))
ORDER BY Feerecipt.ReceivedAmount;

