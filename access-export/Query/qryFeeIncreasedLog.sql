SELECT tblFeeUpdateList.CC, Students.Name, Students.CurrentClass, Students.CurrentSection, tblFeeUpdateList.CurrentFee, tblFeeUpdateList.IncreasedAmount, tblFeeUpdateList.NewFee, tblFeeUpdateList.UpdatedOn, tblFeeUpdateList.ReasonUpdate
FROM Students INNER JOIN tblFeeUpdateList ON Students.CC = tblFeeUpdateList.CC;

