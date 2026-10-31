INSERT INTO Feerecipt ( CC, IssueDate, Period, TuitionFee, ExamsFee, Mis_fee, Other, ProspectusFee, DRCR, DueDate, Description )
SELECT Students.CC, [Forms]![Feeupdate]![IssueDate] AS IssueDate, [Forms]![Feeupdate]![Period] AS Period, FeeDefinitation.TuitionFee, FeeDefinitation.ExamsFee, FeeDefinitation.MiscFee, FeeDefinitation.Other, FeeDefinitation.LabFee, "DR" AS DRCR, [Forms]![Feeupdate]![DueDate] AS DueDate, IIf(IsNull([Forms]![Feeupdate]![txtMessage]),"",[Forms]![Feeupdate]![txtMessage]) AS Description
FROM Students INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC
WHERE (((Students.Status)=True));

