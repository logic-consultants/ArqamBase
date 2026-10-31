SELECT Family.FormID, Family.FatherName, Students.CC, Students.Name, Students.CurrentClass, Students.CurrentSection, Feerecipt.IssueDate, Feerecipt.Period, Feerecipt.TuitionFee, Students.Status
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC]
WHERE (((Feerecipt.Period)="APR/14") AND ((Students.Status)=True));

