SELECT FeeDefinitation.FeeDefId, FeeDefinitation.TuitionFee, FeeDefinitation.FK_CC, FeeDefinitation.Other, Students.Name, Students.CurrentClass
FROM Students INNER JOIN FeeDefinitation ON Students.[CC] = FeeDefinitation.[FK_CC];

