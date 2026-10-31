PARAMETERS Bdate DateTime, Edate DateTime;
SELECT Family.Mobile, Family.FatherName, Feerecipt.ReDate, Sum(Feerecipt.ReceivedAmount) AS SumOfReceivedAmount, Feerecipt.RecipNo, Family.FormID, Count(Feerecipt.Period) AS CountOfPeriod, Feerecipt.CollectionType
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC]
WHERE (((Feerecipt.Paid)=True))
GROUP BY Family.Mobile, Family.FatherName, Feerecipt.ReDate, Feerecipt.RecipNo, Family.FormID, Feerecipt.CollectionType
HAVING (((Feerecipt.ReDate) Between [bdate] And [Edate]))
ORDER BY Feerecipt.RecipNo;

