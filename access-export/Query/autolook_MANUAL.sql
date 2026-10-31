INSERT INTO Feerecipt ( CC, IssueDate, Period, TuitionFee, ExamsFee, Mis_fee, Other, ProspectusFee, DRCR, DueDate )
SELECT Students.CC, #1/25/2023# AS IssueDate, [Forms]![Feeupdate]![Period] AS Period, FeeDefinitation.TuitionFee, FeeDefinitation.ExamsFee, FeeDefinitation.MiscFee, FeeDefinitation.Other, FeeDefinitation.LabFee, "DR" AS DRCR, #2/10/2023# AS DueDate
FROM Students INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC
WHERE (((Students.Status)=True));

