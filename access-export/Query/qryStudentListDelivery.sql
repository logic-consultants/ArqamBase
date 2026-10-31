SELECT Students.CC, Students.Name, Family.FatherName, Family.Mobile, Students.CurrentClass, Students.CurrentSection, Students.Status, FeeDefinitation.TuitionFee
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC
WHERE (((Students.Status)=True) AND ((FeeDefinitation.TuitionFee)<=2500));

