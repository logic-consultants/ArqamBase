PARAMETERS BDate DateTime, EDate DateTime;
SELECT Feerecipt.RecipNo, Feerecipt.CC, Sum(Feerecipt.ReceivedAmount) AS SumOfReceivedAmount
FROM Feerecipt
WHERE (((Feerecipt.ReDate)>=[BDate] And (Feerecipt.ReDate)<=[EDate]) AND ((Feerecipt.Paid)=True))
GROUP BY Feerecipt.RecipNo, Feerecipt.CC;

