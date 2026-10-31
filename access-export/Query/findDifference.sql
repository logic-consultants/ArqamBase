SELECT Students.CC AS Students_CC, Students.FamilyID, Students.Name, Students.CurrentClass, FeeDefinitation.TuitionFee AS FeeDefinitation_TuitionFee, Feerecipt.CC AS Feerecipt_CC, Feerecipt.TuitionFee AS Feerecipt_TuitionFee
FROM (Students INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC]) INNER JOIN FeeDefinitation ON Students.[CC] = FeeDefinitation.[FK_CC];

