SELECT Students.CC, Family.FormID, Students.Name, Family.FatherName, Students.CurrentClass, Students.CurrentSection, FeeDefinitation.TuitionFee
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN FeeDefinitation ON Students.[CC] = FeeDefinitation.[FK_CC]
WHERE (((Students.Status)=True));

