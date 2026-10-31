UPDATE Students INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC SET FeeDefinitation.RevisionDate = Date(), FeeDefinitation.TuitionFee = FeeDefinitation.TuitionFee+300
WHERE (((Students.Flag)=True) AND ((Students.Status)=True) AND ((Students.CC)<2207));

