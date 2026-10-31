UPDATE Students INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC] SET Feerecipt.TuitionFee = [TuitionFee]+300
WHERE (((Students.CC)<2207) AND ((Students.Status)=True) AND ((Feerecipt.Period)="May/24"));

