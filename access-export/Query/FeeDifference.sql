SELECT Students.CC, Students.FamilyID, Students.Name, Students.CurrentClass, FeeDefinitation.TuitionFee, Feerecipt.TuitionFee, Feerecipt.IssueDate, Feerecipt.Period, [FeeDefinitation].[TuitionFee]-[Feerecipt].[TuitionFee] AS Diff
FROM (Students INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC) INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC;

