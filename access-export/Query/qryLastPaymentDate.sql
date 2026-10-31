SELECT Feerecipt.CC, Max(Feerecipt.ReDate) AS MaxOfReDate
FROM Feerecipt
GROUP BY Feerecipt.CC;

