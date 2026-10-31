INSERT INTO Feerecipt ( CC, IssueDate, Period, TuitionFee, ExamsFee, Mis_fee, Other, ProspectusFee, DRCR, DueDate, Description )
SELECT Students.CC, #01/12/2022# AS IssueDate, 'DEC/22' AS Period, FeeDefinitation.TuitionFee, FeeDefinitation.ExamsFee, FeeDefinitation.MiscFee, FeeDefinitation.Other, FeeDefinitation.LabFee, 'DR' AS DRCR, #10/12/2022# AS DueDate, '' AS Description
FROM Students INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC
WHERE Students.Status=True;

